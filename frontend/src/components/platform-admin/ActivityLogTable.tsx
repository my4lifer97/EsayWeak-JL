import { useState } from 'react'

export type ActivityLogEntry = {
  id: string; action: string; description: string; method: string; path: string
  statusCode: number; ipAddress: string | null; createdAt: string; impersonated: boolean
}

// "RecurringAppointmentsController.Delete" -> "Delete" -- same regex as the backend's
// ActivityLogFilter.Prettify, applied to just the action-name half for a short dropdown label.
function prettifyActionLabel(action: string): string {
  const methodName = action.split('.').pop() ?? action
  return methodName.replace(/(?<!^)([A-Z])/g, ' $1')
}

const selectClass = 'bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500'

export function ActivityLogTable({ entries }: { entries: ActivityLogEntry[] | undefined }) {
  const [search, setSearch] = useState('')
  const [actionFilter, setActionFilter] = useState('')
  const [impersonatedFilter, setImpersonatedFilter] = useState<'all' | 'impersonated' | 'direct'>('all')

  if (!entries) return <p className="text-gray-500 text-sm">Loading...</p>
  if (entries.length === 0) return <p className="text-gray-500 text-sm">No activity recorded yet.</p>

  const actionOptions = [...new Set(entries.map((e) => e.action))].sort()

  const filtered = entries.filter((e) => {
    if (search && !e.description.toLowerCase().includes(search.toLowerCase())) return false
    if (actionFilter && e.action !== actionFilter) return false
    if (impersonatedFilter === 'impersonated' && !e.impersonated) return false
    if (impersonatedFilter === 'direct' && e.impersonated) return false
    return true
  })

  const hasActiveFilters = !!search || !!actionFilter || impersonatedFilter !== 'all'
  function clearFilters() {
    setSearch(''); setActionFilter(''); setImpersonatedFilter('all')
  }

  return (
    <div>
      <div className="flex flex-wrap gap-3 mb-4 items-center">
        <input type="text" placeholder="Search activity..." value={search} onChange={(e) => setSearch(e.target.value)}
          className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-white text-sm placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500" />
        <select value={actionFilter} onChange={(e) => setActionFilter(e.target.value)} className={selectClass}>
          <option value="">All actions</option>
          {actionOptions.map((a) => <option key={a} value={a}>{prettifyActionLabel(a)}</option>)}
        </select>
        <select value={impersonatedFilter} onChange={(e) => setImpersonatedFilter(e.target.value as typeof impersonatedFilter)} className={selectClass}>
          <option value="all">All actors</option>
          <option value="impersonated">Via impersonation only</option>
          <option value="direct">Direct only</option>
        </select>
        {hasActiveFilters && (
          <button onClick={clearFilters} className="text-sm text-blue-400 hover:text-blue-300">Clear Filters</button>
        )}
        <span className="text-gray-500 text-xs ms-auto">{filtered.length} / {entries.length} events</span>
      </div>

      {filtered.length === 0 ? (
        <p className="text-gray-500 text-sm">No activity matches these filters.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500 border-b border-gray-800">
                <th className="py-2 pr-4 font-medium">Action</th>
                <th className="py-2 pr-4 font-medium">Status</th>
                <th className="py-2 pr-4 font-medium">When</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((e) => (
                <tr key={e.id} className="border-b border-gray-800/60">
                  <td className="py-2 pr-4">
                    {e.description}
                    {e.impersonated && (
                      <span className="ml-2 text-xs text-yellow-400/80">(via impersonation)</span>
                    )}
                  </td>
                  <td className="py-2 pr-4 text-gray-400">{e.method} {e.statusCode}</td>
                  <td className="py-2 pr-4 text-gray-500">{new Date(e.createdAt).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
