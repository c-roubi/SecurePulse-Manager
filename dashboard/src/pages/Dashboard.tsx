import { useState, useEffect } from 'react'

interface AgentInfo {
  agent_id: string
  hostname?: string
  os?: string
  os_version?: string
  user?: string
  encryption?: string
  ip_address?: string
  country?: string
  city?: string
  isp?: string
  last_envelope_version?: number
  last_message_index?: number
  pending_commands?: number
  total_beacons?: number
  connected_at?: string
  status?: string
}

const Dashboard = () => {
  const [agents, setAgents] = useState<AgentInfo[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    const loadData = () => {
      fetch('/api/live/agents')
        .then(function(r) { return r.json() })
        .then(function(data) {
          if (!active) return
          setTotalCount(data.total || 0)
          // data.agents is a dict {agent_id: info}, convert to array
          const agentsObj = data.agents || {}
          if (typeof agentsObj === 'object' && !Array.isArray(agentsObj)) {
            const arr: AgentInfo[] = Object.keys(agentsObj).map(function(id) {
              return { agent_id: id, ...agentsObj[id] }
            })
            setAgents(arr)
          } else if (Array.isArray(agentsObj)) {
            setAgents(agentsObj)
          } else {
            setAgents([])
          }
          setError('')
        })
        .catch(function(e) {
          if (!active) return
          setError(String(e))
        })
    }

    loadData()
    const interval = setInterval(loadData, 5000)
    return function() { active = false; clearInterval(interval) }
  }, [])

  const liveCount = agents.length

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-bold text-white">Dashboard</h1>

      {error && (
        <div className="p-3 bg-yellow-900 border border-yellow-700 rounded text-yellow-200 text-sm">
          API: {error}
        </div>
      )}

      {/* Stats */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <div className="bg-gray-800 rounded-lg p-6 border border-gray-700 hover:border-blue-500 transition-colors">
          <p className="text-gray-400 text-sm mb-2">Total Agents</p>
          <p className="text-3xl font-bold text-blue-400">{totalCount}</p>
        </div>
        <div className="bg-gray-800 rounded-lg p-6 border border-gray-700 hover:border-green-500 transition-colors">
          <p className="text-gray-400 text-sm mb-2">Active Agents</p>
          <p className={'text-3xl font-bold ' + (liveCount > 0 ? 'text-green-400' : 'text-red-400')}>{liveCount}</p>
        </div>
        <div className="bg-gray-800 rounded-lg p-6 border border-gray-700 hover:border-purple-500 transition-colors">
          <p className="text-gray-400 text-sm mb-2">Status</p>
          <p className={'text-3xl font-bold ' + (liveCount > 0 ? 'text-green-400' : 'text-yellow-400')}>
            {liveCount > 0 ? 'Online' : 'Waiting'}
          </p>
        </div>
      </div>

      {/* Live Agents */}
      <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
        <h2 className="text-xl font-bold text-white mb-4">Live Connected Agents ({liveCount})</h2>
        {liveCount === 0 ? (
          <p className="text-gray-400">No agents connected. Run <code className="bg-gray-700 px-2 py-1 rounded">agent.exe</code> to connect.</p>
        ) : (
          <div className="space-y-3">
            {agents.map(function(agent) {
              return (
                <div key={agent.agent_id} className="flex items-center justify-between p-4 bg-gray-700 rounded-lg">
                  <div className="flex items-center gap-3">
                    <div className="w-3 h-3 bg-green-500 rounded-full animate-pulse"></div>
                    <div>
                      <p className="text-white font-mono text-sm">{agent.agent_id}</p>
                      <p className="text-gray-400 text-xs">
                        {agent.user || 'N/A'}@{agent.hostname || '?'} | {agent.os || '?'} | {agent.ip_address || '?'}{agent.country ? ` (${agent.country})` : ''}
                      </p>
                    </div>
                  </div>
                  <div className="text-right">
                    <span className="px-2 py-1 bg-green-900 text-green-200 rounded text-xs font-medium">
                      {agent.encryption || 'connected'}
                    </span>
                    {agent.total_beacons !== undefined && (
                      <p className="text-gray-400 text-xs mt-1">beacons: {agent.total_beacons}</p>
                    )}
                  </div>
                </div>
              )
            })}
          </div>
        )}
      </div>

      {/* System Status */}
      <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
        <h2 className="text-xl font-bold text-white mb-4">System Status</h2>
        <div className="space-y-3">
          <div className="flex items-center justify-between p-3 bg-gray-700 rounded">
            <span className="text-gray-300">Server</span>
            <span className="flex items-center gap-2">
              <div className="w-2 h-2 bg-green-500 rounded-full"></div>
              <span className="text-green-400">Operational</span>
            </span>
          </div>
          <div className="flex items-center justify-between p-3 bg-gray-700 rounded">
            <span className="text-gray-300">WebSocket</span>
            <span className="flex items-center gap-2">
              <div className={'w-2 h-2 rounded-full ' + (liveCount > 0 ? 'bg-green-500' : 'bg-yellow-500')}></div>
              <span className={liveCount > 0 ? 'text-green-400' : 'text-yellow-400'}>
                {liveCount > 0 ? liveCount + ' agent(s) connected' : 'Waiting for agents'}
              </span>
            </span>
          </div>
        </div>
      </div>
    </div>
  )
}

export default Dashboard
