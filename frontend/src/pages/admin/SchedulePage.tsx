import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'
import PresetEditorModal, { type PresetChange, type PresetSchedule, type SchedulePreset } from '../../components/admin/PresetEditorModal'

type WorkingHour = { id?: string; dayOfWeek: number; startTime: string; endTime: string; isActive: boolean }
type Break = { id: string; dayOfWeek: number; startTime: string; endTime: string }
type BlockedSlot = { id: string; date: string; startTime: string | null; endTime: string | null; reason: string | null }

export default function SchedulePage() {
  const queryClient = useQueryClient()
  const { language: lang } = useAuth()

  const dayName = (i: number) =>
    new Date(2024, 0, 7 + i).toLocaleDateString(
      lang === 'AR' ? 'ar-SA' : lang === 'HE' ? 'he-IL' : 'en-US',
      { weekday: 'long' }
    )
  // Anchored to the upcoming Sunday (always the *next* one, even if today is a Sunday) so the
  // seven rows below read as one consecutive Sun-Sat week -- computing each row's date
  // independently as "the next occurrence of this weekday" broke that ordering, since today's
  // own weekday resolves to today (not next week) while every other row resolves into next week.
  const nextSunday = (() => {
    const today = new Date()
    const daysUntilSunday = (7 - today.getDay()) % 7 || 7
    const d = new Date(today)
    d.setDate(today.getDate() + daysUntilSunday)
    return d
  })()
  const nextDateForDay = (dayOfWeek: number) => {
    const d = new Date(nextSunday)
    d.setDate(nextSunday.getDate() + dayOfWeek)
    return d.toLocaleDateString(
      lang === 'AR' ? 'ar-SA' : lang === 'HE' ? 'he-IL' : 'en-US',
      { day: 'numeric', month: 'short' }
    )
  }

  const [newBlocked, setNewBlocked] = useState({ date: '', startTime: '', endTime: '', reason: '', fullDay: true })
  const [blockRangeMode, setBlockRangeMode] = useState(false)
  const [rangeEndDate, setRangeEndDate] = useState('')
  // null = closed, 'new' = creating a preset from the current live hours, a SchedulePreset = editing it.
  const [modalPreset, setModalPreset] = useState<SchedulePreset | 'new' | null>(null)
  const [notice, setNotice] = useState('')

  useEffect(() => {
    if (!notice) return
    const timer = setTimeout(() => setNotice(''), 5000)
    return () => clearTimeout(timer)
  }, [notice])

  const { data } = useQuery<{ workingHours: WorkingHour[]; breaks: Break[]; blockedSlots: BlockedSlot[] }>({
    queryKey: ['schedule'],
    queryFn: () => api.get('/admin/schedule').then((r) => r.data),
  })

  const { data: presets = [] } = useQuery<SchedulePreset[]>({
    queryKey: ['schedule-presets'],
    queryFn: () => api.get('/admin/schedule/presets').then((r) => r.data),
  })

  const { data: scheduledChange } = useQuery<PresetSchedule | null>({
    queryKey: ['preset-schedule'],
    queryFn: () => api.get('/admin/schedule/preset-schedule').then((r) => r.data.schedule),
  })

  const initHours = Array.from({ length: 7 }, (_, i) => {
    const existing = data?.workingHours.find((h) => h.dayOfWeek === i)
    return existing ?? { dayOfWeek: i, startTime: '09:00', endTime: '18:00', isActive: false }
  })
  const [hours, setHours] = useState<WorkingHour[]>(initHours)
  const [breaks, setBreaks] = useState<Break[]>(data?.breaks ?? [])
  const [blocked, setBlocked] = useState<BlockedSlot[]>(data?.blockedSlots ?? [])

  // Sync state when data loads
  if (data && hours.every((h) => !h.id) && data.workingHours.length > 0) {
    const synced = Array.from({ length: 7 }, (_, i) => data.workingHours.find((h) => h.dayOfWeek === i) ?? { dayOfWeek: i, startTime: '09:00', endTime: '18:00', isActive: false })
    if (JSON.stringify(synced) !== JSON.stringify(hours)) {
      setHours(synced)
      setBreaks(data.breaks)
      setBlocked(data.blockedSlots)
    }
  }

  async function addBlocked() {
    if (!newBlocked.date) return
    const { data: slot } = await api.post('/admin/schedule/blocked', {
      date: newBlocked.date,
      startTime: newBlocked.fullDay ? null : newBlocked.startTime || null,
      endTime: newBlocked.fullDay ? null : newBlocked.endTime || null,
      reason: newBlocked.reason || null,
    })
    setBlocked((prev) => [...prev, slot])
  }

  async function addBlockedRange() {
    if (!newBlocked.date || !rangeEndDate) return
    const { data: slots } = await api.post('/admin/schedule/blocked/range', {
      startDate: newBlocked.date,
      endDate: rangeEndDate,
      startTime: newBlocked.fullDay ? null : newBlocked.startTime || null,
      endTime: newBlocked.fullDay ? null : newBlocked.endTime || null,
      reason: newBlocked.reason || null,
    })
    setBlocked((prev) => [...prev, ...slots])
    setRangeEndDate('')
  }

  async function deleteBlocked(id: string) {
    await api.delete(`/admin/schedule/blocked/${id}`)
    setBlocked((prev) => prev.filter((b) => b.id !== id))
  }

  // Called after the modal saves/applies/schedules/deletes a preset -- the Working Hours section
  // above only syncs its local `hours` state from the query once (to avoid clobbering in-progress
  // manual edits), so an apply/schedule/cancel that changes the live hours needs to push the fresh
  // values into that state directly rather than relying on that one-time sync to notice.
  async function refreshAfterPresetChange(change?: PresetChange) {
    const { data: fresh } = await api.get('/admin/schedule')
    setHours(Array.from({ length: 7 }, (_, i) => fresh.workingHours.find((h: WorkingHour) => h.dayOfWeek === i) ?? { dayOfWeek: i, startTime: '09:00', endTime: '18:00', isActive: false }))
    setBreaks(fresh.breaks)
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['schedule-presets'] }),
      queryClient.invalidateQueries({ queryKey: ['preset-schedule'] }),
      queryClient.invalidateQueries({ queryKey: ['schedule'] }),
    ])
    if (change) setNotice(t(lang, change === 'applied' ? 'toastScheduleUpdated'
      : change === 'scheduled' ? 'toastPresetScheduled'
      : change === 'deleted' ? 'toastPresetDeleted' : 'toastPresetSaved'))
  }

  async function cancelScheduledChange(id: string) {
    if (!confirm(t(lang, 'cancelScheduledChangeConfirm'))) return
    await api.delete(`/admin/schedule/preset-schedule/${id}`)
    await refreshAfterPresetChange()
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-ink mb-6">{t(lang, 'schedule')}</h1>
      {notice && (
        <div role="status" className="fixed bottom-6 inset-x-4 sm:inset-x-auto sm:end-6 z-50 bg-emerald-600 text-white text-sm font-medium rounded-lg px-4 py-3 shadow-lg">
          {notice}
        </div>
      )}
      <div className="space-y-8">
        <section className="bg-surface border border-line rounded-2xl p-6">
          <h2 className="text-ink font-semibold text-lg mb-1">{t(lang, 'workingHours')}</h2>
          <p className="text-muted text-sm mb-5">{t(lang, 'workingHoursReadOnlyHint')}</p>
          {scheduledChange && (
            <div className="bg-blue-50 border border-blue-200 text-blue-700 dark:bg-blue-900/40 dark:border-blue-800/50 dark:text-blue-400 text-sm rounded-lg px-4 py-3 mb-4 flex items-center justify-between gap-3">
              <span>
                {scheduledChange.applied
                  ? `${t(lang, 'yourScheduleIsCurrently')} "${scheduledChange.presetName}" ${t(lang, 'untilDate')} ${scheduledChange.endDate}`
                  : `${t(lang, 'scheduledFrom')} ${scheduledChange.startDate} ${t(lang, 'throughDate')} ${scheduledChange.endDate}, ${t(lang, 'yourScheduleWillBe')} "${scheduledChange.presetName}"`}
              </span>
              <button onClick={() => cancelScheduledChange(scheduledChange.id)} className="shrink-0 text-blue-700 dark:text-blue-400 hover:underline text-xs font-medium">
                {t(lang, 'cancelScheduledChange')}
              </button>
            </div>
          )}
          <div className="space-y-2">
            {hours.map((h) => (
              <div key={h.dayOfWeek} className="flex items-center justify-between bg-cream rounded-lg px-4 py-2.5">
                <span className="text-ink text-sm font-medium">
                  {dayName(h.dayOfWeek)} <span className="text-muted font-normal">· {nextDateForDay(h.dayOfWeek)}</span>
                </span>
                <span className={h.isActive ? 'text-ink text-sm' : 'text-muted text-sm'}>
                  {h.isActive ? `${h.startTime} – ${h.endTime}` : t(lang, 'dayClosedLabel')}
                </span>
              </div>
            ))}
          </div>

          <div className="border-t border-line my-6" />

          <h2 className="text-ink font-semibold text-lg mb-1">{t(lang, 'schedulePresets')}</h2>
          <p className="text-muted text-sm mb-5">{t(lang, 'schedulePresetsHint')}</p>
          {presets.length > 0 && (
            <div className="space-y-2 mb-4">
              {presets.map((p) => (
                <button key={p.id} onClick={() => setModalPreset(p)}
                  className="w-full text-start flex items-center justify-between bg-cream hover:bg-coral-tint/40 rounded-lg px-4 py-2.5 transition-colors">
                  <span className="text-ink text-sm flex items-center gap-2">
                    {p.name}
                    {p.isDefault && (
                      <span className="text-[10px] uppercase tracking-wide bg-teal-tint text-ink px-1.5 py-0.5 rounded">
                        {t(lang, 'defaultPresetBadge')}
                      </span>
                    )}
                    {scheduledChange?.presetId === p.id && (
                      <span className="text-[10px] bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-400 px-1.5 py-0.5 rounded">
                        {scheduledChange.startDate} – {scheduledChange.endDate}
                      </span>
                    )}
                  </span>
                  <span className="text-coral-dark text-xs font-medium">{t(lang, 'edit')}</span>
                </button>
              ))}
            </div>
          )}
          <button onClick={() => setModalPreset('new')} className="text-sm text-coral-dark hover:text-coral">
            {t(lang, 'saveAsPreset')}
          </button>
        </section>

        {modalPreset && (
          <PresetEditorModal lang={lang} preset={modalPreset === 'new' ? null : modalPreset}
            initialDays={hours.map((h) => ({ dayOfWeek: h.dayOfWeek, startTime: h.startTime, endTime: h.endTime, isActive: h.isActive }))}
            initialBreaks={breaks.map((b) => ({ dayOfWeek: b.dayOfWeek, startTime: b.startTime, endTime: b.endTime }))}
            allPresets={presets}
            scheduledChange={scheduledChange ?? null}
            onClose={() => setModalPreset(null)} onSaved={refreshAfterPresetChange} />
        )}

        <section className="bg-surface border border-line rounded-2xl p-6">
          <h2 className="text-ink font-semibold text-lg mb-1">{t(lang, 'recurringBreaks')}</h2>
          <p className="text-muted text-sm mb-5">{t(lang, 'recurringBreaksReadOnlyHint')}</p>
          {breaks.length > 0 && (
            <div className="space-y-2">
              {breaks.map((br) => (
                <div key={br.id} className="flex items-center justify-between bg-cream rounded-lg px-4 py-2">
                  <span className="text-ink text-sm">{dayName(br.dayOfWeek)} · {br.startTime}–{br.endTime}</span>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="bg-surface border border-line rounded-2xl p-6">
          <h2 className="text-ink font-semibold text-lg mb-5">{t(lang, 'blockedDates')}</h2>
          {blocked.length > 0 && (
            <div className="space-y-2 mb-4">
              {blocked.map((b) => (
                <div key={b.id} className="flex items-center justify-between bg-cream rounded-lg px-4 py-2">
                  <span className="text-ink text-sm">
                    {b.date.slice(0, 10)}{b.startTime ? ` · ${b.startTime}–${b.endTime}` : ` · ${t(lang, 'fullDay')}`}{b.reason ? ` — ${b.reason}` : ''}
                  </span>
                  <button onClick={() => deleteBlocked(b.id)} className="text-red-600 hover:text-red-500 text-xs">{t(lang, 'remove')}</button>
                </div>
              ))}
            </div>
          )}
          <label className="flex items-center gap-2 text-ink text-sm cursor-pointer mb-3">
            <input type="checkbox" checked={blockRangeMode} onChange={(e) => setBlockRangeMode(e.target.checked)}
              className="accent-coral" />
            {t(lang, 'blockRangeToggle')}
          </label>
          <div className="flex flex-wrap items-center gap-3">
            <input type="date" value={newBlocked.date} onChange={(e) => setNewBlocked((p) => ({ ...p, date: e.target.value }))}
              className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm focus:outline-none" />
            {blockRangeMode && (
              <>
                <span className="text-muted text-sm">{t(lang, 'throughDate')}</span>
                <input type="date" value={rangeEndDate} onChange={(e) => setRangeEndDate(e.target.value)}
                  className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm focus:outline-none" />
              </>
            )}
            <label className="flex items-center gap-2 text-ink text-sm cursor-pointer">
              <input type="checkbox" checked={newBlocked.fullDay} onChange={(e) => setNewBlocked((p) => ({ ...p, fullDay: e.target.checked }))}
                className="accent-coral" />
              {t(lang, 'fullDay')}
            </label>
            {!newBlocked.fullDay && (
              <>
                <input type="time" value={newBlocked.startTime} onChange={(e) => setNewBlocked((p) => ({ ...p, startTime: e.target.value }))}
                  className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm focus:outline-none" />
                <span className="text-muted text-sm">{t(lang, 'timeTo')}</span>
                <input type="time" value={newBlocked.endTime} onChange={(e) => setNewBlocked((p) => ({ ...p, endTime: e.target.value }))}
                  className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm focus:outline-none" />
              </>
            )}
            <input type="text" placeholder={t(lang, 'reasonOptional')} value={newBlocked.reason}
              onChange={(e) => setNewBlocked((p) => ({ ...p, reason: e.target.value }))}
              className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm placeholder-muted focus:outline-none" />
            <button onClick={blockRangeMode ? addBlockedRange : addBlocked} className="bg-teal-tint hover:bg-teal-tint/70 text-ink text-sm font-medium px-4 py-2 rounded-lg transition-colors">
              {t(lang, blockRangeMode ? 'blockDateRange' : 'blockDate')}
            </button>
          </div>
        </section>
      </div>
    </div>
  )
}
