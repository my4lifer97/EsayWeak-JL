import { format } from 'date-fns'
import { ar, enUS, he } from 'date-fns/locale'

// date-fns locale for an app language, so month names follow the page ("Oct 21" stayed English
// on the HE/AR dashboard, QA run 3).
export function dateLocale(lang: string) {
  return lang === 'AR' ? ar : lang === 'HE' ? he : enUS
}

// "Wed, Oct 28, 2026" in English, day-before-month in Hebrew/Arabic ("יום ד׳, 28 באוק׳ 2026",
// "الأربعاء، 28 أكتوبر 2026"); the English pattern with a Latin comma read wrong there (QA run 4).
const DATE_PATTERNS = {
  EN: { short: 'EEE, MMM d', shortYear: 'EEE, MMM d, yyyy', long: 'EEEE, MMM d' },
  HE: { short: "EEE, d 'ב'MMM", shortYear: "EEE, d 'ב'MMM yyyy", long: "EEEE, d 'ב'MMM" },
  AR: { short: 'EEEE، d MMMM', shortYear: 'EEEE، d MMMM yyyy', long: 'EEEE، d MMMM' },
} as const

export function formatDay(date: Date, lang: string, style: 'short' | 'shortYear' | 'long') {
  const patterns = lang === 'AR' ? DATE_PATTERNS.AR : lang === 'HE' ? DATE_PATTERNS.HE : DATE_PATTERNS.EN
  return format(date, patterns[style], { locale: dateLocale(lang) })
}
