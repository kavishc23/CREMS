/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_CREMS_DEMO_STAGE?: 'demo' | 'full'
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
