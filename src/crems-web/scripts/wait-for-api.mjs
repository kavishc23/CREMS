import { setTimeout as delay } from 'node:timers/promises'

const target = process.env.CREMS_API_URL ?? 'http://localhost:5080'
const health = new URL('/api/health', target)
const deadline = Date.now() + 30_000

async function ready() {
  try {
    const response = await fetch(health, { signal: AbortSignal.timeout(1500) })
    return response.ok && (await response.json()).status === 'healthy'
  } catch {
    return false
  }
}

if (!(await ready())) {
  console.log(`Waiting for the CREMS API at ${target}...`)
  while (Date.now() < deadline) {
    await delay(500)
    if (await ready()) process.exit(0)
  }
  console.error(`The CREMS API is unavailable at ${target}. Start it and wait for /api/health to respond before running npm run dev again.`)
  process.exitCode = 1
}
