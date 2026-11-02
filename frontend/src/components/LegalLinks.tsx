import { Link } from 'react-router-dom'
import { t } from '../lib/i18n'

// Footer links to the legal pages. Shown on the home page, the sign-in/request pages and the legal pages.
export default function LegalLinks({ lang, className = '' }: { lang: string; className?: string }) {
  const cls = 'hover:text-ink hover:underline'
  return (
    <nav className={`flex flex-wrap justify-center gap-x-4 gap-y-1 text-xs text-muted ${className}`}>
      <Link to="/terms" className={cls}>{t(lang, 'legalTerms')}</Link>
      <Link to="/privacy" className={cls}>{t(lang, 'legalPrivacy')}</Link>
      <Link to="/accessibility" className={cls}>{t(lang, 'legalAccessibility')}</Link>
    </nav>
  )
}
