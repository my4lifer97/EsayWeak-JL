import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../../lib/api'
import ThemeToggle from '../../components/ThemeToggle'

type BusinessType = { id: string; key: string; displayNameEn: string }

// First name / family name feed the auto-generated login username, so they must be English letters.
const ENGLISH_NAME = /^[A-Za-z][A-Za-z '-]*$/

export default function RequestBusinessAccountPage() {
  const [businessName, setBusinessName] = useState('')
  const [firstName, setFirstName] = useState('')
  const [familyName, setFamilyName] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const [businessTypeId, setBusinessTypeId] = useState('')
  const [description, setDescription] = useState('')
  const [systemNeeds, setSystemNeeds] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [submitted, setSubmitted] = useState(false)

  const [emailCode, setEmailCode] = useState('')
  const [codeSentForEmail, setCodeSentForEmail] = useState<string | null>(null)
  const [sendingCode, setSendingCode] = useState(false)
  const [codeError, setCodeError] = useState('')
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

  async function handleSendCode() {
    setCodeError(''); setSendingCode(true)
    try {
      const { data } = await api.post('/business-owner-requests/send-email-code', { email })
      setCodeSentForEmail(email)
      setEmailCode('')
      if (data.devCode) { setDevCode(data.devCode); setEmailCode(data.devCode) }
    } catch (err: unknown) {
      const resp = (err as { response?: { status?: number; data?: { error?: string } } })?.response
      setCodeError(resp?.status === 429 ? 'Please wait before requesting another code.' : (resp?.data?.error ?? 'Could not send the code.'))
    } finally {
      setSendingCode(false)
    }
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    if (!ENGLISH_NAME.test(firstName.trim()) || !ENGLISH_NAME.test(familyName.trim())) {
      setError('First name and family name must be in English letters.')
      return
    }
    if (phoneDigitCount < 7 || phoneDigitCount > 15) {
      setError('Please enter a valid phone number.')
      return
    }
    if (!codeSent) {
      setError('Please verify your email address first.')
      return
    }
    if (emailCode.trim().length !== 6) {
      setError('Please enter the 6-digit code sent to your email.')
      return
    }
    if (!businessTypeId) {
      setError('Please choose a business type.')
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
      if (resp?.status === 409) setError('A request from this email is already pending review.')
      else setError(resp?.data?.error ?? 'Could not submit your request. Please try again.')
    } finally {
      setLoading(false)
    }
  }

  const inputClass =
    'w-full bg-cream border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral'

  return (
    <div className="min-h-screen bg-cream flex items-center justify-center p-4 relative">
      <div className="absolute top-4 end-4"><ThemeToggle /></div>
      <div className="w-full max-w-md py-10">
        <h1 className="text-2xl font-bold text-ink mb-2 text-center">Request a business account</h1>
        <p className="text-muted text-center mb-8">
          Tell us about your business. We review every request and email your login once approved.
        </p>

        {submitted ? (
          <div className="bg-green-50 border border-green-200 dark:bg-green-900/40 dark:border-green-800/50 rounded-xl px-4 py-6 text-center">
            <p className="text-green-700 dark:text-green-400 font-medium mb-2">Thanks — your request has been submitted.</p>
            <p className="text-muted text-sm">
              Once an admin approves it, you'll get an email with your username and a temporary password.
            </p>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-5">
            {error && (
              <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">{error}</div>
            )}

            <div>
              <h2 className="text-xs font-semibold uppercase tracking-wide text-muted mb-2">Owner</h2>
              <div className="space-y-3">
                <div className="grid grid-cols-2 gap-3">
                  <input
                    required value={firstName} onChange={(e) => setFirstName(e.target.value)}
                    placeholder="First name" autoComplete="given-name" className={inputClass}
                  />
                  <input
                    required value={familyName} onChange={(e) => setFamilyName(e.target.value)}
                    placeholder="Family name" autoComplete="family-name" className={inputClass}
                  />
                </div>
                <p className={`text-xs ${nameError ? 'text-red-600 dark:text-red-400' : 'text-muted'}`}>
                  Enter both names in English — they're used to create your login username.
                </p>
                <div className="flex gap-2">
                  <input
                    type="email" required value={email} onChange={(e) => setEmail(e.target.value)}
                    placeholder="Active email address" autoComplete="email" className={`flex-1 ${inputClass}`}
                  />
                  <button type="button" onClick={handleSendCode} disabled={!emailLooksValid || sendingCode}
                    className="shrink-0 border border-line text-ink hover:bg-cream disabled:opacity-50 font-semibold text-sm px-4 rounded-xl transition-colors">
                    {sendingCode ? '…' : codeSent ? 'Resend' : 'Send code'}
                  </button>
                </div>
                {codeError && <p className="text-xs text-red-600 dark:text-red-400">{codeError}</p>}
                {codeSent && (
                  <div>
                    <input
                      value={emailCode} onChange={(e) => setEmailCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                      placeholder="6-digit code from your email" inputMode="numeric" className={inputClass}
                    />
                    {devCode && (
                      <p className="text-xs text-muted mt-1">Dev mode — your code is <span className="font-mono font-bold">{devCode}</span></p>
                    )}
                  </div>
                )}
                <input
                  type="tel" required value={phone} onChange={(e) => setPhone(e.target.value)}
                  placeholder="Phone number" autoComplete="tel" className={inputClass}
                />
                {phoneError && (
                  <p className="text-xs text-red-600 dark:text-red-400">Enter a valid phone number (7–15 digits).</p>
                )}
              </div>
            </div>

            <div>
              <h2 className="text-xs font-semibold uppercase tracking-wide text-muted mb-2">Business</h2>
              <div className="space-y-3">
                <input
                  required value={businessName} onChange={(e) => setBusinessName(e.target.value)}
                  placeholder="Business name" className={inputClass}
                />
                <select
                  required value={businessTypeId} onChange={(e) => setBusinessTypeId(e.target.value)}
                  className={inputClass}
                >
                  <option value="">Business type…</option>
                  {businessTypes?.map((t) => (
                    <option key={t.id} value={t.id}>{t.displayNameEn}</option>
                  ))}
                </select>
                <textarea
                  value={description} onChange={(e) => setDescription(e.target.value)}
                  placeholder="Describe your business" rows={3} className={inputClass}
                />
                <textarea
                  value={systemNeeds} onChange={(e) => setSystemNeeds(e.target.value)}
                  placeholder="What do you want to use the system for?" rows={3} className={inputClass}
                />
              </div>
            </div>

            <button
              type="submit" disabled={loading || !!nameError}
              className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
            >
              {loading ? '…' : 'Submit request'}
            </button>
          </form>
        )}

        <p className="text-muted text-center mt-6 text-sm">
          <Link to="/admin/login" className="text-coral-dark hover:underline">← Back to sign in</Link>
        </p>
      </div>
    </div>
  )
}
