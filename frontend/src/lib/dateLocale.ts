import { ar, enUS, he } from 'date-fns/locale'

// date-fns locale for an app language, so month names follow the page ("Oct 21" stayed English
// on the HE/AR dashboard, QA run 3).
export function dateLocale(lang: string) {
  return lang === 'AR' ? ar : lang === 'HE' ? he : enUS
}
