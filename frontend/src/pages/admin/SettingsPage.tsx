import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'

type BarberSettings = {
  name: string; phone: string | null; description: string | null; slug: string
  language: 'EN' | 'AR' | 'HE'; twilioNumber: string | null; twilioSid: string | null
  trialEndsAt: string; subscriptionStatus: string
  maxBookingsPerDay: number | null; maxBookingsPerWeek: number | null
}

export default function SettingsPage() {
  const queryClient = useQueryClient()
  const { language: lang, setLang } = useAuth()
  const { data: barber } = useQuery<BarberSettings>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  const [form, setForm] = useState({
    name: '', phone: '', description: '', language: 'EN' as 'EN' | 'AR' | 'HE',
    twilioNumber: '', twilioSid: '', twilioToken: '',
    maxBookingsPerDay: '', maxBookingsPerWeek: '',
  })
  const [initialized, setInitialized] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')

  if (barber && !initialized) {
    setForm({
      name: barber.name, phone: barber.phone ?? '', description: barber.description ?? '',
      language: barber.language, twilioNumber: barber.twilioNumber ?? '',
      twilioSid: barber.twilioSid ?? '', twilioToken: '',
      maxBookingsPerDay: barber.maxBookingsPerDay?.toString() ?? '',
      maxBookingsPerWeek: barber.maxBookingsPerWeek?.toString() ?? '',
    })
    setLang(barber.language)
    setInitialized(true)
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault(); setSaving(true); setError('')
    const payload: Record<string, string | number | null | undefined> = {
      name: form.name, phone: form.phone, description: form.description,
      language: form.language, twilioNumber: form.twilioNumber, twilioSid: form.twilioSid,
      maxBookingsPerDay: form.maxBookingsPerDay ? Number(form.maxBookingsPerDay) : null,
      maxBookingsPerWeek: form.maxBookingsPerWeek ? Number(form.maxBookingsPerWeek) : null,
    }
    if (form.twilioToken) payload.twilioToken = form.twilioToken
    try {
      await api.patch('/admin/settings', payload)
      setLang(form.language)
      setSaved(true); setTimeout(() => setSaved(false), 2000)
      queryClient.invalidateQueries({ queryKey: ['settings'] })
    } catch {
      setError('Failed to save')
    } finally { setSaving(false) }
  }

  if (!barber) return <div className="text-gray-500">{t(lang, 'loading')}</div>

  const trialDate = parseISO(barber.trialEndsAt)
  const isTrialActive = barber.subscriptionStatus === 'TRIAL' && trialDate > new Date()
  const trialDaysLeft = Math.max(0, Math.ceil((trialDate.getTime() - Date.now()) / 86400000))

  return (
    <div>
      <h1 className="text-2xl font-bold text-white mb-6">{t(lang, 'settings')}</h1>
      <form onSubmit={handleSubmit} className="space-y-6 max-w-2xl">
        {error && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3">{error}</div>}

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
          <h2 className="text-white font-semibold mb-3">{t(lang, 'subscription')}</h2>
          <div className={`text-sm px-4 py-2 rounded-lg inline-block ${
            barber.subscriptionStatus === 'ACTIVE' ? 'bg-green-900/40 text-green-300'
              : isTrialActive ? 'bg-blue-900/40 text-blue-300' : 'bg-red-900/40 text-red-300'
          }`}>
            {barber.subscriptionStatus === 'ACTIVE' ? t(lang, 'subscriptionActive')
              : isTrialActive ? `Free trial · ${trialDaysLeft} days left (expires ${format(trialDate, 'MMM d, yyyy')})`
              : t(lang, 'subscriptionExpired')}
          </div>
          <p className="text-gray-500 text-xs mt-2">
            {t(lang, 'bookingUrl')} <span className="text-blue-400">{window.location.origin}/{barber.slug}</span>
          </p>
        </div>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6 space-y-4">
          <h2 className="text-white font-semibold mb-1">{t(lang, 'businessInfo')}</h2>
          {[[t(lang, 'businessName'), 'name', 'text'], [t(lang, 'phone'), 'phone', 'tel'], [t(lang, 'description'), 'description', 'textarea']].map(([label, key, type]) => (
            <div key={key}>
              <label className="block text-sm font-medium text-gray-300 mb-1.5">{label}</label>
              {type === 'textarea' ? (
                <textarea value={form[key as keyof typeof form]} rows={3}
                  onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
                  placeholder="Tell customers about your barbershop..." />
              ) : (
                <input type={type} value={form[key as keyof typeof form]}
                  onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
              )}
            </div>
          ))}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'language')}</label>
            <select value={form.language} onChange={(e) => setForm((f) => ({ ...f, language: e.target.value as 'EN' | 'AR' | 'HE' }))}
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white focus:outline-none focus:ring-2 focus:ring-blue-500">
              <option value="EN">English</option>
              <option value="AR">العربية (Arabic)</option>
              <option value="HE">עברית (Hebrew)</option>
            </select>
          </div>
        </div>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6 space-y-4">
          <h2 className="text-white font-semibold mb-1">{t(lang, 'bookingLimits')}</h2>
          <p className="text-gray-500 text-sm">{t(lang, 'bookingLimitsHint')}</p>
          <div className="grid grid-cols-2 gap-4">
            {[[t(lang, 'maxBookingsPerDay'), 'maxBookingsPerDay'], [t(lang, 'maxBookingsPerWeek'), 'maxBookingsPerWeek']].map(([label, key]) => (
              <div key={key}>
                <label htmlFor={`settings-${key}`} className="block text-sm font-medium text-gray-300 mb-1.5">{label}</label>
                <input id={`settings-${key}`} type="number" min="1" step="1" value={form[key as 'maxBookingsPerDay' | 'maxBookingsPerWeek']}
                  placeholder={t(lang, 'unlimited')}
                  onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
            ))}
          </div>
        </div>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6 space-y-4">
          <h2 className="text-white font-semibold mb-1">{t(lang, 'whatsappSetup')}</h2>
          {[['Account SID', 'twilioSid', 'text', 'ACxxxxxxxxxxxxxxxxxx'], ['WhatsApp Number', 'twilioNumber', 'text', '+14155238886']].map(([label, key, type, placeholder]) => (
            <div key={key}>
              <label className="block text-sm font-medium text-gray-300 mb-1.5">{label}</label>
              <input type={type} value={form[key as keyof typeof form]} placeholder={placeholder}
                onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white font-mono focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>
          ))}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1.5">
              Auth Token <span className="text-gray-500 font-normal">(leave blank to keep existing)</span>
            </label>
            <input type="password" value={form.twilioToken} onChange={(e) => setForm((f) => ({ ...f, twilioToken: e.target.value }))}
              placeholder="••••••••••••••••••••••••••••••••"
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white font-mono focus:outline-none focus:ring-2 focus:ring-blue-500" />
          </div>
          <div className="bg-blue-900/20 border border-blue-800/40 rounded-lg px-4 py-3 text-sm text-blue-300">
            <strong>Webhook URL:</strong> <code className="text-blue-200">{window.location.origin}/api/whatsapp/webhook</code>
          </div>
        </div>

        <button type="submit" disabled={saving}
          className="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold px-8 py-2.5 rounded-lg transition-colors">
          {saved ? t(lang, 'saved') : saving ? t(lang, 'saving') : t(lang, 'saveChanges')}
        </button>
      </form>
    </div>
  )
}
