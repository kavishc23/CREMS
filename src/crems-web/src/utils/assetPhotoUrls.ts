/** Customer-visible photos can be staff uploads or bundled catalogue images. */
export function assetPhotoUrls(asset: { photoUrlsJson?: string | null }, thumbnails = false): string[] {
  try {
    const urls: unknown = JSON.parse(asset.photoUrlsJson || '[]')
    if (!Array.isArray(urls)) return []
    return urls.filter((url): url is string => typeof url === 'string' && (
      url.startsWith('/api/public/assets/') || /^\/catalog\/[a-z0-9_-]+\.(?:jpe?g|png|webp|svg)$/i.test(url)
    )).map(url => {
      // Only uploaded images have generated .thumb.webp companions.
      const path = thumbnails && url.startsWith('/api/public/assets/') && url.endsWith('.webp')
        ? url.replace(/\.webp$/, '.thumb.webp') : url
      return `${path}${path.includes('?') ? '&' : '?'}v=3`
    })
  } catch { return [] }
}
