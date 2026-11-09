import axios, { AxiosInstance, AxiosError } from 'axios'

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:8000/api'

class ApiClient {
  private client: AxiosInstance

  constructor() {
    this.client = axios.create({
      baseURL: API_BASE_URL,
      headers: {
        'Content-Type': 'application/json',
      },
    })

    // Request interceptor
    this.client.interceptors.request.use((config) => {
      const token = localStorage.getItem('access_token')
      if (token) {
        config.headers.Authorization = `Bearer ${token}`
      }
      return config
    })

    // Response interceptor
    this.client.interceptors.response.use(
      (response) => response,
      (error) => {
        if (error.response?.status === 401 && error.config?.url?.includes('/auth/')) {
          localStorage.removeItem('access_token')
          window.location.href = '/login'
        }
        return Promise.reject(error)
      }
    )
  }

  // Auth endpoints
  async login(username: string, password: string) {
    const response = await this.client.post('/auth/login', {
      username,
      password,
    })
    return response.data
  }

  async register(username: string, email: string, password: string) {
    const response = await this.client.post('/auth/register', {
      username,
      email,
      password,
    })
    return response.data
  }

  async refreshToken(token: string) {
    const response = await this.client.post('/auth/refresh', { old_token: token })
    return response.data
  }

  // Sessions endpoints
  async getSessions(skip: number = 0, limit: number = 100) {
    const response = await this.client.get('/sessions', {
      params: { skip, limit },
    })
    return response.data
  }

  async getSession(sessionId: number) {
    const response = await this.client.get(`/sessions/${sessionId}`)
    return response.data
  }

  async createSession(agentId: number) {
    const response = await this.client.post('/sessions', {
      agent_id: agentId,
    })
    return response.data
  }

  async updateSession(sessionId: number, updates: any) {
    const response = await this.client.patch(`/sessions/${sessionId}`, updates)
    return response.data
  }

  async closeSession(sessionId: number) {
    await this.client.delete(`/sessions/${sessionId}`)
  }

  // Agents endpoints
  async getAgents() {
    const response = await this.client.get('/agents')
    return response.data
  }

  async getAgent(agentId: number) {
    const response = await this.client.get(`/agents/${agentId}`)
    return response.data
  }

  // Commands endpoints
  async executeCommand(sessionId: number, command: string) {
    const response = await this.client.post(`/sessions/${sessionId}/commands`, {
      command_text: command,
    })
    return response.data
  }

  async getCommands(sessionId: number) {
    const response = await this.client.get(`/sessions/${sessionId}/commands`)
    return response.data
  }

  // Monitoring endpoints
  async getDashboardStats() {
    const response = await this.client.get('/monitoring/dashboard')
    return response.data
  }

  async getEvents(limit: number = 50) {
    const response = await this.client.get('/monitoring/events', {
      params: { limit },
    })
    return response.data
  }

  private handleError(error: unknown): Error {
    if (axios.isAxiosError(error)) {
      return new Error(
        error.response?.data?.detail ||
          error.message ||
          'An error occurred'
      )
    }
    return error as Error
  }
}

export const apiClient = new ApiClient()
