import { describe, it, expect } from 'vitest'
import { t, serviceName } from './i18n'

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

describe('serviceName', () => {
  const service = { nameEn: 'Haircut', nameAr: 'قصة شعر', nameHe: 'תספורת' }

  it('returns nameAr for AR', () => {
    expect(serviceName(service, 'AR')).toBe('قصة شعر')
  })

  it('returns nameHe for HE', () => {
    expect(serviceName(service, 'HE')).toBe('תספורת')
  })

  it('returns nameEn for EN and any other language', () => {
    expect(serviceName(service, 'EN')).toBe('Haircut')
    expect(serviceName(service, 'FR')).toBe('Haircut')
  })
})
