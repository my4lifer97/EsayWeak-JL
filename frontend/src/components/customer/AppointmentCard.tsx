import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ar, he, enUS } from 'date-fns/locale'
import { customerApi } from '../../lib/customerApi'
import { t, itemName } from '../../lib/i18n'
import { mediaUrl } from '../../lib/media'

type GalleryPhoto = { id: string; url: string }
export type Appointment = {
  id: string; businessSlug: string; businessName: string
  date: string; startTime: string; endTime: string
  notes: string | null; status: string; cancelToken: string
  item: {
    id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number | null; price: number | null
    photoMode?: 'None' | 'OwnerGallery' | 'CustomerUpload' | 'Both'; galleryPhotos?: GalleryPhoto[] | null
  }
  photoUrl: string | null
}

type Slot = { start: string; end: string }

const STATUS_COLORS: Record<string, string> = {
  CONFIRMED: 'bg-blue-100 text-blue-700 border-blue-200 dark:bg-blue-900/40 dark:text-blue-400 dark:border-blue-800/50',
  COMPLETED: 'bg-green-100 text-green-700 border-green-200 dark:bg-green-900/40 dark:text-green-400 dark:border-green-800/50',
  CANCELLED: 'bg-gray-100 text-gray-500 border-gray-200 dark:bg-gray-800/60 dark:text-gray-400 dark:border-gray-700/50',
}

export default function AppointmentCard({
  appt, lang, showBusinessName = true, onChanged,
}: {
  appt: Appointment
  lang: string
  showBusinessName?: boolean
  onChanged: () => void
}) {
  const navigate = useNavigate()
  const [expandedReschedule, setExpandedReschedule] = useState(false)
  const [expandedNotes, setExpandedNotes] = useState(false)
  const [expandedPhoto, setExpandedPhoto] = useState(false)
  const [noteDraft, setNoteDraft] = useState(appt.notes ?? '')
  const [rescheduleDate, setRescheduleDate] = useState('')
  const [busy, setBusy] = useState(false)
  const [photoUploading, setPhotoUploading] = useState(false)
  const [photoError, setPhotoError] = useState('')

  const { data: slots = [], isFetching: loadingSlots } = useQuery<Slot[]>({
    queryKey: ['reschedule-slots', appt.id, rescheduleDate],
    enabled: expandedReschedule && !!rescheduleDate,
    queryFn: () =>
      customerApi
        .get(`/${appt.businessSlug}/availability?date=${rescheduleDate}&itemId=${appt.item.id}`)
        .then((r) => r.data.slots),
  })

  async function cancelAppointment() {
    if (!confirm(t(lang, 'cancelConfirm'))) return
    setBusy(true)
    try {
      await customerApi.post(`/customer/appointments/${appt.id}/cancel`)
      onChanged()
    } finally { setBusy(false) }
  }

  async function confirmReschedule(startTime: string) {
    setBusy(true)
    try {
      await customerApi.patch(`/customer/appointments/${appt.id}/reschedule`, { date: rescheduleDate, startTime })
      setExpandedReschedule(false)
      setRescheduleDate('')
      onChanged()
    } finally { setBusy(false) }
  }

  async function saveNote() {
    setBusy(true)
    try {
      await customerApi.patch(`/customer/appointments/${appt.id}/notes`, { notes: noteDraft })
      setExpandedNotes(false)
      onChanged()
    } finally { setBusy(false) }
  }

  async function pickGalleryPhoto(photoId: string) {
    setBusy(true); setPhotoError('')
    try {
      await customerApi.patch(`/customer/appointments/${appt.id}/photo`, { galleryPhotoId: photoId })
      setExpandedPhoto(false)
      onChanged()
    } catch {
      setPhotoError(t(lang, 'photoUploadError'))
    } finally { setBusy(false) }
  }

  async function uploadAndSetPhoto(file: File) {
    setPhotoUploading(true); setPhotoError('')
    try {
      const formData = new FormData()
      formData.append('file', file)
      const { data } = await customerApi.post(`/${appt.businessSlug}/appointments/photo`, formData)
      await customerApi.patch(`/customer/appointments/${appt.id}/photo`, { customerPhotoUrl: data.url })
      setExpandedPhoto(false)
      onChanged()
    } catch {
      setPhotoError(t(lang, 'photoUploadError'))
    } finally { setPhotoUploading(false) }
  }

  return (
    <div className={`bg-surface border rounded-2xl p-4 ${appt.status !== 'CONFIRMED' ? 'border-line opacity-60' : 'border-line'}`}>
      <div className="flex items-start justify-between gap-3">
        <div className="flex-1 min-w-0">
          {showBusinessName && <div className="font-semibold text-ink truncate">{appt.businessName}</div>}
          <div className="text-muted text-sm mt-1">{itemName(appt.item, lang)}</div>
          <div className="text-muted text-sm mt-1">
            {format(parseISO(appt.date), 'EEE, MMM d yyyy', { locale: lang === 'AR' ? ar : lang === 'HE' ? he : enUS })}
            {' · '}{appt.startTime}–{appt.endTime}
          </div>
          {appt.notes && !expandedNotes && (
            <div className="text-muted text-sm mt-1 italic">"{appt.notes}"</div>
          )}
          {appt.photoUrl && (
            <a href={mediaUrl(appt.photoUrl)} target="_blank" rel="noreferrer" className="inline-block mt-2">
              <img src={mediaUrl(appt.photoUrl)} alt={t(lang, 'referencePhoto')}
                className="w-14 h-14 object-cover rounded-lg border border-line hover:border-coral transition-colors" />
            </a>
          )}
        </div>
        <span className={`shrink-0 text-xs font-medium px-2.5 py-1 rounded-full border ${STATUS_COLORS[appt.status] ?? STATUS_COLORS.CANCELLED}`}>
          {t(lang, appt.status === 'CONFIRMED' ? 'statusConfirmed' : appt.status === 'COMPLETED' ? 'statusCompleted' : 'statusCancelled')}
        </span>
      </div>

      {appt.status === 'CONFIRMED' && (
        <div className="mt-3 flex flex-wrap gap-2">
          <button
            onClick={() => { setExpandedReschedule(!expandedReschedule); setRescheduleDate('') }}
            className="flex-1 border border-line text-ink hover:bg-cream text-sm font-medium py-2 rounded-xl transition-colors">
            {t(lang, 'rescheduleAppointment')}
          </button>
          <button
            onClick={() => { setExpandedNotes(!expandedNotes); setNoteDraft(appt.notes ?? '') }}
            className="flex-1 border border-line text-ink hover:bg-cream text-sm font-medium py-2 rounded-xl transition-colors">
            {appt.notes ? t(lang, 'editNote') : t(lang, 'addNote')}
          </button>
          {appt.item.photoMode && appt.item.photoMode !== 'None' && (
            <button
              onClick={() => { setExpandedPhoto(!expandedPhoto); setPhotoError('') }}
              className="flex-1 border border-line text-ink hover:bg-cream text-sm font-medium py-2 rounded-xl transition-colors">
              {t(lang, 'changePhoto')}
            </button>
          )}
          <button
            disabled={busy}
            onClick={cancelAppointment}
            className="flex-1 border border-red-200 text-red-600 hover:bg-red-50 text-sm font-medium py-2 rounded-xl transition-colors disabled:opacity-50">
            {t(lang, 'cancelAppointment')}
          </button>
        </div>
      )}

      {appt.status === 'COMPLETED' && (
        <div className="mt-3">
          <button
            onClick={() => navigate(`/${appt.businessSlug}#reviews`)}
            className="w-full border border-line text-ink hover:bg-cream text-sm font-medium py-2 rounded-xl transition-colors">
            ★ {t(lang, 'leaveReview')}
          </button>
        </div>
      )}

      {expandedPhoto && (
        <div className="mt-3 space-y-2">
          {(appt.item.photoMode === 'OwnerGallery' || appt.item.photoMode === 'Both') && (
            <div className="grid grid-cols-4 gap-2">
              {(appt.item.galleryPhotos ?? []).map((p) => (
                <button key={p.id} type="button" disabled={busy} onClick={() => pickGalleryPhoto(p.id)}
                  className="aspect-square rounded-lg overflow-hidden border-2 border-line hover:border-coral transition-colors disabled:opacity-50">
                  <img src={mediaUrl(p.url)} alt="" className="w-full h-full object-cover" />
                </button>
              ))}
            </div>
          )}
          {(appt.item.photoMode === 'CustomerUpload' || appt.item.photoMode === 'Both') && (
            <label className="inline-block cursor-pointer bg-cream border border-line hover:border-coral rounded-xl px-4 py-2 text-sm text-ink">
              {photoUploading ? t(lang, 'uploading') : t(lang, 'uploadYourPhoto')}
              <input type="file" accept="image/jpeg,image/png,image/webp" className="hidden" disabled={photoUploading}
                onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) uploadAndSetPhoto(f) }} />
            </label>
          )}
          {photoError && <p className="text-red-600 text-xs">{photoError}</p>}
        </div>
      )}

      {expandedNotes && (
        <div className="mt-3 space-y-2">
          <textarea
            value={noteDraft} onChange={(e) => setNoteDraft(e.target.value)}
            rows={2}
            className="w-full bg-cream border border-line rounded-xl px-3 py-2 text-ink text-sm placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral"
          />
          <button
            disabled={busy}
            onClick={saveNote}
            className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white text-sm font-semibold py-2 rounded-xl transition-colors">
            {t(lang, 'saveNote')}
          </button>
        </div>
      )}

      {expandedReschedule && (
        <div className="mt-3 space-y-2">
          <p className="text-muted text-sm">{t(lang, 'selectNewTime')}</p>
          <input
            type="date" value={rescheduleDate}
            min={new Date().toISOString().slice(0, 10)}
            onChange={(e) => setRescheduleDate(e.target.value)}
            className="w-full bg-cream border border-line rounded-xl px-3 py-2 text-ink text-sm focus:outline-none focus:ring-2 focus:ring-coral"
          />
          {rescheduleDate && (
            loadingSlots ? (
              <div className="text-center text-muted text-sm py-3">{t(lang, 'loadingTimes')}</div>
            ) : slots.length === 0 ? (
              <div className="text-center text-muted text-sm py-3">{t(lang, 'noTimes')}</div>
            ) : (
              <div className="grid grid-cols-4 gap-2">
                {slots.map((s) => (
                  <button
                    key={s.start}
                    disabled={busy}
                    onClick={() => confirmReschedule(s.start)}
                    className="bg-cream hover:bg-coral hover:text-white border border-line text-ink text-sm py-2 rounded-lg transition-colors disabled:opacity-50">
                    {s.start}
                  </button>
                ))}
              </div>
            )
          )}
        </div>
      )}
    </div>
  )
}
