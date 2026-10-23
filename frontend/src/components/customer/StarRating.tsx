// Read-only star row, or an interactive picker when `onChange` is passed. No icon library in this
// project, so the stars are the ★/☆ glyphs (consistent with the emoji-icon convention elsewhere).

import { t } from '../../lib/i18n'

type Props = {
  value: number
  count?: number
  onChange?: (value: number) => void
  size?: 'sm' | 'md' | 'lg'
  lang?: string
}

const SIZE: Record<NonNullable<Props['size']>, string> = {
  sm: 'text-sm',
  md: 'text-lg',
  lg: 'text-2xl',
}

export default function StarRating({ value, count, onChange, size = 'md', lang = 'EN' }: Props) {
  const editable = typeof onChange === 'function'
  const rounded = Math.round(value)

  return (
    <span className={`inline-flex items-center gap-1 ${SIZE[size]}`}>
      <span className="inline-flex" aria-label={t(lang, 'starsOutOf5').replace('{n}', String(Math.round(value * 10) / 10))}>
        {[1, 2, 3, 4, 5].map((n) => {
          const filled = n <= (editable ? value : rounded)
          const star = (
            <span className={filled ? 'text-yellow-400' : 'text-line'}>{filled ? '★' : '☆'}</span>
          )
          return editable ? (
            <button
              key={n}
              type="button"
              onClick={() => onChange!(n)}
              className="px-0.5 leading-none hover:scale-110 transition-transform"
              aria-label={n === 1 ? t(lang, 'oneStar') : n === 2 ? t(lang, 'twoStars') : t(lang, 'starsN').replace('{n}', String(n))}
            >
              {star}
            </button>
          ) : (
            <span key={n} className="px-px leading-none">{star}</span>
          )
        })}
      </span>
      {typeof count === 'number' && !editable && (
        <span className="text-muted text-xs">({count})</span>
      )}
    </span>
  )
}
