import React, { useState, useEffect, useRef } from 'react';
import { X, RefreshCw, Download, Maximize2 } from 'lucide-react';

interface DesktopScreenProps {
  agentId: string;
  onClose: () => void;
}

export default function DesktopScreen({ agentId, onClose }: DesktopScreenProps) {
  const [screenshot, setScreenshot] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [fps, setFps] = useState(10);
  const [isStreaming, setIsStreaming] = useState(false);
  const [streamStatus, setStreamStatus] = useState('Ready');
  const [scale, setScale] = useState(100);
  const streamIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const frameCountRef = useRef(0);
  const startTimeRef = useRef(Date.now());
  const canvasRef = useRef<HTMLCanvasElement>(null);

  // Capture single screenshot
  const captureScreenshot = async () => {
    setLoading(true);
    try {
      const response = await fetch('/api/live/agents/' + agentId);
      if (!response.ok) throw new Error('Failed to fetch agent');

      const data = await response.json();
      // In real scenario, this would come from agent beacon with screenshot
      setStreamStatus('Screenshot captured');
      setScreenshot('data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iMTYwMCIgaGVpZ2h0PSI5MDAiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+PHJlY3Qgd2lkdGg9IjE2MDAiIGhlaWdodD0iOTAwIiBmaWxsPSIjMmQzYzU2Ii8+PHRleHQgeD0iNTAiIHk9IjUwIiBmaWxsPSIjZmZmIiBmb250LXNpemU9IjI0IiBmb250LWZhbWlseT0ibW9ub3NwYWNlIj5SkeDEgNC4wLjAuMDk2IC0gRGVza3RvcCBTY3JlZW4gQ2FwdHVyZTwvdGV4dD48cmVjdCB4PSI1MCIgeT0iMTAwIiB3aWR0aD0iMTUwMCIgaGVpZ2h0PSI3MDAiIGZpbGw9IiMxZTI4M2MiIHN0cm9rZT0iIzMzNTY5ZiIgc3Ryb2tlLXdpZHRoPSIyIi8+PHRleHQgeD0iNTAiIHk9IjQwMCIgZmlsbD0iIzQyODVmNCIgZm9udC1zaXplPSIxOCIgZm9udC1mYW1pbHk9Im1vbm9zcGFjZSI+QWdlbnQgSUQ6IGFnZW50LTAwMTwvdGV4dD48dGV4dCB4PSI1MCIgeT0iNDQwIiBmaWxsPSIjNDI4NWY0IiBmb250LXNpemU9IjE4IiBmb250LWZhbWlseT0ibW9ub3NwYWNlIj5SZXNvbHV0aW9uOiAxNjAweDkwMDwvdGV4dD48L3N2Zz4=');
    } catch (error) {
      setStreamStatus('Error: ' + (error as Error).message);
    }
    setLoading(false);
  };

  // Start live streaming
  const startStreaming = () => {
    setIsStreaming(true);
    setStreamStatus('Streaming started...');
    frameCountRef.current = 0;
    startTimeRef.current = Date.now();

    const interval = 1000 / fps; // Calculate interval based on FPS

    streamIntervalRef.current = setInterval(async () => {
      try {
        await captureScreenshot();
        frameCountRef.current++;

        // Calculate actual FPS every second
        const elapsed = (Date.now() - startTimeRef.current) / 1000;
        if (elapsed > 0) {
          const actualFps = Math.round(frameCountRef.current / elapsed);
          setStreamStatus(`Streaming: ${actualFps} FPS • ${frameCountRef.current} frames`);
        }
      } catch (error) {
        console.error('Stream error:', error);
      }
    }, interval);
  };

  // Stop streaming
  const stopStreaming = () => {
    if (streamIntervalRef.current) {
      clearInterval(streamIntervalRef.current);
    }
    setIsStreaming(false);
    setStreamStatus('Streaming stopped');
  };

  // Download screenshot
  const downloadScreenshot = () => {
    if (!screenshot) return;

    const link = document.createElement('a');
    link.href = screenshot;
    link.download = `screenshot-${agentId}-${new Date().getTime()}.jpg`;
    link.click();
  };

  // Fullscreen
  const toggleFullscreen = async () => {
    if (canvasRef.current) {
      try {
        await canvasRef.current.requestFullscreen();
      } catch (error) {
        console.error('Fullscreen error:', error);
      }
    }
  };

  useEffect(() => {
    return () => {
      if (streamIntervalRef.current) {
        clearInterval(streamIntervalRef.current);
      }
    };
  }, []);

  return (
    <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
      <div className="bg-slate-900 rounded-xl shadow-2xl max-w-6xl w-full max-h-screen flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between p-4 bg-slate-800 border-b border-slate-700">
          <div>
            <h2 className="text-2xl font-bold text-white flex items-center gap-2">
              📹 Desktop Screen - {agentId}
            </h2>
            <p className="text-sm text-slate-400 mt-1">{streamStatus}</p>
          </div>
          <button
            onClick={onClose}
            className="p-2 text-slate-400 hover:text-white hover:bg-slate-700 rounded transition"
          >
            <X className="w-6 h-6" />
          </button>
        </div>

        {/* Controls */}
        <div className="flex items-center gap-2 p-3 bg-slate-800 border-b border-slate-700">
          <button
            onClick={captureScreenshot}
            disabled={loading || isStreaming}
            className="px-3 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50 flex items-center gap-2 text-sm"
          >
            <RefreshCw className="w-4 h-4" />
            Capture
          </button>

          <button
            onClick={isStreaming ? stopStreaming : startStreaming}
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
              <option value={5}>5 FPS</option>
              <option value={10}>10 FPS</option>
              <option value={15}>15 FPS</option>
              <option value={30}>30 FPS</option>
            </select>
          </div>

          <div className="flex items-center gap-2">
            <label className="text-sm text-slate-300">Scale:</label>
            <input
              type="range"
              min="50"
              max="150"
              value={scale}
              onChange={(e) => setScale(Number(e.target.value))}
              className="w-24"
            />
            <span className="text-sm text-slate-400">{scale}%</span>
          </div>

          <button
            onClick={downloadScreenshot}
            disabled={!screenshot}
            className="p-2 text-slate-400 hover:text-white hover:bg-slate-700 rounded disabled:opacity-50 transition"
            title="Download"
          >
            <Download className="w-4 h-4" />
          </button>

          <button
            onClick={toggleFullscreen}
            className="p-2 text-slate-400 hover:text-white hover:bg-slate-700 rounded transition"
            title="Fullscreen"
          >
            <Maximize2 className="w-4 h-4" />
          </button>
        </div>

        {/* Screen Display */}
        <div className="flex-1 overflow-auto bg-black flex items-center justify-center p-4">
          {screenshot ? (
            <div style={{ width: `${scale}%`, maxWidth: '100%' }}>
              <img
                ref={canvasRef}
                src={screenshot}
                alt="Desktop Screen"
                className="w-full h-auto rounded border border-slate-700 shadow-2xl"
              />
              <div className="text-center mt-2 text-xs text-slate-500">
                Scale: {scale}% • Click to drag • Scroll to zoom
              </div>
            </div>
          ) : (
            <div className="text-center">
              <div className="text-6xl mb-4">📹</div>
              <p className="text-slate-400 mb-2">Click "Capture" or "Live Stream" to start</p>
              <p className="text-slate-500 text-sm">Desktop screen will appear here</p>
            </div>
          )}
        </div>

        {/* Status Bar */}
        <div className="bg-slate-800 border-t border-slate-700 px-4 py-2 text-xs text-slate-400">
          💡 Tip: Use Live Stream for smooth real-time viewing. Single Capture for one-time screenshot.
        </div>
      </div>
    </div>
  );
}
