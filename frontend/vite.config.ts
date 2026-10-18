/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5280',
    },
    // Vite blocks unrecognized Host headers by default (DNS-rebinding protection). Allow
    // ngrok's tunnel domains for local testing against Twilio, which needs a public HTTPS URL.
    allowedHosts: ['.ngrok-free.dev', '.ngrok-free.app'],
  },
  build: {
    rollupOptions: {
      output: {
        // Libraries change far less often than our code, so in their own file they stay cached
        // across deploys. Owner/platform-admin pages are split off separately (App.tsx, lazyPage).
        manualChunks: {
          vendor: ['react', 'react-dom', 'react-router-dom', '@tanstack/react-query', 'axios', 'date-fns'],
        },
      },
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
