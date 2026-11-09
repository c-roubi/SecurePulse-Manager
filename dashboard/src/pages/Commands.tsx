import { useState, useEffect, useRef } from 'react'

interface Agent {
  agent_id: string
  hostname?: string
  user?: string
  ip_address?: string
}

interface CmdEntry {
  cmd_id: number
  command: { command_type?: string; command?: string }
  queued_at: string
  status: string
  output?: string | null
  error?: string | null
  completed_at?: string
}

const Commands = () => {
  const [agents, setAgents] = useState<Agent[]>([])
  const [selectedAgent, setSelectedAgent] = useState('')
  const [command, setCommand] = useState('')
  const [history, setHistory] = useState<CmdEntry[]>([])
  const [sending, setSending] = useState(false)
  const bottomRef = useRef<HTMLDivElement>(null)

  // Load agents
  useEffect(() => {
    const load = async () => {
      try {
        const res = await fetch('/api/live/agents')
        const data = await res.json()
        const obj = data.agents || {}
        if (typeof obj === 'object' && !Array.isArray(obj)) {
          const arr = Object.keys(obj).map(id => ({ agent_id: id, ...obj[id] }))
          setAgents(arr)
          if (!selectedAgent && arr.length > 0) setSelectedAgent(arr[0].agent_id)
        }
      } catch {}
    }
    load()
    const iv = setInterval(load, 5000)
    return () => clearInterval(iv)
  }, [])

  // Poll command history for selected agent
  useEffect(() => {
    if (!selectedAgent) { setHistory([]); return }
    const load = async () => {
      try {
        const res = await fetch(`/api/live/agents/${selectedAgent}/commands`)
        if (res.ok) {
          const data = await res.json()
          setHistory(data.commands || [])
        }
      } catch {}
    }
    load()
    const iv = setInterval(load, 2000)
    return () => clearInterval(iv)
  }, [selectedAgent])

  // Auto-scroll
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [history])

  const handleExecute = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!selectedAgent || !command.trim()) return
    setSending(true)
    try {
      await fetch(`/api/live/agents/${selectedAgent}/command`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ command_type: 'shell', command: command.trim() }),
      })
      setCommand('')
    } catch (err) {
      console.error('Failed to send command:', err)
    } finally {
      setSending(false)
    }
  }

  const agentLabel = (a: Agent) => {
    const parts = [a.user, a.hostname, a.ip_address].filter(Boolean)
    return parts.length > 0 ? `${a.agent_id.slice(0, 8)}... (${parts.join(' | ')})` : a.agent_id.slice(0, 8) + '...'
  }

  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-bold text-white">Commands</h1>

      {/* Agent Selector */}
      <div className="bg-gray-800 rounded-lg p-4 border border-gray-700">
        <label className="text-gray-400 text-sm block mb-2">Target Agent</label>
        {agents.length === 0 ? (
          <p className="text-yellow-400 text-sm">No agents connected</p>
        ) : (
          <select
            value={selectedAgent}
            onChange={(e) => setSelectedAgent(e.target.value)}
            className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded text-white"
          >
            {agents.map(a => (
              <option key={a.agent_id} value={a.agent_id}>{agentLabel(a)}</option>
            ))}
          </select>
        )}
      </div>

      {/* Terminal-style output */}
      <div className="bg-gray-900 rounded-lg border border-gray-700 overflow-hidden">
        <div className="p-3 bg-gray-800 border-b border-gray-700 flex items-center gap-2">
          <div className="w-3 h-3 bg-red-500 rounded-full"></div>
          <div className="w-3 h-3 bg-yellow-500 rounded-full"></div>
          <div className="w-3 h-3 bg-green-500 rounded-full"></div>
          <span className="text-gray-400 text-sm ml-2 font-mono">
            {selectedAgent ? `${selectedAgent.slice(0, 12)}...` : 'No agent'}
          </span>
        </div>
        <div className="p-4 h-96 overflow-y-auto font-mono text-sm">
          {history.length === 0 ? (
            <p className="text-gray-500">No commands executed yet. Type a command below.</p>
          ) : (
            history.map((entry) => (
              <div key={entry.cmd_id} className="mb-4">
                <div className="text-blue-400">
                  <span className="text-gray-500 text-xs mr-2">[{entry.queued_at?.split('T')[1]?.slice(0, 8) || ''}]</span>
                  $ {entry.command?.command || JSON.stringify(entry.command)}
                </div>
                {entry.status === 'queued' || entry.status === 'sent' ? (
                  <div className="text-yellow-400 ml-4 animate-pulse">Waiting for response...</div>
                ) : entry.status === 'error' || entry.error ? (
                  <div className="ml-4">
                    {entry.output && <pre className="text-green-400 whitespace-pre-wrap">{entry.output}</pre>}
                    {entry.error && <pre className="text-red-400 whitespace-pre-wrap">{entry.error}</pre>}
                  </div>
                ) : (
                  <pre className="text-green-400 ml-4 whitespace-pre-wrap">{entry.output || '(no output)'}</pre>
                )}
              </div>
            ))
          )}
          <div ref={bottomRef} />
        </div>

        {/* Command input */}
        <form onSubmit={handleExecute} className="flex border-t border-gray-700">
          <span className="px-3 py-3 text-green-400 font-mono text-sm bg-gray-800">$</span>
          <input
            type="text"
            value={command}
            onChange={(e) => setCommand(e.target.value)}
            placeholder="Enter command..."
            disabled={!selectedAgent || sending}
            className="flex-1 px-2 py-3 bg-gray-900 text-white font-mono text-sm border-none outline-none"
          />
          <button
            type="submit"
            disabled={!selectedAgent || !command.trim() || sending}
            className="px-6 py-3 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-700 text-white text-sm font-medium"
          >
            {sending ? 'Sending...' : 'Execute'}
          </button>
        </form>
      </div>
    </div>
  )
}

export default Commands
