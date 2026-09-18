import type { Config } from 'tailwindcss'

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        cream: '#FFF9F2',
        ink: '#2B2620',
        muted: '#6B6053',
        line: '#F0E1D0',
        coral: { DEFAULT: '#E8623F', dark: '#CB4E30', soft: '#FFDCC9', tint: '#FFEFE3' },
        teal: { DEFAULT: '#2E8F82', tint: '#E2F4F1' },
      },
      fontFamily: {
        sans: ['Nunito', 'ui-sans-serif', 'system-ui', 'sans-serif'],
        display: ['"Baloo 2"', 'ui-sans-serif', 'system-ui', 'sans-serif'],
      },
    },
  },
  plugins: [],
} satisfies Config
