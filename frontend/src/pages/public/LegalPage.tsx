import { useEffect } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t } from '../../lib/i18n'
import { legalText, LEGAL_UPDATED, type LegalDoc } from '../../lib/legalContent'
import LanguagePicker from '../../components/LanguagePicker'
import ThemeToggle from '../../components/ThemeToggle'
import BackButton from '../../components/BackButton'
import LegalLinks from '../../components/LegalLinks'

// /terms, /privacy, /accessibility. Public; uses the customer language like the home page.
export default function LegalPage({ doc }: { doc: LegalDoc }) {
  const { language: customerLang, setLang } = useCustomerAuth()
  // ?lang= from the page that linked here (an owner form uses the owner language). Picking a
  // language on this page drops it and saves the choice like the customer switcher does.
  const [searchParams, setSearchParams] = useSearchParams()
  const asked = searchParams.get('lang')?.toUpperCase()
  const lang = asked === 'HE' || asked === 'AR' || asked === 'EN' ? asked : customerLang
  function pick(l: string) {
    setLang(l)
    if (searchParams.has('lang')) setSearchParams((p) => { p.delete('lang'); return p }, { replace: true })
  }
  const dir = lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'
  const text = legalText(lang, doc)

  useEffect(() => { document.title = `${text.title} · EsayWeek` }, [text.title])

  return (
    <div className="min-h-screen bg-cream text-ink" dir={dir}>
      <div className="max-w-3xl mx-auto px-4 py-6">
        <div className="flex items-center justify-between mb-8">
          <BackButton lang={lang} />
          <div className="flex items-center gap-2">
            <LanguagePicker value={lang} onChange={pick} />
            <ThemeToggle lang={lang} />
          </div>
        </div>
        <Link to="/" className="font-bold text-lg">📅 EsayWeek</Link>
        <h1 className="text-3xl font-bold mt-4 mb-2">{text.title}</h1>
        <p className="text-muted text-sm mb-6">{t(lang, 'legalUpdated')} <bdi dir="ltr">{LEGAL_UPDATED}</bdi></p>
        <p className="leading-relaxed mb-8">{text.intro}</p>
        {text.sections.map((s) => (
          <section key={s.h} className="mb-6">
            <h2 className="text-xl font-semibold mb-2">{s.h}</h2>
            {s.p.map((para, i) => <p key={i} className="text-muted leading-relaxed mb-2">{para}</p>)}
          </section>
        ))}
        <div className="border-t border-line mt-10 pt-6">
          <LegalLinks lang={lang} />
        </div>
      </div>
    </div>
  )
}
