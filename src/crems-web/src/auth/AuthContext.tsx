import { createContext, type ReactNode, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'

export type AuthenticatedUser = {
  id: string
  email: string
  fullName: string
  branchId: string | null
  divisionId: string | null
  branchName: string | null
  divisionName: string | null
  roles: string[]
  mustChangePassword: boolean
}

type AuthContextValue = {
  user: AuthenticatedUser | null
  checkingSession: boolean
  login: (email: string, password: string) => Promise<{ requiresMfa: boolean; challengeId?: string; maskedDestination?: string }>
  verifyMfa: (challengeId: string, code: string) => Promise<void>
  logout: () => Promise<void>
  refreshSession: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthenticatedUser | null>(null)
  const [checkingSession, setCheckingSession] = useState(true)

  const loadSession = useCallback(async () => {
    try {
      const response = await api.get<AuthenticatedUser>('/auth/session')
      setUser(response.data)
    } catch {
      setUser(null)
    } finally {
      setCheckingSession(false)
    }
  }, [])

  useEffect(() => {
    // Session loading is the external synchronization performed on application startup.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadSession()
  }, [loadSession])

  const login = useCallback(async (email: string, password: string) => {
    const response = await api.post<{ requiresMfa?: boolean; challengeId?: string; maskedDestination?: string }>('/auth/login?useCookies=true', { email, password })
    if (response.status === 202) return { requiresMfa: true, challengeId: response.data.challengeId, maskedDestination: response.data.maskedDestination }
    await loadSession(); return { requiresMfa: false }
  }, [loadSession])
  const verifyMfa = useCallback(async (challengeId: string, code: string) => { await api.post('/auth/mfa/verify?useCookies=true', { challengeId, code }); await loadSession() }, [loadSession])

  const logout = useCallback(async () => {
    await api.post('/auth/logout')
    setUser(null)
  }, [])

  const value = useMemo(
    () => ({ user, checkingSession, login, verifyMfa, logout, refreshSession: loadSession }),
    [user, checkingSession, login, verifyMfa, logout, loadSession],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// The provider and its hook intentionally share this small authentication module.
// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}
