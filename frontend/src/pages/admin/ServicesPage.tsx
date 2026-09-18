import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'
import { mediaUrl } from '../../lib/media'

type GalleryPhoto = { id: string; url: string }
type PhotoMode = 'None' | 'OwnerGallery' | 'CustomerUpload' | 'Both'
type Item = {
  id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number | null; price: number | null
  photoMode: PhotoMode; isBookable: boolean; galleryPhotos: GalleryPhoto[]
}
const EMPTY = { nameEn: '', nameAr: '', nameHe: '', durationMinutes: 30, price: '', photoMode: 'None' as PhotoMode, isBookable: true }

export default function ServicesPage() {
  const queryClient = useQueryClient()
  const { language: lang } = useAuth()
  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<Item | null>(null)
  const [form, setForm] = useState(EMPTY)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [uploadingGallery, setUploadingGallery] = useState(false)
  const [galleryError, setGalleryError] = useState('')

  const { data: services = [] } = useQuery<Item[]>({
    queryKey: ['services'],
    queryFn: () => api.get('/admin/items').then((r) => r.data),
  })

  // Keep the modal's gallery list in sync with the latest fetched data while editing.
  const editingService = editing ? services.find((s) => s.id === editing.id) ?? editing : null

  function openCreate() { setEditing(null); setForm(EMPTY); setError(''); setGalleryError(''); setShowForm(true) }
  function openEdit(s: Item) {
    setEditing(s)
    setForm({
      nameEn: s.nameEn, nameAr: s.nameAr, nameHe: s.nameHe,
      durationMinutes: s.durationMinutes ?? 30, price: s.price === null ? '' : String(s.price),
      photoMode: s.photoMode, isBookable: s.isBookable,
    })
    setError(''); setGalleryError(''); setShowForm(true)
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault(); setLoading(true); setError('')
    const wasCreating = !editing
    try {
      const payload = {
        ...form,
        durationMinutes: form.isBookable ? Number(form.durationMinutes) : null,
        price: form.price === '' ? null : Number(form.price),
      }
      if (editing) {
        const { data } = await api.patch(`/admin/items/${editing.id}`, payload)
        setEditing(data)
      } else {
        const { data } = await api.post('/admin/items', payload)
        setEditing(data)
      }
      // On first creation of a gallery/both-mode service, keep the modal open so gallery photos
      // can be added right away (the service has to exist first); on every later save, close it.
      const needsGallerySetup = wasCreating && (form.photoMode === 'OwnerGallery' || form.photoMode === 'Both')
      if (!needsGallerySetup) setShowForm(false)
      queryClient.invalidateQueries({ queryKey: ['services'] })
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setError(msg ?? 'Failed to save')
    } finally { setLoading(false) }
  }

  async function handleDelete(id: string) {
    if (!confirm(t(lang, 'deleteConfirm'))) return
    await api.delete(`/admin/items/${id}`)
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
      await api.post(`/admin/items/${editing.id}/gallery`, formData)
      queryClient.invalidateQueries({ queryKey: ['services'] })
    } catch {
      setGalleryError(t(lang, 'photoUploadError'))
    } finally { setUploadingGallery(false) }
  }

  async function handleGalleryDelete(photoId: string) {
    if (!editing || !confirm(t(lang, 'deletePhotoConfirm'))) return
    await api.delete(`/admin/items/${editing.id}/gallery/${photoId}`)
    queryClient.invalidateQueries({ queryKey: ['services'] })
  }

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-ink">{t(lang, 'services')}</h1>
        <button onClick={openCreate} className="bg-coral hover:bg-coral-dark text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">
          {t(lang, 'addService')}
        </button>
      </div>

      {services.length === 0 ? (
        <div className="text-center text-muted py-16">
          {t(lang, 'noServicesYet')} <button onClick={openCreate} className="text-coral-dark hover:text-coral">{t(lang, 'addFirstService')}</button>
        </div>
      ) : (
        <div className="grid gap-3">
          {services.map((s) => (
            <div key={s.id} className="bg-white border border-line rounded-xl px-5 py-4 flex items-center justify-between">
              <div>
                <div className="text-ink font-medium">{s.nameEn}</div>
                <div className="text-muted text-sm mt-0.5">{s.nameAr} · {s.nameHe}</div>
                <div className="text-muted text-xs mt-1">
                  <span dir="ltr">
                    {s.isBookable ? `${s.durationMinutes} min · ` : ''}
                    {s.price !== null ? `₪${s.price}` : ''}
                  </span>
                </div>
                {s.photoMode !== 'None' && (
                  <div className="text-xs text-teal mt-1">
                    {s.photoMode === 'OwnerGallery' ? t(lang, 'photoModeGallery')
                      : s.photoMode === 'CustomerUpload' ? t(lang, 'photoModeUpload')
                      : t(lang, 'photoModeBoth')}
                  </div>
                )}
              </div>
              <div className="flex gap-3">
                <button onClick={() => openEdit(s)} className="text-sm text-coral-dark hover:text-coral">{t(lang, 'edit')}</button>
                <button onClick={() => handleDelete(s.id)} className="text-sm text-red-600 hover:text-red-500">{t(lang, 'delete')}</button>
              </div>
            </div>
          ))}
        </div>
      )}

      {showForm && (
        <div onClick={() => setShowForm(false)} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
          <div onClick={(e) => e.stopPropagation()} className="bg-white rounded-2xl p-6 w-full max-w-md border border-line max-h-[90vh] overflow-y-auto">
            <div className="flex justify-between items-center mb-5">
              <h2 className="text-ink font-semibold text-lg">{editing ? t(lang, 'editServiceTitle') : t(lang, 'addServiceTitle')}</h2>
              <button onClick={() => setShowForm(false)} aria-label="Close"
                className="text-muted hover:text-ink w-11 h-11 -m-2 flex items-center justify-center rounded-lg hover:bg-cream text-2xl leading-none transition-colors">✕</button>
            </div>
            {error && <div className="bg-red-50 border border-red-200 text-red-700 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}
            <form onSubmit={handleSubmit} className="space-y-4">
              {[['Name (English)', 'nameEn'], ['Name (Arabic)', 'nameAr'], ['Name (Hebrew)', 'nameHe']].map(([label, key]) => (
                <div key={key}>
                  <label htmlFor={`service-${key}`} className="block text-sm font-medium text-ink mb-1.5">{label}</label>
                  <input id={`service-${key}`} type="text" required value={form[key as keyof typeof form] as string}
                    onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                    className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
                </div>
              ))}
              <label className="flex items-center gap-2 text-sm font-medium text-ink">
                <input type="checkbox" checked={form.isBookable}
                  onChange={(e) => setForm((f) => ({ ...f, isBookable: e.target.checked }))}
                  className="rounded border-line bg-cream text-coral focus:ring-coral" />
                {t(lang, 'isBookable')}
              </label>
              <div className="grid grid-cols-2 gap-4">
                {form.isBookable && (
                  <div>
                    <label htmlFor="service-duration" className="block text-sm font-medium text-ink mb-1.5">Duration (min)</label>
                    <select id="service-duration" value={form.durationMinutes} onChange={(e) => setForm((f) => ({ ...f, durationMinutes: Number(e.target.value) }))}
                      className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-coral">
                      {[15, 30, 45, 60, 75, 90, 120].map((v) => <option key={v} value={v}>{v} min</option>)}
                    </select>
                  </div>
                )}
                <div>
                  <label htmlFor="service-price" className="block text-sm font-medium text-ink mb-1.5">Price</label>
                  <input id="service-price" type="number" min="0" step="0.01" value={form.price}
                    onChange={(e) => setForm((f) => ({ ...f, price: e.target.value }))} placeholder="25.00"
                    className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
                </div>
              </div>
              <div>
                <label htmlFor="service-photo-mode" className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'photoMode')}</label>
                <select id="service-photo-mode" value={form.photoMode}
                  onChange={(e) => setForm((f) => ({ ...f, photoMode: e.target.value as PhotoMode }))}
                  className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-coral">
                  <option value="None">{t(lang, 'photoModeNone')}</option>
                  <option value="OwnerGallery">{t(lang, 'photoModeGallery')}</option>
                  <option value="CustomerUpload">{t(lang, 'photoModeUpload')}</option>
                  <option value="Both">{t(lang, 'photoModeBoth')}</option>
                </select>
              </div>

              {(form.photoMode === 'OwnerGallery' || form.photoMode === 'Both') && (
                <div>
                  <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'galleryPhotos')}</label>
                  {!editingService ? (
                    <p className="text-muted text-xs">{t(lang, 'gallerySaveFirstHint')}</p>
                  ) : (
                    <div>
                      <div className="grid grid-cols-3 gap-2 mb-2">
                        {editingService.galleryPhotos.map((p) => (
                          <div key={p.id} className="relative group">
                            <img src={mediaUrl(p.url)} alt="" className="w-full aspect-square object-cover rounded-lg border border-line" />
                            <button type="button" onClick={() => handleGalleryDelete(p.id)}
                              className="absolute top-1 right-1 bg-red-600 hover:bg-red-700 text-white text-xs rounded-full w-5 h-5 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity">
                              ✕
                            </button>
                          </div>
                        ))}
                      </div>
                      <label className="inline-block cursor-pointer text-sm text-coral-dark hover:text-coral">
                        {uploadingGallery ? t(lang, 'saving') : t(lang, 'addGalleryPhoto')}
                        <input type="file" accept="image/jpeg,image/png,image/webp" className="hidden"
                          disabled={uploadingGallery} onChange={handleGalleryUpload} />
                      </label>
                      {galleryError && <p className="text-red-600 text-xs mt-1">{galleryError}</p>}
                    </div>
                  )}
                </div>
              )}

              <button type="submit" disabled={loading}
                className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold py-2.5 rounded-lg transition-colors mt-2">
                {loading ? t(lang, 'saving') : t(lang, 'saveService')}
              </button>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
