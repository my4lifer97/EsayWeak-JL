import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'
import { mediaUrl } from '../../lib/media'
import ThemeToggle from '../../components/ThemeToggle'
import { useTheme } from '../../lib/theme'

type BusinessSettings = {
  name: string; phone: string | null; description: string | null; slug: string; logo: string | null
  language: 'EN' | 'AR' | 'HE'; whatsAppNumber: string | null
  trialEndsAt: string; subscriptionStatus: string
  maxBookingsPerDay: number | null; maxBookingsPerWeek: number | null
  waitlistEnabled: boolean
  requireApprovalOnCustomerCancel: boolean
  chatbotEnabled: boolean
  chatbotWelcomeMessage: string | null
  chatbotConfirmationMessage: string | null
  chatbotFinalMessage: string | null
  chatbotInquiryEnabled: boolean
  inquiryNotifyViaWhatsApp: boolean
  inquiryWhatsAppNumber: string | null
  inquiryNotifyViaEmail: boolean
  inquiryEmail: string | null
  city: string | null
  addressLine: string | null
  mapUrl: string | null
  isListed: boolean
}

export default function SettingsPage() {
  const queryClient = useQueryClient()
  const { language: lang, setLang, updateUserName } = useAuth()
  const { theme } = useTheme()
  const { data: business } = useQuery<BusinessSettings>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  const [form, setForm] = useState({
    name: '', phone: '', description: '', language: 'EN' as 'EN' | 'AR' | 'HE',
    maxBookingsPerDay: '', maxBookingsPerWeek: '',
    chatbotWelcomeMessage: '', chatbotConfirmationMessage: '', chatbotFinalMessage: '',
    inquiryWhatsAppNumber: '', inquiryEmail: '',
    city: '', addressLine: '', mapUrl: '',
  })
  // Kept out of `form` above -- that object's values are read generically via
  // form[key as keyof typeof form] in the text-input .map()s below, and mixing in a boolean
  // would widen every one of those reads to `string | boolean`.
  const [waitlistEnabled, setWaitlistEnabled] = useState(false)
  const [requireApprovalOnCustomerCancel, setRequireApprovalOnCustomerCancel] = useState(false)
  const [chatbotEnabled, setChatbotEnabled] = useState(true)
  const [chatbotInquiryEnabled, setChatbotInquiryEnabled] = useState(false)
  const [inquiryNotifyViaWhatsApp, setInquiryNotifyViaWhatsApp] = useState(false)
  const [inquiryNotifyViaEmail, setInquiryNotifyViaEmail] = useState(false)
  const [isListed, setIsListed] = useState(true)
  const [initialized, setInitialized] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')
  const [logoFile, setLogoFile] = useState<File | null>(null)
  const [logoPreview, setLogoPreview] = useState<string | null>(null)
  const [uploadingLogo, setUploadingLogo] = useState(false)
  const [logoError, setLogoError] = useState('')
  const [billingLoading, setBillingLoading] = useState(false)
  const [billingError, setBillingError] = useState('')
  const [searchParams, setSearchParams] = useSearchParams()
  const [showBillingSuccessBanner, setShowBillingSuccessBanner] = useState(false)

  useEffect(() => {
    if (searchParams.get('billing') !== 'success') return
    // SubscriptionStatus flips to ACTIVE asynchronously via the Cardcom webhook, not
    // synchronously on redirect -- refetch once immediately and again ~3s later to catch the
    // common case where the webhook lands within a couple seconds of the redirect. The banner
    // stays up for that same window rather than disappearing the instant the ?billing= param
    // is stripped from the URL below.
    setShowBillingSuccessBanner(true)
    queryClient.invalidateQueries({ queryKey: ['settings'] })
    const timer = setTimeout(() => {
      queryClient.invalidateQueries({ queryKey: ['settings'] })
      setShowBillingSuccessBanner(false)
    }, 3000)
    setSearchParams((prev) => { prev.delete('billing'); return prev }, { replace: true })
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  if (business && !initialized) {
    setForm({
      name: business.name, phone: business.phone ?? '', description: business.description ?? '',
      language: business.language,
      maxBookingsPerDay: business.maxBookingsPerDay?.toString() ?? '',
      maxBookingsPerWeek: business.maxBookingsPerWeek?.toString() ?? '',
      chatbotWelcomeMessage: business.chatbotWelcomeMessage ?? '',
      chatbotConfirmationMessage: business.chatbotConfirmationMessage ?? '',
      chatbotFinalMessage: business.chatbotFinalMessage ?? '',
      inquiryWhatsAppNumber: business.inquiryWhatsAppNumber ?? '', inquiryEmail: business.inquiryEmail ?? '',
      city: business.city ?? '', addressLine: business.addressLine ?? '', mapUrl: business.mapUrl ?? '',
    })
    setWaitlistEnabled(business.waitlistEnabled)
    setRequireApprovalOnCustomerCancel(business.requireApprovalOnCustomerCancel)
    setChatbotEnabled(business.chatbotEnabled)
    setChatbotInquiryEnabled(business.chatbotInquiryEnabled)
    setInquiryNotifyViaWhatsApp(business.inquiryNotifyViaWhatsApp)
    setInquiryNotifyViaEmail(business.inquiryNotifyViaEmail)
    setIsListed(business.isListed)
    setLang(business.language)
    setInitialized(true)
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault(); setSaving(true); setError('')
    const payload: Record<string, string | number | boolean | null | undefined> = {
      name: form.name, phone: form.phone, description: form.description,
      language: form.language,
      maxBookingsPerDay: form.maxBookingsPerDay ? Number(form.maxBookingsPerDay) : null,
      maxBookingsPerWeek: form.maxBookingsPerWeek ? Number(form.maxBookingsPerWeek) : null,
      waitlistEnabled,
      requireApprovalOnCustomerCancel,
      chatbotEnabled,
      chatbotWelcomeMessage: form.chatbotWelcomeMessage || null,
      chatbotConfirmationMessage: form.chatbotConfirmationMessage || null,
      chatbotFinalMessage: form.chatbotFinalMessage || null,
      chatbotInquiryEnabled,
      inquiryNotifyViaWhatsApp,
      inquiryWhatsAppNumber: form.inquiryWhatsAppNumber || null,
      inquiryNotifyViaEmail,
      inquiryEmail: form.inquiryEmail || null,
      city: form.city || null,
      addressLine: form.addressLine || null,
      mapUrl: form.mapUrl || null,
      isListed,
    }
    try {
      await api.patch('/admin/settings', payload)
      setLang(form.language)
      updateUserName(form.name)
      setSaved(true); setTimeout(() => setSaved(false), 2000)
      queryClient.invalidateQueries({ queryKey: ['settings'] })
    } catch (err) {
      const msg = (err as { response?: { data?: { error?: string } } }).response?.data?.error
      setError(msg || 'Failed to save')
    } finally { setSaving(false) }
  }

  function handleLogoChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    setLogoFile(file)
    setLogoPreview(URL.createObjectURL(file))
    setLogoError('')
  }

  async function uploadLogo() {
    if (!logoFile) return
    setUploadingLogo(true); setLogoError('')
    try {
      const formData = new FormData()
      formData.append('file', logoFile)
      await api.post('/admin/settings/logo', formData)
      setLogoFile(null)
      setLogoPreview(null)
      queryClient.invalidateQueries({ queryKey: ['settings'] })
    } catch {
      setLogoError(t(lang, 'photoUploadError'))
    } finally { setUploadingLogo(false) }
  }

  async function handleSubscribe() {
    setBillingLoading(true); setBillingError('')
    try {
      const { data } = await api.post('/billing/checkout-session')
      window.location.href = data.url
    } catch {
      setBillingError(t(lang, 'billingNotConfigured'))
      setBillingLoading(false)
    }
  }

  if (!business) return <div className="text-muted">{t(lang, 'loading')}</div>

  const trialDate = parseISO(business.trialEndsAt)
  const isTrialActive = business.subscriptionStatus === 'TRIAL' && trialDate > new Date()
  const trialDaysLeft = Math.max(0, Math.ceil((trialDate.getTime() - Date.now()) / 86400000))

  return (
    <div>
      <h1 className="text-2xl font-bold text-ink mb-6">{t(lang, 'settings')}</h1>
      <form onSubmit={handleSubmit} className="space-y-6 max-w-2xl">
        {error && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">{error}</div>}
        {showBillingSuccessBanner && (
          <div className="bg-blue-50 border border-blue-200 text-blue-700 dark:bg-blue-900/40 dark:border-blue-800/50 dark:text-blue-400 text-sm rounded-lg px-4 py-3">
            {t(lang, 'billingRedirecting')}
          </div>
        )}

        <div className="bg-surface border border-line rounded-2xl p-6">
          <h2 className="text-ink font-semibold mb-1">Appearance</h2>
          <div className="flex items-center justify-between gap-4 mt-3">
            <div>
              <div className="text-sm font-medium text-ink">{theme === 'dark' ? 'Dark mode' : 'Light mode'}</div>
              <div className="text-muted text-sm mt-0.5">Switch between the light and dark theme.</div>
            </div>
            <ThemeToggle />
          </div>
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6">
          <h2 className="text-ink font-semibold mb-3">{t(lang, 'subscription')}</h2>
          <div className={`text-sm px-4 py-2 rounded-lg inline-block ${
            business.subscriptionStatus === 'ACTIVE' ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400'
              : isTrialActive ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-400' : 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-400'
          }`}>
            {business.subscriptionStatus === 'ACTIVE' ? t(lang, 'subscriptionActive')
              : isTrialActive ? `Free trial · ${trialDaysLeft} days left (expires ${format(trialDate, 'MMM d, yyyy')})`
              : t(lang, 'subscriptionExpired')}
          </div>
          {business.subscriptionStatus !== 'ACTIVE' && (
            <div className="mt-4">
              <button type="button" onClick={handleSubscribe} disabled={billingLoading}
                className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold text-sm px-4 py-2 rounded-lg transition-colors">
                {billingLoading ? t(lang, 'billingRedirecting') : t(lang, 'billingSubscribeNow')}
              </button>
              {billingError && <p className="text-red-600 text-xs mt-2">{billingError}</p>}
            </div>
          )}
          <p className="text-muted text-xs mt-2">
            {t(lang, 'bookingUrl')} <span className="text-coral-dark">{window.location.origin}/{business.slug}</span>
          </p>
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 space-y-4">
          <h2 className="text-ink font-semibold mb-1">{t(lang, 'businessInfo')}</h2>

          <div>
            <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'profilePhoto')}</label>
            <div className="flex items-center gap-4">
              {logoPreview || business.logo ? (
                <img src={logoPreview ?? mediaUrl(business.logo)} alt={business.name}
                  className="w-16 h-16 rounded-full object-cover border border-line" />
              ) : (
                <div className="w-16 h-16 rounded-full bg-cream border border-line flex items-center justify-center text-2xl">✂️</div>
              )}
              <div className="flex-1">
                <input type="file" accept="image/jpeg,image/png,image/webp" onChange={handleLogoChange}
                  className="block w-full text-sm text-muted file:mr-3 file:py-2 file:px-4 file:rounded-lg file:border-0 file:bg-cream file:text-ink hover:file:bg-line file:cursor-pointer cursor-pointer" />
                {logoFile && (
                  <button type="button" disabled={uploadingLogo} onClick={uploadLogo}
                    className="mt-2 bg-coral hover:bg-coral-dark disabled:opacity-50 text-white text-sm font-semibold px-4 py-1.5 rounded-lg transition-colors">
                    {uploadingLogo ? t(lang, 'saving') : t(lang, 'uploadPhoto')}
                  </button>
                )}
                {logoError && <p className="text-red-600 text-xs mt-1">{logoError}</p>}
              </div>
            </div>
          </div>

          {[[t(lang, 'businessName'), 'name', 'text'], [t(lang, 'phone'), 'phone', 'tel'], [t(lang, 'description'), 'description', 'textarea']].map(([label, key, type]) => (
            <div key={key}>
              <label className="block text-sm font-medium text-ink mb-1.5">{label}</label>
              {type === 'textarea' ? (
                <textarea value={form[key as keyof typeof form]} rows={3}
                  onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                  className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink focus:outline-none focus:ring-2 focus:ring-coral resize-none"
                  placeholder="Tell customers about your business..." />
              ) : (
                <input type={type} value={form[key as keyof typeof form]}
                  onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                  className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
              )}
            </div>
          ))}
          <div>
            <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'language')}</label>
            <select value={form.language} onChange={(e) => setForm((f) => ({ ...f, language: e.target.value as 'EN' | 'AR' | 'HE' }))}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink focus:outline-none focus:ring-2 focus:ring-coral">
              <option value="EN">English</option>
              <option value="AR">العربية (Arabic)</option>
              <option value="HE">עברית (Hebrew)</option>
            </select>
          </div>
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 space-y-4">
          <h2 className="text-ink font-semibold mb-1">{t(lang, 'businessLocation')}</h2>
          <div>
            <label htmlFor="settings-city" className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'city')}</label>
            <input id="settings-city" type="text" value={form.city}
              placeholder={t(lang, 'cityPlaceholder')}
              onChange={(e) => setForm((f) => ({ ...f, city: e.target.value }))}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          </div>
          <div>
            <label htmlFor="settings-address" className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'addressLine')}</label>
            <input id="settings-address" type="text" value={form.addressLine}
              onChange={(e) => setForm((f) => ({ ...f, addressLine: e.target.value }))}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          </div>
          <div>
            <label htmlFor="settings-mapurl" className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'mapUrl')}</label>
            <input id="settings-mapurl" type="url" value={form.mapUrl}
              placeholder={t(lang, 'mapUrlPlaceholder')}
              onChange={(e) => setForm((f) => ({ ...f, mapUrl: e.target.value }))}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          </div>
          <label className="flex items-start gap-3 cursor-pointer border-t border-line pt-4">
            <input type="checkbox" checked={isListed}
              onChange={(e) => setIsListed(e.target.checked)}
              className="mt-1 w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
            <span>
              <span className="block text-sm font-medium text-ink">{t(lang, 'listInDirectory')}</span>
              <span className="block text-muted text-sm mt-0.5">{t(lang, 'listInDirectoryHint')}</span>
            </span>
          </label>
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 space-y-4">
          <h2 className="text-ink font-semibold mb-1">{t(lang, 'bookingLimits')}</h2>
          <p className="text-muted text-sm">{t(lang, 'bookingLimitsHint')}</p>
          <div className="grid grid-cols-2 gap-4">
            {[[t(lang, 'maxBookingsPerDay'), 'maxBookingsPerDay'], [t(lang, 'maxBookingsPerWeek'), 'maxBookingsPerWeek']].map(([label, key]) => (
              <div key={key}>
                <label htmlFor={`settings-${key}`} className="block text-sm font-medium text-ink mb-1.5">{label}</label>
                <input id={`settings-${key}`} type="number" min="1" step="1" value={form[key as 'maxBookingsPerDay' | 'maxBookingsPerWeek']}
                  placeholder={t(lang, 'unlimited')}
                  onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                  className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
              </div>
            ))}
          </div>
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 space-y-4">
          <h2 className="text-ink font-semibold mb-1">{t(lang, 'waitlistSettings')}</h2>
          <label className="flex items-start gap-3 cursor-pointer">
            <input type="checkbox" checked={waitlistEnabled}
              onChange={(e) => setWaitlistEnabled(e.target.checked)}
              className="mt-1 w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
            <span>
              <span className="block text-sm font-medium text-ink">{t(lang, 'waitlistEnabledLabel')}</span>
              <span className="block text-muted text-sm mt-0.5">{t(lang, 'waitlistEnabledHint')}</span>
            </span>
          </label>

          <div className="border-t border-line pt-4">
            <p className="text-sm font-medium text-ink mb-2">{t(lang, 'customerCancelPolicyTitle')}</p>
            <label className="flex items-start gap-3 cursor-pointer mb-2">
              <input type="radio" name="customerCancelPolicy" checked={!requireApprovalOnCustomerCancel}
                onChange={() => setRequireApprovalOnCustomerCancel(false)}
                className="mt-1 w-4 h-4 border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
              <span>
                <span className="block text-sm font-medium text-ink">{t(lang, 'customerCancelAutoLabel')}</span>
                <span className="block text-muted text-sm mt-0.5">{t(lang, 'customerCancelAutoHint')}</span>
              </span>
            </label>
            <label className="flex items-start gap-3 cursor-pointer">
              <input type="radio" name="customerCancelPolicy" checked={requireApprovalOnCustomerCancel}
                onChange={() => setRequireApprovalOnCustomerCancel(true)}
                className="mt-1 w-4 h-4 border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
              <span>
                <span className="block text-sm font-medium text-ink">{t(lang, 'customerCancelApprovalLabel')}</span>
                <span className="block text-muted text-sm mt-0.5">{t(lang, 'customerCancelApprovalHint')}</span>
              </span>
            </label>
          </div>
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 space-y-4">
          <h2 className="text-ink font-semibold mb-1">{t(lang, 'chatbotSettings')}</h2>
          <p className="text-muted text-sm">{t(lang, 'chatbotSettingsHint')}</p>

          <label className="flex items-start gap-3 cursor-pointer">
            <input type="checkbox" checked={chatbotEnabled}
              onChange={(e) => setChatbotEnabled(e.target.checked)}
              className="mt-1 w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
            <span>
              <span className="block text-sm font-medium text-ink">{t(lang, 'chatbotEnabledLabel')}</span>
              <span className="block text-muted text-sm mt-0.5">{t(lang, 'chatbotEnabledHint')}</span>
            </span>
          </label>

          <div>
            <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'chatbotWelcomeMessage')}</label>
            <p className="text-muted text-xs mb-1.5">{t(lang, 'chatbotWelcomeMessageHint')}</p>
            <textarea value={form.chatbotWelcomeMessage} rows={2}
              onChange={(e) => setForm((f) => ({ ...f, chatbotWelcomeMessage: e.target.value }))}
              placeholder={t(lang, 'chatbotDefaultTextPlaceholder')}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral resize-none" />
          </div>

          <div>
            <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'chatbotConfirmationMessage')}</label>
            <p className="text-muted text-xs mb-1.5">{t(lang, 'chatbotConfirmationMessageHint')}</p>
            <textarea value={form.chatbotConfirmationMessage} rows={2}
              onChange={(e) => setForm((f) => ({ ...f, chatbotConfirmationMessage: e.target.value }))}
              placeholder={t(lang, 'chatbotDefaultTextPlaceholder')}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral resize-none" />
          </div>

          <div>
            <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'chatbotFinalMessage')}</label>
            <p className="text-muted text-xs mb-1.5">{t(lang, 'chatbotFinalMessageHint')}</p>
            <textarea value={form.chatbotFinalMessage} rows={2}
              onChange={(e) => setForm((f) => ({ ...f, chatbotFinalMessage: e.target.value }))}
              placeholder={t(lang, 'chatbotDefaultTextPlaceholder')}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral resize-none" />
          </div>

          <div className="border-t border-line pt-4">
            <label className="flex items-start gap-3 cursor-pointer">
              <input type="checkbox" checked={chatbotInquiryEnabled}
                onChange={(e) => setChatbotInquiryEnabled(e.target.checked)}
                className="mt-1 w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
              <span>
                <span className="block text-sm font-medium text-ink">{t(lang, 'chatbotInquiryEnabledLabel')}</span>
                <span className="block text-muted text-sm mt-0.5">{t(lang, 'chatbotInquiryEnabledHint')}</span>
              </span>
            </label>

            {chatbotInquiryEnabled && (
              <div className="mt-4 ms-7 space-y-3">
                <h3 className="text-sm font-semibold text-ink">{t(lang, 'inquiryNotifications')}</h3>

                <label className="flex items-center gap-3 cursor-pointer">
                  <input type="checkbox" checked={inquiryNotifyViaWhatsApp}
                    onChange={(e) => setInquiryNotifyViaWhatsApp(e.target.checked)}
                    className="w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
                  <span className="text-sm text-ink">{t(lang, 'inquiryNotifyViaWhatsApp')}</span>
                </label>
                {inquiryNotifyViaWhatsApp && (
                  <input value={form.inquiryWhatsAppNumber} type="tel"
                    onChange={(e) => setForm((f) => ({ ...f, inquiryWhatsAppNumber: e.target.value }))}
                    placeholder={t(lang, 'inquiryWhatsAppNumberPlaceholder')}
                    className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral" />
                )}

                <label className="flex items-center gap-3 cursor-pointer">
                  <input type="checkbox" checked={inquiryNotifyViaEmail}
                    onChange={(e) => setInquiryNotifyViaEmail(e.target.checked)}
                    className="w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
                  <span className="text-sm text-ink">{t(lang, 'inquiryNotifyViaEmail')}</span>
                </label>
                {inquiryNotifyViaEmail && (
                  <input value={form.inquiryEmail} type="email"
                    onChange={(e) => setForm((f) => ({ ...f, inquiryEmail: e.target.value }))}
                    placeholder={t(lang, 'inquiryEmailPlaceholder')}
                    className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral" />
                )}

                <p className="text-muted text-xs">{t(lang, 'inquiryInboxHint')}</p>
              </div>
            )}
          </div>
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 space-y-2">
          <h2 className="text-ink font-semibold mb-1">{t(lang, 'whatsappSetup')}</h2>
          <p className="text-muted text-sm">{t(lang, 'whatsappNumberHint')}</p>
          <div className="bg-cream border border-line rounded-lg px-3 py-2.5 text-ink font-mono">
            {business?.whatsAppNumber || <span className="text-muted font-sans italic">{t(lang, 'whatsappNumberUnassigned')}</span>}
          </div>
        </div>

        <button type="submit" disabled={saving}
          className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold px-8 py-2.5 rounded-lg transition-colors">
          {saved ? t(lang, 'saved') : saving ? t(lang, 'saving') : t(lang, 'saveChanges')}
        </button>
      </form>
    </div>
  )
}
