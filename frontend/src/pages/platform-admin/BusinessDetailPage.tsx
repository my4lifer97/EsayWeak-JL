import { useEffect, useState, type FormEvent } from 'react'
import { useParams, Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { platformAdminApi } from '../../lib/platformAdminApi'
import { ActivityLogTable, type ActivityLogEntry } from '../../components/platform-admin/ActivityLogTable'
import ThemeToggle from '../../components/ThemeToggle'

type BusinessDetail = {
  id: string; name: string; email: string; slug: string; phone: string | null
  trialEndsAt: string; subscriptionStatus: string; createdAt: string; whatsAppNumber: string | null
  isDisabled: boolean
}

type WhatsAppLinkStatus = { state: 'qr' | 'connecting' | 'connected' | 'disconnected'; qr: string | null; phoneNumber: string | null }

type PlatformReview = {
  id: string; rating: number; comment: string | null; reviewerName: string; itemName: string | null
  createdAt: string; isHidden: boolean; ownerReply: string | null
}

export default function PlatformAdminBusinessDetailPage() {
  const { id } = useParams<{ id: string }>()
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const [impersonating, setImpersonating] = useState(false)
  const [polling, setPolling] = useState(false)
  const [starting, setStarting] = useState(false)
  const [unlinking, setUnlinking] = useState(false)
  const [linkError, setLinkError] = useState('')
  const [togglingDisabled, setTogglingDisabled] = useState(false)
  const [accountError, setAccountError] = useState('')
  const [resettingPassword, setResettingPassword] = useState(false)
  const [tempPassword, setTempPassword] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const [showCustomPasswordForm, setShowCustomPasswordForm] = useState(false)
  const [customPassword, setCustomPassword] = useState('')

  const { data: business } = useQuery<BusinessDetail>({
    queryKey: ['platform-admin-business', id],
    queryFn: () => platformAdminApi.get(`/platform-admin/businesses/${id}`).then((r) => r.data),
  })

  const { data: linkStatus } = useQuery<WhatsAppLinkStatus>({
    queryKey: ['platform-admin-business-whatsapp-status', id],
    queryFn: () => platformAdminApi.get(`/platform-admin/businesses/${id}/whatsapp/status`).then((r) => r.data),
    enabled: polling,
    refetchInterval: polling ? 2000 : false,
  })

  useEffect(() => {
    if (polling && linkStatus?.state === 'connected') {
      setPolling(false)
      queryClient.invalidateQueries({ queryKey: ['platform-admin-business', id] })
    }
  }, [polling, linkStatus?.state, queryClient, id])

  async function handleStartLink() {
    if (!business) return
    setLinkError(''); setStarting(true)
    try {
      await platformAdminApi.post(`/platform-admin/businesses/${business.id}/whatsapp/link`)
      setPolling(true)
    } catch {
      setLinkError('Could not start linking')
    } finally {
      setStarting(false)
    }
  }

  async function handleUnlink() {
    if (!business) return
    setLinkError(''); setUnlinking(true)
    try {
      await platformAdminApi.delete(`/platform-admin/businesses/${business.id}/whatsapp/link`)
      setPolling(false)
      queryClient.invalidateQueries({ queryKey: ['platform-admin-business', id] })
    } catch {
      setLinkError('Could not unlink')
    } finally {
      setUnlinking(false)
    }
  }
  async function handleToggleDisabled() {
    if (!business) return
    setAccountError(''); setTogglingDisabled(true)
    try {
      await platformAdminApi.post(`/platform-admin/businesses/${business.id}/${business.isDisabled ? 'enable' : 'disable'}`)
      queryClient.invalidateQueries({ queryKey: ['platform-admin-business', id] })
    } catch {
      setAccountError('Could not update the account status')
    } finally {
      setTogglingDisabled(false)
    }
  }

  async function handleResetTempPassword() {
    if (!business) return
    setAccountError(''); setTempPassword(null); setCopied(false); setResettingPassword(true)
    try {
      const { data } = await platformAdminApi.post(`/platform-admin/businesses/${business.id}/reset-password`, { temporary: true })
      setTempPassword(data.tempPassword)
    } catch {
      setAccountError('Could not reset the password')
    } finally {
      setResettingPassword(false)
    }
  }

  async function handleSetCustomPassword(e: FormEvent) {
    e.preventDefault()
    if (!business) return
    setAccountError(''); setResettingPassword(true)
    try {
      await platformAdminApi.post(`/platform-admin/businesses/${business.id}/reset-password`, { temporary: false, newPassword: customPassword })
      setShowCustomPasswordForm(false)
      setCustomPassword('')
    } catch (err: unknown) {
      const resp = (err as { response?: { data?: { error?: string } } })?.response
      setAccountError(resp?.data?.error ?? 'Could not set the password')
    } finally {
      setResettingPassword(false)
    }
  }

  function copyTempPassword() {
    if (!tempPassword) return
    navigator.clipboard.writeText(tempPassword)
    setCopied(true)
  }

  const { data: activity } = useQuery<ActivityLogEntry[]>({
    queryKey: ['platform-admin-business-activity', id],
    queryFn: () => platformAdminApi.get(`/platform-admin/businesses/${id}/activity`).then((r) => r.data),
  })

  const { data: reviews = [] } = useQuery<PlatformReview[]>({
    queryKey: ['platform-admin-business-reviews', id],
    queryFn: () => platformAdminApi.get(`/platform-admin/reviews?businessId=${id}`).then((r) => r.data),
  })

  async function toggleHidden(reviewId: string, hidden: boolean) {
    await platformAdminApi.post(`/platform-admin/reviews/${reviewId}/${hidden ? 'unhide' : 'hide'}`)
    queryClient.invalidateQueries({ queryKey: ['platform-admin-business-reviews', id] })
  }

  async function handleImpersonate() {
    if (!business) return
    setError('')
    setImpersonating(true)
    try {
      const { data } = await platformAdminApi.post(`/platform-admin/businesses/${business.id}/impersonate`)
      localStorage.setItem('token', data.token)
      localStorage.setItem('user', JSON.stringify({ id: business.id, name: business.name, email: business.email, slug: business.slug }))
      localStorage.setItem('impersonation', JSON.stringify({
        type: 'business', name: business.name, returnPath: `/platform-admin/businesses/${business.id}`,
      }))
      window.location.href = '/admin/dashboard'
    } catch {
      setError('Could not start impersonation')
      setImpersonating(false)
    }
  }

  if (!business) return <div className="min-h-screen bg-cream text-ink p-6">Loading...</div>

  return (
    <div className="min-h-screen bg-cream text-ink p-6">
      <div className="max-w-3xl mx-auto">
        <div className="flex items-center justify-between mb-6">
          <Link to="/platform-admin" className="text-muted hover:text-ink text-sm inline-block">← Back</Link>
          <ThemeToggle />
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 mb-6">
          <div className="flex items-start justify-between mb-4">
            <div>
              <h1 className="text-xl font-bold">{business.name}</h1>
              <p className="text-muted text-sm">{business.email}</p>
              <p className="text-muted text-sm">/{business.slug} {business.phone && `· ${business.phone}`}</p>
            </div>
            <span className={`text-xs px-2 py-1 rounded-full ${
              business.subscriptionStatus === 'ACTIVE' ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400'
                : business.subscriptionStatus === 'TRIAL' ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-400' : 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-400'
            }`}>{business.subscriptionStatus}</span>
          </div>

          {error && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}

          <button onClick={handleImpersonate} disabled={impersonating}
            className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors">
            {impersonating ? 'Logging in...' : 'Log in as this account'}
          </button>
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 mb-6">
          <h2 className="font-semibold mb-1">Account</h2>
          <p className="text-muted text-sm mb-3">
            {business.isDisabled
              ? 'This account is disabled — the owner cannot sign in to their dashboard. The storefront and WhatsApp bot are unaffected.'
              : 'The owner can sign in normally.'}
          </p>
          {accountError && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3 mb-3">{accountError}</div>}

          <div className="flex items-center gap-2 mb-4">
            <button onClick={handleToggleDisabled} disabled={togglingDisabled}
              className={`font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors disabled:opacity-50 ${
                business.isDisabled
                  ? 'bg-teal-tint hover:bg-teal-tint/70 text-ink'
                  : 'bg-red-100 hover:bg-red-200 dark:bg-red-900/40 dark:hover:bg-red-900/60 text-red-700 dark:text-red-400'
              }`}>
              {togglingDisabled ? 'Updating...' : business.isDisabled ? 'Enable account' : 'Disable account'}
            </button>
          </div>

          <div className="border-t border-line pt-4">
            <h3 className="text-sm font-semibold mb-2">Reset password</h3>
            {tempPassword ? (
              <div className="space-y-2">
                <div className="flex items-center gap-2">
                  <code className="flex-1 bg-cream border border-line rounded-lg px-3 py-2 font-mono text-lg tracking-wide">{tempPassword}</code>
                  <button onClick={copyTempPassword}
                    className="bg-coral hover:bg-coral-dark text-white font-semibold text-sm px-4 py-2 rounded-lg transition-colors">
                    {copied ? 'Copied!' : 'Copy'}
                  </button>
                </div>
                <p className="text-muted text-xs">The owner must change this password the next time they sign in.</p>
                <button onClick={() => setTempPassword(null)} className="text-muted hover:text-ink text-sm">Dismiss</button>
              </div>
            ) : showCustomPasswordForm ? (
              <form onSubmit={handleSetCustomPassword} className="flex items-center gap-2">
                <input type="text" required minLength={6} value={customPassword} onChange={(e) => setCustomPassword(e.target.value)}
                  placeholder="New password" autoComplete="new-password"
                  className="flex-1 bg-cream border border-line rounded-lg px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
                <button type="submit" disabled={resettingPassword}
                  className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold text-sm px-4 py-2 rounded-lg transition-colors">
                  {resettingPassword ? 'Saving...' : 'Set password'}
                </button>
                <button type="button" onClick={() => { setShowCustomPasswordForm(false); setCustomPassword('') }}
                  className="text-muted hover:text-ink text-sm px-2">Cancel</button>
              </form>
            ) : (
              <div className="flex items-center gap-2">
                <button onClick={handleResetTempPassword} disabled={resettingPassword}
                  className="bg-teal-tint hover:bg-teal-tint/70 disabled:opacity-50 text-ink font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors">
                  {resettingPassword ? 'Sending...' : 'Send temporary password'}
                </button>
                <button onClick={() => setShowCustomPasswordForm(true)}
                  className="border border-line text-ink hover:bg-cream font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors">
                  Set specific password
                </button>
              </div>
            )}
          </div>
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 mb-6">
          <h2 className="font-semibold mb-1">WhatsApp</h2>
          <p className="text-muted text-sm mb-3">
            This business's self-hosted WhatsApp chatbot session. Linking asks the owner to scan a
            QR code with their own phone (WhatsApp → Linked Devices → Link a Device) — no Meta
            Business verification needed.
          </p>
          {linkError && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3 mb-3">{linkError}</div>}

          {business.whatsAppNumber && !polling ? (
            <div className="flex items-center justify-between">
              <span className="text-ink font-mono">Connected as {business.whatsAppNumber}</span>
              <button onClick={handleUnlink} disabled={unlinking}
                className="bg-red-100 hover:bg-red-200 dark:bg-red-900/40 dark:hover:bg-red-900/60 disabled:opacity-50 text-red-700 dark:text-red-400 font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors">
                {unlinking ? 'Unlinking...' : 'Unlink'}
              </button>
            </div>
          ) : polling && linkStatus?.state === 'qr' && linkStatus.qr ? (
            <div className="text-center">
              <img src={linkStatus.qr} alt="WhatsApp link QR code" width={220} height={220}
                className="mx-auto mb-3 rounded-lg bg-white p-2" />
              <p className="text-muted text-sm">Waiting for the phone to scan this code...</p>
            </div>
          ) : polling ? (
            <p className="text-muted text-sm">Connecting...</p>
          ) : (
            <button onClick={handleStartLink} disabled={starting}
              className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors">
              {starting ? 'Starting...' : 'Link WhatsApp'}
            </button>
          )}
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 mb-6">
          <h2 className="font-semibold mb-4">Reviews ({reviews.length})</h2>
          {reviews.length === 0 ? (
            <p className="text-muted text-sm">No reviews.</p>
          ) : (
            <div className="space-y-3">
              {reviews.map((r) => (
                <div key={r.id} className={`border rounded-xl p-3 ${r.isHidden ? 'border-line opacity-60' : 'border-line'}`}>
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-sm">
                      {'★'.repeat(r.rating)}<span className="text-line">{'★'.repeat(5 - r.rating)}</span>
                      <span className="text-muted ms-2">{r.reviewerName}</span>
                      {r.itemName && <span className="text-muted text-xs ms-1">· {r.itemName}</span>}
                    </span>
                    <button onClick={() => toggleHidden(r.id, r.isHidden)}
                      className="text-xs border border-line text-ink hover:bg-cream px-2 py-1 rounded-lg transition-colors">
                      {r.isHidden ? 'Unhide' : 'Hide'}
                    </button>
                  </div>
                  {r.comment && <p className="text-ink text-sm mt-1">{r.comment}</p>}
                  {r.ownerReply && <p className="text-muted text-xs mt-1 border-s-2 border-line ps-2">Reply: {r.ownerReply}</p>}
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6">
          <h2 className="font-semibold mb-4">Recent activity</h2>
          <ActivityLogTable entries={activity} />
        </div>
      </div>
    </div>
  )
}
