import { Link } from 'react-router-dom'
import { useCustomerAuth } from '../lib/customerAuth'
import { t } from '../lib/i18n'

// Catch-all for any URL no route matches (QA run 2: it used to render a blank screen).
export default function NotFoundPage() {
  const { language: lang } = useCustomerAuth()
  const dir = lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'
  return (
    <div dir={dir} className="min-h-screen bg-cream flex items-center justify-center p-6">
      <div className="text-center max-w-sm">
        <p className="text-5xl font-bold text-coral mb-3">404</p>
        <h1 className="text-xl font-semibold text-ink mb-2">{t(lang, 'pageNotFound')}</h1>
        <p className="text-muted mb-6">{t(lang, 'pageNotFoundBody')}</p>
        <Link to="/" className="inline-block bg-coral hover:bg-coral-dark text-white font-medium px-5 py-2.5 rounded-xl transition-colors">
          {t(lang, 'goHome')}
        </Link>
      </div>
    </div>
  )
}
