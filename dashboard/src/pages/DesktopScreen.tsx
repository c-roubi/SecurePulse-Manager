import React, { useState, useEffect, useRef, useCallback } from 'react';
import { X, RefreshCw, Download, Maximize2 } from 'lucide-react';

interface DesktopScreenProps {
  agentId: string;
  onClose: () => void;
}

export default function DesktopScreen({ agentId, onClose }: DesktopScreenProps) {
  const [screenshot, setScreenshot] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [fps, setFps] = useState(5);
  const [isStreaming, setIsStreaming] = useState(false);
  const [streamStatus, setStreamStatus] = useState('Ready');
  const [scale, setScale] = useState(100);
  const [error, setError] = useState('');
  const streamingRef = useRef(false);
  const frameCountRef = useRef(0);
  const startTimeRef = useRef(Date.now());
  const canvasRef = useRef<HTMLDivElement>(null);

  const sendScreenshotCommand = useCallback(async (): Promise<string | null> => {
    try {
      // Send screenshot command
      const res = await fetch(`/api/live/agents/${agentId}/command`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ command_type: 'screenshot' }),
      });
      if (!res.ok) throw new Error('Failed to send screenshot command');
      const data = await res.json();
      const cmdId = data.cmd_id;

      // Poll for result using specific command endpoint
      for (let i = 0; i < 30; i++) {
        await new Promise(r => setTimeout(r, 500));
        const cmdRes = await fetch(`/api/live/agents/${agentId}/commands/${cmdId}`);
        if (!cmdRes.ok) continue;
        const cmd = await cmdRes.json();
        if (cmd.status !== 'queued' && cmd.status !== 'sent') {
          if (cmd.status === 'success' && cmd.output && cmd.output.startsWith('data:image')) {
            return cmd.output;
          } else if (cmd.error) {
            throw new Error(cmd.error);
          } else if (cmd.output) {
            return cmd.output;
          }
          return null;
        }
      }
      throw new Error('Timeout waiting for screenshot');
    } catch (err) {
      throw err;
    }
  }, [agentId]);

  const captureScreenshot = async () => {
    setLoading(true);
    setError('');
    try {
      const img = await sendScreenshotCommand();
      if (img) {
        setScreenshot(img);
        setStreamStatus('Screenshot captured');
      } else {
        setStreamStatus('No screenshot data received');
      }
    } catch (err) {
      const msg = (err as Error).message;
      setError(msg);
      setStreamStatus('Error: ' + msg);
    }
    setLoading(false);
  };

  const startStreaming = () => {
    setIsStreaming(true);
    streamingRef.current = true;
    frameCountRef.current = 0;
    startTimeRef.current = Date.now();
    setStreamStatus('Starting stream...');

    const loop = async () => {
      while (streamingRef.current) {
        try {
          const img = await sendScreenshotCommand();
          if (img && streamingRef.current) {
            setScreenshot(img);
            frameCountRef.current++;
            const elapsed = (Date.now() - startTimeRef.current) / 1000;
            const actualFps = elapsed > 0 ? Math.round(frameCountRef.current / elapsed) : 0;
            setStreamStatus(`Streaming: ${actualFps} FPS | ${frameCountRef.current} frames`);
          }
        } catch (err) {
          if (streamingRef.current) {
            setStreamStatus('Stream error: ' + (err as Error).message);
          }
        }
        // Delay between captures based on FPS
        if (streamingRef.current) {
          await new Promise(r => setTimeout(r, 1000 / fps));
        }
      }
    };
    loop();
  };

  const stopStreaming = () => {
    streamingRef.current = false;
    setIsStreaming(false);
    setStreamStatus(`Stopped (${frameCountRef.current} frames captured)`);
  };

  const downloadScreenshot = () => {
    if (!screenshot) return;
    const link = document.createElement('a');
    link.href = screenshot;
    link.download = `screenshot-${agentId.slice(0, 8)}-${Date.now()}.jpg`;
    link.click();
  };

  const toggleFullscreen = async () => {
    if (canvasRef.current) {
      try { await canvasRef.current.requestFullscreen(); } catch {}
    }
  };

  useEffect(() => {
    return () => { streamingRef.current = false; };
  }, []);

  return (
    <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
      <div className="bg-slate-900 rounded-xl shadow-2xl max-w-6xl w-full max-h-screen flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between p-4 bg-slate-800 border-b border-slate-700">
          <div>
            <h2 className="text-xl font-bold text-white flex items-center gap-2">
              Desktop Screen - {agentId.slice(0, 12)}...
            </h2>
            <p className="text-sm text-slate-400 mt-1">{streamStatus}</p>
          </div>
          <button
            onClick={() => { streamingRef.current = false; onClose(); }}
            className="p-2 text-slate-400 hover:text-white hover:bg-slate-700 rounded transition"
          >
            <X className="w-6 h-6" />
          </button>
        </div>

        {/* Controls */}
        <div className="flex items-center gap-2 p-3 bg-slate-800 border-b border-slate-700 flex-wrap">
          <button
            onClick={captureScreenshot}
            disabled={loading || isStreaming}
            className="px-3 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50 flex items-center gap-2 text-sm"
          >
            <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
            Capture
          </button>

          <button
            onClick={isStreaming ? stopStreaming : startStreaming}
            disabled={loading}
            className={`px-3 py-2 rounded flex items-center gap-2 text-sm font-semibold transition ${
              isStreaming
                ? 'bg-red-600 hover:bg-red-700 text-white'
                : 'bg-green-600 hover:bg-green-700 text-white'
            }`}
          >
            <div className={`w-2 h-2 rounded-full ${isStreaming ? 'bg-red-300 animate-pulse' : 'bg-green-300'}`} />
            {isStreaming ? 'Stop Stream' : 'Live Stream'}
          </button>

          <div className="flex items-center gap-2 ml-auto">
            <label className="text-sm text-slate-300">FPS:</label>
            <select
              value={fps}
              onChange={(e) => setFps(Number(e.target.value))}
              disabled={isStreaming}
              className="px-2 py-1 bg-slate-700 text-white rounded text-sm disabled:opacity-50"
            >
              <option value={1}>1 FPS</option>
              <option value={2}>2 FPS</option>
              <option value={5}>5 FPS</option>
              <option value={10}>10 FPS</option>
            </select>
          </div>

          <div className="flex items-center gap-2">
            <label className="text-sm text-slate-300">Scale:</label>
            <input type="range" min="50" max="150" value={scale}
              onChange={(e) => setScale(Number(e.target.value))} className="w-24" />
            <span className="text-sm text-slate-400">{scale}%</span>
          </div>

          <button onClick={downloadScreenshot} disabled={!screenshot}
            className="p-2 text-slate-400 hover:text-white hover:bg-slate-700 rounded disabled:opacity-50 transition" title="Download">
            <Download className="w-4 h-4" />
          </button>
          <button onClick={toggleFullscreen}
            className="p-2 text-slate-400 hover:text-white hover:bg-slate-700 rounded transition" title="Fullscreen">
            <Maximize2 className="w-4 h-4" />
          </button>
        </div>

        {/* Screen Display */}
        <div className="flex-1 overflow-auto bg-black flex items-center justify-center p-4" style={{ minHeight: '400px' }}>
          {error && (
            <div className="absolute top-2 left-2 right-2 p-2 bg-red-900 text-red-200 text-xs rounded z-10">
              {error}
            </div>
          )}
          {screenshot ? (
            <div style={{ width: `${scale}%`, maxWidth: '100%' }} ref={canvasRef}>
              <img
                src={screenshot}
                alt="Desktop Screen"
                className="w-full h-auto rounded border border-slate-700 shadow-2xl"
              />
            </div>
          ) : (
            <div className="text-center">
              <div className="text-6xl mb-4 opacity-50">🖥️</div>
              <p className="text-slate-400 mb-2">Click "Capture" or "Live Stream" to start</p>
              <p className="text-slate-500 text-sm">The agent will take a screenshot and send it back</p>
              {loading && (
                <div className="mt-4">
                  <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-500 mx-auto"></div>
                  <p className="text-blue-400 text-sm mt-2">Requesting screenshot from agent...</p>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Status Bar */}
        <div className="bg-slate-800 border-t border-slate-700 px-4 py-2 text-xs text-slate-400">
          Note: Screenshots are captured by the agent and sent over the network. Higher FPS = more bandwidth.
        </div>
      </div>
    </div>
  );
}
