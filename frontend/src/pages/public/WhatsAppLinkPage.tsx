import { useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t } from '../../lib/i18n'
import ThemeToggle from '../../components/ThemeToggle'

type LinkStatus = {
  businessName: string
  language: string
  state: 'qr' | 'connecting' | 'connected' | 'disconnected'
  qr: string | null
  phoneNumber: string | null
}

// Public, token-secured page a business owner opens themselves (link handed off by the platform
// admin) to complete their own WhatsApp-linking QR scan -- see WhatsAppLinkController and
// PlatformAdminController.CreateWhatsAppLinkToken. No login: the opaque token in the URL is the
// only credential. Polls like PlatformAdminBusinessDetailPage's own QR view, just without needing
// an admin session.
export default function WhatsAppLinkPage() {
  const { token } = useParams<{ token: string }>()

  const { data, isLoading, error } = useQuery<LinkStatus>({
    queryKey: ['wa-link-status', token],
    queryFn: () => api.get(`/wa-link/${token}`).then((r) => r.data),
    refetchInterval: (query) => (query.state.data?.state === 'connected' ? false : 2000),
  })

  const lang = data?.language ?? 'EN'
  const dir = lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'

  return (
    <div className="min-h-screen bg-cream text-ink flex items-center justify-center px-4" dir={dir}>
      <div className="absolute top-4 right-4"><ThemeToggle /></div>
      <div className="text-center max-w-sm w-full">
        {isLoading ? (
          <p className="text-muted">{t(lang, 'loading')}</p>
        ) : error || !data ? (
          <p className="text-muted">{t(lang, 'waLinkExpired')}</p>
        ) : (
          <div className="bg-surface border border-line rounded-2xl p-6">
            <h1 className="text-xl font-bold mb-1">{data.businessName}</h1>
            <p className="text-muted text-sm mb-5">WhatsApp</p>

            {data.state === 'connected' ? (
              <p className="text-green-600 dark:text-green-400 font-medium">
                {t(lang, 'waLinkConnected')} {data.phoneNumber}
              </p>
            ) : data.state === 'qr' && data.qr ? (
              <div>
                <img src={data.qr} alt="WhatsApp link QR code" width={220} height={220}
                  className="mx-auto mb-4 rounded-lg bg-white p-2" />
                <p className="text-muted text-sm">{t(lang, 'waLinkScanInstructions')}</p>
                <p className="text-muted text-xs mt-2">{t(lang, 'waLinkWaiting')}</p>
              </div>
            ) : (
              <p className="text-muted text-sm">{t(lang, 'waLinkConnecting')}</p>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
