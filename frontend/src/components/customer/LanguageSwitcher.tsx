import { useCustomerAuth } from '../../lib/customerAuth'

const OPTIONS = [
  { value: 'HE', label: 'עברית' },
  { value: 'EN', label: 'EN' },
  { value: 'AR', label: 'العربية' },
]

export default function LanguageSwitcher() {
  const { language, setLang } = useCustomerAuth()

  return (
    <div className="flex items-center gap-1 bg-surface border border-line rounded-lg p-0.5">
      {OPTIONS.map((o) => (
        <button
          key={o.value}
          type="button"
          onClick={() => setLang(o.value)}
          className={`px-2.5 py-1 rounded-md text-xs font-medium transition-colors ${
            language === o.value ? 'bg-coral text-white' : 'text-muted hover:text-ink'
          }`}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}
