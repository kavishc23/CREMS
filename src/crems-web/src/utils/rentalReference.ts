/** Preserve legacy references for lookup; compact only their visible label. */
export function rentalReference(value: string) {
  return value.replace(/^((?:BK|QUO|QT|REQ|RA)-\d{4}-)([A-F0-9]{32})$/i, (_, prefix: string, id: string) => prefix + id.slice(0, 8) + '…')
}
