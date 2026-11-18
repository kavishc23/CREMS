import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    // Limit simultaneous jsdom/MUI suites to avoid CPU contention on laptops.
    maxWorkers: 1,
    testTimeout: 30000,
  },
})
