import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    coverage: {
      enabled: true,
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/**/*.d.ts'],
      reporter: ['html', 'json-summary', 'cobertura', 'text-summary'],
      reportsDirectory: '../../output/coverage/frontend',
    },
  },
})
