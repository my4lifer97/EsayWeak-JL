import { useState } from 'react'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { platformAdminApi } from '../../lib/platformAdminApi'
import { usePlatformAdminAuth } from '../../lib/platformAdminAuth'
import ThemeToggle from '../../components/ThemeToggle'
import OwnerEmailHealthBanner from '../../components/platform-admin/OwnerEmailHealthBanner'

type BusinessSummary = { id: string; name: string; email: string; slug: string; subscriptionStatus: string }
type CustomerSummary = { id: string; name: string; familyName: string; phone: string }
type PagedResult<T> = { items: T[]; page: number; pageSize: number; total: number; hasMore: boolean }

// Both lists are paged server-side (50 a page); they used to stop silently at 50 rows.
function usePagedSearch<T>(path: string, search: string) {
  const query = useInfiniteQuery({
    queryKey: [path, search],
    queryFn: ({ pageParam }) =>
      platformAdminApi.get<PagedResult<T>>(path, { params: { search: search || undefined, page: pageParam } }).then((r) => r.data),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasMore ? last.page + 1 : undefined),
  })
  return {
    items: query.data?.pages.flatMap((p) => p.items),
    total: query.data?.pages[0]?.total,
    hasMore: query.hasNextPage,
    loadingMore: query.isFetchingNextPage,
    loadMore: () => query.fetchNextPage(),
  }
}

function ListFooter({ shown, total, hasMore, loadingMore, loadMore }: {
  shown: number; total?: number; hasMore: boolean; loadingMore: boolean; loadMore: () => void
}) {
  if (total === undefined || total === 0) return null
  return (
    <div className="flex items-center justify-between mt-2 px-3 text-xs text-muted">
      <span>{shown} of {total}</span>
      {hasMore && (
        <button onClick={loadMore} disabled={loadingMore} className="text-coral-dark hover:underline disabled:opacity-50">
          {loadingMore ? 'Loading…' : 'Load more'}
        </button>
      )}
    </div>
  )
}

export default function PlatformAdminDashboardPage() {
  const { user, logout } = usePlatformAdminAuth()
  const [businessSearch, setBusinessSearch] = useState('')
  const [customerSearch, setCustomerSearch] = useState('')

  const businessList = usePagedSearch<BusinessSummary>('/platform-admin/businesses', businessSearch)
  const customerList = usePagedSearch<CustomerSummary>('/platform-admin/customers', customerSearch)
  const businesses = businessList.items
  const customers = customerList.items

  return (
    <div className="min-h-screen bg-cream text-ink p-6">
      <div className="max-w-5xl mx-auto">
        <div className="flex items-center justify-between mb-8">
          <h1 className="text-2xl font-bold">Platform Admin</h1>
          <div className="flex items-center gap-4 text-sm text-muted">
            <Link to="/platform-admin/requests" className="hover:text-ink transition-colors">Business requests</Link>
            <span>{user?.name}</span>
            <button onClick={logout} className="hover:text-ink transition-colors">Sign out</button>
            <ThemeToggle />
          </div>
        </div>

        <OwnerEmailHealthBanner />

        <div className="grid md:grid-cols-2 gap-6">
          <section className="bg-surface border border-line rounded-2xl p-5">
            <h2 className="font-semibold mb-3">Business owners</h2>
            <input
              value={businessSearch} onChange={(e) => setBusinessSearch(e.target.value)}
              placeholder="Search by name, email, or URL"
              className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-sm mb-3 focus:outline-none focus:ring-2 focus:ring-coral"
            />
            <div className="space-y-1 max-h-96 overflow-y-auto">
              {businesses?.map((b) => (
                <Link key={b.id} to={`/platform-admin/businesses/${b.id}`}
                  className="flex items-center justify-between px-3 py-2.5 rounded-lg hover:bg-cream transition-colors">
                  <div>
                    <div className="text-sm font-medium">{b.name}</div>
                    <div className="text-xs text-muted">{b.email} · /{b.slug}</div>
                  </div>
                  <span className={`text-xs px-2 py-0.5 rounded-full ${
                    b.subscriptionStatus === 'ACTIVE' ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400'
                      : b.subscriptionStatus === 'TRIAL' ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-400' : 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-400'
                  }`}>{b.subscriptionStatus}</span>
                </Link>
              ))}
              {businesses?.length === 0 && <p className="text-muted text-sm px-3 py-2">No results</p>}
            </div>
            <ListFooter shown={businesses?.length ?? 0} {...businessList} />
          </section>

          <section className="bg-surface border border-line rounded-2xl p-5">
            <h2 className="font-semibold mb-3">Customers</h2>
            <input
              value={customerSearch} onChange={(e) => setCustomerSearch(e.target.value)}
              placeholder="Search by name or phone"
              className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-sm mb-3 focus:outline-none focus:ring-2 focus:ring-coral"
            />
            <div className="space-y-1 max-h-96 overflow-y-auto">
              {customers?.map((c) => (
                <Link key={c.id} to={`/platform-admin/customers/${c.id}`}
                  className="flex items-center justify-between px-3 py-2.5 rounded-lg hover:bg-cream transition-colors">
                  <div>
                    <div className="text-sm font-medium">{c.name} {c.familyName}</div>
                    <div className="text-xs text-muted">{c.phone}</div>
                  </div>
                </Link>
              ))}
              {customers?.length === 0 && <p className="text-muted text-sm px-3 py-2">No results</p>}
            </div>
            <ListFooter shown={customers?.length ?? 0} {...customerList} />
          </section>
        </div>
      </div>
    </div>
  )
}
