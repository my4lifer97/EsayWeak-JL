import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'

type Service = { id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number; price: number }
const EMPTY = { nameEn: '', nameAr: '', nameHe: '', durationMinutes: 30, price: '' }

export default function ServicesPage() {
  const queryClient = useQueryClient()
  const { language: lang } = useAuth()
  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<Service | null>(null)
  const [form, setForm] = useState(EMPTY)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const { data: services = [] } = useQuery<Service[]>({
    queryKey: ['services'],
    queryFn: () => api.get('/admin/services').then((r) => r.data),
  })

  function openCreate() { setEditing(null); setForm(EMPTY); setError(''); setShowForm(true) }
  function openEdit(s: Service) {
    setEditing(s)
    setForm({ nameEn: s.nameEn, nameAr: s.nameAr, nameHe: s.nameHe, durationMinutes: s.durationMinutes, price: String(s.price) })
    setError(''); setShowForm(true)
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault(); setLoading(true); setError('')
    try {
      const payload = { ...form, durationMinutes: Number(form.durationMinutes), price: Number(form.price) }
      if (editing) await api.patch(`/admin/services/${editing.id}`, payload)
      else await api.post('/admin/services', payload)
      setShowForm(false)
      queryClient.invalidateQueries({ queryKey: ['services'] })
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setError(msg ?? 'Failed to save')
    } finally { setLoading(false) }
  }

  async function handleDelete(id: string) {
    if (!confirm(t(lang, 'deleteConfirm'))) return
    await api.delete(`/admin/services/${id}`)
    queryClient.invalidateQueries({ queryKey: ['services'] })
  }

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-white">{t(lang, 'services')}</h1>
        <button onClick={openCreate} className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">
          {t(lang, 'addService')}
        </button>
      </div>

      {services.length === 0 ? (
        <div className="text-center text-gray-500 py-16">
          {t(lang, 'noServicesYet')} <button onClick={openCreate} className="text-blue-400 hover:text-blue-300">{t(lang, 'addFirstService')}</button>
        </div>
      ) : (
        <div className="grid gap-3">
          {services.map((s) => (
            <div key={s.id} className="bg-gray-900 border border-gray-800 rounded-xl px-5 py-4 flex items-center justify-between">
              <div>
                <div className="text-white font-medium">{s.nameEn}</div>
                <div className="text-gray-400 text-sm mt-0.5">{s.nameAr} · {s.nameHe}</div>
                <div className="text-gray-500 text-xs mt-1">{s.durationMinutes} min · ${s.price}</div>
              </div>
              <div className="flex gap-3">
                <button onClick={() => openEdit(s)} className="text-sm text-blue-400 hover:text-blue-300">{t(lang, 'edit')}</button>
                <button onClick={() => handleDelete(s.id)} className="text-sm text-red-400 hover:text-red-300">{t(lang, 'delete')}</button>
              </div>
            </div>
          ))}
        </div>
      )}

      {showForm && (
        <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
          <div className="bg-gray-900 rounded-2xl p-6 w-full max-w-md border border-gray-800">
            <div className="flex justify-between items-center mb-5">
              <h2 className="text-white font-semibold text-lg">{editing ? t(lang, 'editServiceTitle') : t(lang, 'addServiceTitle')}</h2>
              <button onClick={() => setShowForm(false)} className="text-gray-500 hover:text-white text-xl">✕</button>
            </div>
            {error && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}
            <form onSubmit={handleSubmit} className="space-y-4">
              {[['Name (English)', 'nameEn'], ['Name (Arabic)', 'nameAr'], ['Name (Hebrew)', 'nameHe']].map(([label, key]) => (
                <div key={key}>
                  <label className="block text-sm font-medium text-gray-300 mb-1.5">{label}</label>
                  <input type="text" required value={form[key as keyof typeof form]}
                    onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
              ))}
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-1.5">Duration (min)</label>
                  <select value={form.durationMinutes} onChange={(e) => setForm((f) => ({ ...f, durationMinutes: Number(e.target.value) }))}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500">
                    {[15, 30, 45, 60, 75, 90, 120].map((v) => <option key={v} value={v}>{v} min</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-1.5">Price</label>
                  <input type="number" required min="0" step="0.01" value={form.price}
                    onChange={(e) => setForm((f) => ({ ...f, price: e.target.value }))} placeholder="25.00"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
              </div>
              <button type="submit" disabled={loading}
                className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold py-2.5 rounded-lg transition-colors mt-2">
                {loading ? t(lang, 'saving') : t(lang, 'saveService')}
              </button>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
