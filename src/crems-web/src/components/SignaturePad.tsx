import { useEffect, useRef, type PointerEvent } from 'react'
import { Box, Button, Stack, Typography } from '@mui/material'

export function SignaturePad({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const drawing = useRef(false)
  const hasStroke = useRef(false)

  useEffect(() => {
    const canvas = canvasRef.current; if (!canvas) return
    const ratio = window.devicePixelRatio || 1; const width = canvas.clientWidth; const height = canvas.clientHeight
    canvas.width = width * ratio; canvas.height = height * ratio
    const context = canvas.getContext('2d'); if (!context) return
    context.scale(ratio, ratio); context.lineWidth = 2.2; context.lineCap = 'round'; context.strokeStyle = '#111'
    if (value) { const image = new Image(); image.onload = () => context.drawImage(image, 0, 0, width, height); image.src = value }
  }, [])

  const point = (event: PointerEvent<HTMLCanvasElement>) => { const rect = event.currentTarget.getBoundingClientRect(); return { x: event.clientX - rect.left, y: event.clientY - rect.top } }
  const start = (event: PointerEvent<HTMLCanvasElement>) => { drawing.current = true; hasStroke.current = false; event.currentTarget.setPointerCapture(event.pointerId); const context = event.currentTarget.getContext('2d'); const p = point(event); context?.beginPath(); context?.moveTo(p.x, p.y) }
  const move = (event: PointerEvent<HTMLCanvasElement>) => { if (!drawing.current) return; hasStroke.current = true; const p = point(event); const context = event.currentTarget.getContext('2d'); context?.lineTo(p.x, p.y); context?.stroke() }
  const finish = () => { if (!drawing.current) return; drawing.current = false; const canvas = canvasRef.current; if (canvas && hasStroke.current) onChange(canvas.toDataURL('image/png')) }
  const clear = () => { const canvas = canvasRef.current; if (!canvas) return; canvas.getContext('2d')?.clearRect(0, 0, canvas.width, canvas.height); onChange('') }

  return <Box><Stack direction="row" justifyContent="space-between" alignItems="center" mb={.75}><Typography variant="subtitle2">Customer drawn signature *</Typography><Button size="small" onClick={clear} disabled={!value}>Clear</Button></Stack><Box component="canvas" ref={canvasRef} onPointerDown={start} onPointerMove={move} onPointerUp={finish} onPointerCancel={finish} sx={{ width: '100%', height: 150, display: 'block', bgcolor: '#fff', border: '1px solid', borderColor: value ? 'text.primary' : 'divider', borderRadius: 1, touchAction: 'none', cursor: 'crosshair' }} /><Typography variant="caption" color="text.secondary">Ask the customer to sign inside the box using a touchscreen, mouse or trackpad.</Typography></Box>
}
