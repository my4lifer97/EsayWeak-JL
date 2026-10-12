import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
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
  const { token } = useParams<{ token: string }>()
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
    <div className="min-h-screen bg-cream text-ink flex items-center justify-center px-4">
      <div className="text-center max-w-sm">
        {error ? (
          <p className="text-muted">{t(lang, 'whatsappLinkExpired')}</p>
        ) : (
          <p className="text-muted">{t(lang, 'loading')}</p>
        )}
      </div>
    </div>
  )
}
