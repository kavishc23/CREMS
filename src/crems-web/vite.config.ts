import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    // Shared React/MUI runtime is about 517 kB uncompressed (about 167 kB over
    // the wire). Feature pages and document tooling are split into lazy chunks.
    chunkSizeWarningLimit: 600,
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
