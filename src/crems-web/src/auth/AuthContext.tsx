import { createContext, type ReactNode, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'

export type AuthenticatedUser = {
  id: string
  email: string
  fullName: string
  branchId: string | null
  roles: string[]
}

type AuthContextValue = {
  user: AuthenticatedUser | null
  checkingSession: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
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
    await api.post('/auth/login?useCookies=true', { email, password })
    await loadSession()
  }, [loadSession])

  const logout = useCallback(async () => {
    await api.post('/auth/logout')
    setUser(null)
  }, [])

  const value = useMemo(
    () => ({ user, checkingSession, login, logout }),
    [user, checkingSession, login, logout],
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
