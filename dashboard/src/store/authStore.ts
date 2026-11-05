import { create } from 'zustand'
import { apiClient } from '../services/api'

interface User {
  id: number
  username: string
  email: string
  role: 'admin' | 'operator' | 'viewer'
}

interface AuthStore {
  user: User | null
  token: string | null
  isAuthenticated: boolean
  isLoading: boolean
  error: string | null

  login: (username: string, password: string) => Promise<void>
  logout: () => void
  restoreSession: () => void
  clearError: () => void
}

export const useAuthStore = create<AuthStore>((set) => ({
  user: null,
  token: localStorage.getItem('access_token'),
  isAuthenticated: !!localStorage.getItem('access_token'),
  isLoading: false,
  error: null,

  login: async (username: string, password: string) => {
    set({ isLoading: true, error: null })
    try {
      const response = await apiClient.login(username, password)

      localStorage.setItem('access_token', response.access_token)
      localStorage.setItem('token_type', response.token_type)
      localStorage.setItem('expires_in', response.expires_in)

      // Mock user data (in production, get from API)
      const mockUser: User = {
        id: 1,
        username,
        email: `${username}@example.com`,
        role: 'operator',
      }

      set({
        token: response.access_token,
        isAuthenticated: true,
        user: mockUser,
        isLoading: false,
      })
    } catch (error) {
      const errorMessage =
        error instanceof Error ? error.message : 'Login failed'
      set({
        error: errorMessage,
        isLoading: false,
      })
      throw error
    }
  },

  logout: () => {
    localStorage.removeItem('access_token')
    localStorage.removeItem('token_type')
    localStorage.removeItem('expires_in')
    set({
      user: null,
      token: null,
      isAuthenticated: false,
      error: null,
    })
  },

  restoreSession: () => {
    const token = localStorage.getItem('access_token')
    if (token) {
      set({ token, isAuthenticated: true })
    }
  },

  clearError: () => {
    set({ error: null })
  },
}))
