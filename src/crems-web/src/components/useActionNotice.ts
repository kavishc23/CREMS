import { useCallback } from 'react'
import { toast } from './ToastHost'

/** Completed actions use temporary feedback without adding height to the page. */
export function useActionNotice() {
  return useCallback((message: string) => {
    if (message.trim()) toast(message, 'success')
  }, [])
}
