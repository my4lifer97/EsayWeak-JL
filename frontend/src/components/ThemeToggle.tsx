import { useTheme } from '../lib/theme'

export default function ThemeToggle() {
  const { theme, toggleTheme } = useTheme()

  return (
    <button
      type="button"
      onClick={toggleTheme}
      aria-label="Toggle dark mode"
      className="w-8 h-8 flex items-center justify-center rounded-lg bg-surface border border-line text-ink hover:bg-cream transition-colors text-sm shrink-0"
    >
      {theme === 'dark' ? '☀️' : '🌙'}
    </button>
  )
}
