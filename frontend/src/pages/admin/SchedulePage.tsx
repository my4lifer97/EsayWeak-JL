import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'

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
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [newBreak, setNewBreak] = useState({ dayOfWeek: 1, startTime: '12:00', endTime: '13:00' })
  const [newBlocked, setNewBlocked] = useState({ date: '', startTime: '', endTime: '', reason: '', fullDay: true })

  const { data } = useQuery<{ workingHours: WorkingHour[]; breaks: Break[]; blockedSlots: BlockedSlot[] }>({
    queryKey: ['schedule'],
    queryFn: () => api.get('/admin/schedule').then((r) => r.data),
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

  async function deleteBlocked(id: string) {
    await api.delete(`/admin/schedule/blocked/${id}`)
    setBlocked((prev) => prev.filter((b) => b.id !== id))
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-white mb-6">{t(lang, 'schedule')}</h1>
      <div className="space-y-8">
        <section className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
          <h2 className="text-white font-semibold text-lg mb-5">{t(lang, 'workingHours')}</h2>
          <div className="space-y-3">
            {hours.map((h, i) => (
              <div key={i} className="flex items-center gap-4">
                <label className="flex items-center gap-2 w-32 cursor-pointer">
                  <input type="checkbox" checked={h.isActive}
                    onChange={(e) => setHours((prev) => prev.map((x, j) => j === i ? { ...x, isActive: e.target.checked } : x))}
                    className="w-4 h-4 rounded accent-blue-500" />
                  <span className={h.isActive ? 'text-white' : 'text-gray-500'}>{dayName(i)}</span>
                </label>
                {h.isActive && (
                  <>
                    <input type="time" value={h.startTime}
                      onChange={(e) => setHours((prev) => prev.map((x, j) => j === i ? { ...x, startTime: e.target.value } : x))}
                      className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                    <span className="text-gray-500">{t(lang, 'timeTo')}</span>
                    <input type="time" value={h.endTime}
                      onChange={(e) => setHours((prev) => prev.map((x, j) => j === i ? { ...x, endTime: e.target.value } : x))}
                      className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                  </>
                )}
              </div>
            ))}
          </div>
          <button onClick={saveHours} disabled={saving}
            className="mt-5 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium px-5 py-2 rounded-lg transition-colors">
            {saved ? t(lang, 'saved') : saving ? t(lang, 'saving') : t(lang, 'saveWorkingHours')}
          </button>
        </section>

        <section className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
          <h2 className="text-white font-semibold text-lg mb-5">{t(lang, 'recurringBreaks')}</h2>
          {breaks.length > 0 && (
            <div className="space-y-2 mb-4">
              {breaks.map((br) => (
                <div key={br.id} className="flex items-center justify-between bg-gray-800/50 rounded-lg px-4 py-2">
                  <span className="text-gray-300 text-sm">{dayName(br.dayOfWeek)} · {br.startTime}–{br.endTime}</span>
                  <button onClick={() => deleteBreak(br.id)} className="text-red-400 hover:text-red-300 text-xs">{t(lang, 'remove')}</button>
                </div>
              ))}
            </div>
          )}
          <div className="flex flex-wrap items-center gap-3">
            <select value={newBreak.dayOfWeek} onChange={(e) => setNewBreak((p) => ({ ...p, dayOfWeek: Number(e.target.value) }))}
              className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none">
              {Array.from({ length: 7 }, (_, i) => <option key={i} value={i}>{dayName(i)}</option>)}
            </select>
            <input type="time" value={newBreak.startTime} onChange={(e) => setNewBreak((p) => ({ ...p, startTime: e.target.value }))}
              className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none" />
            <span className="text-gray-500 text-sm">{t(lang, 'timeTo')}</span>
            <input type="time" value={newBreak.endTime} onChange={(e) => setNewBreak((p) => ({ ...p, endTime: e.target.value }))}
              className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none" />
            <button onClick={addBreak} className="bg-gray-700 hover:bg-gray-600 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">
              {t(lang, 'addBreak')}
            </button>
          </div>
        </section>

        <section className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
          <h2 className="text-white font-semibold text-lg mb-5">{t(lang, 'blockedDates')}</h2>
          {blocked.length > 0 && (
            <div className="space-y-2 mb-4">
              {blocked.map((b) => (
                <div key={b.id} className="flex items-center justify-between bg-gray-800/50 rounded-lg px-4 py-2">
                  <span className="text-gray-300 text-sm">
                    {b.date.slice(0, 10)}{b.startTime ? ` · ${b.startTime}–${b.endTime}` : ` · ${t(lang, 'fullDay')}`}{b.reason ? ` — ${b.reason}` : ''}
                  </span>
                  <button onClick={() => deleteBlocked(b.id)} className="text-red-400 hover:text-red-300 text-xs">{t(lang, 'remove')}</button>
                </div>
              ))}
            </div>
          )}
          <div className="flex flex-wrap items-center gap-3">
            <input type="date" value={newBlocked.date} onChange={(e) => setNewBlocked((p) => ({ ...p, date: e.target.value }))}
              className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none" />
            <label className="flex items-center gap-2 text-gray-300 text-sm cursor-pointer">
              <input type="checkbox" checked={newBlocked.fullDay} onChange={(e) => setNewBlocked((p) => ({ ...p, fullDay: e.target.checked }))}
                className="accent-blue-500" />
              {t(lang, 'fullDay')}
            </label>
            {!newBlocked.fullDay && (
              <>
                <input type="time" value={newBlocked.startTime} onChange={(e) => setNewBlocked((p) => ({ ...p, startTime: e.target.value }))}
                  className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none" />
                <span className="text-gray-500 text-sm">{t(lang, 'timeTo')}</span>
                <input type="time" value={newBlocked.endTime} onChange={(e) => setNewBlocked((p) => ({ ...p, endTime: e.target.value }))}
                  className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none" />
              </>
            )}
            <input type="text" placeholder={t(lang, 'reasonOptional')} value={newBlocked.reason}
              onChange={(e) => setNewBlocked((p) => ({ ...p, reason: e.target.value }))}
              className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-500 focus:outline-none" />
            <button onClick={addBlocked} className="bg-gray-700 hover:bg-gray-600 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">
              {t(lang, 'blockDate')}
            </button>
          </div>
        </section>
      </div>
    </div>
  )
}
