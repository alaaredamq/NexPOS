/// <reference types="vitest/config" />
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The SPA is served by the .NET host from Web/wwwroot/app (decision D3/D7), so `base` is /app/
// and the build goes straight into the web project.
export default defineConfig({
  base: '/app/',
  plugins: [react()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  build: {
    outDir: fileURLToPath(new URL('../Web/wwwroot/app', import.meta.url)),
    emptyOutDir: true,
    sourcemap: false,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5000', changeOrigin: false },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    include: ['src/**/*.test.{ts,tsx}', 'scripts/**/*.test.mjs'],
    css: false,
    // Node 25+ ships its own global localStorage, which is undefined without --localstorage-file and hides jsdom's
    // working one, so every test touching storage crashed in beforeEach. Turn Node's off in the test workers.
    execArgv: ['--no-experimental-webstorage'],
    // Room for findBy*'s 5 s (see test-setup.ts) inside a test that also loads its page chunk.
    testTimeout: 15000,
  },
})
