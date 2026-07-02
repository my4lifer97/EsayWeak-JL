import { defineConfig } from '@playwright/test'

// Requires the real backend (dotnet run, :5280) and this dev server (vite, :5173) both
// running already — E2E tests exercise the actual API, not mocks. See CLAUDE.md.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  retries: 0,
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:5173',
    screenshot: 'only-on-failure',
  },
})
