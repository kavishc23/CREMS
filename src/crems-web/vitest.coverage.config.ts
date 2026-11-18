import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    maxWorkers: 1,
    testTimeout: 30000,
    coverage: {
      enabled: true,
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/**/*.d.ts'],
      reporter: ['html', 'json-summary', 'json', 'cobertura', 'text-summary'],
      reportsDirectory: '../../output/coverage/frontend',
    },
  },
})
