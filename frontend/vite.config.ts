/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5280',
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    // e2e/ holds Playwright specs (also *.spec.ts) — vitest's default glob would otherwise
    // pick them up too and collide with Playwright's own `test` global.
    exclude: ['**/node_modules/**', '**/e2e/**'],
  },
})
