import { useEffect, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { format, addDays } from 'date-fns'
import { ar, he, enUS } from 'date-fns/locale'
import { customerApi } from '../../lib/customerApi'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t, itemName } from '../../lib/i18n'
import { mediaUrl } from '../../lib/media'
import BackButton from '../BackButton'
import LanguageSwitcher from '../customer/LanguageSwitcher'
import SlotBookedModal from './SlotBookedModal'
import BookingSuccessModal from './BookingSuccessModal'

type GalleryPhoto = { id: string; url: string }
type Item = {
  id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number | null; price: number | null
  photoMode: 'None' | 'OwnerGallery' | 'CustomerUpload' | 'Both'; isBookable: boolean; galleryPhotos: GalleryPhoto[]
}
type BusinessInfo = {
  slug: string; name: string; language: string; isRTL: boolean; activeDays: number[]; items: Item[]
  waitlistEnabled: boolean
}
type Slot = { start: string; end: string; available: boolean; appointmentId?: string }
type Step = 1 | 2 | 3 | 4

export default function BookingWizard({ business }: { business: BusinessInfo }) {
  const { user, isAuthenticated, language: lang } = useCustomerAuth()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  // A WhatsApp-issued link (booking link or waitlist notification) always carries ?itemId= --
  // the service (and sometimes date/time) was already chosen there, so this session gets no way
  // back to reconsider it, on this page or via the browser. A customer who opened the site
  // directly always starts at step 1 with no itemId param, so they keep normal back navigation.
  const [isFromLink] = useState(() => !!searchParams.get('itemId'))

  // Can't truly disable the browser/OS's own back gesture, but this is the standard best-effort
  // trap: re-push the current entry whenever the user navigates back to it, so leaving via browser
  // back isn't a real escape hatch either -- only the "Booking Confirmed" modal's close button
  // (or manually retyping a URL) gets them off this page once they've arrived via a WhatsApp link.
  useEffect(() => {
    if (!isFromLink) return
    window.history.pushState(null, '', window.location.href)
    const onPopState = () => window.history.pushState(null, '', window.location.href)
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [isFromLink])

  const [step, setStep] = useState<Step>(1)
  const [item, setItem] = useState<Item | null>(null)
  const bookableItems = business.items.filter((s) => s.isBookable)
  const [date, setDate] = useState('')
  const [slot, setSlot] = useState<Slot | null>(null)
  const prefillName = () => user?.name ?? ''
  const prefillFamilyName = () => user?.familyName ?? ''
  const prefillPhone = () => user?.phone ?? ''
  const [name, setName] = useState(prefillName)
  const [familyName, setFamilyName] = useState(prefillFamilyName)
  const [phone, setPhone] = useState(prefillPhone)
  const [notes, setNotes] = useState('')
  const [slots, setSlots] = useState<Slot[]>([])
  const [slotsLoading, setSlotsLoading] = useState(false)
  const [confirmLoading, setConfirmLoading] = useState(false)
  const [error, setError] = useState('')
  const [bookingSuccess, setBookingSuccess] = useState(false)
  const [selectedGalleryPhotoId, setSelectedGalleryPhotoId] = useState<string | null>(null)
  const [uploadedPhotoUrl, setUploadedPhotoUrl] = useState<string | null>(null)
  const [photoUploading, setPhotoUploading] = useState(false)
  const [photoError, setPhotoError] = useState('')
  const [bookedSlot, setBookedSlot] = useState<Slot | null>(null)
  const [joiningWaitlist, setJoiningWaitlist] = useState(false)
  const [joinedWaitlist, setJoinedWaitlist] = useState(false)

  // The customer's own language choice drives the UI everywhere, overriding this specific
  // business's configured storefront language.
  const dir = lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'
  const dateLocale = lang === 'AR' ? ar : lang === 'HE' ? he : enUS

  async function fetchSlots(d: string, it: Item) {
    setSlotsLoading(true); setSlots([])
    const { data } = await customerApi.get(`/${business.slug}/availability/full?date=${d}&itemId=${it.id}`)
    setSlots(data.slots ?? [])
    setSlotsLoading(false)
  }

  function pickDate(d: string) {
    setDate(d); setSlot(null)
    if (item) fetchSlots(d, item)
    setStep(3)
  }

  function pickSlot(s: Slot) {
    if (s.available) { setSlot(s); setStep(4); return }
    setJoinedWaitlist(false)
    setBookedSlot(s)
  }

  async function joinWaitlist() {
    if (!bookedSlot?.appointmentId) return
    setJoiningWaitlist(true)
    try {
      await customerApi.post(`/${business.slug}/waitlist/${bookedSlot.appointmentId}`)
      setJoinedWaitlist(true)
    } finally { setJoiningWaitlist(false) }
  }

  // Deep-link prefill from a WhatsApp notification or booking link (?itemId=&date=&time=):
  // jump straight past whichever steps are already decided instead of making the customer re-pick
  // from scratch.
  // - itemId + date (+ time): a waitlist "slot opened up" notification -- jump to that slot; if
  //   it's already gone by the time they arrive (someone beat them to it), they land on step 3
  //   seeing the real current state, no special-case error needed.
  // - itemId only: the WhatsApp chatbot booking link (WhatsAppLandingPage) -- the customer
  //   already picked their item in the chat, so skip step 1 entirely and land on date
  //   selection (step 2), with no sign-up/item-selection step in between.
  useEffect(() => {
    const prefillItemId = searchParams.get('itemId')
    const prefillDate = searchParams.get('date')
    const prefillTime = searchParams.get('time')
    if (!prefillItemId) return

    const it = business.items.find((s) => s.id === prefillItemId)
    if (!it) return

    setItem(it)
    if (!prefillDate) { setStep(2); return }

    setDate(prefillDate)
    setStep(3)
    customerApi.get(`/${business.slug}/availability/full?date=${prefillDate}&itemId=${it.id}`).then(({ data }) => {
      const fetchedSlots: Slot[] = data.slots ?? []
      setSlots(fetchedSlots)
      const match = prefillTime && fetchedSlots.find((s) => s.start === prefillTime && s.available)
      if (match) { setSlot(match); setStep(4) }
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function uploadPhoto(file: File) {
    setPhotoUploading(true); setPhotoError('')
    try {
      const formData = new FormData()
      formData.append('file', file)
      const { data } = await customerApi.post(`/${business.slug}/appointments/photo`, formData)
      setUploadedPhotoUrl(data.url)
      // Both-mode items send whichever of these is set -- the backend prefers a gallery pick over
      // an upload, so an upload has to clear any earlier gallery selection or it'd be silently
      // ignored (the actual bug: a customer picks a gallery photo, then uploads their own instead,
      // but the gallery pick still wins).
      setSelectedGalleryPhotoId(null)
    } catch {
      setPhotoError(t(lang, 'photoUploadError'))
    } finally { setPhotoUploading(false) }
  }

  const photoSatisfied =
    item?.photoMode === 'OwnerGallery' ? !!selectedGalleryPhotoId
      : item?.photoMode === 'CustomerUpload' ? !!uploadedPhotoUrl
      : item?.photoMode === 'Both' ? !!selectedGalleryPhotoId || !!uploadedPhotoUrl
      : true

  async function confirm() {
    if (!item || !date || !slot || !photoSatisfied) return
    setConfirmLoading(true); setError('')
    try {
      await customerApi.post(`/${business.slug}/appointments`, {
        itemId: item.id, date, startTime: slot.start,
        customerName: name, customerFamilyName: familyName, customerPhone: phone, notes: notes || undefined,
        galleryPhotoId: selectedGalleryPhotoId ?? undefined,
        customerPhotoUrl: uploadedPhotoUrl ?? undefined,
      })
      setBookingSuccess(true)
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setError(msg ?? 'Booking failed. Please try again.')
    } finally { setConfirmLoading(false) }
  }

  const today = new Date()
  const calDays = Array.from({ length: 60 }, (_, i) => addDays(today, i))
  const availableDays = calDays.filter((d) => business.activeDays.includes(d.getDay()))

  return (
    <div className="min-h-screen bg-cream text-ink" dir={dir}>
      <div className="max-w-lg mx-auto px-4 py-10">
        <div className="flex items-center gap-2 mb-8">
          {isFromLink ? (
            <div />
          ) : step > 1 ? (
            <button onClick={() => setStep((s) => (s - 1) as Step)} className="text-muted hover:text-ink text-sm">
              ← {t(lang, 'back')}
            </button>
          ) : (
            <BackButton lang={lang} />
          )}
          <div className="flex gap-1 mx-auto">
            {[1, 2, 3, 4].map((s) => (
              <div key={s} className={`h-1.5 w-8 rounded-full transition-colors ${s <= step ? 'bg-coral' : 'bg-line'}`} />
            ))}
          </div>
          <LanguageSwitcher />
        </div>

        <h1 className="text-2xl font-bold mb-2">{business.name}</h1>

        {step === 1 && (
          <div>
            <p className="text-muted mb-6">{t(lang, 'selectService')}</p>
            <div className="space-y-3">
              {bookableItems.map((s) => (
                <button key={s.id} onClick={() => {
                  setItem(s); setSelectedGalleryPhotoId(null); setUploadedPhotoUrl(null); setPhotoError(''); setStep(2)
                }}
                  className="w-full bg-white hover:bg-cream border border-line hover:border-coral rounded-xl px-5 py-4 flex justify-between items-center transition-colors text-start">
                  <div>
                    <div className="font-medium">{itemName(s, lang)}</div>
                    <div className="text-muted text-sm mt-0.5">{s.durationMinutes} {t(lang, 'min')}</div>
                  </div>
                  <div className="text-coral-dark font-semibold">{s.price !== null ? `₪${Number(s.price).toFixed(0)}` : ''}</div>
                </button>
              ))}
            </div>
          </div>
        )}

        {step === 2 && (
          <div>
            <p className="text-muted mb-6">{t(lang, 'selectDate')}</p>
            <div className="grid grid-cols-4 gap-2">
              {availableDays.slice(0, 28).map((d) => {
                const str = format(d, 'yyyy-MM-dd')
                return (
                  <button key={str} onClick={() => pickDate(str)}
                    className={`bg-white hover:bg-coral hover:text-white border border-line hover:border-coral-dark rounded-xl p-3 text-center transition-colors ${date === str ? 'bg-coral text-white border-coral-dark' : ''}`}>
                    <div className="text-xs text-muted">{format(d, 'EEE', { locale: dateLocale })}</div>
                    <div className="text-ink font-medium mt-0.5">{format(d, 'd')}</div>
                    <div className="text-xs text-muted">{format(d, 'MMM', { locale: dateLocale })}</div>
                  </button>
                )
              })}
            </div>
            {isAuthenticated && (
              <Link to="/account/bookings"
                className="block text-center text-muted hover:text-ink text-sm mt-6 transition-colors">
                {t(lang, 'viewMyAppointments')}
              </Link>
            )}
          </div>
        )}

        {step === 3 && (
          <div>
            <p className="text-muted mb-1">{t(lang, 'selectTime')}</p>
            <p className="text-sm text-muted mb-5">{date}</p>
            {slotsLoading ? (
              <div className="text-muted text-center py-8">{t(lang, 'loadingTimes')}</div>
            ) : slots.length === 0 ? (
              <div className="text-muted text-center py-8">{t(lang, 'noTimes')}</div>
            ) : (
              <div className="grid grid-cols-3 gap-2">
                {slots.map((s) => (
                  <button key={s.start} onClick={() => pickSlot(s)}
                    className={`rounded-xl py-3 text-center text-sm font-medium transition-colors border ${
                      s.available
                        ? `bg-white hover:bg-coral hover:text-white border-line hover:border-coral-dark ${slot?.start === s.start ? 'bg-coral text-white border-coral-dark' : ''}`
                        : 'bg-cream border-line text-muted'
                    }`}>
                    {s.start}
                    {!s.available && <div className="text-[10px] uppercase tracking-wide text-muted mt-0.5">{t(lang, 'booked')}</div>}
                  </button>
                ))}
              </div>
            )}
          </div>
        )}

        {step === 4 && (
          <div>
            <p className="text-muted mb-6">{t(lang, 'yourDetails')}</p>
            {error && <div className="bg-red-50 border border-red-200 text-red-700 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}
            <div className="space-y-4">
              <div>
                <label htmlFor="booking-name" className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'fullName')}</label>
                <input id="booking-name" type="text" required value={name} onChange={(e) => setName(e.target.value)}
                  className="w-full bg-white border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
              </div>
              <div>
                <label htmlFor="booking-family-name" className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'familyName')}</label>
                <input id="booking-family-name" type="text" required value={familyName} onChange={(e) => setFamilyName(e.target.value)}
                  className="w-full bg-white border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
              </div>
              <div>
                <label htmlFor="booking-phone" className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'phoneNumber')}</label>
                <input id="booking-phone" type="tel" required value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+1234567890"
                  disabled={isAuthenticated}
                  className="w-full bg-white border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral disabled:opacity-60" />
              </div>
              <div>
                <label htmlFor="booking-notes" className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'notes')}</label>
                <textarea id="booking-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={3}
                  placeholder={t(lang, 'notesPlaceholder')}
                  className="w-full bg-white border border-line rounded-xl px-4 py-3 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral resize-none" />
              </div>
              {(item?.photoMode === 'OwnerGallery' || item?.photoMode === 'Both') && (
                <div>
                  <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'choosePhoto')}</label>
                  <p className="text-muted text-xs mb-2">{t(lang, 'choosePhotoHint')}</p>
                  <div className="grid grid-cols-3 gap-2">
                    {item.galleryPhotos.map((p) => (
                      <button key={p.id} type="button" onClick={() => { setSelectedGalleryPhotoId(p.id); setUploadedPhotoUrl(null) }}
                        className={`aspect-square rounded-lg overflow-hidden border-2 transition-colors ${
                          selectedGalleryPhotoId === p.id ? 'border-coral' : 'border-line hover:border-muted'
                        }`}>
                        <img src={mediaUrl(p.url)} alt="" className="w-full h-full object-cover" />
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {(item?.photoMode === 'CustomerUpload' || item?.photoMode === 'Both') && (
                <div>
                  <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'uploadYourPhoto')}</label>
                  <p className="text-muted text-xs mb-2">{t(lang, 'uploadYourPhotoHint')}</p>
                  {uploadedPhotoUrl ? (
                    <div className="relative w-24 h-24">
                      <img src={mediaUrl(uploadedPhotoUrl)} alt="" className="w-24 h-24 object-cover rounded-lg border border-line" />
                      <button type="button" onClick={() => setUploadedPhotoUrl(null)}
                        className="absolute -top-2 -right-2 bg-red-600 hover:bg-red-700 text-white text-xs rounded-full w-5 h-5 flex items-center justify-center">
                        ✕
                      </button>
                    </div>
                  ) : (
                    <label className="inline-block cursor-pointer bg-white border border-line hover:border-coral rounded-xl px-4 py-3 text-sm text-ink">
                      {photoUploading ? t(lang, 'uploading') : t(lang, 'uploadYourPhoto')}
                      <input type="file" accept="image/jpeg,image/png,image/webp" className="hidden" disabled={photoUploading}
                        onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) uploadPhoto(f) }} />
                    </label>
                  )}
                  {photoError && <p className="text-red-600 text-xs mt-1">{photoError}</p>}
                </div>
              )}

              {item && slot && (
                <div className="bg-white border border-line rounded-xl p-4 text-sm space-y-1">
                  <div className="flex justify-between"><span className="text-muted">{t(lang, 'service')}</span><span className="text-ink">{itemName(item, lang)}</span></div>
                  <div className="flex justify-between"><span className="text-muted">{t(lang, 'date')}</span><span className="text-ink">{date}</span></div>
                  <div className="flex justify-between"><span className="text-muted">{t(lang, 'time')}</span><span className="text-ink">{slot.start} – {slot.end}</span></div>
                </div>
              )}
              <button onClick={confirm} disabled={!name || !familyName || !phone || !photoSatisfied || confirmLoading}
                className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-4 rounded-2xl transition-colors">
                {confirmLoading ? '...' : t(lang, 'confirm')}
              </button>
            </div>
          </div>
        )}
      </div>

      {bookedSlot && (
        <SlotBookedModal
          lang={lang}
          dir={dir}
          waitlistEnabled={business.waitlistEnabled}
          joining={joiningWaitlist}
          joined={joinedWaitlist}
          onJoinWaitlist={joinWaitlist}
          onClose={() => setBookedSlot(null)}
        />
      )}

      {bookingSuccess && (
        <BookingSuccessModal lang={lang} dir={dir} onClose={() => navigate(`/${business.slug}`)} />
      )}
    </div>
  )
}
