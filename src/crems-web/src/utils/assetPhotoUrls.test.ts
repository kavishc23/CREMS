import { describe, expect, it } from 'vitest'
import { assetPhotoUrls } from './assetPhotoUrls'

describe('customer catalogue photos', () => {
  it('keeps bundled vehicle and projector photos for both cards and details', () => {
    const asset = { photoUrlsJson: JSON.stringify(['/catalog/sedan.jpg', '/catalog/hatchback-v2.png', '/catalog/projector-eb-x49.jpg']) }
    const photos = ['/catalog/sedan.jpg?v=3', '/catalog/hatchback-v2.png?v=3', '/catalog/projector-eb-x49.jpg?v=3']
    expect(assetPhotoUrls(asset)).toEqual(photos)
    expect(assetPhotoUrls(asset, true)).toEqual(photos)
  })
  it('uses thumbnails only for uploaded webp photos', () => {
    const upload = '/api/public/assets/123/photos/front.webp'
    const asset = { photoUrlsJson: JSON.stringify([upload, '/catalog/photo.webp']) }
    expect(assetPhotoUrls(asset, true)).toEqual([upload.replace('.webp', '.thumb.webp') + '?v=3', '/catalog/photo.webp?v=3'])
    expect(assetPhotoUrls(asset)[0]).toBe(upload + '?v=3')
  })
  it('ignores malformed records and paths outside the allowed photo locations', () => {
    for (const photoUrlsJson of [null, '', '{}', 'broken']) expect(assetPhotoUrls({ photoUrlsJson })).toEqual([])
    expect(assetPhotoUrls({ photoUrlsJson: JSON.stringify([null, 3, 'https://unrelated.example/a.jpg', '//example/a.jpg', '/catalog/../private.png', '/catalog/not-a-photo.html']) })).toEqual([])
  })
})
