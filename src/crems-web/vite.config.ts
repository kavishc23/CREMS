import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const apiTarget = process.env.CREMS_API_URL ?? 'http://localhost:5080'
const frontendUrl = new URL(process.env.CREMS_FRONTEND_URL ?? 'http://localhost:5173')

export default defineConfig({
  plugins: [react()],
  build: {
    // Shared React/MUI runtime is about 517 kB uncompressed (about 167 kB over
    // the wire). Feature pages and document tooling are split into lazy chunks.
    chunkSizeWarningLimit: 600,
  },
  server: {
    host: frontendUrl.hostname,
    port: Number(frontendUrl.port || 5173),
    strictPort: true,
    proxy: {
      '/api': apiTarget,
    },
  },
  preview: {
    host: '127.0.0.1',
    port: 4173,
    strictPort: true,
    allowedHosts: true,
    proxy: {
      '/api': apiTarget,
    },
  },
})
