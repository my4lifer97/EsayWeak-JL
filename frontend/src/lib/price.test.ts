import { describe, expect, it } from 'vitest'
import { formatPrice } from './price'
import { toAsciiDigits } from './digits'

describe('formatPrice', () => {
  it('keeps decimals only when there are some', () => {
    expect(formatPrice(80)).toBe('₪80')
    expect(formatPrice('12.5')).toBe('₪12.50')
    expect(formatPrice(12.49)).toBe('₪12.49')
  })
})

describe('toAsciiDigits', () => {
  it('reads Arabic-Indic, Persian and fullwidth digits as 0-9', () => {
    expect(toAsciiDigits('١٢٣')).toBe('123')
    expect(toAsciiDigits('۴۵')).toBe('45')
    expect(toAsciiDigits('７a')).toBe('7a')
  })
})
