import type { ReactNode } from 'react'
import { useAuth } from '../../lib/auth'
import LanguagePicker from '../LanguagePicker'
import ThemeToggle from '../ThemeToggle'

// Frame for the signed-out owner pages (login, register, forgot/set password, account request):
// centered column, theme toggle + language picker in the corner, rtl for AR/HE. The language is
// the owner one (localStorage 'lang'); signing in replaces it with the business's own setting.
export default function OwnerAuthShell({ children, wide }: { children: ReactNode; wide?: boolean }) {
  const { language, setLang } = useAuth()
  const rtl = language === 'AR' || language === 'HE'
  return (
    <div className="relative min-h-screen bg-cream flex items-center justify-center p-4" dir={rtl ? 'rtl' : 'ltr'}>
      <div className="absolute top-4 end-4 flex items-center gap-2">
        <LanguagePicker value={language} onChange={setLang} />
        <ThemeToggle />
      </div>
      <div className={wide ? 'w-full max-w-md py-16' : 'w-full max-w-sm py-16'}>{children}</div>
    </div>
  )
}
