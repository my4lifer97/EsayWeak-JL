import { Link } from 'react-router-dom'
import { useCustomerAuth } from '../lib/customerAuth'
import { t } from '../lib/i18n'
import LanguageSwitcher from '../components/customer/LanguageSwitcher'

export default function HomePage() {
  const { language: lang } = useCustomerAuth()
  const dir = lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'

  const FEATURES = [
    { icon: '📅', title: t(lang, 'homeFeature1Title'), desc: t(lang, 'homeFeature1Desc') },
    { icon: '💬', title: t(lang, 'homeFeature2Title'), desc: t(lang, 'homeFeature2Desc') },
    { icon: '🤖', title: t(lang, 'homeFeature3Title'), desc: t(lang, 'homeFeature3Desc') },
    { icon: '🌍', title: t(lang, 'homeFeature4Title'), desc: t(lang, 'homeFeature4Desc') },
    { icon: '📱', title: t(lang, 'homeFeature5Title'), desc: t(lang, 'homeFeature5Desc') },
    { icon: '✂️', title: t(lang, 'homeFeature6Title'), desc: t(lang, 'homeFeature6Desc') },
  ]

  const pricingItems = [
    t(lang, 'homePricingItem1'), t(lang, 'homePricingItem2'), t(lang, 'homePricingItem3'),
    t(lang, 'homePricingItem4'), t(lang, 'homePricingItem5'), t(lang, 'homePricingItem6'),
    t(lang, 'homePricingItem7'),
  ]

  return (
    <div className="min-h-screen bg-gray-950 text-white" dir={dir}>
      <nav className="flex items-center justify-between px-6 py-4 border-b border-gray-900 max-w-6xl mx-auto">
        <div className="font-bold text-xl">✂️ EsayWeek</div>
        <div className="flex items-center gap-4">
          <LanguageSwitcher />
          <Link to="/admin/login" className="text-gray-400 hover:text-white text-sm transition-colors">{t(lang, 'homeSignIn')}</Link>
          <Link to="/admin/register" className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">{t(lang, 'homeGetStarted')}</Link>
        </div>
      </nav>

      <section className="text-center py-24 px-4 max-w-3xl mx-auto">
        <div className="inline-block bg-blue-600/10 border border-blue-600/30 text-blue-400 text-sm font-medium px-4 py-1.5 rounded-full mb-6">
          {t(lang, 'homeTrialBadge')}
        </div>
        <h1 className="text-5xl font-bold leading-tight mb-6">{t(lang, 'homeHeadline1')}<br /><span className="text-blue-400">{t(lang, 'homeHeadline2')}</span></h1>
        <p className="text-gray-400 text-xl mb-10 leading-relaxed">{t(lang, 'homeSubheadline')}</p>
        <div className="flex gap-4 justify-center flex-wrap">
          <Link to="/admin/register" className="bg-blue-600 hover:bg-blue-700 text-white font-bold px-8 py-4 rounded-2xl text-lg transition-colors">{t(lang, 'homeStartTrial')}</Link>
          <Link to="/admin/login" className="bg-gray-800 hover:bg-gray-700 text-white font-semibold px-8 py-4 rounded-2xl text-lg transition-colors">{t(lang, 'homeSignIn')}</Link>
        </div>
      </section>

      <section className="py-20 px-4 max-w-5xl mx-auto">
        <h2 className="text-3xl font-bold text-center mb-12">{t(lang, 'homeFeaturesTitle')}</h2>
        <div className="grid md:grid-cols-3 gap-6">
          {FEATURES.map((f) => (
            <div key={f.title} className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
              <div className="text-3xl mb-4">{f.icon}</div>
              <h3 className="font-semibold text-lg mb-2">{f.title}</h3>
              <p className="text-gray-400 text-sm leading-relaxed">{f.desc}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="py-20 px-4 max-w-md mx-auto text-center">
        <h2 className="text-3xl font-bold mb-4">{t(lang, 'homePricingTitle')}</h2>
        <p className="text-gray-400 mb-10">{t(lang, 'homePricingSubtitle')}</p>
        <div className="bg-gray-900 border border-blue-600/40 rounded-2xl p-8">
          <div className="text-5xl font-bold mb-2">₪20<span className="text-xl text-gray-400 font-normal">{t(lang, 'homePricingPerMonth')}</span></div>
          <p className="text-gray-400 mb-8">{t(lang, 'homePricingPerBarber')}</p>
          <ul className="text-start space-y-3 mb-8">
            {pricingItems.map((item) => (
              <li key={item} className="flex items-center gap-3 text-sm">
                <span className="text-green-400">✓</span>
                <span className="text-gray-300">{item}</span>
              </li>
            ))}
          </ul>
          <Link to="/admin/register" className="block w-full bg-blue-600 hover:bg-blue-700 text-white font-bold py-3.5 rounded-xl transition-colors">
            {t(lang, 'homePricingCta')}
          </Link>
        </div>
      </section>

      <footer className="border-t border-gray-900 py-8 text-center text-gray-600 text-sm">
        {t(lang, 'homeFooter')}
      </footer>
    </div>
  )
}
