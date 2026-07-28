import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t } from '../../lib/i18n'

type CustomerSummary = { id: string; name: string; familyName: string; phone: string }

export type CustomerSelection =
  | { customerId: string; label: string }
  | { customerName: string; customerPhone: string }

export default function CustomerPicker({
  lang, value, onChange,
}: {
  lang: string
  value: CustomerSelection | null
  onChange: (selection: CustomerSelection | null) => void
}) {
  const [mode, setMode] = useState<'existing' | 'new'>('existing')
  const [query, setQuery] = useState('')
  const [newName, setNewName] = useState('')
  const [newPhone, setNewPhone] = useState('')

  const { data: results = [], isFetching } = useQuery<CustomerSummary[]>({
    queryKey: ['customer-search', query],
    queryFn: () => api.get(`/admin/customers/search?query=${encodeURIComponent(query)}`).then((r) => r.data),
    enabled: mode === 'existing' && query.trim().length >= 2,
  })

  function selectExisting(c: CustomerSummary) {
    setQuery(`${c.name} · ${c.phone}`)
    onChange({ customerId: c.id, label: `${c.name} · ${c.phone}` })
  }

  function switchMode(next: 'existing' | 'new') {
    setMode(next)
    onChange(null)
    setQuery(''); setNewName(''); setNewPhone('')
  }

  return (
    <div>
      <div className="flex gap-1 bg-gray-800 rounded-lg p-1 mb-2 w-fit">
        <button type="button" onClick={() => switchMode('existing')}
          className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${mode === 'existing' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white'}`}>
          {t(lang, 'existingCustomer')}
        </button>
        <button type="button" onClick={() => switchMode('new')}
          className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${mode === 'new' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white'}`}>
          {t(lang, 'newCustomerOption')}
        </button>
      </div>

      {mode === 'existing' ? (
        <div className="relative">
          <input type="text" value={query}
            onChange={(e) => { setQuery(e.target.value); onChange(null) }}
            placeholder={t(lang, 'customerSearchPlaceholder')}
            className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
          {query.trim().length >= 2 && !('customerId' in (value ?? {})) && (
            <div className="absolute z-10 mt-1 w-full bg-gray-800 border border-gray-700 rounded-lg max-h-48 overflow-y-auto">
              {isFetching ? (
                <div className="px-3 py-2 text-gray-500 text-sm">{t(lang, 'loading')}</div>
              ) : results.length === 0 ? (
                <div className="px-3 py-2 text-gray-500 text-sm">{t(lang, 'noCustomersFound')}</div>
              ) : (
                results.map((c) => (
                  <button key={c.id} type="button" onClick={() => selectExisting(c)}
                    className="w-full text-start px-3 py-2 text-sm text-gray-200 hover:bg-gray-700">
                    {c.name} {c.familyName} · {c.phone}
                  </button>
                ))
              )}
            </div>
          )}
        </div>
      ) : (
        <div className="space-y-2">
          <input type="text" required value={newName}
            onChange={(e) => { setNewName(e.target.value); onChange({ customerName: e.target.value, customerPhone: newPhone }) }}
            placeholder={t(lang, 'customerName')}
            className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
          <input type="tel" required value={newPhone}
            onChange={(e) => { setNewPhone(e.target.value); onChange({ customerName: newName, customerPhone: e.target.value }) }}
            placeholder={t(lang, 'phoneNumber')}
            className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
      )}
    </div>
  )
}
