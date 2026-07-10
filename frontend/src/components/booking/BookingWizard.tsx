import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { format, addDays } from 'date-fns'
import { ar, he, enUS } from 'date-fns/locale'
import { customerApi } from '../../lib/customerApi'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t, serviceName } from '../../lib/i18n'
import BackButton from '../BackButton'
import LanguageSwitcher from '../customer/LanguageSwitcher'

type Service = { id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number; price: number }
type BarberInfo = { slug: string; name: string; language: string; isRTL: boolean; activeDays: number[]; services: Service[] }
type Slot = { start: string; end: string }
type Step = 1 | 2 | 3 | 4

export default function BookingWizard({ barber }: { barber: BarberInfo }) {
  const { user, isAuthenticated, language: lang } = useCustomerAuth()
  const navigate = useNavigate()
  const [step, setStep] = useState<Step>(1)
  const [service, setService] = useState<Service | null>(null)
  const [date, setDate] = useState('')
  const [slot, setSlot] = useState<Slot | null>(null)
  const prefillName = () => (user ? `${user.name} ${user.familyName}`.trim() : '')
  const prefillPhone = () => user?.phone ?? ''
  const [name, setName] = useState(prefillName)
  const [phone, setPhone] = useState(prefillPhone)
  const [notes, setNotes] = useState('')
  const [slots, setSlots] = useState<Slot[]>([])
  const [slotsLoading, setSlotsLoading] = useState(false)
  const [confirmLoading, setConfirmLoading] = useState(false)
  const [error, setError] = useState('')

  // The customer's own language choice drives the UI everywhere, overriding this specific
  // barber's configured storefront language.
  const dir = lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'
  const dateLocale = lang === 'AR' ? ar : lang === 'HE' ? he : enUS

  async function fetchSlots(d: string, svc: Service) {
    setSlotsLoading(true); setSlots([])
    const { data } = await customerApi.get(`/${barber.slug}/availability?date=${d}&serviceId=${svc.id}`)
    setSlots(data.slots ?? [])
    setSlotsLoading(false)
  }

  function pickDate(d: string) {
    setDate(d); setSlot(null)
    if (service) fetchSlots(d, service)
    setStep(3)
  }

  async function confirm() {
    if (!service || !date || !slot) return
    setConfirmLoading(true); setError('')
    try {
      await customerApi.post(`/${barber.slug}/appointments`, {
        serviceId: service.id, date, startTime: slot.start,
        customerName: name, customerPhone: phone, notes: notes || undefined,
      })
      navigate(`/${barber.slug}`)
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setError(msg ?? 'Booking failed. Please try again.')
    } finally { setConfirmLoading(false) }
  }

  const today = new Date()
  const calDays = Array.from({ length: 60 }, (_, i) => addDays(today, i))
  const availableDays = calDays.filter((d) => barber.activeDays.includes(d.getDay()))

  return (
    <div className="min-h-screen bg-gray-950 text-white" dir={dir}>
      <div className="max-w-lg mx-auto px-4 py-10">
        <div className="flex items-center gap-2 mb-8">
          {step > 1 ? (
            <button onClick={() => setStep((s) => (s - 1) as Step)} className="text-gray-400 hover:text-white text-sm">
              ← {t(lang, 'back')}
            </button>
          ) : (
            <BackButton lang={lang} />
          )}
          <div className="flex gap-1 mx-auto">
            {[1, 2, 3, 4].map((s) => (
              <div key={s} className={`h-1.5 w-8 rounded-full transition-colors ${s <= step ? 'bg-blue-500' : 'bg-gray-700'}`} />
            ))}
          </div>
          <LanguageSwitcher />
        </div>

        <h1 className="text-2xl font-bold mb-2">{barber.name}</h1>

        {step === 1 && (
          <div>
            <p className="text-gray-400 mb-6">{t(lang, 'selectService')}</p>
            <div className="space-y-3">
              {barber.services.map((s) => (
                <button key={s.id} onClick={() => { setService(s); setStep(2) }}
                  className="w-full bg-gray-900 hover:bg-gray-800 border border-gray-700 hover:border-blue-500 rounded-xl px-5 py-4 flex justify-between items-center transition-colors text-start">
                  <div>
                    <div className="font-medium">{serviceName(s, lang)}</div>
                    <div className="text-gray-400 text-sm mt-0.5">{s.durationMinutes} {t(lang, 'min')}</div>
                  </div>
                  <div className="text-blue-400 font-semibold">₪{Number(s.price).toFixed(0)}</div>
                </button>
              ))}
            </div>
          </div>
        )}

        {step === 2 && (
          <div>
            <p className="text-gray-400 mb-6">{t(lang, 'selectDate')}</p>
            <div className="grid grid-cols-4 gap-2">
              {availableDays.slice(0, 28).map((d) => {
                const str = format(d, 'yyyy-MM-dd')
                return (
                  <button key={str} onClick={() => pickDate(str)}
                    className={`bg-gray-900 hover:bg-blue-600 border border-gray-800 hover:border-blue-500 rounded-xl p-3 text-center transition-colors ${date === str ? 'bg-blue-600 border-blue-500' : ''}`}>
                    <div className="text-xs text-gray-400">{format(d, 'EEE', { locale: dateLocale })}</div>
                    <div className="text-white font-medium mt-0.5">{format(d, 'd')}</div>
                    <div className="text-xs text-gray-500">{format(d, 'MMM', { locale: dateLocale })}</div>
                  </button>
                )
              })}
            </div>
          </div>
        )}

        {step === 3 && (
          <div>
            <p className="text-gray-400 mb-1">{t(lang, 'selectTime')}</p>
            <p className="text-sm text-gray-500 mb-5">{date}</p>
            {slotsLoading ? (
              <div className="text-gray-500 text-center py-8">{t(lang, 'loadingTimes')}</div>
            ) : slots.length === 0 ? (
              <div className="text-gray-500 text-center py-8">{t(lang, 'noTimes')}</div>
            ) : (
              <div className="grid grid-cols-3 gap-2">
                {slots.map((s) => (
                  <button key={s.start} onClick={() => { setSlot(s); setStep(4) }}
                    className={`bg-gray-900 hover:bg-blue-600 border border-gray-800 hover:border-blue-500 rounded-xl py-3 text-center text-sm font-medium transition-colors ${slot?.start === s.start ? 'bg-blue-600 border-blue-500' : ''}`}>
                    {s.start}
                  </button>
                ))}
              </div>
            )}
          </div>
        )}

        {step === 4 && (
          <div>
            <p className="text-gray-400 mb-6">{t(lang, 'yourDetails')}</p>
            {error && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}
            <div className="space-y-4">
              <div>
                <label htmlFor="booking-name" className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'fullName')}</label>
                <input id="booking-name" type="text" required value={name} onChange={(e) => setName(e.target.value)}
                  className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
              <div>
                <label htmlFor="booking-phone" className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'phoneNumber')}</label>
                <input id="booking-phone" type="tel" required value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+1234567890"
                  disabled={isAuthenticated}
                  className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-60" />
              </div>
              <div>
                <label htmlFor="booking-notes" className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'notes')}</label>
                <textarea id="booking-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={3}
                  placeholder={t(lang, 'notesPlaceholder')}
                  className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white placeholder-gray-600 focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none" />
              </div>
              {service && slot && (
                <div className="bg-gray-900 border border-gray-700 rounded-xl p-4 text-sm space-y-1">
                  <div className="flex justify-between"><span className="text-gray-400">{t(lang, 'service')}</span><span className="text-white">{serviceName(service, lang)}</span></div>
                  <div className="flex justify-between"><span className="text-gray-400">{t(lang, 'date')}</span><span className="text-white">{date}</span></div>
                  <div className="flex justify-between"><span className="text-gray-400">{t(lang, 'time')}</span><span className="text-white">{slot.start} – {slot.end}</span></div>
                </div>
              )}
              <button onClick={confirm} disabled={!name || !phone || confirmLoading}
                className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold py-4 rounded-2xl transition-colors">
                {confirmLoading ? '...' : t(lang, 'confirm')}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
