import { useState, useEffect } from 'react'

interface LiveAgent {
  agent_id: string
  hostname?: string
  os?: string
  user?: string
  encryption?: string
  last_message_index?: number
}

const Dashboard = () => {
  const [liveAgents, setLiveAgents] = useState<LiveAgent[]>([])
  const [liveCount, setLiveCount] = useState(0)
  const [fetchError, setFetchError] = useState('')

  useEffect(() => {
    let active = true
    const loadData = () => {
      fetch('/api/live/agents')
        .then(r => r.json())
        .then(data => {
          if (!active) return
          setLiveAgents(data.agents || [])
          setLiveCount(data.total || 0)
          setFetchError('')
        })
        .catch(err => {
          if (!active) return
          setFetchError(String(err))
        })
    }

    loadData()
    const interval = setInterval(loadData, 5000)
    return () => { active = false; clearInterval(interval) }
  }, [])

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-bold text-white">Dashboard</h1>

      {fetchError && (
        <div className="p-3 bg-yellow-900 border border-yellow-700 rounded text-yellow-200 text-sm">
          API: {fetchError}
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
          <p className="text-gray-400 text-sm mb-2">Total Agents</p>
          <p className="text-3xl font-bold text-blue-400">{liveCount}</p>
        </div>
        <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
          <p className="text-gray-400 text-sm mb-2">Active Agents</p>
          <p className={`text-3xl font-bold ${liveCount > 0 ? 'text-green-400' : 'text-red-400'}`}>{liveCount}</p>
        </div>
        <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
          <p className="text-gray-400 text-sm mb-2">Status</p>
          <p className="text-3xl font-bold text-purple-400">{liveCount > 0 ? 'Online' : 'Waiting'}</p>
        </div>
      </div>

      <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
        <h2 className="text-xl font-bold text-white mb-4">Live Connected Agents</h2>
        {liveAgents.length === 0 ? (
          <p className="text-gray-400">No agents connected. Run agent.exe to connect.</p>
        ) : (
          <div className="space-y-3">
            {liveAgents.map((agent) => (
              <div key={agent.agent_id} className="flex items-center justify-between p-4 bg-gray-700 rounded-lg">
                <div className="flex items-center gap-3">
                  <div className="w-3 h-3 bg-green-500 rounded-full animate-pulse"></div>
                  <div>
                    <p className="text-white font-mono text-sm">{agent.agent_id}</p>
                    <p className="text-gray-400 text-xs">
                      {agent.hostname || 'Unknown'} | {agent.os || 'Unknown'}
                    </p>
                  </div>
                </div>
                <span className="px-2 py-1 bg-green-900 text-green-200 rounded text-xs">
                  {agent.encryption || 'connected'}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

export default Dashboard
