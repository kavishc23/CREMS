/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_CREMS_DEMO_STAGE?: 'week2' | 'full'
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
