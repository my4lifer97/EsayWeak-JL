import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { platformAdminApi } from '../../lib/platformAdminApi'

type OwnerEmailHealth = { status: 'ok' | 'failing' | 'unknown'; failingSince: string | null; lastError: string | null; lastCheckedAt: string | null }
type SystemHealth = { ownerEmail: OwnerEmailHealth }

// Shown on the platform-admin dashboard only while owner emails (Gmail) are failing -- the
// backend also emails the admin about it (OwnerEmailMonitor), this is the in-app side.
export default function OwnerEmailHealthBanner() {
  const queryClient = useQueryClient()
  const { data } = useQuery<SystemHealth>({
    queryKey: ['platform-admin-system-health'],
    queryFn: () => platformAdminApi.get('/platform-admin/system-health').then((r) => r.data),
    refetchInterval: 60_000,
  })
  const recheck = useMutation({
    mutationFn: () => platformAdminApi.post<SystemHealth>('/platform-admin/system-health/owner-email/check').then((r) => r.data),
    onSuccess: (fresh) => queryClient.setQueryData(['platform-admin-system-health'], fresh),
  })

  const health = data?.ownerEmail
  if (health?.status !== 'failing') return null

  return (
    <div role="alert" className="mb-6 rounded-2xl border border-red-300 bg-red-50 text-red-800 dark:border-red-800 dark:bg-red-950/40 dark:text-red-300 p-4">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <p className="font-semibold">Business owner emails are failing</p>
          <p className="text-sm mt-1">
            {health.failingSince && <>Since {new Date(health.failingSince).toLocaleString()}. </>}
            Owner login details, request email codes, cancellation approvals and inquiry emails aren't being delivered.
          </p>
          {health.lastError?.includes('invalid_grant') && (
            <p className="text-sm mt-1">
              The Gmail refresh token expired or was revoked. Get a new one from the OAuth Playground and replace{' '}
              <code className="font-mono">Gmail__RefreshToken</code> in Railway.
            </p>
          )}
          {health.lastError && <p className="text-xs mt-2 font-mono break-all opacity-80">{health.lastError}</p>}
        </div>
        <button
          onClick={() => recheck.mutate()} disabled={recheck.isPending}
          className="shrink-0 text-sm px-3 py-1.5 rounded-lg border border-red-300 dark:border-red-800 hover:bg-red-100 dark:hover:bg-red-900/40 disabled:opacity-50 transition-colors"
        >
          {recheck.isPending ? 'Checking…' : 'Check again'}
        </button>
      </div>
    </div>
  )
}
