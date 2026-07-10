import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'

type GalleryPhoto = { id: string; url: string }
type PhotoMode = 'None' | 'OwnerGallery' | 'CustomerUpload'
type Service = {
  id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number; price: number
  photoMode: PhotoMode; galleryPhotos: GalleryPhoto[]
}
const EMPTY = { nameEn: '', nameAr: '', nameHe: '', durationMinutes: 30, price: '', photoMode: 'None' as PhotoMode }

export default function ServicesPage() {
  const queryClient = useQueryClient()
  const { language: lang } = useAuth()
  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<Service | null>(null)
  const [form, setForm] = useState(EMPTY)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [uploadingGallery, setUploadingGallery] = useState(false)
  const [galleryError, setGalleryError] = useState('')

  const { data: services = [] } = useQuery<Service[]>({
    queryKey: ['services'],
    queryFn: () => api.get('/admin/services').then((r) => r.data),
  })

  // Keep the modal's gallery list in sync with the latest fetched data while editing.
  const editingService = editing ? services.find((s) => s.id === editing.id) ?? editing : null

  function openCreate() { setEditing(null); setForm(EMPTY); setError(''); setGalleryError(''); setShowForm(true) }
  function openEdit(s: Service) {
    setEditing(s)
    setForm({ nameEn: s.nameEn, nameAr: s.nameAr, nameHe: s.nameHe, durationMinutes: s.durationMinutes, price: String(s.price), photoMode: s.photoMode })
    setError(''); setGalleryError(''); setShowForm(true)
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault(); setLoading(true); setError('')
    try {
      const payload = { ...form, durationMinutes: Number(form.durationMinutes), price: Number(form.price) }
      if (editing) {
        const { data } = await api.patch(`/admin/services/${editing.id}`, payload)
        setEditing(data)
      } else {
        const { data } = await api.post('/admin/services', payload)
        setEditing(data)
      }
      // Keep the modal open when gallery photos still need to be added; otherwise close it,
      // matching the pre-photo-feature behavior of closing right after a successful save.
      if (form.photoMode !== 'OwnerGallery') setShowForm(false)
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

  async function handleGalleryUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file || !editing) return
    setUploadingGallery(true); setGalleryError('')
    try {
      const formData = new FormData()
      formData.append('file', file)
      await api.post(`/admin/services/${editing.id}/gallery`, formData)
      queryClient.invalidateQueries({ queryKey: ['services'] })
    } catch {
      setGalleryError(t(lang, 'photoUploadError'))
    } finally { setUploadingGallery(false) }
  }

  async function handleGalleryDelete(photoId: string) {
    if (!editing || !confirm(t(lang, 'deletePhotoConfirm'))) return
    await api.delete(`/admin/services/${editing.id}/gallery/${photoId}`)
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
                <div className="text-gray-500 text-xs mt-1">
                  <span dir="ltr">{s.durationMinutes} min · ₪{s.price}</span>
                </div>
                {s.photoMode !== 'None' && (
                  <div className="text-xs text-blue-400 mt-1">
                    {s.photoMode === 'OwnerGallery' ? t(lang, 'photoModeGallery') : t(lang, 'photoModeUpload')}
                  </div>
                )}
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
          <div className="bg-gray-900 rounded-2xl p-6 w-full max-w-md border border-gray-800 max-h-[90vh] overflow-y-auto">
            <div className="flex justify-between items-center mb-5">
              <h2 className="text-white font-semibold text-lg">{editing ? t(lang, 'editServiceTitle') : t(lang, 'addServiceTitle')}</h2>
              <button onClick={() => setShowForm(false)} className="text-gray-500 hover:text-white text-xl">✕</button>
            </div>
            {error && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}
            <form onSubmit={handleSubmit} className="space-y-4">
              {[['Name (English)', 'nameEn'], ['Name (Arabic)', 'nameAr'], ['Name (Hebrew)', 'nameHe']].map(([label, key]) => (
                <div key={key}>
                  <label htmlFor={`service-${key}`} className="block text-sm font-medium text-gray-300 mb-1.5">{label}</label>
                  <input id={`service-${key}`} type="text" required value={form[key as keyof typeof form]}
                    onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
              ))}
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label htmlFor="service-duration" className="block text-sm font-medium text-gray-300 mb-1.5">Duration (min)</label>
                  <select id="service-duration" value={form.durationMinutes} onChange={(e) => setForm((f) => ({ ...f, durationMinutes: Number(e.target.value) }))}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500">
                    {[15, 30, 45, 60, 75, 90, 120].map((v) => <option key={v} value={v}>{v} min</option>)}
                  </select>
                </div>
                <div>
                  <label htmlFor="service-price" className="block text-sm font-medium text-gray-300 mb-1.5">Price</label>
                  <input id="service-price" type="number" required min="0" step="0.01" value={form.price}
                    onChange={(e) => setForm((f) => ({ ...f, price: e.target.value }))} placeholder="25.00"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
              </div>
              <div>
                <label htmlFor="service-photo-mode" className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'photoMode')}</label>
                <select id="service-photo-mode" value={form.photoMode}
                  onChange={(e) => setForm((f) => ({ ...f, photoMode: e.target.value as PhotoMode }))}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500">
                  <option value="None">{t(lang, 'photoModeNone')}</option>
                  <option value="OwnerGallery">{t(lang, 'photoModeGallery')}</option>
                  <option value="CustomerUpload">{t(lang, 'photoModeUpload')}</option>
                </select>
              </div>

              {form.photoMode === 'OwnerGallery' && (
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'galleryPhotos')}</label>
                  {!editingService ? (
                    <p className="text-gray-500 text-xs">{t(lang, 'gallerySaveFirstHint')}</p>
                  ) : (
                    <div>
                      <div className="grid grid-cols-3 gap-2 mb-2">
                        {editingService.galleryPhotos.map((p) => (
                          <div key={p.id} className="relative group">
                            <img src={p.url} alt="" className="w-full aspect-square object-cover rounded-lg border border-gray-700" />
                            <button type="button" onClick={() => handleGalleryDelete(p.id)}
                              className="absolute top-1 right-1 bg-red-600 hover:bg-red-700 text-white text-xs rounded-full w-5 h-5 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity">
                              ✕
                            </button>
                          </div>
                        ))}
                      </div>
                      <label className="inline-block cursor-pointer text-sm text-blue-400 hover:text-blue-300">
                        {uploadingGallery ? t(lang, 'saving') : t(lang, 'addGalleryPhoto')}
                        <input type="file" accept="image/jpeg,image/png,image/webp" className="hidden"
                          disabled={uploadingGallery} onChange={handleGalleryUpload} />
                      </label>
                      {galleryError && <p className="text-red-400 text-xs mt-1">{galleryError}</p>}
                    </div>
                  )}
                </div>
              )}

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
