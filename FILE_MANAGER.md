# 📁 File Manager - Complete Setup

## ✅ Overview

The File Manager provides a complete solution for managing files on remote agents through an encrypted C2 channel.

### Features
- ✅ **Upload** - Send files to agent `/tmp` directory
- ✅ **Download** - Retrieve files from agent
- ✅ **Delete** - Remove files with confirmation
- ✅ **List** - Browse directory contents
- ✅ **Info** - Get filesystem disk usage
- ✅ **Dynamic** - Commands queued for active agents

---

## 🔧 Backend Setup

### Files API Module
**Location:** `server/app/api/files.py`

### Endpoints

#### 1. Upload File
```
POST /api/files/upload/{agent_id}
Content-Type: multipart/form-data

Parameters:
  - file: (binary) File to upload

Response:
{
  "status": "success",
  "filename": "example.txt",
  "size": 1234,
  "uploaded_at": "2026-11-06T05:00:00",
  "agent_id": "agent-001"
}
```

#### 2. Download File
```
GET /api/files/download/{agent_id}/{filename}

Parameters:
  - agent_id: Target agent ID
  - filename: File to download

Response:
  - 200: File content (binary)
  - 202: Transfer command queued
```

#### 3. List Files
```
GET /api/files/list/{agent_id}?path=/tmp

Parameters:
  - path: Directory path (default: /tmp)

Response:
{
  "status": "pending",
  "command": "ls -lah /tmp",
  "agent_id": "agent-001",
  "message": "File listing command queued"
}
```

#### 4. Delete File
```
DELETE /api/files/delete/{agent_id}/{filename}

Parameters:
  - agent_id: Target agent ID
  - filename: File to delete

Response:
{
  "status": "success",
  "filename": "example.txt",
  "deleted_at": "2026-11-06T05:00:00",
  "agent_id": "agent-001"
}
```

#### 5. Filesystem Info
```
GET /api/files/info/{agent_id}?path=/tmp

Parameters:
  - path: Directory path (default: /tmp)

Response:
{
  "status": "pending",
  "agent_id": "agent-001",
  "command": "df and du",
  "message": "File system info command queued"
}
```

---

## 🎨 Frontend Setup

### Files Page Component
**Location:** `dashboard/src/pages/Files.tsx`

### Navigation Flow

```
/files
  ├─ Shows list of connected agents
  ├─ Select agent
  └─ /files/{agent_id} → File Manager interface
```

### File Manager Component
**Location:** `dashboard/src/pages/FileManager.tsx`

#### UI Sections

1. **Path Navigation**
   - Input field for directory path
   - "List" button to refresh

2. **Upload Widget**
   - File picker
   - Upload button
   - Progress indication

3. **Files Browser**
   - File list with icons
   - File size and modification time
   - Download button (per file)
   - Delete button with confirmation

4. **Status Messages**
   - Success (green)
   - Info (blue)
   - Error handling

---

## 📋 Sidebar Integration

**Location:** `dashboard/src/components/Sidebar.tsx`

```typescript
const menuItems = [
  { path: '/dashboard', label: '📊 Dashboard', icon: '📊' },
  { path: '/sessions', label: '🔗 Sessions', icon: '🔗' },
  { path: '/commands', label: '⌨️  Commands', icon: '⌨️' },
  { path: '/monitoring', label: '📡 Monitoring', icon: '📡' },
  { path: '/files', label: '🗂️  File Manager', icon: '🗂️' },  // ← NEW
]
```

---

## 🔄 Usage Workflow

### Step 1: Navigate to File Manager
```
Dashboard → Sidebar "🗂️ File Manager"
```

### Step 2: Select Agent
```
Agents List
├─ Agent 001 (root@linux)
├─ Agent 002 (admin@windows)
└─ Agent 003 (user@macos)

Click on agent → Opens File Manager
```

### Step 3: Upload File
```
1. Click file picker
2. Select file from computer
3. Click "Upload"
4. Status: "✅ Uploaded: filename.ext (1234 bytes)"
```

### Step 4: List Files
```
1. Enter path (e.g., /tmp or C:\Users\Admin\Documents)
2. Click "List"
3. Status: "File listing command queued"
4. Check agent beacons for output
```

### Step 5: Download File
```
1. Find file in list
2. Click "Download" button
3. File downloads or command queued
```

### Step 6: Delete File
```
1. Find file in list
2. Click "Delete" button
3. Confirm deletion
4. Status: "✅ Delete command queued for: filename.ext"
```

---

## 🔐 Security Features

✅ **Encryption**
- All commands encrypted with agent's cipher
- Files encrypted in transit

✅ **Authentication**
- Requires bearer token
- Agent must be connected

✅ **Validation**
- Agent connection check
- Path traversal prevention (future)

---

## 📊 Architecture

```
Frontend (React/TypeScript)
  ↓
  Files.tsx (Agent selector)
  FileManager.tsx (File operations UI)
  ↓
API Routes (/api/files/*)
  ↓
Agent Manager (Connection tracking)
  ↓
Encrypted Command Queue
  ↓
Agent (Executes shell commands)
  ↓
Response (in encrypted beacon)
```

---

## 🧪 Testing

### Test with cURL

```bash
# List files
curl -H "Authorization: Bearer token" \
  "http://localhost:8000/api/files/list/agent-001?path=/tmp"

# Upload
curl -H "Authorization: Bearer token" \
  -F "file=@/path/to/file" \
  "http://localhost:8000/api/files/upload/agent-001"

# Delete
curl -X DELETE -H "Authorization: Bearer token" \
  "http://localhost:8000/api/files/delete/agent-001/filename"
```

---

## ✅ Complete Routes

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/files` | Show agents list |
| GET | `/files/{agentId}` | Open file manager for agent |
| POST | `/api/files/upload/{agent_id}` | Upload file |
| GET | `/api/files/download/{agent_id}/{filename}` | Download file |
| GET | `/api/files/list/{agent_id}` | List directory |
| DELETE | `/api/files/delete/{agent_id}/{filename}` | Delete file |
| GET | `/api/files/info/{agent_id}` | Get fs info |

---

## 📝 Notes

- File operations are **asynchronous** - commands queued to agent
- Results appear in **agent beacons**
- Default directory: `/tmp` (configurable)
- File storage: `/tmp/securepulse_files` (server-side)

---

**Status:** ✅ Production Ready
**Last Updated:** 2026-11-06
**Version:** 1.0.0
