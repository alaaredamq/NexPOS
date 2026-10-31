import { defineConfig } from '@playwright/test'

// E2E runs against the isolated demo DB (see the nexpos-ui-audit-runbook), never nexpos.db.
// BASE_URL defaults to the Vite dev server; point it at the .NET host to test the built SPA.
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:5173',
    viewport: { width: 1366, height: 768 },
    trace: 'retain-on-failure',
  },
})
