import { useCustomerAuth } from '../../lib/customerAuth'
import LanguagePicker from '../LanguagePicker'

export default function LanguageSwitcher() {
  const { language, setLang } = useCustomerAuth()
  return <LanguagePicker value={language} onChange={setLang} />
}
