import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCustomerAuth } from '../../lib/customerAuth'
import { customerApi } from '../../lib/customerApi'
import { t } from '../../lib/i18n'
import { mediaUrl } from '../../lib/media'
import CustomerAccountNav from '../../components/customer/CustomerAccountNav'
import BackButton from '../../components/BackButton'
import StarRating from '../../components/customer/StarRating'

type BusinessResult = {
  slug: string; name: string; description: string | null; logo: string | null; isFollowed: boolean
  businessTypeKey: string | null; city: string | null
  ratingAverage: number; ratingCount: number; followerCount: number
}
type PagedResult = { items: BusinessResult[]; page: number; pageSize: number; total: number; hasMore: boolean }
type BusinessType = { id: string; key: string; displayNameEn: string; displayNameAr: string; displayNameHe: string }

const SORTS = ['rating', 'popular', 'newest', 'name'] as const
const SORT_KEY = { rating: 'sortRating', popular: 'sortPopular', newest: 'sortNewest', name: 'sortName' } as const
const PAGE_SIZE = 12

export default function BrowseBusinessesPage() {
  const { language: lang, isAuthenticated } = useCustomerAuth()
  const queryClient = useQueryClient()
  const [query, setQuery] = useState('')
  const [categoryKey, setCategoryKey] = useState('')
  const [city, setCity] = useState('')
  const [sort, setSort] = useState<(typeof SORTS)[number]>('rating')
  const [pending, setPending] = useState<string | null>(null)

  const trimmedQuery = query.trim()

  const { data: businessTypes = [] } = useQuery<BusinessType[]>({
    queryKey: ['business-types'],
    queryFn: () => customerApi.get('/business-types').then((r) => r.data),
  })
  const { data: cities = [] } = useQuery<string[]>({
    queryKey: ['business-cities'],
    queryFn: () => customerApi.get('/businesses/cities').then((r) => r.data),
  })
  const { data: followed = [] } = useQuery<BusinessResult[]>({
    queryKey: ['followed-businesses'],
    queryFn: () => customerApi.get('/businesses/followed').then((r) => r.data),
    enabled: isAuthenticated,
  })

  const {
    data, isLoading, fetchNextPage, hasNextPage, isFetchingNextPage,
  } = useInfiniteQuery({
    queryKey: ['business-search', trimmedQuery, categoryKey, city, sort],
    queryFn: ({ pageParam }) =>
      customerApi.get('/businesses/search', {
        params: {
          query: trimmedQuery || undefined,
          businessTypeKey: categoryKey || undefined,
          city: city || undefined,
          sort, page: pageParam, pageSize: PAGE_SIZE,
        },
      }).then((r) => r.data as PagedResult),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasMore ? last.page + 1 : undefined),
  })

  const results = data?.pages.flatMap((p) => p.items) ?? []
  const total = data?.pages[0]?.total ?? 0

  const typeLabelOf = (key: string | null) => {
    const bt = businessTypes.find((x) => x.key === key)
    if (!bt) return null
    return lang === 'AR' ? bt.displayNameAr : lang === 'HE' ? bt.displayNameHe : bt.displayNameEn
  }

  async function unfollow(slug: string) {
    setPending(slug)
    try {
      await customerApi.delete(`/businesses/${slug}/follow`)
      queryClient.invalidateQueries({ queryKey: ['followed-businesses'] })
    } finally { setPending(null) }
  }

  async function toggleFollow(slug: string, isFollowed: boolean) {
    // Following requires a customer session (WhatsApp-link only) -- the button is disabled for
    // logged-out visitors, so this is just a guard.
    if (!isAuthenticated) return
    setPending(slug)
    try {
      if (isFollowed) await customerApi.delete(`/businesses/${slug}/follow`)
      else await customerApi.post(`/businesses/${slug}/follow`)
      queryClient.invalidateQueries({ queryKey: ['business-search'] })
      queryClient.invalidateQueries({ queryKey: ['followed-businesses'] })
    } finally { setPending(null) }
  }

  return (
    <div className="min-h-screen bg-cream text-ink">
      <CustomerAccountNav />
      <div className="max-w-2xl mx-auto px-4 py-8">
        <BackButton lang={lang} />
        <h1 className="text-2xl font-bold text-ink mb-6 mt-3">{t(lang, 'discoverBusinesses')}</h1>

        <input
          type="text" value={query} onChange={(e) => setQuery(e.target.value)}
          placeholder={t(lang, 'searchBusinessesPlaceholder')}
          className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral"
        />

        {/* Category chips */}
        <div className="flex gap-2 mt-3 overflow-x-auto pb-1 -mx-1 px-1">
          <button
            onClick={() => setCategoryKey('')}
            className={`shrink-0 text-sm font-medium px-3 py-1.5 rounded-full border transition-colors ${
              categoryKey === '' ? 'bg-coral border-coral text-white' : 'bg-surface border-line text-ink hover:bg-cream'
            }`}>
            {t(lang, 'allCategories')}
          </button>
          {businessTypes.map((bt) => (
            <button
              key={bt.key}
              onClick={() => setCategoryKey(bt.key)}
              className={`shrink-0 text-sm font-medium px-3 py-1.5 rounded-full border transition-colors ${
                categoryKey === bt.key ? 'bg-coral border-coral text-white' : 'bg-surface border-line text-ink hover:bg-cream'
              }`}>
              {lang === 'AR' ? bt.displayNameAr : lang === 'HE' ? bt.displayNameHe : bt.displayNameEn}
            </button>
          ))}
        </div>

        {/* City + sort */}
        <div className="flex gap-2 mt-3">
          <select
            value={city} onChange={(e) => setCity(e.target.value)}
            className="flex-1 bg-surface border border-line rounded-xl px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-coral">
            <option value="">{t(lang, 'allCities')}</option>
            {cities.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
          <select
            value={sort} onChange={(e) => setSort(e.target.value as (typeof SORTS)[number])}
            className="flex-1 bg-surface border border-line rounded-xl px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-coral">
            {SORTS.map((s) => <option key={s} value={s}>{t(lang, SORT_KEY[s])}</option>)}
          </select>
        </div>

        {/* Results */}
        <div className="mt-5 mb-8">
          {isLoading ? (
            <div className="text-center text-muted py-8">{t(lang, 'loading')}</div>
          ) : results.length === 0 ? (
            <div className="text-center text-muted py-8">{t(lang, 'noBusinessesMatch')}</div>
          ) : (
            <>
              <div className="text-xs text-muted mb-2">{results.length} / {total}</div>
              <div className="space-y-3">
                {results.map((b) => (
                  <div key={b.slug} className="bg-surface border border-line rounded-2xl p-4 flex items-start gap-3">
                    <Link to={`/${b.slug}`} className="shrink-0">
                      {b.logo ? (
                        <img src={mediaUrl(b.logo)} alt={b.name} className="w-12 h-12 rounded-full object-cover border border-line" />
                      ) : (
                        <div className="w-12 h-12 rounded-full bg-cream flex items-center justify-center text-xl">✂️</div>
                      )}
                    </Link>
                    <div className="flex-1 min-w-0">
                      <Link to={`/${b.slug}`} className="block">
                        <div className="font-semibold text-ink truncate">{b.name}</div>
                        <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-muted mt-0.5">
                          {typeLabelOf(b.businessTypeKey) && <span>{typeLabelOf(b.businessTypeKey)}</span>}
                          {b.city && <span>· {b.city}</span>}
                          {b.followerCount > 0 && <span>· {b.followerCount} {t(lang, 'followerCountSuffix')}</span>}
                        </div>
                        {b.ratingCount > 0 && (
                          <div className="mt-1"><StarRating value={b.ratingAverage} count={b.ratingCount} size="sm" /></div>
                        )}
                        {b.description && <div className="text-muted text-sm truncate mt-1">{b.description}</div>}
                      </Link>
                    </div>
                    <button
                      disabled={pending === b.slug || !isAuthenticated}
                      title={isAuthenticated ? undefined : t(lang, 'whatsappOnlyAccess')}
                      onClick={() => toggleFollow(b.slug, b.isFollowed)}
                      className={`shrink-0 text-sm font-medium px-4 py-2 rounded-xl transition-colors disabled:opacity-50 ${
                        b.isFollowed
                          ? 'bg-teal-tint text-ink border border-line hover:bg-teal-tint/70'
                          : 'bg-coral hover:bg-coral-dark text-white'
                      }`}>
                      {b.isFollowed ? t(lang, 'following') : t(lang, 'followBusiness')}
                    </button>
                  </div>
                ))}
              </div>
              {hasNextPage && (
                <button
                  onClick={() => fetchNextPage()}
                  disabled={isFetchingNextPage}
                  className="w-full mt-4 text-sm font-medium py-2.5 rounded-xl border border-line text-ink hover:bg-cream transition-colors disabled:opacity-50">
                  {isFetchingNextPage ? t(lang, 'loading') : t(lang, 'loadMore')}
                </button>
              )}
            </>
          )}
        </div>

        {isAuthenticated && (
          <div className="mt-6">
            <h2 className="text-sm font-semibold text-muted mb-2">{t(lang, 'followedBusinesses')}</h2>
            {followed.length === 0 ? (
              <div className="text-center text-muted py-8">{t(lang, 'noFollowedYet')}</div>
            ) : (
              <div className="space-y-2">
                {followed.map((b) => (
                  <div key={b.slug} className="bg-surface border border-line rounded-xl px-4 py-2.5 flex items-center justify-between gap-3">
                    <Link to={`/${b.slug}`} className="flex-1 min-w-0 text-sm font-medium text-ink truncate">{b.name}</Link>
                    <button
                      disabled={pending === b.slug}
                      onClick={() => unfollow(b.slug)}
                      className="shrink-0 text-xs text-muted hover:text-red-600 font-medium px-2 py-1 disabled:opacity-50">
                      {t(lang, 'remove')}
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
