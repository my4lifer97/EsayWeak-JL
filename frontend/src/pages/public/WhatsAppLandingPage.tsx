import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t } from '../../lib/i18n'

// Landing point for the booking link the WhatsApp bot sends after a customer picks a service
// (WhatsAppController -> POST /customer/auth/whatsapp). Redeems the token, then sends the
// customer straight into the booking wizard with their service already chosen -- no sign-up/
// sign-in step and no service-selection step, per the WhatsApp booking flow.
export default function WhatsAppLandingPage() {
  const { token } = useParams<{ token: string }>()
  const { loginWithWhatsAppToken, language: lang } = useCustomerAuth()
  const navigate = useNavigate()
  const [error, setError] = useState(false)

  useEffect(() => {
    if (!token) { setError(true); return }
    loginWithWhatsAppToken(token)
      .then(({ barberSlug, serviceId }) => navigate(`/${barberSlug}/book?serviceId=${serviceId}`, { replace: true }))
      .catch(() => setError(true))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token])

  return (
    <div className="min-h-screen bg-gray-950 text-white flex items-center justify-center px-4">
      <div className="text-center max-w-sm">
        {error ? (
          <p className="text-gray-400">{t(lang, 'whatsappLinkExpired')}</p>
        ) : (
          <p className="text-gray-500">{t(lang, 'loading')}</p>
        )}
      </div>
    </div>
  )
}
