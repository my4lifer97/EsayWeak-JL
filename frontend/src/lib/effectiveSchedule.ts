import type { PresetSchedule, SchedulePreset } from '../components/admin/PresetEditorModal'

// Mirrors the backend's AvailabilityService.GetEffectiveDay: the live working hours/breaks are one
// weekly template that a scheduled range only overwrites once its start date arrives, so for any
// specific date the preset that really applies is resolved here instead:
//   - date inside the scheduled range      -> the scheduled preset
//   - date outside an already-running range -> the Default preset it reverts to
//   - otherwise                            -> null (use the live template)
// `dateStr` is "yyyy-MM-dd".
export function presetForDate(
  dateStr: string,
  scheduledChange: PresetSchedule | null | undefined,
  presets: SchedulePreset[],
): { preset: SchedulePreset; inRange: boolean } | null {
  if (!scheduledChange) return null
  const inRange = dateStr >= scheduledChange.startDate.slice(0, 10) && dateStr <= scheduledChange.endDate.slice(0, 10)
  const preset = inRange
    ? presets.find((p) => p.id === scheduledChange.presetId)
    : scheduledChange.applied ? presets.find((p) => p.isDefault) : undefined
  return preset ? { preset, inRange } : null
}
