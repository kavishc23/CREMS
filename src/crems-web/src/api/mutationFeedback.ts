/** Workflows with local feedback handle their own mutation messages. */
export function usesContextualFeedback(url: string) {
  const path = url.replace(/^\/api(?=\/)/, '').split('?')[0]
  if (['/notifications/staff','/notifications/send'].includes(path)) return true
  if (/^\/maintenance-jobs(?:\/[^/]+(?:\/(?:release|documents))?)?$/.test(path)) return true
  return /^\/(customer-account|public|auth)(\/|$)/.test(path) || /^\/rentals\/[^/]+\/(return|pre-hire-inspection|incident-settlement)$/.test(path) || /^\/rental-agreements\/[^/]+\/pickup$/.test(path) || /\/licence\/(scan|manual|[^/]+\/confirm)$/.test(path)
}
