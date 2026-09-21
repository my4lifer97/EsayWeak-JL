import { useState } from 'react'
import { api } from '../../lib/api'
import { t } from '../../lib/i18n'

export type DayHours = { dayOfWeek: number; startTime: string; endTime: string; isActive: boolean }
export type PresetBreak = { dayOfWeek: number; startTime: string; endTime: string }
export type SchedulePreset = { id: string; name: string; createdAt: string; isDefault: boolean; days: DayHours[]; breaks: PresetBreak[] }

const dayName = (dayOfWeek: number, lang: string) =>
  new Date(2024, 0, 7 + dayOfWeek).toLocaleDateString(
    lang === 'AR' ? 'ar-SA' : lang === 'HE' ? 'he-IL' : 'en-US',
    { weekday: 'long' }
  )

// The same 7-day toggle+time-range grid used by both the main Working Hours section and this
// modal -- the preset editor is meant to feel identical to editing the real weekly template.
export function WeekHoursEditor({ lang, days, onChange }: { lang: string; days: DayHours[]; onChange: (days: DayHours[]) => void }) {
  return (
    <div className="space-y-3">
      {days.map((h, i) => (
        <div key={h.dayOfWeek} className="flex items-center gap-4">
          <label className="flex items-center gap-2 w-32 cursor-pointer">
            <input type="checkbox" checked={h.isActive}
              onChange={(e) => onChange(days.map((x, j) => j === i ? { ...x, isActive: e.target.checked } : x))}
              className="w-4 h-4 rounded accent-coral" />
            <span className={h.isActive ? 'text-ink' : 'text-muted'}>{dayName(h.dayOfWeek, lang)}</span>
          </label>
          {h.isActive && (
            <>
              <input type="time" value={h.startTime}
                onChange={(e) => onChange(days.map((x, j) => j === i ? { ...x, startTime: e.target.value } : x))}
                className="bg-cream border border-line rounded-lg px-3 py-1.5 text-ink text-sm focus:outline-none focus:ring-2 focus:ring-coral" />
              <span className="text-muted">{t(lang, 'timeTo')}</span>
              <input type="time" value={h.endTime}
                onChange={(e) => onChange(days.map((x, j) => j === i ? { ...x, endTime: e.target.value } : x))}
                className="bg-cream border border-line rounded-lg px-3 py-1.5 text-ink text-sm focus:outline-none focus:ring-2 focus:ring-coral" />
            </>
          )}
        </div>
      ))}
    </div>
  )
}

export default function PresetEditorModal({
  lang, preset, initialDays, initialBreaks, allPresets, onClose, onSaved,
}: {
  lang: string
  preset: SchedulePreset | null // null = creating a brand-new preset
  initialDays: DayHours[] // used only when preset is null, pre-filled from the current live hours
  initialBreaks: PresetBreak[] // used only when preset is null, pre-filled from the current live breaks
  allPresets: SchedulePreset[] // full preset list, for "copy breaks from" -- excludes itself when editing
  onClose: () => void
  onSaved: () => void // parent refetches presets + the live schedule
}) {
  const [name, setName] = useState(preset?.name ?? '')
  const [days, setDays] = useState<DayHours[]>(preset?.days ?? initialDays)
  const [breaks, setBreaks] = useState<PresetBreak[]>(preset?.breaks ?? initialBreaks)
  const [newBreak, setNewBreak] = useState({ dayOfWeek: 1, startTime: '12:00', endTime: '13:00' })
  const [copyFromId, setCopyFromId] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [rangeStart, setRangeStart] = useState('')
  const [rangeEnd, setRangeEnd] = useState('')
  const [scheduling, setScheduling] = useState(false)
  const [scheduleError, setScheduleError] = useState('')

  const copyCandidates = allPresets.filter((p) => p.id !== preset?.id)

  function errorMessage(err: unknown, fallback: string) {
    return (err as { response?: { data?: { error?: string } } })?.response?.data?.error ?? fallback
  }

  function copyBreaksFrom() {
    const source = copyCandidates.find((p) => p.id === copyFromId)
    if (!source) return
    setBreaks(source.breaks.map((b) => ({ ...b })))
  }

  function addBreak() {
    setBreaks((prev) => [...prev, { ...newBreak }])
  }

  function removeBreak(index: number) {
    setBreaks((prev) => prev.filter((_, i) => i !== index))
  }

  async function handleSave() {
    if (!preset?.isDefault && !name.trim()) { setError(t(lang, 'presetNameRequired')); return }
    setSaving(true); setError('')
    try {
      if (preset) {
        await api.put(`/admin/schedule/presets/${preset.id}`, { name: preset.isDefault ? 'Default' : name.trim(), days, breaks })
      } else {
        await api.post('/admin/schedule/presets', { name: name.trim(), days, breaks })
      }
      onSaved(); onClose()
    } catch (err) {
      setError(errorMessage(err, 'Failed to save'))
    } finally { setSaving(false) }
  }

  async function handleApplyNow() {
    if (!preset) return
    setSaving(true); setError('')
    try {
      await api.post(`/admin/schedule/presets/${preset.id}/apply`)
      onSaved(); onClose()
    } catch (err) {
      setError(errorMessage(err, 'Failed to apply'))
    } finally { setSaving(false) }
  }

  async function handleScheduleRange() {
    if (!preset || !rangeStart || !rangeEnd) return
    setScheduling(true); setScheduleError('')
    try {
      await api.post(`/admin/schedule/presets/${preset.id}/schedule`, { startDate: rangeStart, endDate: rangeEnd })
      onSaved(); onClose()
    } catch (err) {
      setScheduleError(errorMessage(err, 'Failed to schedule'))
    } finally { setScheduling(false) }
  }

  async function handleDelete() {
    if (!preset) return
    if (!confirm(t(lang, 'deletePresetConfirm'))) return
    setSaving(true); setError('')
    try {
      await api.delete(`/admin/schedule/presets/${preset.id}`)
      onSaved(); onClose()
    } catch (err) {
      setError(errorMessage(err, 'Failed to delete'))
    } finally { setSaving(false) }
  }

  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div onClick={(e) => e.stopPropagation()} className="bg-surface rounded-2xl p-6 w-full max-w-lg border border-line max-h-[90vh] overflow-y-auto">
        <div className="flex justify-between items-center mb-5">
          <h2 className="text-ink font-semibold text-lg">{t(lang, preset ? 'editPreset' : 'newPreset')}</h2>
          <button onClick={onClose} aria-label="Close"
            className="text-muted hover:text-ink w-11 h-11 -m-2 flex items-center justify-center rounded-lg hover:bg-cream text-2xl leading-none transition-colors">✕</button>
        </div>
        {error && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}

        <div className="space-y-4">
          {!preset?.isDefault && (
            <input type="text" value={name} placeholder={t(lang, 'presetNamePlaceholder')}
              onChange={(e) => setName(e.target.value)}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm placeholder-muted focus:outline-none" />
          )}

          <WeekHoursEditor lang={lang} days={days} onChange={setDays} />

          <div className="border-t border-line pt-4 space-y-3">
            <h3 className="text-ink font-medium text-sm">{t(lang, 'recurringBreaks')}</h3>

            {breaks.length > 0 && (
              <div className="space-y-2">
                {breaks.map((br, i) => (
                  <div key={i} className="flex items-center justify-between bg-cream rounded-lg px-4 py-2">
                    <span className="text-ink text-sm">{dayName(br.dayOfWeek, lang)} · {br.startTime}–{br.endTime}</span>
                    <button onClick={() => removeBreak(i)} className="text-red-600 hover:text-red-500 text-xs">{t(lang, 'remove')}</button>
                  </div>
                ))}
              </div>
            )}

            {copyCandidates.length > 0 && (
              <div className="flex flex-wrap items-center gap-2">
                <select value={copyFromId} onChange={(e) => setCopyFromId(e.target.value)}
                  className="bg-cream border border-line rounded-lg px-3 py-1.5 text-ink text-sm focus:outline-none">
                  <option value="">{t(lang, 'copyBreaksFrom')}</option>
                  {copyCandidates.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
                <button onClick={copyBreaksFrom} disabled={!copyFromId}
                  className="bg-teal-tint hover:bg-teal-tint/70 disabled:opacity-50 text-ink text-sm font-medium px-3 py-1.5 rounded-lg transition-colors">
                  {t(lang, 'copyBreaksButton')}
                </button>
              </div>
            )}

            <div className="flex flex-wrap items-center gap-2">
              <select value={newBreak.dayOfWeek} onChange={(e) => setNewBreak((p) => ({ ...p, dayOfWeek: Number(e.target.value) }))}
                className="bg-cream border border-line rounded-lg px-3 py-1.5 text-ink text-sm focus:outline-none">
                {Array.from({ length: 7 }, (_, i) => <option key={i} value={i}>{dayName(i, lang)}</option>)}
              </select>
              <input type="time" value={newBreak.startTime} onChange={(e) => setNewBreak((p) => ({ ...p, startTime: e.target.value }))}
                className="bg-cream border border-line rounded-lg px-3 py-1.5 text-ink text-sm focus:outline-none" />
              <span className="text-muted text-sm">{t(lang, 'timeTo')}</span>
              <input type="time" value={newBreak.endTime} onChange={(e) => setNewBreak((p) => ({ ...p, endTime: e.target.value }))}
                className="bg-cream border border-line rounded-lg px-3 py-1.5 text-ink text-sm focus:outline-none" />
              <button onClick={addBreak} className="bg-teal-tint hover:bg-teal-tint/70 text-ink text-sm font-medium px-3 py-1.5 rounded-lg transition-colors">
                {t(lang, 'addBreak')}
              </button>
            </div>
          </div>

          <button onClick={handleSave} disabled={saving}
            className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold py-2.5 rounded-lg transition-colors">
            {saving ? t(lang, 'saving') : t(lang, 'saveChanges')}
          </button>

          {preset && !preset.isDefault && (
            <div className="border-t border-line pt-4 space-y-3">
              <h3 className="text-ink font-medium text-sm">{t(lang, 'useThisSchedule')}</h3>

              <div>
                <button onClick={handleApplyNow} disabled={saving}
                  className="text-sm text-coral-dark hover:text-coral font-medium disabled:opacity-50">
                  {t(lang, 'applyPresetNow')}
                </button>
                <p className="text-muted text-xs mt-0.5">{t(lang, 'applyPresetNowHint')}</p>
              </div>

              <div>
                <div className="flex flex-wrap items-center gap-2 mb-1">
                  <input type="date" value={rangeStart} onChange={(e) => setRangeStart(e.target.value)}
                    className="bg-cream border border-line rounded-lg px-3 py-1.5 text-ink text-sm focus:outline-none" />
                  <span className="text-muted text-sm">{t(lang, 'throughDate')}</span>
                  <input type="date" value={rangeEnd} onChange={(e) => setRangeEnd(e.target.value)}
                    className="bg-cream border border-line rounded-lg px-3 py-1.5 text-ink text-sm focus:outline-none" />
                  <button onClick={handleScheduleRange} disabled={scheduling || !rangeStart || !rangeEnd}
                    className="bg-teal-tint hover:bg-teal-tint/70 disabled:opacity-50 text-ink text-sm font-medium px-3 py-1.5 rounded-lg transition-colors">
                    {t(lang, 'scheduleRangeButton')}
                  </button>
                </div>
                <p className="text-muted text-xs">{t(lang, 'scheduleRangeHint')}</p>
                {scheduleError && <p className="text-red-600 text-xs mt-1">{scheduleError}</p>}
              </div>

              <button onClick={handleDelete} disabled={saving}
                className="text-red-600 hover:text-red-500 text-sm disabled:opacity-50">
                {t(lang, 'deletePreset')}
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
