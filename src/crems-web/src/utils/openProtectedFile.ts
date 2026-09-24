import { api } from '../api/client'

export async function openProtectedFile(endpoint: string) {
  const pendingTab = window.open('about:blank', '_blank')
  if (pendingTab) {
    pendingTab.document.title = 'Opening licence…'
    pendingTab.document.body.innerHTML = '<p style="font:16px system-ui;padding:24px">Opening licence…</p>'
  }
  try {
    const response = await api.get<Blob>(endpoint, {
      responseType: 'blob',
      timeout: 30_000,
      headers: { Accept: 'application/pdf,image/png,image/jpeg' },
    })
    const responseContentType = response.headers['content-type']
    const contentType = typeof responseContentType === 'string'
      ? responseContentType
      : response.data.type || 'application/octet-stream'
    const documentBlob = response.data.type === contentType
      ? response.data
      : new Blob([response.data], { type: contentType })
    const objectUrl = URL.createObjectURL(documentBlob)
    if (pendingTab) pendingTab.location.href = objectUrl
    else {
      const link = document.createElement('a')
      link.href = objectUrl
      link.target = '_blank'
      link.rel = 'noopener noreferrer'
      link.click()
    }
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 5 * 60_000)
  } catch {
    pendingTab?.close()
    window.dispatchEvent(new CustomEvent('crems:toast', { detail: { severity: 'error', message: 'Unable to open the licence file.' } }))
  }
}
