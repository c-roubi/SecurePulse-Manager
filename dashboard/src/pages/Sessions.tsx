import { useState, useEffect } from 'react'

interface LiveAgent {
  agent_id: string
  hostname?: string
  os?: string
  os_version?: string
  user?: string
  architecture?: string
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
}

const Sessions = () => {
  const [agents, setAgents] = useState<LiveAgent[]>([])
  const [loading, setLoading] = useState(true)
  const [selectedAgent, setSelectedAgent] = useState<string | null>(null)
  const [agentDetail, setAgentDetail] = useState<LiveAgent | null>(null)

  useEffect(() => {
    const loadAgents = async () => {
      try {
        const res = await fetch('/api/live/agents')
        const data = await res.json()
        const agentsObj = data.agents || {}
        if (typeof agentsObj === 'object' && !Array.isArray(agentsObj)) {
          setAgents(Object.keys(agentsObj).map(id => ({ agent_id: id, ...agentsObj[id] })))
        } else if (Array.isArray(agentsObj)) {
          setAgents(agentsObj)
        } else {
          setAgents([])
        }
      } catch (err) {
        console.error('Failed to load agents:', err)
      } finally {
        setLoading(false)
      }
    }

    loadAgents()
    const interval = setInterval(loadAgents, 3000)
    return () => clearInterval(interval)
  }, [])

  useEffect(() => {
    if (!selectedAgent) { setAgentDetail(null); return }
    const loadDetail = async () => {
      try {
        const res = await fetch(`/api/live/agents/${selectedAgent}`)
        if (res.ok) setAgentDetail(await res.json())
      } catch {}
    }
    loadDetail()
    const interval = setInterval(loadDetail, 3000)
    return () => clearInterval(interval)
  }, [selectedAgent])

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-bold text-white">Sessions & Agents</h1>

      <div className="bg-gray-800 rounded-lg border border-gray-700 overflow-hidden">
        <div className="p-4 bg-gray-750 border-b border-gray-700">
          <h2 className="text-lg font-semibold text-white">
            Live Connected Agents ({agents.length})
          </h2>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-700">
              <tr>
                <th className="px-4 py-3 text-left text-sm font-medium text-gray-300">Status</th>
                <th className="px-4 py-3 text-left text-sm font-medium text-gray-300">Agent ID</th>
                <th className="px-4 py-3 text-left text-sm font-medium text-gray-300">User</th>
                <th className="px-4 py-3 text-left text-sm font-medium text-gray-300">Hostname</th>
                <th className="px-4 py-3 text-left text-sm font-medium text-gray-300">IP Address</th>
                <th className="px-4 py-3 text-left text-sm font-medium text-gray-300">Country</th>
                <th className="px-4 py-3 text-left text-sm font-medium text-gray-300">OS</th>
                <th className="px-4 py-3 text-left text-sm font-medium text-gray-300">Beacons</th>
                <th className="px-4 py-3 text-left text-sm font-medium text-gray-300">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-700">
              {loading ? (
                <tr>
                  <td colSpan={9} className="px-4 py-4 text-center text-gray-400">Loading...</td>
                </tr>
              ) : agents.length === 0 ? (
                <tr>
                  <td colSpan={9} className="px-4 py-8 text-center text-gray-400">
                    <p className="text-lg mb-2">No agents connected</p>
                    <p className="text-sm">Run <code className="bg-gray-700 px-2 py-1 rounded">agent.exe</code> to connect an agent</p>
                  </td>
                </tr>
              ) : (
                agents.map((agent) => (
                  <tr key={agent.agent_id} className="hover:bg-gray-700 transition-colors">
                    <td className="px-4 py-4">
                      <div className="w-3 h-3 bg-green-500 rounded-full animate-pulse"></div>
                    </td>
                    <td className="px-4 py-4 text-sm text-white font-mono">{agent.agent_id.slice(0, 8)}...</td>
                    <td className="px-4 py-4 text-sm text-gray-300">{agent.user || '-'}</td>
                    <td className="px-4 py-4 text-sm text-gray-300">{agent.hostname || '-'}</td>
                    <td className="px-4 py-4 text-sm text-gray-300 font-mono">{agent.ip_address || '-'}</td>
                    <td className="px-4 py-4 text-sm text-gray-300">
                      {agent.country ? (
                        <span>{agent.country}{agent.city ? `, ${agent.city}` : ''}</span>
                      ) : '-'}
                    </td>
                    <td className="px-4 py-4 text-sm text-gray-300">{agent.os || '-'}</td>
                    <td className="px-4 py-4 text-sm text-gray-300">{agent.total_beacons ?? 0}</td>
                    <td className="px-4 py-4 text-sm">
                      <button
                        onClick={() => setSelectedAgent(agent.agent_id)}
                        className="text-blue-400 hover:text-blue-300"
                      >
                        Details
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Agent Detail Panel */}
      {selectedAgent && agentDetail && (
        <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
          <div className="flex items-center justify-between mb-4">
            <h2 className="text-xl font-bold text-white">Agent Details</h2>
            <button onClick={() => setSelectedAgent(null)} className="text-gray-400 hover:text-white">✕</button>
          </div>
          <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">Agent ID</p>
              <p className="text-white font-mono text-sm">{agentDetail.agent_id}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">User</p>
              <p className="text-white">{agentDetail.user || 'Unknown'}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">Hostname</p>
              <p className="text-white">{agentDetail.hostname || 'Unknown'}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">IP Address</p>
              <p className="text-white font-mono">{agentDetail.ip_address || 'Unknown'}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">Country</p>
              <p className="text-white">{agentDetail.country || 'Unknown'}{agentDetail.city ? `, ${agentDetail.city}` : ''}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">ISP</p>
              <p className="text-white">{agentDetail.isp || 'Unknown'}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">OS</p>
              <p className="text-white">{agentDetail.os || 'Unknown'} {agentDetail.os_version || ''}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">Architecture</p>
              <p className="text-white">{agentDetail.architecture || 'Unknown'}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">Encryption</p>
              <p className="text-white">{agentDetail.encryption || 'active'}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">Total Beacons</p>
              <p className="text-white">{agentDetail.total_beacons || 0}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">Pending Commands</p>
              <p className="text-white">{agentDetail.pending_commands || 0}</p>
            </div>
            <div className="p-3 bg-gray-700 rounded">
              <p className="text-gray-400 text-xs">Connected At</p>
              <p className="text-white text-sm">{agentDetail.connected_at || 'Unknown'}</p>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export default Sessions
