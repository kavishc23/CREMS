import { useEffect, useState } from 'react'
import { Alert, Snackbar } from '@mui/material'

export function toast(message: string, severity: 'success' | 'error' | 'info' = 'info') {
  window.dispatchEvent(new CustomEvent('crems:toast', { detail: { message, severity } }))
}
export function ToastHost() {
  const [notice, setNotice] = useState<{message:string;severity:'success'|'error'|'info';id:number}|null>(null)
  useEffect(() => { const listener = (event: Event) => setNotice({ ...(event as CustomEvent).detail, id: Date.now() }); window.addEventListener('crems:toast', listener); return () => window.removeEventListener('crems:toast', listener) }, [])
  return <Snackbar key={notice?.id} open={Boolean(notice)} autoHideDuration={5500} onClose={(_, reason) => { if (reason !== 'clickaway') setNotice(null) }} anchorOrigin={{vertical:'top',horizontal:'right'}} sx={{top:{xs:72,sm:80},right:{xs:12,sm:24},left:'auto',maxWidth:'calc(100vw - 24px)',zIndex:theme=>theme.zIndex.modal+100}}><Alert severity={notice?.severity??'info'} onClose={()=>setNotice(null)} variant="filled" sx={{maxWidth:380,width:"100%",boxShadow:6,alignItems:"flex-start","& .MuiAlert-message":{maxHeight:160,overflowY:"auto",overflowWrap:"anywhere"}}}>{notice?.message}</Alert></Snackbar>
}
