import { useState, useEffect } from 'react'
import axios from 'axios'

interface Agent {
  agent_id: string
  hostname: string
  os: string
  os_version?: string
  architecture?: string
  user?: string
  sandbox_detected?: boolean
  sandbox_flags?: string[]
}

const SystemInfo = () => {
  const [agents, setAgents] = useState<Agent[]>([])
  const [selectedAgent, setSelectedAgent] = useState('')
  const [sysInfo, setSysInfo] = useState('')
  const [netInfo, setNetInfo] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    axios.get('/api/live/agents').then(res => {
      const list = Object.entries(res.data).map(([id, info]: [string, any]) => ({
        agent_id: id, ...info,
      }))
      setAgents(list)
      if (list.length > 0) setSelectedAgent(list[0].agent_id)
    }).catch(() => {})
  }, [])

  const sendCommand = async (cmd: any): Promise<string> => {
    const res = await axios.post(`/api/live/agents/${selectedAgent}/command`, cmd)
    const cmdId = res.data?.cmd_id
    for (let i = 0; i < 30; i++) {
      await new Promise(r => setTimeout(r, 1000))
      const poll = await axios.get(`/api/live/agents/${selectedAgent}/result/${cmdId}`)
      if (poll.data?.status && poll.data.status !== 'pending') {
        if (poll.data.status === 'error') throw new Error(poll.data.error || 'Failed')
        return poll.data.output || ''
      }
    }
    throw new Error('Timeout')
  }

  const fetchAll = async () => {
    if (!selectedAgent) return
    setLoading(true)
    setError('')
    try {
      const [sys, net] = await Promise.all([
        sendCommand({ type: 'system_info' }),
        sendCommand({ type: 'network_info' }),
      ])
      setSysInfo(sys)
      setNetInfo(net)
    } catch (e: any) { setError(e.message) }
    setLoading(false)
  }

  const agent = agents.find(a => a.agent_id === selectedAgent)

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-white">System Intelligence</h1>
        <div className="flex gap-3">
          <select
            value={selectedAgent}
            onChange={e => setSelectedAgent(e.target.value)}
            className="bg-gray-800 text-white border border-gray-600 rounded px-3 py-2"
          >
            {agents.map(a => (
              <option key={a.agent_id} value={a.agent_id}>{a.hostname} ({a.os})</option>
            ))}
          </select>
          <button
            onClick={fetchAll}
            disabled={loading || !selectedAgent}
            className="bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded disabled:opacity-50"
          >
            {loading ? 'Gathering...' : 'Gather Intel'}
          </button>
        </div>
      </div>

      {error && <div className="bg-red-900/30 border border-red-700 text-red-400 p-3 rounded">{error}</div>}

      {/* Agent Overview Card */}
      {agent && (
        <div className="bg-gray-800 p-5 rounded-lg border border-gray-700">
          <h3 className="text-white font-semibold mb-3">Agent Overview</h3>
          <div className="grid grid-cols-4 gap-4">
            <div>
              <span className="text-gray-400 text-sm">Hostname</span>
              <p className="text-white font-mono">{agent.hostname}</p>
            </div>
            <div>
              <span className="text-gray-400 text-sm">OS</span>
              <p className="text-white">{agent.os} {agent.os_version || ''}</p>
            </div>
            <div>
              <span className="text-gray-400 text-sm">Architecture</span>
              <p className="text-white">{agent.architecture || 'Unknown'}</p>
            </div>
            <div>
              <span className="text-gray-400 text-sm">User</span>
              <p className="text-white font-mono">{agent.user || 'Unknown'}</p>
            </div>
          </div>
          {agent.sandbox_detected && (
            <div className="mt-3 bg-yellow-900/30 border border-yellow-700 text-yellow-400 p-2 rounded text-sm">
              Sandbox detected: {agent.sandbox_flags?.join(', ') || 'Unknown flags'}
            </div>
          )}
        </div>
      )}

      {/* System Info */}
      <div className="grid grid-cols-2 gap-4">
        <div className="bg-gray-800 p-5 rounded-lg border border-gray-700">
          <h3 className="text-cyan-400 font-semibold mb-3">System Information</h3>
          <pre className="text-gray-300 text-sm whitespace-pre-wrap font-mono bg-gray-900 p-4 rounded max-h-[400px] overflow-auto">
            {sysInfo || 'Click "Gather Intel" to fetch'}
          </pre>
        </div>
        <div className="bg-gray-800 p-5 rounded-lg border border-gray-700">
          <h3 className="text-green-400 font-semibold mb-3">Network Information</h3>
          <pre className="text-gray-300 text-sm whitespace-pre-wrap font-mono bg-gray-900 p-4 rounded max-h-[400px] overflow-auto">
            {netInfo || 'Click "Gather Intel" to fetch'}
          </pre>
        </div>
      </div>
    </div>
  )
}

export default SystemInfo
