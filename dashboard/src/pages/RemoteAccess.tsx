import React, { useState, useEffect } from 'react';
import { Monitor, Play, Pause } from 'lucide-react';
import DesktopScreen from './DesktopScreen';

interface Agent {
  id: string;
  hostname: string;
  os: string;
  status: string;
}

export default function RemoteAccess() {
  const [agents, setAgents] = useState<Agent[]>([]);
  const [selectedAgent, setSelectedAgent] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchAgents();
    const interval = setInterval(fetchAgents, 3000);
    return () => clearInterval(interval);
  }, []);

  const fetchAgents = async () => {
    try {
      const response = await fetch('/api/live/agents');
      const data = await response.json();
      const agentsObj = data.agents || data;
      if (typeof agentsObj === 'object' && !Array.isArray(agentsObj)) {
        setAgents(Object.keys(agentsObj).filter(k => k !== 'total').map(id => ({ id, ...agentsObj[id] })));
      } else {
        setAgents([]);
      }
    } catch (error) {
      console.error('Error fetching agents:', error);
    } finally {
      setLoading(false);
    }
  };

  if (selectedAgent) {
    return (
      <DesktopScreen
        agentId={selectedAgent}
        onClose={() => setSelectedAgent(null)}
      />
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-3xl font-bold text-white flex items-center gap-2">
          <Monitor className="w-8 h-8" />
          Remote Desktop Access
        </h1>
        <span className="text-sm text-slate-400">{agents.length} agents available</span>
      </div>

      {loading ? (
        <div className="flex items-center justify-center h-64">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-500"></div>
        </div>
      ) : agents.length === 0 ? (
        <div className="bg-slate-700 rounded-lg p-8 text-center">
          <Monitor className="w-16 h-16 text-slate-400 mx-auto mb-4 opacity-50" />
          <p className="text-slate-300 text-lg">No agents connected</p>
          <p className="text-slate-400 text-sm mt-2">Connect an agent to use remote desktop access</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {agents.map((agent) => (
            <div
              key={agent.id}
              className="bg-gradient-to-br from-slate-800 to-slate-900 rounded-lg overflow-hidden hover:shadow-xl transition border border-slate-700 hover:border-blue-500"
            >
              {/* Thumbnail Preview */}
              <div className="bg-black h-40 flex items-center justify-center relative overflow-hidden group">
                <div className="text-6xl opacity-50 group-hover:scale-110 transition-transform">
                  🖥️
                </div>
                <div className="absolute inset-0 bg-gradient-to-t from-black to-transparent opacity-0 group-hover:opacity-100 transition-opacity flex items-end">
                  <button
                    onClick={() => setSelectedAgent(agent.id)}
                    className="w-full py-3 bg-blue-600 hover:bg-blue-700 text-white font-semibold flex items-center justify-center gap-2 transition"
                  >
                    <Play className="w-4 h-4" />
                    View Desktop
                  </button>
                </div>
              </div>

              {/* Agent Info */}
              <div className="p-4">
                <div className="flex items-start justify-between mb-3">
                  <div className="flex-1">
                    <h3 className="text-lg font-semibold text-white">
                      {agent.hostname || agent.id}
                    </h3>
                    <p className="text-sm text-slate-400">
                      🆔 {agent.id.substring(0, 8)}...
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <div className="w-2 h-2 bg-green-500 rounded-full animate-pulse"></div>
                    <span className="text-xs font-semibold text-green-400">LIVE</span>
                  </div>
                </div>

                {/* System Info */}
                <div className="space-y-2 mb-4 text-sm">
                  <div className="flex items-center gap-2 text-slate-400">
                    <span>🖥️</span>
                    <span>{agent.os}</span>
                  </div>
                </div>

                {/* Actions */}
                <button
                  onClick={() => setSelectedAgent(agent.id)}
                  className="w-full px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg font-medium transition flex items-center justify-center gap-2"
                >
                  <Monitor className="w-4 h-4" />
                  Open Remote Desktop
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Info Panel */}
      <div className="bg-slate-800 rounded-lg p-6 border border-slate-700">
        <h2 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
          💡 Remote Desktop Features
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm text-slate-300">
          <div className="flex gap-3">
            <span className="text-lg">📹</span>
            <div>
              <p className="font-semibold text-white">Live Streaming</p>
              <p className="text-slate-400">Real-time desktop view at 5-30 FPS</p>
            </div>
          </div>
          <div className="flex gap-3">
            <span className="text-lg">⚡</span>
            <div>
              <p className="font-semibold text-white">Low Latency</p>
              <p className="text-slate-400">Optimized encryption & compression</p>
            </div>
          </div>
          <div className="flex gap-3">
            <span className="text-lg">🎮</span>
            <div>
              <p className="font-semibold text-white">Smooth Playback</p>
              <p className="text-slate-400">Adjustable FPS for bandwidth control</p>
            </div>
          </div>
          <div className="flex gap-3">
            <span className="text-lg">💾</span>
            <div>
              <p className="font-semibold text-white">Screenshot Download</p>
              <p className="text-slate-400">Save screenshots locally</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
