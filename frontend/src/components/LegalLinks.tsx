import { Link } from 'react-router-dom'
import { t } from '../lib/i18n'

// Footer links to the legal pages, in the language of the page they're on (?lang=, QA run 11:
// the owner forms default to English but the legal pages opened in Hebrew). Shown on the home page, the sign-in/request pages and the legal pages.
export default function LegalLinks({ lang, className = '' }: { lang: string; className?: string }) {
  const cls = 'hover:text-ink hover:underline'
  return (
    <nav className={`flex flex-wrap justify-center gap-x-4 gap-y-1 text-xs text-muted ${className}`}>
      <Link to={`/terms?lang=${lang}`} className={cls}>{t(lang, 'legalTerms')}</Link>
      <Link to={`/privacy?lang=${lang}`} className={cls}>{t(lang, 'legalPrivacy')}</Link>
      <Link to={`/accessibility?lang=${lang}`} className={cls}>{t(lang, 'legalAccessibility')}</Link>
    </nav>
  )
}
