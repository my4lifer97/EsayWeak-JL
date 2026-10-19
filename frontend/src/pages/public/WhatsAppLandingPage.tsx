import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t } from '../../lib/i18n'

// Landing point for the booking link the WhatsApp bot sends after a customer picks a service
// (WhatsAppController -> POST /customer/auth/whatsapp). Redeems the token, then sends the
// customer straight into the booking wizard with their service already chosen -- no sign-up/
// sign-in step and no service-selection step, per the WhatsApp booking flow.
function browserLanguage(): string | null {
  const code = (navigator.language || '').slice(0, 2).toLowerCase()
  return code === 'he' || code === 'iw' ? 'HE' : code === 'ar' ? 'AR' : code === 'en' ? 'EN' : null
}

export default function WhatsAppLandingPage() {
  const { slug, token } = useParams<{ slug: string; token: string }>()
  const { loginWithWhatsAppToken, language: lang, setLang } = useCustomerAuth()
  const navigate = useNavigate()
  const [error, setError] = useState(false)

  useEffect(() => {
    if (!token) { setError(true); return }
    loginWithWhatsAppToken(token)
      .then(({ businessSlug, itemId }) => navigate(`/${businessSlug}/book?itemId=${itemId}`, { replace: true }))
      .catch((err: unknown) => {
        // An expired link still knows the language the customer chatted in. For a link the
        // server never issued, use the browser's language if this browser has no saved choice.
        const chatLang = (err as { response?: { data?: { language?: string } } })?.response?.data?.language
        const fallback = localStorage.getItem('customerLang') ? null : browserLanguage()
        if (chatLang || fallback) setLang((chatLang || fallback)!)
        setError(true)
      })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token])

  return (
    <div className="min-h-screen bg-cream text-ink flex items-center justify-center px-4" dir={lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'}>
      <div className="text-center max-w-sm">
        {error ? (
          <>
            <p className="text-muted mb-4">{t(lang, 'whatsappLinkExpired')}</p>
            {/* Not a dead end (QA run 3): the business page, or sign in with the phone instead. */}
            <div className="flex flex-wrap justify-center gap-3">
              {slug && <Link to={`/${slug}`} className="bg-coral hover:bg-coral-dark text-white font-semibold px-5 py-2.5 rounded-xl transition-colors">{t(lang, 'viewBusinessPage')}</Link>}
              <Link to={slug ? `/login?next=${encodeURIComponent(`/${slug}/book`)}` : '/login'} className="border border-line px-5 py-2.5 rounded-xl text-ink">{t(lang, 'navLogin')}</Link>
            </div>
          </>
        ) : (
          <p className="text-muted">{t(lang, 'loading')}</p>
        )}
      </div>
    </div>
  )
}
