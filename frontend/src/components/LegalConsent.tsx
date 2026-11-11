import { Fragment } from 'react'
import { Link } from 'react-router-dom'
import { t, type TKey } from '../lib/i18n'

// Fills {terms} and {privacy} in a translated sentence with links that open in a new tab, so
// reading them doesn't lose what the user typed into the form.
function WithLegalLinks({ lang, textKey }: { lang: string; textKey: TKey }) {
  const parts = t(lang, textKey).split(/(\{terms\}|\{privacy\})/)
  return (
    <>
      {parts.map((part, i) => {
        if (part === '{terms}' || part === '{privacy}') {
          const terms = part === '{terms}'
          return (
            <Link key={i} to={`${terms ? '/terms' : '/privacy'}?lang=${lang}`} target="_blank" rel="noopener"
              className="text-coral-dark hover:underline">
              {t(lang, terms ? 'legalTerms' : 'legalPrivacy')}
            </Link>
          )
        }
        return <Fragment key={i}>{part}</Fragment>
      })}
    </>
  )
}

// Required "I agree" checkbox for account forms.
export function ConsentCheckbox({ lang, checked, onChange }: { lang: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="flex items-start gap-2 text-sm text-ink cursor-pointer">
      <input type="checkbox" required checked={checked} onChange={(e) => onChange(e.target.checked)}
        className="mt-0.5 h-4 w-4 shrink-0 accent-coral" />
      <span><WithLegalLinks lang={lang} textKey="consentAgree" /></span>
    </label>
  )
}

// One-line notice under a customer action (sign-in, booking): no checkbox, as on most booking sites.
export function ConsentNotice({ lang, className = '' }: { lang: string; className?: string }) {
  return <p className={`text-xs text-muted text-center ${className}`}><WithLegalLinks lang={lang} textKey="consentNotice" /></p>
}
