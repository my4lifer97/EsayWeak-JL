import { useNavigate } from 'react-router-dom'
import { t } from '../lib/i18n'

// Browser-history back, not a fixed route — works consistently regardless of which page
// the customer arrived from (a barber's page, a search result, a direct link, etc).
export default function BackButton({ lang, fallback = '/' }: { lang: string; fallback?: string }) {
  const navigate = useNavigate()

  function goBack() {
    if (window.history.length > 1) navigate(-1)
    else navigate(fallback)
  }

  return (
    <button onClick={goBack} className="text-gray-400 hover:text-white text-sm transition-colors">
      ← {t(lang, 'back')}
    </button>
  )
}
