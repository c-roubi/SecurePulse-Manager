import { useState, useEffect } from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useAuthStore } from './store/authStore'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import Sessions from './pages/Sessions'
import Commands from './pages/Commands'
import Monitoring from './pages/Monitoring'
import Files from './pages/Files'
import RemoteAccess from './pages/RemoteAccess'
import Keylogger from './pages/Keylogger'
import Layout from './components/Layout'
import PrivateRoute from './components/PrivateRoute'

function App() {
  const { isAuthenticated, restoreSession } = useAuthStore()
  const [isInitialized, setIsInitialized] = useState(false)

  useEffect(() => {
    // Restore session from localStorage
    restoreSession()
    setIsInitialized(true)
  }, [])

  if (!isInitialized) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-gray-900">
        <div className="text-white">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-500"></div>
          <p className="mt-4">Loading...</p>
        </div>
      </div>
    )
  }

  return (
    <BrowserRouter>
      <Routes>
        {/* Public routes */}
        <Route path="/login" element={<Login />} />

        {/* Protected routes */}
        <Route element={<PrivateRoute />}>
          <Route element={<Layout />}>
            <Route path="/" element={<Dashboard />} />
            <Route path="/dashboard" element={<Dashboard />} />
            <Route path="/sessions" element={<Sessions />} />
            <Route path="/commands" element={<Commands />} />
            <Route path="/monitoring" element={<Monitoring />} />
            <Route path="/files" element={<Files />} />
            <Route path="/files/:agentId" element={<Files />} />
            <Route path="/remote-access" element={<RemoteAccess />} />
            <Route path="/keylogger" element={<Keylogger />} />
          </Route>
        </Route>

        {/* Redirect unknown routes */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}

export default App
