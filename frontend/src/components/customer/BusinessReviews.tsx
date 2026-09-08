import { useState } from 'react'
import { useQuery, useInfiniteQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ar, he, enUS } from 'date-fns/locale'
import { customerApi } from '../../lib/customerApi'
import { t } from '../../lib/i18n'
import StarRating from './StarRating'

type PublicReview = {
  id: string; rating: number; comment: string | null; reviewerName: string
  createdAt: string; ownerReply: string | null; ownerRepliedAt: string | null
}
type OwnReview = {
  id: string; rating: number; comment: string | null
  ownerReply: string | null; ownerRepliedAt: string | null
}
type Eligibility = { canReview: boolean; alreadyReviewed: boolean; review: OwnReview | null }
type ReviewPage = {
  rating: { count: number; average: number }
  reviews: { items: PublicReview[]; page: number; pageSize: number; total: number; hasMore: boolean }
}

const PAGE_SIZE = 10

export default function BusinessReviews({
  slug, lang, isAuthenticated, openForm,
}: {
  slug: string; lang: string; isAuthenticated: boolean; openForm?: boolean
}) {
  const queryClient = useQueryClient()
  const locale = lang === 'AR' ? ar : lang === 'HE' ? he : enUS

  const list = useInfiniteQuery({
    queryKey: ['business-reviews', slug],
    initialPageParam: 1,
    queryFn: ({ pageParam }) =>
      customerApi.get(`/businesses/${slug}/reviews?page=${pageParam}&pageSize=${PAGE_SIZE}`).then((r) => r.data as ReviewPage),
    getNextPageParam: (last) => (last.reviews.hasMore ? last.reviews.page + 1 : undefined),
  })

  const { data: eligibility } = useQuery<Eligibility>({
    queryKey: ['review-eligibility', slug],
    queryFn: () => customerApi.get(`/reviews/eligibility?businessSlug=${slug}`).then((r) => r.data),
    enabled: isAuthenticated,
  })

  const [editing, setEditing] = useState(false)
  const [rating, setRating] = useState(0)
  const [comment, setComment] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const pages = list.data?.pages ?? []
  const agg = pages[0]?.rating ?? { count: 0, average: 0 }
  const items = pages.flatMap((p) => p.reviews.items)
  const own = eligibility?.review ?? null
  const formOpen = editing || (openForm && !own && eligibility?.canReview)

  function startEdit() {
    setRating(own?.rating ?? 0)
    setComment(own?.comment ?? '')
    setError('')
    setEditing(true)
  }
  function startCreate() {
    setRating(0)
    setComment('')
    setError('')
    setEditing(true)
  }

  function refresh() {
    queryClient.invalidateQueries({ queryKey: ['business-reviews', slug] })
    queryClient.invalidateQueries({ queryKey: ['review-eligibility', slug] })
    queryClient.invalidateQueries({ queryKey: ['business', slug] })
  }

  async function submit() {
    if (rating < 1) { setError(t(lang, 'yourRating')); return }
    setBusy(true); setError('')
    try {
      if (own) await customerApi.patch(`/reviews/${own.id}`, { rating, comment: comment.trim() || null })
      else await customerApi.post('/reviews', { businessSlug: slug, rating, comment: comment.trim() || null })
      setEditing(false)
      refresh()
    } catch {
      setError(t(lang, 'reviewSubmitError'))
    } finally { setBusy(false) }
  }

  async function remove() {
    if (!own || !confirm(t(lang, 'reviewDeleteConfirm'))) return
    setBusy(true)
    try {
      await customerApi.delete(`/reviews/${own.id}`)
      setEditing(false)
      refresh()
    } finally { setBusy(false) }
  }

  return (
    <div className="mt-8">
      <div className="flex items-center gap-3 mb-3">
        <h2 className="text-lg font-bold text-white">{t(lang, 'reviews')}</h2>
        {agg.count > 0 && (
          <span className="flex items-center gap-1.5 text-sm text-gray-400">
            <StarRating value={agg.average} size="sm" />
            {agg.average.toFixed(1)} · {agg.count} {t(lang, 'reviewsCountSuffix')}
          </span>
        )}
      </div>

      {isAuthenticated && !own && eligibility?.canReview && !formOpen && (
        <button onClick={startCreate}
          className="w-full bg-blue-600 hover:bg-blue-700 text-white font-semibold py-3 rounded-2xl transition-colors mb-4">
          {t(lang, 'leaveReview')}
        </button>
      )}
      {isAuthenticated && !own && eligibility && !eligibility.canReview && (
        <p className="text-gray-500 text-sm mb-4">{t(lang, 'reviewNeedsCompletedVisit')}</p>
      )}

      {/* The customer's own review (edit/delete) */}
      {own && !editing && (
        <div className="bg-gray-900 border border-blue-600/30 rounded-2xl p-4 mb-4">
          <div className="text-xs text-gray-500 mb-1">{t(lang, 'yourReview')}</div>
          <StarRating value={own.rating} size="sm" />
          {own.comment && <p className="text-gray-300 text-sm mt-2">{own.comment}</p>}
          {own.ownerReply && (
            <div className="mt-3 border-s-2 border-gray-700 ps-3">
              <div className="text-xs text-gray-500">{t(lang, 'ownerReply')}</div>
              <p className="text-gray-400 text-sm mt-0.5">{own.ownerReply}</p>
            </div>
          )}
          <div className="flex gap-2 mt-3">
            <button onClick={startEdit} disabled={busy}
              className="border border-gray-700 text-gray-300 hover:bg-gray-800 text-sm font-medium py-1.5 px-3 rounded-xl transition-colors disabled:opacity-50">
              {t(lang, 'editReview')}
            </button>
            <button onClick={remove} disabled={busy}
              className="border border-red-800/60 text-red-400 hover:bg-red-900/30 text-sm font-medium py-1.5 px-3 rounded-xl transition-colors disabled:opacity-50">
              {t(lang, 'deleteReview')}
            </button>
          </div>
        </div>
      )}

      {formOpen && (
        <div className="bg-gray-900 border border-gray-700 rounded-2xl p-4 mb-4 space-y-3">
          <div>
            <div className="text-sm text-gray-400 mb-1">{t(lang, 'yourRating')}</div>
            <StarRating value={rating} onChange={setRating} size="lg" />
          </div>
          <textarea
            value={comment} onChange={(e) => setComment(e.target.value)} rows={3} maxLength={1000}
            placeholder={t(lang, 'reviewCommentPlaceholder')}
            className="w-full bg-gray-800 border border-gray-700 rounded-xl px-3 py-2 text-white text-sm placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          {error && <p className="text-red-400 text-xs">{error}</p>}
          <div className="flex gap-2">
            <button onClick={submit} disabled={busy}
              className="flex-1 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-semibold py-2 rounded-xl transition-colors">
              {own ? t(lang, 'saveReview') : t(lang, 'submitReview')}
            </button>
            <button onClick={() => setEditing(false)} disabled={busy}
              className="border border-gray-700 text-gray-400 hover:bg-gray-800 text-sm font-medium py-2 px-4 rounded-xl transition-colors disabled:opacity-50">
              {t(lang, 'back')}
            </button>
          </div>
        </div>
      )}

      {/* Public list */}
      {items.length === 0 ? (
        <p className="text-gray-500 text-sm">{t(lang, 'noReviews')}</p>
      ) : (
        <div className="space-y-3">
          {items.map((r) => (
            <div key={r.id} className="bg-gray-900 border border-gray-800 rounded-2xl p-4">
              <div className="flex items-center justify-between gap-2">
                <span className="text-white text-sm font-medium">{r.reviewerName}</span>
                <span className="text-gray-500 text-xs">{format(parseISO(r.createdAt), 'MMM d, yyyy', { locale })}</span>
              </div>
              <div className="mt-1"><StarRating value={r.rating} size="sm" /></div>
              {r.comment && <p className="text-gray-300 text-sm mt-2">{r.comment}</p>}
              {r.ownerReply && (
                <div className="mt-3 border-s-2 border-gray-700 ps-3">
                  <div className="text-xs text-gray-500">{t(lang, 'ownerReply')}</div>
                  <p className="text-gray-400 text-sm mt-0.5">{r.ownerReply}</p>
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {list.hasNextPage && (
        <button onClick={() => list.fetchNextPage()} disabled={list.isFetchingNextPage}
          className="w-full mt-3 border border-gray-700 text-gray-300 hover:bg-gray-800 text-sm font-medium py-2 rounded-xl transition-colors disabled:opacity-50">
          {t(lang, 'loadMore')}
        </button>
      )}
    </div>
  )
}
