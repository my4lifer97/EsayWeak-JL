// Read-only star row, or an interactive picker when `onChange` is passed. No icon library in this
// project, so the stars are the ★/☆ glyphs (consistent with the emoji-icon convention elsewhere).

type Props = {
  value: number
  count?: number
  onChange?: (value: number) => void
  size?: 'sm' | 'md' | 'lg'
}

const SIZE: Record<NonNullable<Props['size']>, string> = {
  sm: 'text-sm',
  md: 'text-lg',
  lg: 'text-2xl',
}

export default function StarRating({ value, count, onChange, size = 'md' }: Props) {
  const editable = typeof onChange === 'function'
  const rounded = Math.round(value)

  return (
    <span className={`inline-flex items-center gap-1 ${SIZE[size]}`}>
      <span className="inline-flex" aria-label={`${value} out of 5`}>
        {[1, 2, 3, 4, 5].map((n) => {
          const filled = n <= (editable ? value : rounded)
          const star = (
            <span className={filled ? 'text-yellow-400' : 'text-gray-600'}>{filled ? '★' : '☆'}</span>
          )
          return editable ? (
            <button
              key={n}
              type="button"
              onClick={() => onChange!(n)}
              className="px-0.5 leading-none hover:scale-110 transition-transform"
              aria-label={`${n} star${n > 1 ? 's' : ''}`}
            >
              {star}
            </button>
          ) : (
            <span key={n} className="px-px leading-none">{star}</span>
          )
        })}
      </span>
      {typeof count === 'number' && !editable && (
        <span className="text-gray-500 text-xs">({count})</span>
      )}
    </span>
  )
}
