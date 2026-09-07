import { describe, it, expect } from 'vitest'
import { t, itemName } from './i18n'

describe('t', () => {
  it('returns the English string for EN', () => {
    expect(t('EN', 'dashboard')).toBe('Dashboard')
  })

  it('returns the Arabic string for AR', () => {
    expect(t('AR', 'dashboard')).toBe('لوحة التحكم')
  })

  it('returns the Hebrew string for HE', () => {
    expect(t('HE', 'dashboard')).toBe('לוח מחוונים')
  })

  it('falls back to EN for an unknown locale', () => {
    expect(t('FR', 'dashboard')).toBe(t('EN', 'dashboard'))
  })
})

describe('itemName', () => {
  const item = { nameEn: 'Haircut', nameAr: 'قصة شعر', nameHe: 'תספורת' }

  it('returns nameAr for AR', () => {
    expect(itemName(item, 'AR')).toBe('قصة شعر')
  })

  it('returns nameHe for HE', () => {
    expect(itemName(item, 'HE')).toBe('תספורת')
  })

  it('returns nameEn for EN and any other language', () => {
    expect(itemName(item, 'EN')).toBe('Haircut')
    expect(itemName(item, 'FR')).toBe('Haircut')
  })
})
