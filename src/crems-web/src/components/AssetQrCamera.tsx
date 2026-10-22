import { useState } from 'react'
import { Alert, Stack } from '@mui/material'
import { api } from '../api/client'
import { QrCameraScanner } from './QrCameraScanner'

export function AssetQrCamera({onScan}:{onScan:(assetNumber:string)=>void}) {
  const [error,setError]=useState('')
  async function resolve(code:string) {
    setError('')
    try {
      const response=await api.post<{asset:{assetNumber:string}}>('/asset-qr/resolve',{code})
      onScan(response.data.asset.assetNumber)
    } catch {
      setError('This QR code could not be matched to an accessible CREMS asset. Try again or enter the asset number.')
    }
  }
  return <Stack spacing={1} sx={{width:'100%'}}>
    <QrCameraScanner onScan={resolve}/>
    {error&&<Alert severity="error">{error}</Alert>}
  </Stack>
}
