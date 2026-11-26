import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The console is served by the Cloud at /console (see Cloud/Program.cs). In `npm run dev` the API, the shared stylesheet
// (/cloud.css) and the owner pages come from a running Cloud: set CLOUD_URL, default the Cloud's own dev address.
const cloud = process.env.CLOUD_URL ?? 'http://localhost:51158'

export default defineConfig({
  base: '/console/',
  plugins: [react()],
  build: {
    outDir: '../Cloud/wwwroot/console',
    emptyOutDir: true,
    chunkSizeWarningLimit: 700,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': cloud,
      '/menu': cloud,
      '/owner': cloud,
      '/cloud.css': cloud,
      '/cloud-icon.svg': cloud,
    },
  },
})
