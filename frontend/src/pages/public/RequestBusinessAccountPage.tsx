import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../../lib/api'

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

  const { data: businessTypes } = useQuery<BusinessType[]>({
    queryKey: ['business-types'],
    queryFn: () => api.get('/business-types').then((r) => r.data),
  })

  const nameError =
    (firstName && !ENGLISH_NAME.test(firstName.trim())) || (familyName && !ENGLISH_NAME.test(familyName.trim()))

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    if (!ENGLISH_NAME.test(firstName.trim()) || !ENGLISH_NAME.test(familyName.trim())) {
      setError('First name and family name must be in English letters.')
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
    'w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500'

  return (
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4">
      <div className="w-full max-w-md py-10">
        <h1 className="text-2xl font-bold text-white mb-2 text-center">Request a business account</h1>
        <p className="text-gray-400 text-center mb-8">
          Tell us about your business. We review every request and email your login once approved.
        </p>

        {submitted ? (
          <div className="bg-green-900/30 border border-green-700/50 rounded-xl px-4 py-6 text-center">
            <p className="text-green-300 font-medium mb-2">Thanks — your request has been submitted.</p>
            <p className="text-gray-400 text-sm">
              Once an admin approves it, you'll get an email with your username and a temporary password.
            </p>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-5">
            {error && (
              <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3">{error}</div>
            )}

            <div>
              <h2 className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">Owner</h2>
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
                <p className={`text-xs ${nameError ? 'text-red-400' : 'text-gray-500'}`}>
                  Enter both names in English — they're used to create your login username.
                </p>
                <input
                  type="email" required value={email} onChange={(e) => setEmail(e.target.value)}
                  placeholder="Active email address" autoComplete="email" className={inputClass}
                />
                <input
                  required value={phone} onChange={(e) => setPhone(e.target.value)}
                  placeholder="Phone number" autoComplete="tel" className={inputClass}
                />
              </div>
            </div>

            <div>
              <h2 className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">Business</h2>
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
              className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
            >
              {loading ? '…' : 'Submit request'}
            </button>
          </form>
        )}

        <p className="text-gray-500 text-center mt-6 text-sm">
          <Link to="/admin/login" className="text-blue-400 hover:underline">← Back to sign in</Link>
        </p>
      </div>
    </div>
  )
}
