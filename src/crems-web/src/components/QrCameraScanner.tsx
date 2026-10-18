import { useEffect, useRef, useState } from 'react'
import jsQR from 'jsqr'
import CameraAltOutlined from '@mui/icons-material/CameraAltOutlined'
import StopCircleOutlined from '@mui/icons-material/StopCircleOutlined'
import { Alert, Box, Button, Stack } from '@mui/material'

export function QrCameraScanner({onScan}:{onScan:(value:string)=>void|Promise<void>}) {
  const [scanning, setScanning] = useState(false)
  const [error, setError] = useState('')
  const videoRef = useRef<HTMLVideoElement>(null); const streamRef = useRef<MediaStream | null>(null); const timerRef = useRef<number | null>(null)
  const cameraSession = useRef(0)

  function releaseCamera() { cameraSession.current++; if (timerRef.current) window.clearInterval(timerRef.current); timerRef.current = null; streamRef.current?.getTracks().forEach(track => track.stop()); streamRef.current = null; if (videoRef.current) videoRef.current.srcObject = null }
  function stopScanner() { releaseCamera(); setScanning(false) }
  useEffect(() => () => releaseCamera(), [])
  async function startScanner() {
    setError(''); const Detector = (window as unknown as { BarcodeDetector?: new (options: { formats: string[] }) => { detect(source: HTMLVideoElement): Promise<{ rawValue: string }[]> } }).BarcodeDetector
    if (!navigator.mediaDevices?.getUserMedia) { setError('Camera access requires HTTPS or localhost. Open CREMS using a secure address, or enter the asset number.'); return }
    releaseCamera(); const session = cameraSession.current; setScanning(true)
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false })
      if (session !== cameraSession.current) { stream.getTracks().forEach(track => track.stop()); return }
      streamRef.current = stream
      const video = videoRef.current
      if (!video) throw new Error('Preview unavailable')
      video.srcObject = stream; video.muted = true; await video.play()
      if (session !== cameraSession.current) return
      let detector: InstanceType<NonNullable<typeof Detector>> | null = null
      try { if (Detector) detector = new Detector({ formats: ['qr_code'] }) } catch { /* Use the software decoder. */ }
      const canvas = document.createElement('canvas')
      const drawing = canvas.getContext('2d', { willReadFrequently: true })
      let detecting = false
      timerRef.current = window.setInterval(async () => {
        if (detecting || session !== cameraSession.current || video.readyState < 2 || !video.videoWidth) return
        detecting = true
        try {
          let value = ''
          if (detector) {
            try { value = (await detector.detect(video))[0]?.rawValue ?? '' }
            catch { detector = null }
          }
          if (!detector && drawing) {
            const scale = Math.min(1, 960 / video.videoWidth)
            canvas.width = Math.round(video.videoWidth * scale)
            canvas.height = Math.round(video.videoHeight * scale)
            drawing.drawImage(video, 0, 0, canvas.width, canvas.height)
            const frame = drawing.getImageData(0, 0, canvas.width, canvas.height)
            value = jsQR(frame.data, frame.width, frame.height)?.data ?? ''
          }
          if (session !== cameraSession.current) return
          if (value) { stopScanner(); await onScan(value) }
        } catch { /* A frame without a readable QR code is normal. */ }
        finally { detecting = false }
      }, 600)
    } catch (reason) {
      if (session !== cameraSession.current) return
      stopScanner()
      const name = reason instanceof DOMException ? reason.name : ''
      setError(name === 'NotAllowedError' ? 'Camera permission was denied. Allow camera access in your browser and try again.' : name === 'NotReadableError' ? 'The camera is busy or unavailable. Close other apps using it and try again.' : name === 'NotFoundError' ? 'No camera was found. Connect a camera or enter the asset number.' : 'The camera preview could not start. Try opening the camera again or enter the asset number.')
    }
  }

  return <Stack spacing={1} sx={{width:'100%'}}>
    <Button sx={{alignSelf:'flex-start'}} variant="outlined" startIcon={scanning ? <StopCircleOutlined/> : <CameraAltOutlined/>} onClick={()=>scanning ? stopScanner() : void startScanner()}>{scanning ? 'Stop' : 'Use camera'}</Button>
    {error && <Alert severity="error">{error}</Alert>}
    <Box sx={{display:scanning?'block':'none',bgcolor:'#111',borderRadius:2,overflow:'hidden',height:{xs:280,sm:420}}}>
      <video ref={videoRef} autoPlay muted playsInline aria-label="Live camera preview for QR scanning" style={{display:'block',width:'100%',height:'100%',objectFit:'contain'}}/>
    </Box>
  </Stack>
}
