import { createReadStream, statSync } from 'node:fs'
import { createServer, request as proxyRequest } from 'node:http'
import { extname, join, normalize } from 'node:path'
import { createBrotliCompress, createGzip } from 'node:zlib'

const host = process.env.HOST || '127.0.0.1'
const port = Number(process.env.PORT || 4173)
const apiOrigin = new URL(process.env.API_ORIGIN || 'http://127.0.0.1:5080')
const distRoot = join(import.meta.dirname, '..', 'dist')

const contentTypes = new Map([
  ['.css', 'text/css; charset=utf-8'],
  ['.html', 'text/html; charset=utf-8'],
  ['.ico', 'image/x-icon'],
  ['.jpeg', 'image/jpeg'],
  ['.jpg', 'image/jpeg'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.png', 'image/png'],
  ['.svg', 'image/svg+xml'],
  ['.webp', 'image/webp'],
  ['.woff', 'font/woff'],
  ['.woff2', 'font/woff2'],
])

function proxyApi(req, res) {
  const upstream = proxyRequest({
    protocol: apiOrigin.protocol,
    hostname: apiOrigin.hostname,
    port: apiOrigin.port,
    method: req.method,
    path: req.url,
    headers: { ...req.headers, host: apiOrigin.host },
  }, upstreamResponse => {
    res.writeHead(upstreamResponse.statusCode ?? 502, upstreamResponse.headers)
    upstreamResponse.pipe(res)
  })
  upstream.on('error', () => {
    if (!res.headersSent) res.writeHead(502, { 'content-type': 'application/json' })
    res.end(JSON.stringify({ message: 'The CREMS API is unavailable.' }))
  })
  req.pipe(upstream)
}

function serveFile(req, res) {
  const requestedPath = decodeURIComponent(new URL(req.url, 'http://localhost').pathname)
  const relativePath = normalize(requestedPath).replace(/^(\.\.(\/|\\|$))+/, '').replace(/^[/\\]+/, '')
  let filePath = join(distRoot, relativePath)
  try {
    if (!relativePath || statSync(filePath).isDirectory()) filePath = join(distRoot, 'index.html')
    else statSync(filePath)
  } catch {
    filePath = join(distRoot, 'index.html')
  }

  const extension = extname(filePath).toLowerCase()
  const isFingerprintedAsset = requestedPath.startsWith('/assets/')
  const headers = {
    'content-type': contentTypes.get(extension) || 'application/octet-stream',
    'cache-control': isFingerprintedAsset
      ? 'public, max-age=31536000, immutable'
      : 'no-cache',
    'x-content-type-options': 'nosniff',
  }
  const accepts = req.headers['accept-encoding'] || ''
  const compressible = ['.css', '.html', '.js', '.json', '.svg'].includes(extension)
  let compressor = null
  if (compressible && accepts.includes('br')) {
    headers['content-encoding'] = 'br'
    compressor = createBrotliCompress()
  } else if (compressible && accepts.includes('gzip')) {
    headers['content-encoding'] = 'gzip'
    compressor = createGzip()
  }
  if (compressor) headers.vary = 'Accept-Encoding'
  res.writeHead(200, headers)
  if (req.method === 'HEAD') return res.end()
  const stream = createReadStream(filePath)
  stream.on('error', () => res.destroy())
  if (compressor) stream.pipe(compressor).pipe(res)
  else stream.pipe(res)
}

createServer((req, res) => {
  if (req.url?.startsWith('/api/')) proxyApi(req, res)
  else serveFile(req, res)
}).listen(port, host, () => {
  console.log(`CREMS production server listening on http://${host}:${port}`)
})
