import { useEffect, useMemo, useState } from 'react'
import { api } from '../../lib/api'
import { t } from '../../lib/i18n'

export type DayHours = { dayOfWeek: number; startTime: string; endTime: string; isActive: boolean }
export type PresetBreak = { dayOfWeek: number; startTime: string; endTime: string }
export type SchedulePreset = { id: string; name: string; createdAt: string; isDefault: boolean; days: DayHours[]; breaks: PresetBreak[] }
export type PresetChange = 'saved' | 'applied' | 'scheduled' | 'deleted'
export type PresetSchedule = { id: string; presetId: string; presetName: string; startDate: string; endDate: string; applied: boolean }

type Action = 'save' | 'apply' | 'range'

const dayName = (dayOfWeek: number, lang: string) =>
  new Date(2024, 0, 7 + dayOfWeek).toLocaleDateString(
    lang === 'AR' ? 'ar-SA' : lang === 'HE' ? 'he-IL' : 'en-US',
    { weekday: 'long' }
  )

// "HH:MM" strings are zero-padded, so plain string comparison orders them correctly.
const addMinutes = (time: string, minutes: number) => {
  const [h, m] = time.split(':').map(Number)
  const total = Math.min(h * 60 + m + minutes, 23 * 60 + 59)
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`
}

// A sensible default for a new break: lunch if it fits inside the day's hours, else the first half hour.
function suggestBreak(day: DayHours): PresetBreak {
  if (day.startTime <= '12:00' && day.endTime >= '13:00') return { dayOfWeek: day.dayOfWeek, startTime: '12:00', endTime: '13:00' }
  return { dayOfWeek: day.dayOfWeek, startTime: day.startTime, endTime: addMinutes(day.startTime, 30) }
}

const timeInput = (invalid: boolean) =>
  `bg-cream border rounded-lg px-2.5 py-1.5 text-ink text-sm focus:outline-none focus:ring-2 focus:ring-coral ${invalid ? 'border-red-500' : 'border-line'}`

function Toggle({ checked, onChange, label }: { checked: boolean; onChange: (v: boolean) => void; label: string }) {
  return (
    <button type="button" role="switch" aria-checked={checked} aria-label={label} onClick={() => onChange(!checked)}
      className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-coral ${checked ? 'bg-coral' : 'bg-line'}`}>
      <span className={`inline-block h-5 w-5 rounded-full bg-white shadow transition-transform ${checked ? 'translate-x-[22px] rtl:-translate-x-[22px]' : 'translate-x-0.5 rtl:-translate-x-0.5'}`} />
    </button>
  )
}

// The 7-day toggle + time-range grid, with each day's recurring breaks shown directly under it.
// Kept as a named export because it's the same editor the old editable Working Hours section used.
export function WeekHoursEditor({ lang, days, onChange, breaks, onBreaksChange }: {
  lang: string
  days: DayHours[]
  onChange: (days: DayHours[]) => void
  breaks?: PresetBreak[]
  onBreaksChange?: (breaks: PresetBreak[]) => void
}) {
  const updateDay = (dayOfWeek: number, patch: Partial<DayHours>) =>
    onChange(days.map((d) => d.dayOfWeek === dayOfWeek ? { ...d, ...patch } : d))

  const copyToAllOpenDays = (source: DayHours) =>
    onChange(days.map((d) => d.isActive ? { ...d, startTime: source.startTime, endTime: source.endTime } : d))

  const updateBreak = (index: number, patch: Partial<PresetBreak>) =>
    onBreaksChange?.((breaks ?? []).map((b, i) => i === index ? { ...b, ...patch } : b))

  return (
    <div className="space-y-2">
      {days.map((d) => {
        const hoursInvalid = d.isActive && d.endTime <= d.startTime
        const dayBreaks = (breaks ?? []).map((b, index) => ({ b, index })).filter(({ b }) => b.dayOfWeek === d.dayOfWeek)
        return (
          <div key={d.dayOfWeek} className={`rounded-xl border px-3 py-2.5 transition-colors ${d.isActive ? 'bg-surface border-line' : 'bg-cream/60 border-transparent'}`}>
            <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
              <div className="flex items-center gap-3 min-w-[9rem] flex-1">
                <Toggle checked={d.isActive} onChange={(v) => updateDay(d.dayOfWeek, { isActive: v })} label={dayName(d.dayOfWeek, lang)} />
                <span className={`text-sm font-medium ${d.isActive ? 'text-ink' : 'text-muted'}`}>{dayName(d.dayOfWeek, lang)}</span>
              </div>
              {d.isActive ? (
                <div className="flex items-center gap-2" dir="ltr">
                  <input type="time" value={d.startTime} aria-label={`${dayName(d.dayOfWeek, lang)} start`}
                    onChange={(e) => updateDay(d.dayOfWeek, { startTime: e.target.value })} className={timeInput(hoursInvalid)} />
                  <span className="text-muted text-sm">–</span>
                  <input type="time" value={d.endTime} aria-label={`${dayName(d.dayOfWeek, lang)} end`}
                    onChange={(e) => updateDay(d.dayOfWeek, { endTime: e.target.value })} className={timeInput(hoursInvalid)} />
                  <button type="button" onClick={() => copyToAllOpenDays(d)} title={t(lang, 'copyHoursToAll')} aria-label={t(lang, 'copyHoursToAll')}
                    className="w-8 h-8 flex items-center justify-center rounded-lg text-muted hover:text-ink hover:bg-cream transition-colors">
                    <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" className="w-4 h-4" aria-hidden="true">
                      <rect x="7" y="7" width="10" height="10" rx="2" /><path d="M13 7V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2" />
                    </svg>
                  </button>
                </div>
              ) : (
                <span className="text-muted text-sm">{t(lang, 'dayClosedLabel')}</span>
              )}
            </div>
            {hoursInvalid && <p className="text-red-600 text-xs mt-1.5">{t(lang, 'invalidTimeRange')}</p>}

            {d.isActive && onBreaksChange && (
              <div className="mt-2 ps-14 space-y-1.5">
                {dayBreaks.map(({ b, index }) => {
                  const breakInvalid = b.endTime <= b.startTime
                  return (
                    <div key={index}>
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="text-xs text-muted w-12">{t(lang, 'breakLabel')}</span>
                        <div className="flex items-center gap-2" dir="ltr">
                          <input type="time" value={b.startTime} onChange={(e) => updateBreak(index, { startTime: e.target.value })} className={timeInput(breakInvalid)} />
                          <span className="text-muted text-sm">–</span>
                          <input type="time" value={b.endTime} onChange={(e) => updateBreak(index, { endTime: e.target.value })} className={timeInput(breakInvalid)} />
                        </div>
                        <button type="button" onClick={() => onBreaksChange((breaks ?? []).filter((_, i) => i !== index))} aria-label={t(lang, 'remove')}
                          className="w-8 h-8 flex items-center justify-center rounded-lg text-muted hover:text-red-600 hover:bg-cream transition-colors">✕</button>
                      </div>
                      {breakInvalid && <p className="text-red-600 text-xs mt-1">{t(lang, 'invalidTimeRange')}</p>}
                    </div>
                  )
                })}
                <button type="button" onClick={() => onBreaksChange([...(breaks ?? []), suggestBreak(d)])}
                  className="text-xs font-medium text-coral-dark hover:text-coral">
                  {t(lang, 'addBreakShort')}
                </button>
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}

export default function PresetEditorModal({
  lang, preset, initialDays, initialBreaks, allPresets, scheduledChange, onClose, onSaved,
}: {
  lang: string
  preset: SchedulePreset | null // null = creating a brand-new preset
  initialDays: DayHours[] // used only when preset is null, pre-filled from the current live hours
  initialBreaks: PresetBreak[] // used only when preset is null, pre-filled from the current live breaks
  allPresets: SchedulePreset[] // full preset list, for "copy breaks from" -- excludes itself when editing
  scheduledChange: PresetSchedule | null // the business's one pending/running date-range schedule, if any
  onClose: () => void
  onSaved: (change: PresetChange) => Promise<void> // parent refetches presets + the live schedule
}) {
  const isDefault = preset?.isDefault ?? false
  const hasActiveRange = !!scheduledChange?.applied
  // This preset already has dates booked -- open straight onto them so they can be moved.
  const scheduledHere = !!preset && scheduledChange?.presetId === preset.id ? scheduledChange : null
  const scheduledElsewhere = scheduledChange && !scheduledHere ? scheduledChange : null
  const initial = useMemo(() => ({
    name: preset?.name ?? '',
    days: [...(preset?.days ?? initialDays)].sort((a, b) => a.dayOfWeek - b.dayOfWeek),
    breaks: preset?.breaks ?? initialBreaks,
  }), []) // eslint-disable-line react-hooks/exhaustive-deps -- snapshot taken once when the modal opens

  const [name, setName] = useState(initial.name)
  const [days, setDays] = useState<DayHours[]>(initial.days)
  const [breaks, setBreaks] = useState<PresetBreak[]>(initial.breaks)
  const [action, setAction] = useState<Action>(scheduledHere ? 'range' : 'save')
  const [rangeStart, setRangeStart] = useState(scheduledHere?.startDate ?? '')
  const [rangeEnd, setRangeEnd] = useState(scheduledHere?.endDate ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  // Id of the preset once it exists server-side -- set after the first successful create, so a
  // retry after a failed apply/schedule updates that preset instead of creating a duplicate.
  const [savedId, setSavedId] = useState<string | null>(preset?.id ?? null)
  const [savedSnapshot, setSavedSnapshot] = useState(JSON.stringify(initial))

  const copyCandidates = allPresets.filter((p) => p.id !== preset?.id)
  const isDirty = JSON.stringify({ name, days, breaks }) !== savedSnapshot
  const hasInvalidTimes =
    days.some((d) => d.isActive && d.endTime <= d.startTime) ||
    breaks.some((b) => days.find((d) => d.dayOfWeek === b.dayOfWeek)?.isActive && b.endTime <= b.startTime)

  function requestClose() {
    if (busy) return
    if (isDirty && !confirm(t(lang, 'discardChangesConfirm'))) return
    onClose()
  }

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') requestClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  function errorMessage(err: unknown, fallback: string) {
    return (err as { response?: { data?: { error?: string } } })?.response?.data?.error ?? fallback
  }

  // Persists the form (create or update) and returns the preset id. Apply/Schedule always run
  // through this first -- they act on the server-side copy, so unsaved edits would otherwise be
  // silently ignored.
  async function persist(): Promise<string> {
    const body = { name: isDefault ? 'Default' : name.trim(), days, breaks }
    let id = savedId
    if (id) {
      await api.put(`/admin/schedule/presets/${id}`, body)
    } else {
      const { data } = await api.post<SchedulePreset>('/admin/schedule/presets', body)
      id = data.id
      setSavedId(id)
    }
    setSavedSnapshot(JSON.stringify({ name, days, breaks }))
    return id
  }

  async function handleSubmit() {
    setError('')
    if (!isDefault && !name.trim()) { setError(t(lang, 'presetNameRequired')); return }
    if (hasInvalidTimes) { setError(t(lang, 'fixTimesFirst')); return }
    if (action === 'range' && (!rangeStart || !rangeEnd)) { setError(t(lang, 'pickDates')); return }

    setBusy(true)
    try {
      const id = await persist()
      let change: PresetChange = isDefault ? 'applied' : 'saved'
      if (action === 'apply') {
        await api.post(`/admin/schedule/presets/${id}/apply`)
        change = 'applied'
      } else if (action === 'range') {
        await api.post(`/admin/schedule/presets/${id}/schedule`, { startDate: rangeStart, endDate: rangeEnd })
        change = 'scheduled'
      }
      await onSaved(change)
      onClose()
    } catch (err) {
      setError(errorMessage(err, t(lang, 'somethingWentWrong')))
    } finally { setBusy(false) }
  }

  async function handleDelete() {
    if (!savedId || !confirm(t(lang, 'deletePresetConfirm'))) return
    setBusy(true); setError('')
    try {
      await api.delete(`/admin/schedule/presets/${savedId}`)
      await onSaved('deleted')
      onClose()
    } catch (err) {
      setError(errorMessage(err, t(lang, 'somethingWentWrong')))
    } finally { setBusy(false) }
  }

  const submitLabel = busy ? t(lang, 'saving')
    : isDefault ? t(lang, 'saveAndUpdateSchedule')
    : action === 'apply' ? t(lang, 'saveAndApplyNow')
    : action === 'range' ? t(lang, scheduledHere ? 'saveAndUpdateDates' : 'saveAndSchedule')
    : t(lang, 'saveChanges')

  const actionOptions: { value: Action; title: string; hint: string }[] = [
    { value: 'save', title: t(lang, 'presetActionSaveOnly'), hint: t(lang, scheduledHere ? 'presetActionSaveOnlyScheduledHint' : 'presetActionSaveOnlyHint') },
    { value: 'apply', title: t(lang, 'presetActionApplyNow'), hint: t(lang, 'applyPresetNowHint') },
    { value: 'range', title: t(lang, 'presetActionRange'), hint: t(lang, 'scheduleRangeHint') },
  ]

  return (
    <div onClick={requestClose} className="fixed inset-0 bg-black/60 flex items-end sm:items-center justify-center z-50 sm:p-4">
      <div onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true" aria-labelledby="preset-editor-title"
        className="bg-surface w-full sm:max-w-xl border border-line rounded-t-2xl sm:rounded-2xl max-h-[92vh] flex flex-col">

        <div className="flex justify-between items-center px-5 sm:px-6 py-4 border-b border-line">
          <div className="flex items-center gap-2">
            <h2 id="preset-editor-title" className="text-ink font-semibold text-lg">
              {isDefault ? t(lang, 'defaultPresetBadge') : t(lang, preset ? 'editPreset' : 'newPreset')}
            </h2>
            {isDirty && <span className="text-[11px] bg-coral-tint text-coral-dark px-2 py-0.5 rounded-full">{t(lang, 'unsavedBadge')}</span>}
          </div>
          <button onClick={requestClose} aria-label={t(lang, 'close')}
            className="text-muted hover:text-ink w-10 h-10 -me-2 flex items-center justify-center rounded-lg hover:bg-cream text-xl leading-none transition-colors">✕</button>
        </div>

        <div className="flex-1 overflow-y-auto px-5 sm:px-6 py-5 space-y-6">
          {isDefault ? (
            <p className="text-sm text-muted bg-cream rounded-lg px-4 py-3">
              {t(lang, hasActiveRange ? 'defaultPresetActiveRangeHint' : 'defaultPresetHint')}
            </p>
          ) : (
            <label className="block">
              <span className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'presetNameLabel')}</span>
              <input type="text" value={name} placeholder={t(lang, 'presetNamePlaceholder')} autoFocus={!preset}
                onChange={(e) => setName(e.target.value)}
                className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink text-sm placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral" />
            </label>
          )}

          <div>
            <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
              <h3 className="text-ink font-medium text-sm">{t(lang, 'workingHours')}</h3>
              {copyCandidates.length > 0 && (
                <select value="" onChange={(e) => {
                  const source = copyCandidates.find((p) => p.id === e.target.value)
                  if (source) setBreaks(source.breaks.map((b) => ({ ...b })))
                }}
                  className="bg-cream border border-line rounded-lg px-2.5 py-1.5 text-ink text-xs focus:outline-none focus:ring-2 focus:ring-coral">
                  <option value="">{t(lang, 'copyBreaksFrom')}</option>
                  {copyCandidates.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              )}
            </div>
            <WeekHoursEditor lang={lang} days={days} onChange={setDays} breaks={breaks} onBreaksChange={setBreaks} />
          </div>

          {!isDefault && (
            <fieldset>
              <legend className="text-ink font-medium text-sm mb-3">{t(lang, 'useThisSchedule')}</legend>
              <div className="space-y-2">
                {actionOptions.map((opt) => (
                  <label key={opt.value}
                    className={`block rounded-xl border px-4 py-3 cursor-pointer transition-colors ${action === opt.value ? 'border-coral bg-coral-tint/40' : 'border-line hover:bg-cream'}`}>
                    <div className="flex items-start gap-3">
                      <input type="radio" name="preset-action" value={opt.value} checked={action === opt.value}
                        onChange={() => setAction(opt.value)} className="mt-1 accent-coral" />
                      <div className="flex-1">
                        <span className="block text-sm font-medium text-ink">{opt.title}</span>
                        <span className="block text-xs text-muted mt-0.5">{opt.hint}</span>
                        {opt.value === 'apply' && action === 'apply' && hasActiveRange && (
                          <span className="block text-xs text-amber-700 dark:text-amber-400 mt-1">{t(lang, 'activeRangeApplyHint')}</span>
                        )}
                        {opt.value === 'range' && scheduledHere && (
                          <span className="block text-xs text-coral-dark font-medium mt-1">
                            {t(lang, scheduledHere.applied ? 'rangeRunningNow' : 'rangeBookedFor')} {scheduledHere.startDate} – {scheduledHere.endDate}
                          </span>
                        )}
                        {opt.value === 'range' && action === 'range' && scheduledElsewhere && (
                          <span className="block text-xs text-amber-700 dark:text-amber-400 mt-1">
                            {t(lang, 'replacesOtherRange')} "{scheduledElsewhere.presetName}" ({scheduledElsewhere.startDate} – {scheduledElsewhere.endDate})
                          </span>
                        )}
                        {opt.value === 'range' && action === 'range' && (
                          <div className="flex flex-wrap items-center gap-2 mt-2.5">
                            <input type="date" value={rangeStart} onChange={(e) => setRangeStart(e.target.value)}
                              className="bg-surface border border-line rounded-lg px-2.5 py-1.5 text-ink text-sm focus:outline-none focus:ring-2 focus:ring-coral" />
                            <span className="text-muted text-sm">{t(lang, 'throughDate')}</span>
                            <input type="date" value={rangeEnd} min={rangeStart || undefined} onChange={(e) => setRangeEnd(e.target.value)}
                              className="bg-surface border border-line rounded-lg px-2.5 py-1.5 text-ink text-sm focus:outline-none focus:ring-2 focus:ring-coral" />
                          </div>
                        )}
                      </div>
                    </div>
                  </label>
                ))}
              </div>
            </fieldset>
          )}
        </div>

        <div className="border-t border-line px-5 sm:px-6 py-4 space-y-3">
          {error && (
            <div role="alert" className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-3 py-2">
              {error}
            </div>
          )}
          <div className="flex items-center gap-2">
            {savedId && !isDefault && (
              <button onClick={handleDelete} disabled={busy}
                className="text-red-600 hover:text-red-500 text-sm px-2 py-2 disabled:opacity-50">
                {t(lang, 'deletePreset')}
              </button>
            )}
            <div className="flex-1" />
            <button onClick={requestClose} disabled={busy}
              className="text-sm text-ink px-4 py-2.5 rounded-lg hover:bg-cream disabled:opacity-50 transition-colors">
              {t(lang, 'cancel')}
            </button>
            <button onClick={handleSubmit} disabled={busy}
              className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold text-sm px-5 py-2.5 rounded-lg transition-colors">
              {submitLabel}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
