import { describe, expect, it } from 'vitest'
import { validateLicenceConfirmation } from './LicenceWorkflowDialog'

const image = new File(['image'], 'licence.png', { type: 'image/png' })

describe('customer licence confirmation workflow', () => {
  it('requires a document, number and one of classes 1 through 9', () => {
    expect(validateLicenceConfirmation(null, '', [])).toContain('Choose')
    expect(validateLicenceConfirmation(image, '', [1])).toContain('number')
    expect(validateLicenceConfirmation(image, 'DL-10', [])).toContain('class')
    expect(validateLicenceConfirmation(image, 'DL-10', [1, 9])).toBe('')
  })
})

describe('administrator manual licence workflow', () => {
  it('accepts confirmed details only when the required image is present', () => {
    expect(validateLicenceConfirmation(null, 'DL-20', [2])).not.toBe('')
    expect(validateLicenceConfirmation(image, 'DL-20', [2])).toBe('')
  })
})
