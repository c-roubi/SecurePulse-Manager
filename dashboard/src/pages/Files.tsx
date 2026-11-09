import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { FolderOpen, ChevronRight } from 'lucide-react'
import FileManager from './FileManager'

interface Agent {
  id: string
  hostname: string
  os: string
  status: string
}

export default function Files() {
  const [agents, setAgents] = useState<Agent[]>([])
  const [selectedAgent, setSelectedAgent] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetchAgents()
  }, [])

  const fetchAgents = async () => {
    try {
      const response = await fetch('/api/live/agents')
      const data = await response.json()
      const agentsObj = data.agents || {}
      if (typeof agentsObj === 'object' && !Array.isArray(agentsObj)) {
        setAgents(Object.keys(agentsObj).map(id => ({ id, ...agentsObj[id] })))
      } else if (Array.isArray(agentsObj)) {
        setAgents(agentsObj)
      } else {
        setAgents([])
      }
    } catch (error) {
      console.error('Error fetching agents:', error)
    } finally {
      setLoading(false)
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-slate-900">
        <div className="text-white">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-500"></div>
          <p className="mt-4">Loading agents...</p>
        </div>
      </div>
    )
  }

  if (selectedAgent) {
    return (
      <div className="p-6">
        <button
          onClick={() => setSelectedAgent(null)}
          className="mb-4 px-4 py-2 bg-slate-700 text-white rounded hover:bg-slate-600 transition"
        >
          ← Back to Agents
        </button>
        <FileManager agentId={selectedAgent} />
      </div>
    )
  }

  return (
    <div className="space-y-6 p-6 bg-gradient-to-br from-slate-900 to-slate-800 rounded-xl">
      <div className="flex items-center justify-between">
        <h1 className="text-3xl font-bold text-white flex items-center gap-2">
          <FolderOpen className="w-8 h-8" />
          File Manager
        </h1>
        <span className="text-sm text-slate-400">{agents.length} agents available</span>
      </div>

      {agents.length === 0 ? (
        <div className="bg-slate-700 rounded-lg p-8 text-center">
          <p className="text-slate-300">No agents connected</p>
          <p className="text-slate-400 text-sm mt-2">Connect an agent to manage files</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {agents.map((agent) => (
            <button
              key={agent.id}
              onClick={() => setSelectedAgent(agent.id)}
              className="bg-slate-700 rounded-lg p-4 hover:bg-slate-600 transition text-left group"
            >
              <div className="flex items-center justify-between">
                <div className="flex-1">
                  <h3 className="text-lg font-semibold text-white mb-2">
                    {agent.hostname || agent.id}
                  </h3>
                  <div className="space-y-1 text-sm text-slate-400">
                    <p>🖥️ {agent.os}</p>
                    <p>🆔 {agent.id}</p>
                    <p className="text-green-400">● Connected</p>
                  </div>
                </div>
                <ChevronRight className="w-6 h-6 text-slate-400 group-hover:text-blue-400 transition" />
              </div>
            </button>
          ))}
        </div>
      )}

      <div className="bg-blue-900 rounded-lg p-4 text-blue-200 text-sm">
        <p className="font-semibold mb-2">💡 How to use:</p>
        <ul className="space-y-1 text-blue-300">
          <li>1️⃣ Select an agent from the list above</li>
          <li>2️⃣ Upload files to send to the agent</li>
          <li>3️⃣ Download files from the agent</li>
          <li>4️⃣ Delete files on the agent</li>
          <li>5️⃣ Browse directories and check filesystem info</li>
        </ul>
      </div>
    </div>
  )
}
