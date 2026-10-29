/** Customer workflows display feedback at the step that actually completed. */
export function usesContextualFeedback(url: string) {
  const path = url.replace(/^\/api(?=\/)/, '').split('?')[0]
  return /^\/(customer-account|public|auth)(\/|$)/.test(path) || /^\/rentals\/[^/]+\/(return|pre-hire-inspection)$/.test(path) || /^\/rental-agreements\/[^/]+\/pickup$/.test(path) || /\/licence\/(scan|manual|[^/]+\/confirm)$/.test(path)
}
