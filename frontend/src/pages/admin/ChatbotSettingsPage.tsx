import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'
import { useUnsavedChangesGuard } from '../../components/admin/useUnsavedChangesGuard'

type ChatbotSettings = {
  slug: string
  chatbotEnabled: boolean
  chatbotWelcomeMessageEn: string | null
  chatbotWelcomeMessageAr: string | null
  chatbotWelcomeMessageHe: string | null
  chatbotConfirmationMessageEn: string | null
  chatbotConfirmationMessageAr: string | null
  chatbotConfirmationMessageHe: string | null
  chatbotFinalMessageEn: string | null
  chatbotFinalMessageAr: string | null
  chatbotFinalMessageHe: string | null
  chatbotDefaultLanguage: string | null
  chatbotInquiryEnabled: boolean
  inquiryNotifyViaWhatsApp: boolean
  inquiryWhatsAppNumber: string | null
  inquiryNotifyViaEmail: boolean
  inquiryEmail: string | null
}

type MessageFieldKey =
  | 'chatbotWelcomeMessageEn' | 'chatbotWelcomeMessageAr' | 'chatbotWelcomeMessageHe'
  | 'chatbotConfirmationMessageEn' | 'chatbotConfirmationMessageAr' | 'chatbotConfirmationMessageHe'
  | 'chatbotFinalMessageEn' | 'chatbotFinalMessageAr' | 'chatbotFinalMessageHe'

type ChatbotForm = {
  chatbotWelcomeMessageEn: string; chatbotWelcomeMessageAr: string; chatbotWelcomeMessageHe: string
  chatbotConfirmationMessageEn: string; chatbotConfirmationMessageAr: string; chatbotConfirmationMessageHe: string
  chatbotFinalMessageEn: string; chatbotFinalMessageAr: string; chatbotFinalMessageHe: string
  inquiryWhatsAppNumber: string; inquiryEmail: string
}

type LangNameKey = 'langEnglish' | 'langArabic' | 'langHebrew'
type MessageTypeId = 'welcome' | 'confirmation' | 'final'

const MESSAGE_TYPES: {
  id: MessageTypeId
  labelKey: 'chatbotWelcomeMessage' | 'chatbotConfirmationMessage' | 'chatbotFinalMessage'
  hintKey: 'chatbotWelcomeMessageHint' | 'chatbotConfirmationMessageHint' | 'chatbotFinalMessageHint'
  fields: [LangNameKey, MessageFieldKey][]
}[] = [
  {
    id: 'welcome', labelKey: 'chatbotWelcomeMessage', hintKey: 'chatbotWelcomeMessageHint',
    fields: [['langEnglish', 'chatbotWelcomeMessageEn'], ['langArabic', 'chatbotWelcomeMessageAr'], ['langHebrew', 'chatbotWelcomeMessageHe']],
  },
  {
    id: 'confirmation', labelKey: 'chatbotConfirmationMessage', hintKey: 'chatbotConfirmationMessageHint',
    fields: [['langEnglish', 'chatbotConfirmationMessageEn'], ['langArabic', 'chatbotConfirmationMessageAr'], ['langHebrew', 'chatbotConfirmationMessageHe']],
  },
  {
    id: 'final', labelKey: 'chatbotFinalMessage', hintKey: 'chatbotFinalMessageHint',
    fields: [['langEnglish', 'chatbotFinalMessageEn'], ['langArabic', 'chatbotFinalMessageAr'], ['langHebrew', 'chatbotFinalMessageHe']],
  },
]

type ChatbotValues = {
  form: ChatbotForm
  chatbotEnabled: boolean
  chatbotDefaultLanguage: '' | 'EN' | 'AR' | 'HE'
  chatbotInquiryEnabled: boolean
  inquiryNotifyViaWhatsApp: boolean
  inquiryNotifyViaEmail: boolean
}

function valuesFrom(business: ChatbotSettings): ChatbotValues {
  return {
    form: {
      chatbotWelcomeMessageEn: business.chatbotWelcomeMessageEn ?? '',
      chatbotWelcomeMessageAr: business.chatbotWelcomeMessageAr ?? '',
      chatbotWelcomeMessageHe: business.chatbotWelcomeMessageHe ?? '',
      chatbotConfirmationMessageEn: business.chatbotConfirmationMessageEn ?? '',
      chatbotConfirmationMessageAr: business.chatbotConfirmationMessageAr ?? '',
      chatbotConfirmationMessageHe: business.chatbotConfirmationMessageHe ?? '',
      chatbotFinalMessageEn: business.chatbotFinalMessageEn ?? '',
      chatbotFinalMessageAr: business.chatbotFinalMessageAr ?? '',
      chatbotFinalMessageHe: business.chatbotFinalMessageHe ?? '',
      inquiryWhatsAppNumber: business.inquiryWhatsAppNumber ?? '',
      inquiryEmail: business.inquiryEmail ?? '',
    },
    chatbotEnabled: business.chatbotEnabled,
    chatbotDefaultLanguage: (business.chatbotDefaultLanguage as 'EN' | 'AR' | 'HE' | null) ?? '',
    chatbotInquiryEnabled: business.chatbotInquiryEnabled,
    inquiryNotifyViaWhatsApp: business.inquiryNotifyViaWhatsApp,
    inquiryNotifyViaEmail: business.inquiryNotifyViaEmail,
  }
}

// What Save sends -- also compared against the server's values to tell whether there are unsaved edits.
function toPayload(v: ChatbotValues) {
  const { form } = v
  return {
    chatbotEnabled: v.chatbotEnabled,
    chatbotWelcomeMessageEn: form.chatbotWelcomeMessageEn || null,
    chatbotWelcomeMessageAr: form.chatbotWelcomeMessageAr || null,
    chatbotWelcomeMessageHe: form.chatbotWelcomeMessageHe || null,
    chatbotConfirmationMessageEn: form.chatbotConfirmationMessageEn || null,
    chatbotConfirmationMessageAr: form.chatbotConfirmationMessageAr || null,
    chatbotConfirmationMessageHe: form.chatbotConfirmationMessageHe || null,
    chatbotFinalMessageEn: form.chatbotFinalMessageEn || null,
    chatbotFinalMessageAr: form.chatbotFinalMessageAr || null,
    chatbotFinalMessageHe: form.chatbotFinalMessageHe || null,
    chatbotDefaultLanguage: v.chatbotDefaultLanguage || null,
    chatbotInquiryEnabled: v.chatbotInquiryEnabled,
    inquiryNotifyViaWhatsApp: v.inquiryNotifyViaWhatsApp,
    inquiryWhatsAppNumber: form.inquiryWhatsAppNumber || null,
    inquiryNotifyViaEmail: v.inquiryNotifyViaEmail,
    inquiryEmail: form.inquiryEmail || null,
  }
}

export default function ChatbotSettingsPage() {
  const queryClient = useQueryClient()
  const { language: lang } = useAuth()
  const { data: business } = useQuery<ChatbotSettings>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })
  const [openMessageModal, setOpenMessageModal] = useState<MessageTypeId | null>(null)

  const [form, setForm] = useState<ChatbotForm>({
    chatbotWelcomeMessageEn: '', chatbotWelcomeMessageAr: '', chatbotWelcomeMessageHe: '',
    chatbotConfirmationMessageEn: '', chatbotConfirmationMessageAr: '', chatbotConfirmationMessageHe: '',
    chatbotFinalMessageEn: '', chatbotFinalMessageAr: '', chatbotFinalMessageHe: '',
    inquiryWhatsAppNumber: '', inquiryEmail: '',
  })
  const [chatbotEnabled, setChatbotEnabled] = useState(true)
  const [chatbotDefaultLanguage, setChatbotDefaultLanguage] = useState<'' | 'EN' | 'AR' | 'HE'>('')
  const [chatbotInquiryEnabled, setChatbotInquiryEnabled] = useState(false)
  const [inquiryNotifyViaWhatsApp, setInquiryNotifyViaWhatsApp] = useState(false)
  const [inquiryNotifyViaEmail, setInquiryNotifyViaEmail] = useState(false)
  // Which business the form was filled from -- refills if a different owner's settings arrive,
  // instead of keeping (and later saving) the previous business's values.
  const [initializedFor, setInitializedFor] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')

  if (business && initializedFor !== business.slug) {
    const v = valuesFrom(business)
    setForm(v.form)
    setChatbotEnabled(v.chatbotEnabled)
    setChatbotDefaultLanguage(v.chatbotDefaultLanguage)
    setChatbotInquiryEnabled(v.chatbotInquiryEnabled)
    setInquiryNotifyViaWhatsApp(v.inquiryNotifyViaWhatsApp)
    setInquiryNotifyViaEmail(v.inquiryNotifyViaEmail)
    setInitializedFor(business.slug)
  }

  const payload = toPayload({
    form, chatbotEnabled, chatbotDefaultLanguage, chatbotInquiryEnabled, inquiryNotifyViaWhatsApp, inquiryNotifyViaEmail,
  })
  // Compared against the last save too, so leaving right after Save doesn't ask while the
  // settings refetch is still in flight.
  const payloadJson = JSON.stringify(payload)
  const [lastSavedJson, setLastSavedJson] = useState<string | null>(null)
  const dirty = !!business && initializedFor === business.slug
    && payloadJson !== JSON.stringify(toPayload(valuesFrom(business))) && payloadJson !== lastSavedJson
  const unsavedDialog = useUnsavedChangesGuard(dirty, lang)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault(); setSaving(true); setError('')
    try {
      await api.patch('/admin/settings', payload)
      setLastSavedJson(payloadJson)
      setSaved(true); setTimeout(() => setSaved(false), 2000)
      queryClient.invalidateQueries({ queryKey: ['settings'] })
    } catch (err) {
      const msg = (err as { response?: { data?: { error?: string } } }).response?.data?.error
      setError(msg || t(lang, 'failedToSave'))
    } finally { setSaving(false) }
  }

  if (!business) return <div className="text-muted">{t(lang, 'loading')}</div>

  return (
    <div>
      {unsavedDialog}
      <h1 className="text-2xl font-bold text-ink mb-6">{t(lang, 'chatbotSettings')}</h1>
      <form onSubmit={handleSubmit} className="space-y-6 max-w-2xl">
        {error && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">{error}</div>}

        <div className="bg-surface border border-line rounded-2xl p-6 space-y-4">
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

          <div className="space-y-2 border-t border-line pt-4 first:border-t-0 first:pt-0">
            {MESSAGE_TYPES.map((mt) => {
              const filledCount = mt.fields.filter(([, key]) => form[key].trim() !== '').length
              return (
                <button key={mt.id} type="button" onClick={() => setOpenMessageModal(mt.id)}
                  className="w-full flex items-center justify-between gap-3 bg-cream hover:bg-cream/70 rounded-lg px-4 py-3 text-start transition-colors">
                  <span>
                    <span className="block text-sm font-medium text-ink">{t(lang, mt.labelKey)}</span>
                    <span className="block text-muted text-xs mt-0.5">{t(lang, mt.hintKey)}</span>
                  </span>
                  <span className="shrink-0 text-xs text-muted">
                    {filledCount > 0 ? `${filledCount}/3` : t(lang, 'chatbotDefaultTextPlaceholder')}
                  </span>
                </button>
              )
            })}
          </div>

          <div>
            <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'chatbotDefaultLanguage')}</label>
            <p className="text-muted text-xs mb-1.5">{t(lang, 'chatbotDefaultLanguageHint')}</p>
            <select value={chatbotDefaultLanguage}
              onChange={(e) => setChatbotDefaultLanguage(e.target.value as '' | 'EN' | 'AR' | 'HE')}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink focus:outline-none focus:ring-2 focus:ring-coral">
              <option value="">{t(lang, 'chatbotDefaultLanguageSameAsBusiness')}</option>
              <option value="EN">English</option>
              <option value="AR">العربية (Arabic)</option>
              <option value="HE">עברית (Hebrew)</option>
            </select>
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

                {(inquiryNotifyViaWhatsApp || inquiryNotifyViaEmail) && (
                  <p className="text-muted text-xs">{t(lang, 'inquiryContactFallback')}</p>
                )}
              </div>
            )}
          </div>
        </div>

        <button type="submit" disabled={saving}
          className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold px-8 py-2.5 rounded-lg transition-colors">
          {saved ? t(lang, 'saved') : saving ? t(lang, 'saving') : t(lang, 'saveChanges')}
        </button>
      </form>

      {openMessageModal && (
        <MessageEditorModal
          lang={lang}
          messageType={MESSAGE_TYPES.find((mt) => mt.id === openMessageModal)!}
          form={form}
          setForm={setForm}
          onClose={() => setOpenMessageModal(null)}
        />
      )}
    </div>
  )
}

function MessageEditorModal({
  lang, messageType, form, setForm, onClose,
}: {
  lang: string
  messageType: (typeof MESSAGE_TYPES)[number]
  form: ChatbotForm
  setForm: React.Dispatch<React.SetStateAction<ChatbotForm>>
  onClose: () => void
}) {
  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div onClick={(e) => e.stopPropagation()} className="bg-surface rounded-2xl p-6 w-full max-w-lg border border-line max-h-[90vh] overflow-y-auto">
        <div className="relative mb-4">
          <div className="px-11">
            <h2 className="text-ink font-semibold text-lg">{t(lang, messageType.labelKey)}</h2>
            <p className="text-muted text-xs mt-0.5">{t(lang, messageType.hintKey)}</p>
          </div>
          <button onClick={onClose}
            className="absolute top-1/2 -translate-y-1/2 end-0 w-11 h-11 flex items-center justify-center rounded-lg text-muted hover:text-ink hover:bg-cream text-2xl leading-none transition-colors"
            aria-label={t(lang, 'close')}>✕</button>
        </div>

        <div className="space-y-4">
          {messageType.fields.map(([langKey, key]) => (
            <div key={key}>
              <label className="block text-xs text-muted mb-1">{t(lang, messageType.labelKey)} ({t(lang, langKey)})</label>
              <textarea value={form[key]} rows={3} maxLength={1000}
                onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                placeholder={t(lang, 'chatbotDefaultTextPlaceholder')}
                className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral resize-none" />
            </div>
          ))}
        </div>

        <button onClick={onClose}
          className="w-full mt-6 bg-coral hover:bg-coral-dark text-white font-semibold py-2.5 rounded-lg transition-colors">
          {t(lang, 'close')}
        </button>
      </div>
    </div>
  )
}
