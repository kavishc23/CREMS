import { useEffect, useState } from 'react'
import QRCode from 'qrcode'
import PrintOutlined from '@mui/icons-material/PrintOutlined'
import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, Typography } from '@mui/material'
import { api } from '../api/client'

type Asset = { id: string; assetNumber: string; name: string; branchName: string; registrationNumber: string | null; serialNumber: string | null }
export function AssetQrLabelDialog({ asset, open, onClose }: { asset: Asset | null; open: boolean; onClose: () => void }) {
  const [image, setImage] = useState(''); const [error, setError] = useState('')
  useEffect(() => { if (!open || !asset) return; void (async () => { try { const result = await api.get<{ staffUrl: string }>(`/corporate-operations/assets/${asset.id}/qr`); const url = new URL(result.data.staffUrl, window.location.origin).toString(); setImage(await QRCode.toDataURL(url, { errorCorrectionLevel: 'H', width: 520, margin: 2, color: { dark: '#000000', light: '#ffffff' } })); setError('') } catch { setError('The QR label could not be generated.') } })() }, [asset, open])
  function print() { window.print() }
  return <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth><DialogTitle>Asset QR label</DialogTitle><DialogContent>{error && <Alert severity="error">{error}</Alert>}{asset && <Box id="crems-qr-label" sx={{ p: 3, border: '2px solid #111', borderRadius: 2, textAlign: 'center', bgcolor: '#fff', color: '#111' }}><Typography fontWeight={900} variant="h5">CARPENTERS RENTALS</Typography><Typography variant="body2">Scan for check-out or check-in</Typography>{image && <Box component="img" src={image} alt={`QR code for ${asset.assetNumber}`} sx={{ width: '100%', maxWidth: 300, my: 1 }} />}<Typography variant="h5" fontWeight={900}>{asset.assetNumber}</Typography><Typography fontWeight={700}>{asset.name}</Typography><Typography variant="body2">{asset.registrationNumber || asset.serialNumber || asset.branchName}</Typography><Typography variant="caption">If scanning fails, enter the asset number in CREMS.</Typography></Box>}</DialogContent><DialogActions><Button onClick={onClose}>Close</Button><Button variant="contained" startIcon={<PrintOutlined />} onClick={print} disabled={!image}>Print label</Button></DialogActions></Dialog>
}
