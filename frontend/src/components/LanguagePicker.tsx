const OPTIONS = [
  { value: 'HE', label: 'עברית' },
  { value: 'EN', label: 'EN' },
  { value: 'AR', label: 'العربية' },
]

// Plain HE/EN/AR toggle. Callers decide where the choice is stored (customer vs owner language).
export default function LanguagePicker({ value, onChange }: { value: string; onChange: (lang: string) => void }) {
  return (
    <div className="flex items-center gap-1 bg-surface border border-line rounded-lg p-0.5">
      {OPTIONS.map((o) => (
        <button
          key={o.value}
          type="button"
          onClick={() => onChange(o.value)}
          className={`px-2.5 py-1 rounded-md text-xs font-medium transition-colors ${
            value === o.value ? 'bg-coral text-white' : 'text-muted hover:text-ink'
          }`}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}
