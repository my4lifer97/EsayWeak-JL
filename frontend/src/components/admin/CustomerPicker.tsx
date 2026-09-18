import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t } from '../../lib/i18n'

type CustomerSummary = { id: string; name: string; familyName: string; phone: string }
export type WaitlistEntrySummary = { id: string; name: string; familyName: string; phone: string }

export type CustomerSelection =
  | { customerId: string; label: string }
  | { customerName: string; customerFamilyName: string; customerPhone: string }
  | { waitlistEntryId: string; label: string }

export default function CustomerPicker({
  lang, value, onChange, waitlistEntries,
}: {
  lang: string
  value: CustomerSelection | null
  onChange: (selection: CustomerSelection | null) => void
  waitlistEntries?: WaitlistEntrySummary[]
}) {
  const [mode, setMode] = useState<'existing' | 'new' | 'waitlist'>('existing')
  const [query, setQuery] = useState('')
  const [newName, setNewName] = useState('')
  const [newFamilyName, setNewFamilyName] = useState('')
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

  function selectFromWaitlist(w: WaitlistEntrySummary) {
    onChange({ waitlistEntryId: w.id, label: `${w.name} ${w.familyName} · ${w.phone}` })
  }

  function switchMode(next: 'existing' | 'new' | 'waitlist') {
    setMode(next)
    onChange(null)
    setQuery(''); setNewName(''); setNewFamilyName(''); setNewPhone('')
  }

  return (
    <div>
      <div className="flex gap-1 bg-cream rounded-lg p-1 mb-2 w-fit">
        <button type="button" onClick={() => switchMode('existing')}
          className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${mode === 'existing' ? 'bg-coral text-white' : 'text-muted hover:text-ink'}`}>
          {t(lang, 'existingCustomer')}
        </button>
        <button type="button" onClick={() => switchMode('new')}
          className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${mode === 'new' ? 'bg-coral text-white' : 'text-muted hover:text-ink'}`}>
          {t(lang, 'newCustomerOption')}
        </button>
        {waitlistEntries && waitlistEntries.length > 0 && (
          <button type="button" onClick={() => switchMode('waitlist')}
            className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${mode === 'waitlist' ? 'bg-coral text-white' : 'text-muted hover:text-ink'}`}>
            {t(lang, 'fromWaitlistOption')}
          </button>
        )}
      </div>

      {mode === 'waitlist' && waitlistEntries ? (
        <div className="bg-cream border border-line rounded-lg max-h-48 overflow-y-auto">
          {waitlistEntries.map((w) => (
            <button key={w.id} type="button" onClick={() => selectFromWaitlist(w)}
              className={`w-full text-start px-3 py-2 text-sm hover:bg-surface ${
                value && 'waitlistEntryId' in value && value.waitlistEntryId === w.id ? 'bg-surface text-ink' : 'text-ink'
              }`}>
              {w.name} {w.familyName} · {w.phone}
            </button>
          ))}
        </div>
      ) : mode === 'existing' ? (
        <div className="relative">
          <input type="text" value={query}
            onChange={(e) => { setQuery(e.target.value); onChange(null) }}
            placeholder={t(lang, 'customerSearchPlaceholder')}
            className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          {query.trim().length >= 2 && !('customerId' in (value ?? {})) && (
            <div className="absolute z-10 mt-1 w-full bg-surface border border-line rounded-lg max-h-48 overflow-y-auto">
              {isFetching ? (
                <div className="px-3 py-2 text-muted text-sm">{t(lang, 'loading')}</div>
              ) : results.length === 0 ? (
                <div className="px-3 py-2 text-muted text-sm">{t(lang, 'noCustomersFound')}</div>
              ) : (
                results.map((c) => (
                  <button key={c.id} type="button" onClick={() => selectExisting(c)}
                    className="w-full text-start px-3 py-2 text-sm text-ink hover:bg-cream">
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
            onChange={(e) => { setNewName(e.target.value); onChange({ customerName: e.target.value, customerFamilyName: newFamilyName, customerPhone: newPhone }) }}
            placeholder={t(lang, 'customerName')}
            className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          <input type="text" required value={newFamilyName}
            onChange={(e) => { setNewFamilyName(e.target.value); onChange({ customerName: newName, customerFamilyName: e.target.value, customerPhone: newPhone }) }}
            placeholder={t(lang, 'familyName')}
            className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          <input type="tel" required value={newPhone}
            onChange={(e) => { setNewPhone(e.target.value); onChange({ customerName: newName, customerFamilyName: newFamilyName, customerPhone: e.target.value }) }}
            placeholder={t(lang, 'phoneNumber')}
            className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
        </div>
      )}
    </div>
  )
}
