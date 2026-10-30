import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t, type TKey } from '../../lib/i18n'
import OwnerAuthShell from '../../components/admin/OwnerAuthShell'

type BusinessType = { id: string; key: string; displayNameEn: string; displayNameAr: string; displayNameHe: string }
// A translation key, or the server's own message (which is not translated).
type Message = { key: TKey } | { text: string }

// First name / family name feed the auto-generated login username, so they must be English letters.
const ENGLISH_NAME = /^[A-Za-z][A-Za-z '-]*$/

export default function RequestBusinessAccountPage() {
  // A prospective owner: same language preference as the owner sign-in pages.
  const { language: lang } = useAuth()
  const [businessName, setBusinessName] = useState('')
  const [firstName, setFirstName] = useState('')
  const [familyName, setFamilyName] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const [businessTypeId, setBusinessTypeId] = useState('')
  const [description, setDescription] = useState('')
  const [systemNeeds, setSystemNeeds] = useState('')
  const [error, setError] = useState<Message | null>(null)
  const [loading, setLoading] = useState(false)
  const [submitted, setSubmitted] = useState(false)

  const [emailCode, setEmailCode] = useState('')
  const [codeSentForEmail, setCodeSentForEmail] = useState<string | null>(null)
  const [sendingCode, setSendingCode] = useState(false)
  const [codeError, setCodeError] = useState<Message | null>(null)
  const [devCode, setDevCode] = useState<string | null>(null)

  const { data: businessTypes } = useQuery<BusinessType[]>({
    queryKey: ['business-types'],
    queryFn: () => api.get('/business-types').then((r) => r.data),
  })

  const nameError =
    (firstName && !ENGLISH_NAME.test(firstName.trim())) || (familyName && !ENGLISH_NAME.test(familyName.trim()))

  const phoneDigitCount = phone.replace(/\D/g, '').length
  const phoneError = phone.length > 0 && (phoneDigitCount < 7 || phoneDigitCount > 15)

  const emailLooksValid = /\S+@\S+\.\S+/.test(email)
  const codeSent = !!email && codeSentForEmail === email

  function show(m: Message) {
    return 'key' in m ? t(lang, m.key) : m.text
  }

  function typeName(bt: BusinessType) {
    if (lang === 'AR') return bt.displayNameAr
    if (lang === 'HE') return bt.displayNameHe
    return bt.displayNameEn
  }

  async function handleSendCode() {
    setCodeError(null); setSendingCode(true)
    try {
      const { data } = await api.post('/business-owner-requests/send-email-code', { email })
      setCodeSentForEmail(email)
      setEmailCode('')
      if (data.devCode) { setDevCode(data.devCode); setEmailCode(data.devCode) }
    } catch (err: unknown) {
      const resp = (err as { response?: { status?: number; data?: { error?: string } } })?.response
      if (resp?.status === 429) setCodeError({ key: 'reqWaitForCode' })
      else setCodeError(resp?.data?.error ? { text: resp.data.error } : { key: 'reqSendCodeFailed' })
    } finally {
      setSendingCode(false)
    }
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (!ENGLISH_NAME.test(firstName.trim()) || !ENGLISH_NAME.test(familyName.trim())) {
      setError({ key: 'reqNamesEnglish' })
      return
    }
    if (phoneDigitCount < 7 || phoneDigitCount > 15) {
      setError({ key: 'reqPhoneInvalid' })
      return
    }
    if (!codeSent) {
      setError({ key: 'reqVerifyEmailFirst' })
      return
    }
    if (emailCode.trim().length !== 6) {
      setError({ key: 'reqEnterCode' })
      return
    }
    if (!businessTypeId) {
      setError({ key: 'reqChooseType' })
      return
    }
    setLoading(true)
    try {
      await api.post('/business-owner-requests', {
        businessName,
        ownerFirstName: firstName.trim(),
        ownerFamilyName: familyName.trim(),
        email,
        phone,
        businessTypeId,
        businessDescription: description || null,
        systemNeeds: systemNeeds || null,
        code: emailCode.trim(),
      })
      setSubmitted(true)
    } catch (err: unknown) {
      const resp = (err as { response?: { status?: number; data?: { error?: string } } })?.response
      if (resp?.status === 409) setError({ key: 'reqAlreadyPending' })
      else setError(resp?.data?.error ? { text: resp.data.error } : { key: 'reqSubmitFailed' })
    } finally {
      setLoading(false)
    }
  }

  const inputClass =
    'w-full bg-cream border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral'

  return (
    <OwnerAuthShell wide>
      <h1 className="text-2xl font-bold text-ink mb-2 text-center">{t(lang, 'reqTitle')}</h1>
      <p className="text-muted text-center mb-8">{t(lang, 'reqIntro')}</p>

      {submitted ? (
        <div className="bg-green-50 border border-green-200 dark:bg-green-900/40 dark:border-green-800/50 rounded-xl px-4 py-6 text-center">
          <p className="text-green-700 dark:text-green-400 font-medium mb-2">{t(lang, 'reqSubmittedTitle')}</p>
          <p className="text-muted text-sm">{t(lang, 'reqSubmittedBody')}</p>
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="space-y-5">
          {error && (
            <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">{show(error)}</div>
          )}

          <div>
            <h2 className="text-xs font-semibold uppercase tracking-wide text-muted mb-2">{t(lang, 'reqOwner')}</h2>
            <div className="space-y-3">
              <div className="grid grid-cols-2 gap-3">
                <input
                  required value={firstName} onChange={(e) => setFirstName(e.target.value)}
                  placeholder={t(lang, 'reqFirstName')} autoComplete="given-name" className={inputClass}
                />
                <input
                  required value={familyName} onChange={(e) => setFamilyName(e.target.value)}
                  placeholder={t(lang, 'reqFamilyName')} autoComplete="family-name" className={inputClass}
                />
              </div>
              <p className={`text-xs ${nameError ? 'text-red-600 dark:text-red-400' : 'text-muted'}`}>
                {t(lang, 'reqEnglishNamesHint')}
              </p>
              <div className="flex gap-2">
                <input
                  type="email" dir="ltr" required value={email} onChange={(e) => setEmail(e.target.value)}
                  placeholder={t(lang, 'reqEmail')} autoComplete="email" className={`flex-1 min-w-0 ${inputClass}`}
                />
                <button type="button" onClick={handleSendCode} disabled={!emailLooksValid || sendingCode}
                  className="shrink-0 border border-line text-ink hover:bg-cream disabled:opacity-50 font-semibold text-sm px-4 rounded-xl transition-colors">
                  {sendingCode ? '…' : codeSent ? t(lang, 'reqResend') : t(lang, 'reqSendCode')}
                </button>
              </div>
              {codeError && <p className="text-xs text-red-600 dark:text-red-400">{show(codeError)}</p>}
              {codeSent && (
                <div>
                  <input
                    value={emailCode} onChange={(e) => setEmailCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                    placeholder={t(lang, 'reqCodePlaceholder')} inputMode="numeric" className={inputClass}
                  />
                  {devCode && (
                    <p className="text-xs text-muted mt-1">{t(lang, 'authDevCode')} <span className="font-mono font-bold">{devCode}</span></p>
                  )}
                </div>
              )}
              <input
                type="tel" required value={phone} onChange={(e) => setPhone(e.target.value)}
                placeholder={t(lang, 'reqPhone')} autoComplete="tel" dir="ltr" className={inputClass}
              />
              {phoneError && (
                <p className="text-xs text-red-600 dark:text-red-400">{t(lang, 'reqPhoneInvalidHint')}</p>
              )}
            </div>
          </div>

          <div>
            <h2 className="text-xs font-semibold uppercase tracking-wide text-muted mb-2">{t(lang, 'reqBusiness')}</h2>
            <div className="space-y-3">
              <input
                required value={businessName} onChange={(e) => setBusinessName(e.target.value)}
                placeholder={t(lang, 'reqBusinessName')} className={inputClass}
              />
              <select
                required value={businessTypeId} onChange={(e) => setBusinessTypeId(e.target.value)}
                className={inputClass}
              >
                <option value="">{t(lang, 'authBusinessType')}</option>
                {businessTypes?.map((bt) => (
                  <option key={bt.id} value={bt.id}>{typeName(bt)}</option>
                ))}
              </select>
              <textarea
                value={description} onChange={(e) => setDescription(e.target.value)}
                placeholder={t(lang, 'reqDescription')} rows={3} className={inputClass}
              />
              <textarea
                value={systemNeeds} onChange={(e) => setSystemNeeds(e.target.value)}
                placeholder={t(lang, 'reqSystemNeeds')} rows={3} className={inputClass}
              />
            </div>
          </div>

          <button
            type="submit" disabled={loading || !!nameError}
            className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
          >
            {loading ? '…' : t(lang, 'reqSubmit')}
          </button>
        </form>
      )}

      <p className="text-muted text-center mt-6 text-sm">
        <Link to="/admin/login" className="text-coral-dark hover:underline">{t(lang, 'authBackToSignIn')}</Link>
      </p>
    </OwnerAuthShell>
  )
}
