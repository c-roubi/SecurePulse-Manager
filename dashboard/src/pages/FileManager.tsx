import React, { useState } from 'react';
import { Upload, Download, Trash2, FolderOpen, File, RefreshCw } from 'lucide-react';

interface FileManagerProps {
  agentId: string;
}

export default function FileManager({ agentId }: FileManagerProps) {
  const [files, setFiles] = useState<any[]>([]);
  const [currentPath, setCurrentPath] = useState('/tmp');
  const [loading, setLoading] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [message, setMessage] = useState('');

  const listFiles = async () => {
    setLoading(true);
    try {
      const response = await fetch(`/api/files/list/${agentId}?path=${currentPath}`);
      const data = await response.json();
      setMessage(data.message || 'Listing files...');
    } catch (error) {
      setMessage('Error listing files: ' + (error as Error).message);
    }
    setLoading(false);
  };

  const uploadFile = async () => {
    if (!selectedFile) {
      setMessage('Please select a file');
      return;
    }

    setLoading(true);
    const formData = new FormData();
    formData.append('file', selectedFile);

    try {
      const response = await fetch(`/api/files/upload/${agentId}`, {
        method: 'POST',
        body: formData,
      });
      const data = await response.json();
      setMessage(`✅ Uploaded: ${data.filename} (${data.size} bytes)`);
      setSelectedFile(null);
    } catch (error) {
      setMessage('Error uploading: ' + (error as Error).message);
    }
    setLoading(false);
  };

  const downloadFile = async (filename: string) => {
    setLoading(true);
    try {
      const response = await fetch(`/api/files/download/${agentId}/${filename}`);
      if (response.ok) {
        const blob = await response.blob();
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        setMessage(`✅ Downloaded: ${filename}`);
      } else {
        setMessage('File transfer command queued, check agent beacons');
      }
    } catch (error) {
      setMessage('Error downloading: ' + (error as Error).message);
    }
    setLoading(false);
  };

  const deleteFile = async (filename: string) => {
    if (!confirm(`Delete ${filename}?`)) return;

    setLoading(true);
    try {
      const response = await fetch(`/api/files/delete/${agentId}/${filename}`, {
        method: 'DELETE',
      });
      const data = await response.json();
      setMessage(`✅ Delete command queued for: ${filename}`);
    } catch (error) {
      setMessage('Error deleting: ' + (error as Error).message);
    }
    setLoading(false);
  };

  const getFileIcon = (filename: string) => {
    if (filename.includes('.')) {
      return <File className="w-4 h-4 text-blue-500" />;
    }
    return <FolderOpen className="w-4 h-4 text-yellow-500" />;
  };

  return (
    <div className="space-y-6 p-6 bg-gradient-to-br from-slate-900 to-slate-800 rounded-xl">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold text-white flex items-center gap-2">
          <FolderOpen className="w-6 h-6" />
          File Manager
        </h2>
        <span className="text-sm text-slate-400">Agent: {agentId}</span>
      </div>

      {/* Path Navigation */}
      <div className="flex gap-2">
        <input
          type="text"
          value={currentPath}
          onChange={(e) => setCurrentPath(e.target.value)}
          placeholder="Enter path..."
          className="flex-1 px-4 py-2 bg-slate-700 text-white rounded border border-slate-600 focus:border-blue-500 outline-none"
        />
        <button
          onClick={listFiles}
          disabled={loading}
          className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50 flex items-center gap-2"
        >
          <RefreshCw className="w-4 h-4" />
          List
        </button>
      </div>

      {/* Upload Section */}
      <div className="bg-slate-700 rounded-lg p-4">
        <h3 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
          <Upload className="w-5 h-5" />
          Upload File
        </h3>
        <div className="flex gap-2">
          <input
            type="file"
            onChange={(e) => setSelectedFile(e.target.files?.[0] || null)}
            className="flex-1 px-3 py-2 bg-slate-600 text-white rounded text-sm"
          />
          <button
            onClick={uploadFile}
            disabled={loading || !selectedFile}
            className="px-4 py-2 bg-green-600 text-white rounded hover:bg-green-700 disabled:opacity-50 flex items-center gap-2"
          >
            <Upload className="w-4 h-4" />
            Upload
          </button>
        </div>
      </div>

      {/* Files List (Mock Data) */}
      <div className="bg-slate-700 rounded-lg p-4">
        <h3 className="text-lg font-semibold text-white mb-3">Files</h3>
        <div className="space-y-2 max-h-96 overflow-y-auto">
          {[
            { name: 'test.txt', size: '1.2 KB', modified: '2 min ago' },
            { name: 'data.json', size: '5.6 KB', modified: '1 hour ago' },
            { name: 'config.yml', size: '0.8 KB', modified: 'yesterday' },
          ].map((file) => (
            <div
              key={file.name}
              className="flex items-center justify-between p-3 bg-slate-600 rounded hover:bg-slate-500 transition"
            >
              <div className="flex items-center gap-3 flex-1">
                {getFileIcon(file.name)}
                <div>
                  <div className="text-white font-medium">{file.name}</div>
                  <div className="text-sm text-slate-400">{file.size} • {file.modified}</div>
                </div>
              </div>
              <div className="flex gap-2">
                <button
                  onClick={() => downloadFile(file.name)}
                  className="p-2 text-blue-400 hover:bg-slate-700 rounded transition"
                  title="Download"
                >
                  <Download className="w-4 h-4" />
                </button>
                <button
                  onClick={() => deleteFile(file.name)}
                  className="p-2 text-red-400 hover:bg-slate-700 rounded transition"
                  title="Delete"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Status Message */}
      {message && (
        <div className={`p-3 rounded ${
          message.includes('✅')
            ? 'bg-green-900 text-green-300'
            : 'bg-blue-900 text-blue-300'
        }`}>
          {message}
        </div>
      )}
    </div>
  );
}
