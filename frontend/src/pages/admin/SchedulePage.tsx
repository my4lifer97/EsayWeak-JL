import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'

type WorkingHour = { id?: string; dayOfWeek: number; startTime: string; endTime: string; isActive: boolean }
type Break = { id: string; dayOfWeek: number; startTime: string; endTime: string }
type BlockedSlot = { id: string; date: string; startTime: string | null; endTime: string | null; reason: string | null }
type WorkingHoursOverrideRow = { id: string; date: string; startTime: string; endTime: string; isActive: boolean }
type SchedulePresetDay = { dayOfWeek: number; startTime: string; endTime: string; isActive: boolean }
type SchedulePreset = { id: string; name: string; createdAt: string; days: SchedulePresetDay[] }

export default function SchedulePage() {
  const queryClient = useQueryClient()
  const { language: lang } = useAuth()

  const dayName = (i: number) =>
    new Date(2024, 0, 7 + i).toLocaleDateString(
      lang === 'AR' ? 'ar-SA' : lang === 'HE' ? 'he-IL' : 'en-US',
      { weekday: 'long' }
    )
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [newBreak, setNewBreak] = useState({ dayOfWeek: 1, startTime: '12:00', endTime: '13:00' })
  const [newBlocked, setNewBlocked] = useState({ date: '', startTime: '', endTime: '', reason: '', fullDay: true })
  const [blockRangeMode, setBlockRangeMode] = useState(false)
  const [rangeEndDate, setRangeEndDate] = useState('')
  const [newException, setNewException] = useState({ date: '', startTime: '09:00', endTime: '18:00' })
  const [newPresetName, setNewPresetName] = useState('')
  const [showPresetInput, setShowPresetInput] = useState(false)

  const { data } = useQuery<{ workingHours: WorkingHour[]; breaks: Break[]; blockedSlots: BlockedSlot[]; overrides: WorkingHoursOverrideRow[] }>({
    queryKey: ['schedule'],
    queryFn: () => api.get('/admin/schedule').then((r) => r.data),
  })

  const { data: presets = [] } = useQuery<SchedulePreset[]>({
    queryKey: ['schedule-presets'],
    queryFn: () => api.get('/admin/schedule/presets').then((r) => r.data),
  })

  const initHours = Array.from({ length: 7 }, (_, i) => {
    const existing = data?.workingHours.find((h) => h.dayOfWeek === i)
    return existing ?? { dayOfWeek: i, startTime: '09:00', endTime: '18:00', isActive: false }
  })
  const [hours, setHours] = useState<WorkingHour[]>(initHours)
  const [breaks, setBreaks] = useState<Break[]>(data?.breaks ?? [])
  const [blocked, setBlocked] = useState<BlockedSlot[]>(data?.blockedSlots ?? [])
  const [overrides, setOverrides] = useState<WorkingHoursOverrideRow[]>(data?.overrides ?? [])

  // Sync state when data loads
  if (data && hours.every((h) => !h.id) && data.workingHours.length > 0) {
    const synced = Array.from({ length: 7 }, (_, i) => data.workingHours.find((h) => h.dayOfWeek === i) ?? { dayOfWeek: i, startTime: '09:00', endTime: '18:00', isActive: false })
    if (JSON.stringify(synced) !== JSON.stringify(hours)) {
      setHours(synced)
      setBreaks(data.breaks)
      setBlocked(data.blockedSlots)
      setOverrides(data.overrides)
    }
  }

  async function saveHours() {
    setSaving(true)
    await api.post('/admin/schedule', hours)
    setSaving(false); setSaved(true)
    setTimeout(() => setSaved(false), 2000)
    queryClient.invalidateQueries({ queryKey: ['schedule'] })
  }

  async function addBreak() {
    const { data: br } = await api.post('/admin/schedule/breaks', newBreak)
    setBreaks((prev) => [...prev, br])
  }

  async function deleteBreak(id: string) {
    await api.delete(`/admin/schedule/breaks/${id}`)
    setBreaks((prev) => prev.filter((b) => b.id !== id))
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

  async function addException() {
    if (!newException.date) return
    // Reuses the WorkingHoursOverride upsert endpoint -- an exception day is just an override
    // that's always active/open; AvailabilityService treats an active override as superseding
    // any BlockedSlot for that date, which is what reopens it within a blocked range.
    const { data: ovr } = await api.post('/admin/schedule/overrides', {
      date: newException.date,
      startTime: newException.startTime,
      endTime: newException.endTime,
      isActive: true,
    })
    setOverrides((prev) => [...prev.filter((o) => o.date !== ovr.date), ovr])
    setNewException((p) => ({ ...p, date: '' }))
  }

  async function deleteException(id: string) {
    await api.delete(`/admin/schedule/overrides/${id}`)
    setOverrides((prev) => prev.filter((o) => o.id !== id))
  }

  async function savePreset() {
    if (!newPresetName.trim()) return
    await api.post('/admin/schedule/presets', { name: newPresetName.trim() })
    setNewPresetName(''); setShowPresetInput(false)
    queryClient.invalidateQueries({ queryKey: ['schedule-presets'] })
  }

  async function applyPreset(id: string) {
    if (!confirm(t(lang, 'applyPresetConfirm'))) return
    await api.post(`/admin/schedule/presets/${id}/apply`)
    // The Working Hours section above only syncs from the query once (to avoid clobbering
    // in-progress edits), so re-fetch and push the applied hours into it directly here rather
    // than relying on that one-time sync to notice the change.
    const { data: fresh } = await api.get('/admin/schedule')
    setHours(Array.from({ length: 7 }, (_, i) => fresh.workingHours.find((h: WorkingHour) => h.dayOfWeek === i) ?? { dayOfWeek: i, startTime: '09:00', endTime: '18:00', isActive: false }))
    queryClient.invalidateQueries({ queryKey: ['schedule'] })
  }

  async function deletePreset(id: string) {
    await api.delete(`/admin/schedule/presets/${id}`)
    queryClient.invalidateQueries({ queryKey: ['schedule-presets'] })
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-ink mb-6">{t(lang, 'schedule')}</h1>
      <div className="space-y-8">
        <section className="bg-surface border border-line rounded-2xl p-6">
          <h2 className="text-ink font-semibold text-lg mb-5">{t(lang, 'workingHours')}</h2>
          <div className="space-y-3">
            {hours.map((h, i) => (
              <div key={i} className="flex items-center gap-4">
                <label className="flex items-center gap-2 w-32 cursor-pointer">
                  <input type="checkbox" checked={h.isActive}
                    onChange={(e) => setHours((prev) => prev.map((x, j) => j === i ? { ...x, isActive: e.target.checked } : x))}
                    className="w-4 h-4 rounded accent-coral" />
                  <span className={h.isActive ? 'text-ink' : 'text-muted'}>{dayName(i)}</span>
                </label>
                {h.isActive && (
                  <>
                    <input type="time" value={h.startTime}
                      onChange={(e) => setHours((prev) => prev.map((x, j) => j === i ? { ...x, startTime: e.target.value } : x))}
                      className="bg-cream border border-line rounded-lg px-3 py-1.5 text-ink text-sm focus:outline-none focus:ring-2 focus:ring-coral" />
                    <span className="text-muted">{t(lang, 'timeTo')}</span>
                    <input type="time" value={h.endTime}
                      onChange={(e) => setHours((prev) => prev.map((x, j) => j === i ? { ...x, endTime: e.target.value } : x))}
                      className="bg-cream border border-line rounded-lg px-3 py-1.5 text-ink text-sm focus:outline-none focus:ring-2 focus:ring-coral" />
                  </>
                )}
              </div>
            ))}
          </div>
          <button onClick={saveHours} disabled={saving}
            className="mt-5 bg-coral hover:bg-coral-dark disabled:opacity-50 text-white text-sm font-medium px-5 py-2 rounded-lg transition-colors">
            {saved ? t(lang, 'saved') : saving ? t(lang, 'saving') : t(lang, 'saveWorkingHours')}
          </button>
        </section>

        <section className="bg-surface border border-line rounded-2xl p-6">
          <h2 className="text-ink font-semibold text-lg mb-1">{t(lang, 'schedulePresets')}</h2>
          <p className="text-muted text-sm mb-5">{t(lang, 'schedulePresetsHint')}</p>
          {presets.length > 0 && (
            <div className="space-y-2 mb-4">
              {presets.map((p) => (
                <div key={p.id} className="flex items-center justify-between bg-cream rounded-lg px-4 py-2">
                  <span className="text-ink text-sm">{p.name}</span>
                  <div className="flex items-center gap-3">
                    <button onClick={() => applyPreset(p.id)} className="text-coral-dark hover:text-coral text-xs font-medium">{t(lang, 'applyPreset')}</button>
                    <button onClick={() => deletePreset(p.id)} className="text-red-600 hover:text-red-500 text-xs">{t(lang, 'remove')}</button>
                  </div>
                </div>
              ))}
            </div>
          )}
          {showPresetInput ? (
            <div className="flex flex-wrap items-center gap-3">
              <input type="text" value={newPresetName} placeholder={t(lang, 'presetNamePlaceholder')}
                onChange={(e) => setNewPresetName(e.target.value)}
                className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm placeholder-muted focus:outline-none" />
              <button onClick={savePreset} className="bg-teal-tint hover:bg-teal-tint/70 text-ink text-sm font-medium px-4 py-2 rounded-lg transition-colors">
                {t(lang, 'saveChanges')}
              </button>
              <button onClick={() => { setShowPresetInput(false); setNewPresetName('') }} className="text-muted hover:text-ink text-sm">
                {t(lang, 'back')}
              </button>
            </div>
          ) : (
            <button onClick={() => setShowPresetInput(true)} className="text-sm text-coral-dark hover:text-coral">
              {t(lang, 'saveAsPreset')}
            </button>
          )}
        </section>

        <section className="bg-surface border border-line rounded-2xl p-6">
          <h2 className="text-ink font-semibold text-lg mb-5">{t(lang, 'recurringBreaks')}</h2>
          {breaks.length > 0 && (
            <div className="space-y-2 mb-4">
              {breaks.map((br) => (
                <div key={br.id} className="flex items-center justify-between bg-cream rounded-lg px-4 py-2">
                  <span className="text-ink text-sm">{dayName(br.dayOfWeek)} · {br.startTime}–{br.endTime}</span>
                  <button onClick={() => deleteBreak(br.id)} className="text-red-600 hover:text-red-500 text-xs">{t(lang, 'remove')}</button>
                </div>
              ))}
            </div>
          )}
          <div className="flex flex-wrap items-center gap-3">
            <select value={newBreak.dayOfWeek} onChange={(e) => setNewBreak((p) => ({ ...p, dayOfWeek: Number(e.target.value) }))}
              className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm focus:outline-none">
              {Array.from({ length: 7 }, (_, i) => <option key={i} value={i}>{dayName(i)}</option>)}
            </select>
            <input type="time" value={newBreak.startTime} onChange={(e) => setNewBreak((p) => ({ ...p, startTime: e.target.value }))}
              className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm focus:outline-none" />
            <span className="text-muted text-sm">{t(lang, 'timeTo')}</span>
            <input type="time" value={newBreak.endTime} onChange={(e) => setNewBreak((p) => ({ ...p, endTime: e.target.value }))}
              className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm focus:outline-none" />
            <button onClick={addBreak} className="bg-teal-tint hover:bg-teal-tint/70 text-ink text-sm font-medium px-4 py-2 rounded-lg transition-colors">
              {t(lang, 'addBreak')}
            </button>
          </div>
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

          <div className="border-t border-line mt-5 pt-4">
            <h3 className="text-ink font-medium text-sm mb-1">{t(lang, 'scheduleExceptions')}</h3>
            <p className="text-muted text-xs mb-3">{t(lang, 'scheduleExceptionsHint')}</p>
            {overrides.length > 0 && (
              <div className="space-y-2 mb-3">
                {overrides.map((o) => (
                  <div key={o.id} className="flex items-center justify-between bg-cream rounded-lg px-4 py-2">
                    <span className="text-ink text-sm">{o.date} · {o.startTime}–{o.endTime}</span>
                    <button onClick={() => deleteException(o.id)} className="text-red-600 hover:text-red-500 text-xs">{t(lang, 'remove')}</button>
                  </div>
                ))}
              </div>
            )}
            <div className="flex flex-wrap items-center gap-3">
              <input type="date" value={newException.date} onChange={(e) => setNewException((p) => ({ ...p, date: e.target.value }))}
                className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm focus:outline-none" />
              <input type="time" value={newException.startTime} onChange={(e) => setNewException((p) => ({ ...p, startTime: e.target.value }))}
                className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm focus:outline-none" />
              <span className="text-muted text-sm">{t(lang, 'timeTo')}</span>
              <input type="time" value={newException.endTime} onChange={(e) => setNewException((p) => ({ ...p, endTime: e.target.value }))}
                className="bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm focus:outline-none" />
              <button onClick={addException} className="bg-teal-tint hover:bg-teal-tint/70 text-ink text-sm font-medium px-4 py-2 rounded-lg transition-colors">
                {t(lang, 'addException')}
              </button>
            </div>
          </div>
        </section>
      </div>
    </div>
  )
}
