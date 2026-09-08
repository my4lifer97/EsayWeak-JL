import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'
import StarRating from '../../components/customer/StarRating'

type AdminReview = {
  id: string; rating: number; comment: string | null; reviewerName: string; itemName: string | null
  createdAt: string; updatedAt: string; isHidden: boolean
  ownerReply: string | null; ownerRepliedAt: string | null
}

export default function ReviewsPage() {
  const queryClient = useQueryClient()
  const { language: lang } = useAuth()
  const [replyingId, setReplyingId] = useState<string | null>(null)
  const [draft, setDraft] = useState('')
  const [busy, setBusy] = useState(false)

  const { data: reviews = [], isLoading } = useQuery<AdminReview[]>({
    queryKey: ['admin-reviews'],
    queryFn: () => api.get('/admin/reviews').then((r) => r.data),
  })

  function startReply(r: AdminReview) {
    setReplyingId(r.id)
    setDraft(r.ownerReply ?? '')
  }

  async function saveReply(id: string) {
    if (!draft.trim()) return
    setBusy(true)
    try {
      await api.post(`/admin/reviews/${id}/reply`, { reply: draft.trim() })
      setReplyingId(null)
      queryClient.invalidateQueries({ queryKey: ['admin-reviews'] })
    } finally { setBusy(false) }
  }

  async function deleteReply(id: string) {
    setBusy(true)
    try {
      await api.delete(`/admin/reviews/${id}/reply`)
      setReplyingId(null)
      queryClient.invalidateQueries({ queryKey: ['admin-reviews'] })
    } finally { setBusy(false) }
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-white mb-6">{t(lang, 'reviews')}</h1>

      {isLoading ? (
        <div className="text-gray-500 py-12 text-center">{t(lang, 'loading')}</div>
      ) : reviews.length === 0 ? (
        <div className="text-gray-500 py-12 text-center">{t(lang, 'noReviews')}</div>
      ) : (
        <div className="space-y-3">
          {reviews.map((r) => (
            <div key={r.id} className={`bg-gray-900 border rounded-2xl p-4 ${r.isHidden ? 'border-gray-800 opacity-60' : 'border-gray-800'}`}>
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <span className="text-white text-sm font-medium">{r.reviewerName}</span>
                  {r.isHidden && (
                    <span className="text-xs px-2 py-0.5 rounded-full bg-gray-800 text-gray-400 border border-gray-700">
                      {t(lang, 'reviewHidden')}
                    </span>
                  )}
                </div>
                <span className="text-gray-500 text-xs">{format(parseISO(r.createdAt), 'MMM d, yyyy')}</span>
              </div>
              <div className="mt-1 flex items-center gap-2">
                <StarRating value={r.rating} size="sm" />
                {r.itemName && <span className="text-gray-500 text-xs">· {r.itemName}</span>}
              </div>
              {r.comment && <p className="text-gray-300 text-sm mt-2">{r.comment}</p>}

              {r.ownerReply && replyingId !== r.id && (
                <div className="mt-3 border-s-2 border-gray-700 ps-3">
                  <div className="text-xs text-gray-500">{t(lang, 'ownerReply')}</div>
                  <p className="text-gray-400 text-sm mt-0.5">{r.ownerReply}</p>
                </div>
              )}

              {replyingId === r.id ? (
                <div className="mt-3 space-y-2">
                  <textarea
                    value={draft} onChange={(e) => setDraft(e.target.value)} rows={2} maxLength={1000}
                    placeholder={t(lang, 'replyPlaceholder')}
                    className="w-full bg-gray-800 border border-gray-700 rounded-xl px-3 py-2 text-white text-sm placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
                  />
                  <div className="flex gap-2">
                    <button onClick={() => saveReply(r.id)} disabled={busy}
                      className="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-semibold py-1.5 px-4 rounded-xl transition-colors">
                      {t(lang, 'saveReply')}
                    </button>
                    {r.ownerReply && (
                      <button onClick={() => deleteReply(r.id)} disabled={busy}
                        className="border border-red-800/60 text-red-400 hover:bg-red-900/30 text-sm font-medium py-1.5 px-3 rounded-xl transition-colors disabled:opacity-50">
                        {t(lang, 'deleteReply')}
                      </button>
                    )}
                    <button onClick={() => setReplyingId(null)} disabled={busy}
                      className="border border-gray-700 text-gray-400 hover:bg-gray-800 text-sm font-medium py-1.5 px-3 rounded-xl transition-colors disabled:opacity-50">
                      {t(lang, 'back')}
                    </button>
                  </div>
                </div>
              ) : (
                <button onClick={() => startReply(r)}
                  className="mt-3 border border-gray-700 text-gray-300 hover:bg-gray-800 text-sm font-medium py-1.5 px-3 rounded-xl transition-colors">
                  {r.ownerReply ? t(lang, 'editReply') : t(lang, 'replyToReview')}
                </button>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
