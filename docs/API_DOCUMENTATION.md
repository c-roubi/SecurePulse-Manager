# 📚 API Documentation

**Version:** 0.1.0  
**Last Updated:** 2024-11-05  
**Base URLs:**
- Server: `http://localhost:8000`
- Detection: `http://localhost:8001`
- Dashboard: `http://localhost:5173`

---

## 🔐 Authentication

### Token-Based Authentication

All protected endpoints require a valid JWT token in the `Authorization` header:

```
Authorization: Bearer <access_token>
```

### Login
```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "password123"
}
```

**Response:**
```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIs...",
  "token_type": "bearer",
  "expires_in": 3600
}
```

### Register
```http
POST /api/auth/register
Content-Type: application/json

{
  "username": "newuser",
  "email": "user@example.com",
  "password": "SecurePassword123!"
}
```

### Refresh Token
```http
POST /api/auth/refresh
Authorization: Bearer <refresh_token>
```

---

## 🏥 Health & Status

### Server Health
```http
GET /health
```

**Response (200):**
```json
{
  "status": "healthy",
  "version": "0.1.0",
  "timestamp": "2024-11-05T14:30:00Z"
}
```

### Detection Module Health
```http
GET http://localhost:8001/health
```

---

## 🤖 Agent Management

### Create Agent Session
```http
POST /api/sessions
Authorization: Bearer <token>
Content-Type: application/json

{
  "agent_id": "agent_abc123",
  "name": "Windows Target Server"
}
```

**Response (200):**
```json
{
  "id": "session_123",
  "agent_id": "agent_abc123",
  "name": "Windows Target Server",
  "status": "active",
  "created_at": "2024-11-05T14:30:00Z",
  "last_activity": "2024-11-05T14:30:00Z"
}
```

### List Sessions
```http
GET /api/sessions
Authorization: Bearer <token>
```

**Query Parameters:**
- `status`: Filter by status (active, inactive, closed)
- `limit`: Max results (default: 100)
- `offset`: Pagination offset

**Response (200):**
```json
{
  "sessions": [
    {
      "id": "session_123",
      "agent_id": "agent_abc123",
      "status": "active",
      "command_count": 5,
      "created_at": "2024-11-05T14:30:00Z"
    }
  ],
  "total": 1
}
```

### Get Session Details
```http
GET /api/sessions/{session_id}
Authorization: Bearer <token>
```

### Update Session
```http
PATCH /api/sessions/{session_id}
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Updated Name",
  "status": "inactive"
}
```

### Close Session
```http
POST /api/sessions/{session_id}/close
Authorization: Bearer <token>
```

---

## 📋 Command Execution

### Execute Command
```http
POST /api/commands
Authorization: Bearer <token>
Content-Type: application/json

{
  "session_id": "session_123",
  "type": "Execute",
  "args": {
    "command": "whoami",
    "timeout": 30
  }
}
```

**Supported Command Types:**
- `Execute` - Run shell command
- `Shell` - Interactive shell
- `WhoAmI` - Current user
- `Pwd` - Current directory
- `ListDir` - List directory contents
- `ReadFile` - Read file contents
- `WriteFile` - Write to file
- `DeleteFile` - Delete file
- `CreateDir` - Create directory
- `SystemInfo` - System information
- `ProcessList` - List running processes
- `KillProcess` - Terminate process
- `NetworkInfo` - Network information
- `Sleep` - Delay execution
- `Echo` - Echo message

**Response (200):**
```json
{
  "id": "cmd_456",
  "session_id": "session_123",
  "type": "Execute",
  "status": "pending",
  "created_at": "2024-11-05T14:30:00Z"
}
```

### Get Command Status
```http
GET /api/commands/{command_id}
Authorization: Bearer <token>
```

**Response (200):**
```json
{
  "id": "cmd_456",
  "session_id": "session_123",
  "type": "Execute",
  "status": "completed",
  "output": "admin\n",
  "error": null,
  "created_at": "2024-11-05T14:30:00Z",
  "completed_at": "2024-11-05T14:30:05Z"
}
```

### List Commands
```http
GET /api/commands
Authorization: Bearer <token>
```

**Query Parameters:**
- `session_id`: Filter by session
- `status`: Filter by status (pending, running, completed, failed)
- `limit`: Max results (default: 100)

---

## 🔍 Detection Module

### Get Detection Statistics
```http
GET http://localhost:8001/api/stats
```

**Response (200):**
```json
{
  "network": {
    "total_connections": 152,
    "suspicious_activity": 3,
    "port_scans_detected": 0,
    "c2_patterns": 1
  },
  "behavior": {
    "total_processes": 45,
    "suspicious_processes": 2,
    "total_alerts": 5
  },
  "detection": {
    "total_rules": 9,
    "total_matches": 8,
    "severity_breakdown": {
      "CRITICAL": 0,
      "HIGH": 2,
      "MEDIUM": 5,
      "LOW": 1
    }
  },
  "alerts": {
    "total_alerts": 5,
    "critical_alerts": 0,
    "open_alerts": 3
  }
}
```

### Detect Command Line
```http
POST http://localhost:8001/api/detect/cmdline
Content-Type: application/json

{
  "command_line": "powershell -NoProfile -ExecutionPolicy Bypass -Command Get-Content C:\\secrets.txt"
}
```

**Response (200):**
```json
{
  "matches": 2,
  "detections": [
    {
      "rule": "command_line_obfuscation",
      "type": "SIGMA",
      "severity": "HIGH"
    },
    {
      "rule": "suspicious_process",
      "type": "SIGMA",
      "severity": "HIGH"
    }
  ]
}
```

### Scan File
```http
POST http://localhost:8001/api/detect/file
Content-Type: application/json

{
  "filename": "malware.exe",
  "content": "<binary_content_as_base64>"
}
```

### Scan Event
```http
POST http://localhost:8001/api/detect/event
Content-Type: application/json

{
  "type": "process_creation",
  "image": "C:\\Windows\\System32\\powershell.exe",
  "command_line": "powershell -NoProfile",
  "parent_image": "C:\\Windows\\explorer.exe"
}
```

---

## 🚨 Alert Management

### Get Alerts
```http
GET http://localhost:8001/api/alerts
```

**Query Parameters:**
- `severity`: Filter by severity (LOW, MEDIUM, HIGH, CRITICAL)
- `status`: Filter by status (NEW, ACKNOWLEDGED, INVESTIGATING, RESOLVED)
- `limit`: Max results (default: 100)

**Response (200):**
```json
{
  "alerts": [
    {
      "id": "alert_0_1730755200",
      "type": "command_line_obfuscation",
      "severity": "HIGH",
      "title": "Detection: command_line_obfuscation",
      "description": "Command line matched detection rule",
      "source": "detection_engine",
      "status": "NEW",
      "timestamp": "2024-11-05T14:30:00Z",
      "indicators": ["Pattern: *base64*"]
    }
  ],
  "count": 1
}
```

### Acknowledge Alert
```http
POST http://localhost:8001/api/alerts/{alert_id}/acknowledge
Content-Type: application/json

{
  "acknowledged_by": "security_analyst"
}
```

### Add Note to Alert
```http
POST http://localhost:8001/api/alerts/{alert_id}/note
Content-Type: application/json

{
  "note": "Investigated - confirmed benign PowerShell script"
}
```

### Resolve Alert
```http
POST http://localhost:8001/api/alerts/{alert_id}/resolve
Content-Type: application/json

{
  "resolution": "False positive - developer testing"
}
```

---

## 📊 Dashboard Endpoints

### Dashboard Statistics
```http
GET /api/dashboard/stats
Authorization: Bearer <token>
```

**Response (200):**
```json
{
  "total_agents": 5,
  "active_agents": 3,
  "active_sessions": 2,
  "total_commands": 42,
  "completed_commands": 40,
  "success_rate": 95.2
}
```

### List Agents
```http
GET /api/agents
Authorization: Bearer <token>
```

---

## 🔌 WebSocket Endpoints

### Agent Communication
```
ws://localhost:8000/ws/agent
```

**Message Format (Agent Beacon):**
```json
{
  "type": "beacon",
  "agent_id": "agent_abc123",
  "timestamp": "2024-11-05T14:30:00Z",
  "status": "online",
  "os": "Linux",
  "hostname": "target-server"
}
```

### Real-time Detections
```
ws://localhost:8001/ws/detections
```

### Alert Streaming
```
ws://localhost:8001/ws/alerts
```

**Message Format:**
```json
{
  "alerts": [
    {
      "id": "alert_0_1730755200",
      "type": "command_line_obfuscation",
      "severity": "HIGH",
      "status": "NEW"
    }
  ],
  "timestamp": "2024-11-05T14:30:00Z"
}
```

---

## ⚠️ Error Responses

### Authentication Error (401)
```json
{
  "detail": "Invalid credentials"
}
```

### Authorization Error (403)
```json
{
  "detail": "Insufficient permissions"
}
```

### Not Found (404)
```json
{
  "detail": "Resource not found"
}
```

### Server Error (500)
```json
{
  "detail": "Internal server error",
  "timestamp": "2024-11-05T14:30:00Z"
}
```

---

## 📋 System Rules

### Get Rules Information
```http
GET http://localhost:8001/api/system/rules
```

**Response (200):**
```json
{
  "total_rules": 9,
  "rule_types": {
    "yara": 3,
    "sigma": 3,
    "custom": 3
  },
  "yara_rules": 3,
  "sigma_rules": 3,
  "custom_rules": 3
}
```

### Get System Configuration
```http
GET http://localhost:8001/api/system/config
```

**Response (200):**
```json
{
  "version": "0.1.0",
  "enabled_modules": {
    "network_analyzer": true,
    "dns_analyzer": true,
    "behavior_monitor": true,
    "detection_engine": true,
    "alert_system": true
  },
  "alert_thresholds": {
    "critical": 1,
    "high": 5,
    "medium": 20,
    "low": 100
  }
}
```

---

## 🔒 Security Best Practices

1. **Always use HTTPS** in production
2. **Rotate tokens** regularly
3. **Use strong passwords** (min 12 characters)
4. **Enable CORS** only for trusted origins
5. **Monitor audit logs** for suspicious activity
6. **Keep TLS 1.3** as minimum
7. **Implement rate limiting** on endpoints
8. **Validate all inputs** server-side

---

## 📖 Testing

Run integration tests:
```bash
cd tests
pip install -r requirements.txt
pytest integration/test_system_integration.py -v
```

Run security tests:
```bash
pytest security/test_security.py -v
```

Run load tests:
```bash
locust -f load/load_test.py --host=http://localhost:8000
```

---

## 📞 Support

For issues or questions:
1. Check `/QUICKSTART.md`
2. Review endpoint-specific documentation
3. Check server logs: `docker logs secure-remote-admin-server-1`
4. Check detection logs: `docker logs secure-remote-admin-detection-1`
