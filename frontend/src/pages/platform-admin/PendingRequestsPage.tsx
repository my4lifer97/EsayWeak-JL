import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { platformAdminApi } from '../../lib/platformAdminApi'

type BusinessOwnerRequest = {
  id: string; businessName: string; ownerName: string; email: string; phone: string
  businessTypeId: string | null; businessTypeName: string | null
  status: string; rejectionNote: string | null; createdAt: string
}

function slugify(name: string) {
  return name.toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
}

export default function PlatformAdminPendingRequestsPage() {
  const queryClient = useQueryClient()
  const [expandedApprove, setExpandedApprove] = useState<string | null>(null)
  const [expandedReject, setExpandedReject] = useState<string | null>(null)
  const [slugDraft, setSlugDraft] = useState('')
  const [noteDraft, setNoteDraft] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)
  const [rowError, setRowError] = useState('')
  const [tempPasswordResult, setTempPasswordResult] = useState<{ slug: string; tempPassword: string } | null>(null)
  const [copied, setCopied] = useState(false)

  const { data: requests } = useQuery<BusinessOwnerRequest[]>({
    queryKey: ['platform-admin-pending-requests'],
    queryFn: () => platformAdminApi.get('/platform-admin/business-owner-requests', { params: { status: 'Pending' } }).then((r) => r.data),
  })

  function startApprove(req: BusinessOwnerRequest) {
    setExpandedReject(null)
    setRowError('')
    setExpandedApprove(req.id)
    setSlugDraft(slugify(req.businessName))
  }

  function startReject(req: BusinessOwnerRequest) {
    setExpandedApprove(null)
    setRowError('')
    setExpandedReject(req.id)
    setNoteDraft('')
  }

  async function handleApprove(id: string) {
    setBusyId(id)
    setRowError('')
    try {
      const { data } = await platformAdminApi.post(`/platform-admin/business-owner-requests/${id}/approve`, { slug: slugDraft })
      setTempPasswordResult({ slug: data.slug, tempPassword: data.tempPassword })
      setExpandedApprove(null)
      queryClient.invalidateQueries({ queryKey: ['platform-admin-pending-requests'] })
    } catch (err: unknown) {
      const resp = (err as { response?: { data?: { error?: string } } })?.response
      setRowError(resp?.data?.error ?? 'Could not approve this request')
    } finally {
      setBusyId(null)
    }
  }

  async function handleReject(id: string) {
    setBusyId(id)
    setRowError('')
    try {
      await platformAdminApi.post(`/platform-admin/business-owner-requests/${id}/reject`, { note: noteDraft || null })
      setExpandedReject(null)
      queryClient.invalidateQueries({ queryKey: ['platform-admin-pending-requests'] })
    } catch {
      setRowError('Could not reject this request')
    } finally {
      setBusyId(null)
    }
  }

  function copyTempPassword() {
    if (!tempPasswordResult) return
    navigator.clipboard.writeText(tempPasswordResult.tempPassword).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    })
  }

  return (
    <div className="min-h-screen bg-gray-950 text-white p-6">
      <div className="max-w-3xl mx-auto">
        <Link to="/platform-admin" className="text-gray-500 hover:text-gray-300 text-sm mb-6 inline-block">← Back</Link>
        <h1 className="text-xl font-bold mb-6">Pending business account requests</h1>

        {tempPasswordResult && (
          <div className="bg-yellow-900/30 border border-yellow-700/50 rounded-2xl p-5 mb-6">
            <p className="text-yellow-300 font-semibold mb-1">
              Account created for /{tempPasswordResult.slug}
            </p>
            <p className="text-gray-400 text-sm mb-3">
              Copy this now and send it to the owner yourself — it will not be shown again.
            </p>
            <div className="flex items-center gap-2">
              <code className="flex-1 bg-gray-900 border border-gray-700 rounded-lg px-3 py-2 font-mono text-lg tracking-wide">
                {tempPasswordResult.tempPassword}
              </code>
              <button onClick={copyTempPassword}
                className="bg-blue-600 hover:bg-blue-700 text-white font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors">
                {copied ? 'Copied!' : 'Copy'}
              </button>
            </div>
            <button onClick={() => setTempPasswordResult(null)}
              className="text-gray-500 hover:text-gray-300 text-sm mt-3">
              Dismiss
            </button>
          </div>
        )}

        <div className="space-y-3">
          {requests?.map((req) => (
            <div key={req.id} className="bg-gray-900 border border-gray-800 rounded-2xl p-5">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <div className="font-semibold">{req.businessName}</div>
                  <div className="text-sm text-gray-400">{req.ownerName} · {req.email} · {req.phone}</div>
                  {req.businessTypeName && <div className="text-xs text-gray-500 mt-1">{req.businessTypeName}</div>}
                  <div className="text-xs text-gray-600 mt-1">Submitted {new Date(req.createdAt).toLocaleString()}</div>
                </div>
                <div className="flex gap-2 shrink-0">
                  <button onClick={() => startApprove(req)}
                    className="bg-green-700 hover:bg-green-600 text-white font-semibold text-sm px-3 py-2 rounded-lg transition-colors">
                    Approve
                  </button>
                  <button onClick={() => startReject(req)}
                    className="bg-red-900/60 hover:bg-red-800 text-red-200 font-semibold text-sm px-3 py-2 rounded-lg transition-colors">
                    Reject
                  </button>
                </div>
              </div>

              {expandedApprove === req.id && (
                <div className="mt-4 pt-4 border-t border-gray-800">
                  {rowError && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-3 py-2 mb-3">{rowError}</div>}
                  <label className="block text-xs text-gray-500 mb-1">URL slug</label>
                  <div className="flex gap-2">
                    <input value={slugDraft} onChange={(e) => setSlugDraft(e.target.value)}
                      className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                    <button onClick={() => handleApprove(req.id)} disabled={busyId === req.id}
                      className="bg-green-700 hover:bg-green-600 disabled:opacity-50 text-white font-semibold text-sm px-4 py-2 rounded-lg transition-colors">
                      {busyId === req.id ? '...' : 'Confirm approve'}
                    </button>
                  </div>
                </div>
              )}

              {expandedReject === req.id && (
                <div className="mt-4 pt-4 border-t border-gray-800">
                  {rowError && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-3 py-2 mb-3">{rowError}</div>}
                  <label className="block text-xs text-gray-500 mb-1">Note (optional)</label>
                  <div className="flex gap-2">
                    <input value={noteDraft} onChange={(e) => setNoteDraft(e.target.value)}
                      placeholder="Reason for rejecting"
                      className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                    <button onClick={() => handleReject(req.id)} disabled={busyId === req.id}
                      className="bg-red-900/60 hover:bg-red-800 disabled:opacity-50 text-red-200 font-semibold text-sm px-4 py-2 rounded-lg transition-colors">
                      {busyId === req.id ? '...' : 'Confirm reject'}
                    </button>
                  </div>
                </div>
              )}
            </div>
          ))}
          {requests?.length === 0 && <p className="text-gray-500 text-sm">No pending requests.</p>}
        </div>
      </div>
    </div>
  )
}
