import { useState, useEffect } from 'react'
import { apiClient } from '../services/api'

interface Stats {
  total_agents: number
  active_agents: number
  total_sessions: number
  active_sessions: number
  total_commands: number
  completed_commands: number
}

const Dashboard = () => {
  const [stats, setStats] = useState<Stats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const loadDashboardData = async () => {
      try {
        const data = await apiClient.getDashboardStats()
        setStats(data.stats)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load dashboard')
      } finally {
        setLoading(false)
      }
    }

    loadDashboardData()
    const interval = setInterval(loadDashboardData, 5000) // Refresh every 5s

    return () => clearInterval(interval)
  }, [])

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-500 mx-auto mb-4"></div>
          <p className="text-gray-400">Loading dashboard...</p>
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="p-4 bg-red-900 border border-red-700 rounded text-red-100">
        {error}
      </div>
    )
  }

  const StatCard = ({ title, value, icon, color }: { title: string; value: number; icon: string; color: string }) => (
    <div className="bg-gray-800 rounded-lg p-6 border border-gray-700 hover:border-blue-500 transition-colors">
      <div className="flex items-center justify-between">
        <div>
          <p className="text-gray-400 text-sm mb-2">{title}</p>
          <p className={`text-3xl font-bold ${color}`}>{value}</p>
        </div>
        <span className="text-4xl">{icon}</span>
      </div>
    </div>
  )

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-bold text-white">Dashboard</h1>

      {/* Stats Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        <StatCard
          title="Total Agents"
          value={stats?.total_agents || 0}
          icon="🖥️"
          color="text-blue-400"
        />
        <StatCard
          title="Active Agents"
          value={stats?.active_agents || 0}
          icon="✅"
          color="text-green-400"
        />
        <StatCard
          title="Active Sessions"
          value={stats?.active_sessions || 0}
          icon="🔗"
          color="text-purple-400"
        />
        <StatCard
          title="Total Commands"
          value={stats?.total_commands || 0}
          icon="⌨️"
          color="text-yellow-400"
        />
        <StatCard
          title="Completed Commands"
          value={stats?.completed_commands || 0}
          icon="✔️"
          color="text-green-400"
        />
        <StatCard
          title="Success Rate"
          value={
            stats && stats.total_commands > 0
              ? Math.round((stats.completed_commands / stats.total_commands) * 100)
              : 0
          }
          icon="📊"
          color="text-cyan-400"
        />
      </div>

      {/* Recent Activity */}
      <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
        <h2 className="text-xl font-bold text-white mb-4">📡 System Status</h2>
        <div className="space-y-3">
          <div className="flex items-center justify-between p-3 bg-gray-700 rounded">
            <span className="text-gray-300">Server Status</span>
            <span className="flex items-center gap-2">
              <div className="w-2 h-2 bg-green-500 rounded-full"></div>
              <span className="text-green-400">Operational</span>
            </span>
          </div>
          <div className="flex items-center justify-between p-3 bg-gray-700 rounded">
            <span className="text-gray-300">Database Status</span>
            <span className="flex items-center gap-2">
              <div className="w-2 h-2 bg-green-500 rounded-full"></div>
              <span className="text-green-400">Connected</span>
            </span>
          </div>
          <div className="flex items-center justify-between p-3 bg-gray-700 rounded">
            <span className="text-gray-300">API Status</span>
            <span className="flex items-center gap-2">
              <div className="w-2 h-2 bg-green-500 rounded-full"></div>
              <span className="text-green-400">Responding</span>
            </span>
          </div>
        </div>
      </div>

      {/* Quick Actions */}
      <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
        <h2 className="text-xl font-bold text-white mb-4">⚡ Quick Actions</h2>
        <div className="flex gap-4 flex-wrap">
          <button className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded transition-colors">
            🔗 New Session
          </button>
          <button className="px-4 py-2 bg-purple-600 hover:bg-purple-700 text-white rounded transition-colors">
            ⌨️ Execute Command
          </button>
          <button className="px-4 py-2 bg-cyan-600 hover:bg-cyan-700 text-white rounded transition-colors">
            📊 View Reports
          </button>
          <button className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded transition-colors">
            ⚠️ Show Alerts
          </button>
        </div>
      </div>
    </div>
  )
}

export default Dashboard
