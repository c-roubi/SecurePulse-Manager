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

interface LiveAgent {
  agent_id: string
  hostname?: string
  os?: string
  user?: string
  encryption?: string
  last_envelope_version?: number
  last_message_index?: number
}

const Dashboard = () => {
  const [stats, setStats] = useState<Stats | null>(null)
  const [liveAgents, setLiveAgents] = useState<LiveAgent[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const loadData = async () => {
      try {
        const [dashData, liveData] = await Promise.all([
          apiClient.getDashboardStats().catch(() => null),
          fetch('http://localhost:8000/api/live/agents').then(r => r.json()).catch(() => ({ agents: [] })),
        ])
        if (dashData) setStats(dashData.stats)
        setLiveAgents(liveData.agents || [])
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load dashboard')
      } finally {
        setLoading(false)
      }
    }

    loadData()
    const interval = setInterval(loadData, 3000)
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

  const liveCount = liveAgents.length

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

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        <StatCard
          title="Total Agents"
          value={Math.max(stats?.total_agents || 0, liveCount)}
          icon="🖥️"
          color="text-blue-400"
        />
        <StatCard
          title="Active Agents"
          value={liveCount}
          icon="✅"
          color={liveCount > 0 ? "text-green-400" : "text-red-400"}
        />
        <StatCard
          title="Active Sessions"
          value={Math.max(stats?.active_sessions || 0, liveCount)}
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

      {/* Live Agents */}
      <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
        <h2 className="text-xl font-bold text-white mb-4">🟢 Live Connected Agents ({liveCount})</h2>
        {liveCount === 0 ? (
          <p className="text-gray-400">No agents currently connected. Run agent.exe to connect.</p>
        ) : (
          <div className="space-y-3">
            {liveAgents.map((agent) => (
              <div key={agent.agent_id} className="flex items-center justify-between p-4 bg-gray-700 rounded-lg">
                <div className="flex items-center gap-3">
                  <div className="w-3 h-3 bg-green-500 rounded-full animate-pulse"></div>
                  <div>
                    <p className="text-white font-mono text-sm">{agent.agent_id}</p>
                    <p className="text-gray-400 text-xs">
                      {agent.hostname || 'Unknown'} | {agent.os || 'Unknown OS'} | {agent.user || 'Unknown user'}
                    </p>
                  </div>
                </div>
                <div className="text-right">
                  <span className="px-2 py-1 bg-green-900 text-green-200 rounded text-xs font-medium">
                    {agent.encryption || 'connected'}
                  </span>
                  {agent.last_message_index !== undefined && (
                    <p className="text-gray-400 text-xs mt-1">msgs: {agent.last_message_index}</p>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* System Status */}
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
            <span className="text-gray-300">WebSocket</span>
            <span className="flex items-center gap-2">
              <div className={`w-2 h-2 rounded-full ${liveCount > 0 ? 'bg-green-500' : 'bg-yellow-500'}`}></div>
              <span className={liveCount > 0 ? 'text-green-400' : 'text-yellow-400'}>
                {liveCount > 0 ? `${liveCount} agent(s) connected` : 'Waiting for agents'}
              </span>
            </span>
          </div>
        </div>
      </div>
    </div>
  )
}

export default Dashboard
