import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { platformAdminApi } from '../../lib/platformAdminApi'

type BusinessOwnerRequest = {
  id: string
  businessName: string
  ownerFirstName: string
  ownerFamilyName: string
  email: string
  phone: string
  businessTypeId: string | null
  businessTypeName: string | null
  businessDescription: string | null
  systemNeeds: string | null
  status: 'Pending' | 'Approved' | 'Rejected'
  rejectionNote: string | null
  createdAt: string
  reviewedAt: string | null
  createdBusinessSlug: string | null
  createdUsername: string | null
}

const STATUS_TABS = ['All', 'Pending', 'Approved', 'Rejected'] as const
type Tab = (typeof STATUS_TABS)[number]

function slugify(name: string) {
  return name.toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
}

function StatusBadge({ status }: { status: BusinessOwnerRequest['status'] }) {
  const cls =
    status === 'Approved' ? 'bg-green-900/40 text-green-300'
      : status === 'Rejected' ? 'bg-red-900/40 text-red-300'
        : 'bg-yellow-900/40 text-yellow-300'
  return <span className={`text-xs px-2 py-0.5 rounded-full ${cls}`}>{status}</span>
}

export default function PlatformAdminRequestsPage() {
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<Tab>('Pending')
  const [openId, setOpenId] = useState<string | null>(null)
  const [slugDraft, setSlugDraft] = useState('')
  const [noteDraft, setNoteDraft] = useState('')
  const [busy, setBusy] = useState(false)
  const [rowError, setRowError] = useState('')
  const [credsResult, setCredsResult] = useState<{ slug: string; username: string; tempPassword: string; emailSent: boolean; email: string } | null>(null)
  const [copied, setCopied] = useState<'user' | 'pass' | null>(null)

  const { data: requests } = useQuery<BusinessOwnerRequest[]>({
    queryKey: ['platform-admin-requests', tab],
    queryFn: () =>
      platformAdminApi
        .get('/platform-admin/business-owner-requests', { params: tab === 'All' ? {} : { status: tab } })
        .then((r) => r.data),
  })

  const open = requests?.find((r) => r.id === openId) ?? null

  function openDetail(req: BusinessOwnerRequest) {
    setRowError('')
    setOpenId(req.id)
    setSlugDraft(slugify(req.businessName))
    setNoteDraft('')
  }

  async function approve() {
    if (!open) return
    setBusy(true)
    setRowError('')
    try {
      const { data } = await platformAdminApi.post(`/platform-admin/business-owner-requests/${open.id}/approve`, { slug: slugDraft })
      setCredsResult({ slug: data.slug, username: data.username, tempPassword: data.tempPassword, emailSent: data.emailSent, email: open.email })
      setOpenId(null)
      queryClient.invalidateQueries({ queryKey: ['platform-admin-requests'] })
    } catch (err: unknown) {
      const resp = (err as { response?: { data?: { error?: string } } })?.response
      setRowError(resp?.data?.error ?? 'Could not approve this request')
    } finally {
      setBusy(false)
    }
  }

  async function reject() {
    if (!open) return
    setBusy(true)
    setRowError('')
    try {
      await platformAdminApi.post(`/platform-admin/business-owner-requests/${open.id}/reject`, { note: noteDraft || null })
      setOpenId(null)
      queryClient.invalidateQueries({ queryKey: ['platform-admin-requests'] })
    } catch {
      setRowError('Could not reject this request')
    } finally {
      setBusy(false)
    }
  }

  function copy(text: string, which: 'user' | 'pass') {
    navigator.clipboard.writeText(text).then(() => {
      setCopied(which)
      setTimeout(() => setCopied(null), 2000)
    })
  }

  return (
    <div className="min-h-screen bg-gray-950 text-white p-6">
      <div className="max-w-5xl mx-auto">
        <Link to="/platform-admin" className="text-gray-500 hover:text-gray-300 text-sm mb-6 inline-block">← Back</Link>
        <h1 className="text-xl font-bold mb-6">Business account requests</h1>

        {credsResult && (
          <div className="bg-yellow-900/30 border border-yellow-700/50 rounded-2xl p-5 mb-6">
            <p className="text-yellow-300 font-semibold mb-1">Account created for /{credsResult.slug}</p>
            <p className="text-gray-400 text-sm mb-3">
              {credsResult.emailSent
                ? `Credentials were emailed to ${credsResult.email}. Copy them below only if the email doesn't arrive — the password will not be shown again.`
                : `Couldn't email the owner — send these to them yourself. The password will not be shown again.`}
            </p>
            <div className="space-y-2">
              <div className="flex items-center gap-2">
                <span className="text-xs text-gray-500 w-20 shrink-0">Username</span>
                <code className="flex-1 bg-gray-900 border border-gray-700 rounded-lg px-3 py-2 font-mono">{credsResult.username}</code>
                <button onClick={() => copy(credsResult.username, 'user')}
                  className="bg-blue-600 hover:bg-blue-700 text-white font-semibold text-sm px-4 py-2 rounded-lg transition-colors">
                  {copied === 'user' ? 'Copied!' : 'Copy'}
                </button>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-xs text-gray-500 w-20 shrink-0">Password</span>
                <code className="flex-1 bg-gray-900 border border-gray-700 rounded-lg px-3 py-2 font-mono text-lg tracking-wide">{credsResult.tempPassword}</code>
                <button onClick={() => copy(credsResult.tempPassword, 'pass')}
                  className="bg-blue-600 hover:bg-blue-700 text-white font-semibold text-sm px-4 py-2 rounded-lg transition-colors">
                  {copied === 'pass' ? 'Copied!' : 'Copy'}
                </button>
              </div>
            </div>
            <button onClick={() => setCredsResult(null)} className="text-gray-500 hover:text-gray-300 text-sm mt-3">Dismiss</button>
          </div>
        )}

        <div className="flex gap-1 mb-4">
          {STATUS_TABS.map((t) => (
            <button key={t} onClick={() => setTab(t)}
              className={`text-sm px-3 py-1.5 rounded-lg transition-colors ${
                tab === t ? 'bg-gray-700 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-800'
              }`}>
              {t}
            </button>
          ))}
        </div>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-xs uppercase tracking-wide text-gray-500 border-b border-gray-800">
                  <th className="px-4 py-3 font-medium">Owner</th>
                  <th className="px-4 py-3 font-medium">Family name</th>
                  <th className="px-4 py-3 font-medium">Business</th>
                  <th className="px-4 py-3 font-medium">Type</th>
                  <th className="px-4 py-3 font-medium">Phone</th>
                  <th className="px-4 py-3 font-medium">Email</th>
                  <th className="px-4 py-3 font-medium">Submitted</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                </tr>
              </thead>
              <tbody>
                {requests?.map((req) => (
                  <tr key={req.id} onClick={() => openDetail(req)}
                    className="border-b border-gray-800 last:border-0 hover:bg-gray-800/60 cursor-pointer transition-colors">
                    <td className="px-4 py-3">{req.ownerFirstName}</td>
                    <td className="px-4 py-3">{req.ownerFamilyName}</td>
                    <td className="px-4 py-3">{req.businessName}</td>
                    <td className="px-4 py-3 text-gray-400">{req.businessTypeName ?? '—'}</td>
                    <td className="px-4 py-3 text-gray-400">{req.phone}</td>
                    <td className="px-4 py-3 text-gray-400">{req.email}</td>
                    <td className="px-4 py-3 text-gray-400">{new Date(req.createdAt).toLocaleDateString()}</td>
                    <td className="px-4 py-3"><StatusBadge status={req.status} /></td>
                  </tr>
                ))}
                {requests?.length === 0 && (
                  <tr><td colSpan={8} className="px-4 py-6 text-center text-gray-500">No {tab === 'All' ? '' : tab.toLowerCase()} requests.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {open && (
        <div className="fixed inset-0 bg-black/60 flex items-start justify-center p-4 overflow-y-auto z-50" onClick={() => setOpenId(null)}>
          <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6 w-full max-w-lg my-8" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-start justify-between mb-4">
              <div>
                <h2 className="text-lg font-bold">{open.businessName}</h2>
                <p className="text-sm text-gray-400">{open.businessTypeName ?? 'No type'}</p>
              </div>
              <StatusBadge status={open.status} />
            </div>

            {rowError && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-3 py-2 mb-3">{rowError}</div>}

            <dl className="space-y-2 text-sm mb-4">
              <Field label="Owner" value={`${open.ownerFirstName} ${open.ownerFamilyName}`} />
              <Field label="Email" value={open.email} />
              <Field label="Phone" value={open.phone} />
              <Field label="Submitted" value={new Date(open.createdAt).toLocaleString()} />
              {open.businessDescription && <Field label="Description" value={open.businessDescription} />}
              {open.systemNeeds && <Field label="Wants from system" value={open.systemNeeds} />}
              {open.status === 'Approved' && (
                <>
                  <Field label="Username" value={open.createdUsername ?? '—'} mono />
                  <Field label="URL" value={open.createdBusinessSlug ? `/${open.createdBusinessSlug}` : '—'} mono />
                </>
              )}
              {open.status === 'Rejected' && open.rejectionNote && <Field label="Rejection note" value={open.rejectionNote} />}
            </dl>

            {open.status === 'Pending' ? (
              <div className="border-t border-gray-800 pt-4 space-y-4">
                <div>
                  <label className="block text-xs text-gray-500 mb-1">URL slug</label>
                  <div className="flex gap-2">
                    <input value={slugDraft} onChange={(e) => setSlugDraft(e.target.value)}
                      className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                    <button onClick={approve} disabled={busy}
                      className="bg-green-700 hover:bg-green-600 disabled:opacity-50 text-white font-semibold text-sm px-4 py-2 rounded-lg transition-colors">
                      {busy ? '…' : 'Approve'}
                    </button>
                  </div>
                </div>
                <div>
                  <label className="block text-xs text-gray-500 mb-1">Reject with a note (optional)</label>
                  <div className="flex gap-2">
                    <input value={noteDraft} onChange={(e) => setNoteDraft(e.target.value)}
                      placeholder="Reason for rejecting"
                      className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                    <button onClick={reject} disabled={busy}
                      className="bg-red-900/60 hover:bg-red-800 disabled:opacity-50 text-red-200 font-semibold text-sm px-4 py-2 rounded-lg transition-colors">
                      {busy ? '…' : 'Reject'}
                    </button>
                  </div>
                </div>
              </div>
            ) : (
              <button onClick={() => setOpenId(null)} className="border-t border-gray-800 pt-4 text-gray-500 hover:text-gray-300 text-sm w-full text-left">Close</button>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function Field({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex gap-3">
      <dt className="text-gray-500 w-36 shrink-0">{label}</dt>
      <dd className={`text-gray-200 ${mono ? 'font-mono' : ''} whitespace-pre-wrap`}>{value}</dd>
    </div>
  )
}
