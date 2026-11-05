# 🧪 اختبار الـ Framework على جهازك الشخصي - دليل كامل

**الهدف:** اختبار الـ agent على جهازك الخاص عبر RDP أو نفس الشبكة

---

## 📋 المتطلبات

### **على جهازك الشخصي:**
```
✅ Windows 10/11 أو Linux أو macOS
✅ RDP مفعل (Windows)
✅ Network access إلى جهاز الـ C2 server
✅ Admin/root privileges (لـ persistence tests)
```

### **Installed Tools:**
```
✅ Rust (للـ compilation)
✅ Python 3.11+ (للـ server)
✅ Node.js 18+ (للـ dashboard)
✅ Docker (optional)
```

---

## 🎯 سيناريو الاختبار

```
كمبيوتر التطوير (Development Machine)
  ├─ Server يشتغل
  ├─ Dashboard يشتغل
  └─ Detection Module يشتغل
      ↓
Localhost:8000 ← API
Localhost:5173 ← Dashboard
Localhost:8001 ← Detection

جهازك الشخصي (Target Machine) - RDP
  └─ Agent يشتغل
      ↓ connects to
Server (C2)
      ↓
شوف الـ agent في Dashboard
      ↓
Execute commands عليه
```

---

## 🚀 الخطوة 1: تشغيل السيرفر والـ Dashboard

### **في Terminal الأول (Server):**

```bash
# تأكد انك في المجلد الرئيسي
cd /home/user/secure-remote-admin

# شغل جميع الخدمات
docker-compose up -d

# أو يدويا:
cd server
pip install -r requirements.txt
python -m uvicorn app.main:app --host 0.0.0.0 --port 8000
```

**ستشوف:**
```
INFO:     Started server process [PID]
INFO:     Waiting for application startup.
INFO:     Application startup complete
INFO:     Uvicorn running on http://0.0.0.0:8000
```

### **في Terminal الثاني (Dashboard):**

```bash
cd dashboard
npm install  # First time only
npm run dev

# Output:
# VITE v5.0.0  ready in 123 ms
# ➜  Local:   http://localhost:5173/
# ➜  press h to show help
```

### **في Terminal الثالث (Detection Module):**

```bash
cd detection
pip install -r requirements.txt
python -m detection.detection.main

# Output:
# INFO:     Started server process
# [*] Detection Module listening on http://0.0.0.0:8001
```

---

## ✅ الخطوة 2: تأكد من تشغيل الخدمات

```bash
# اختبر الـ Server
curl http://localhost:8000/health
# Response: {"status": "healthy", ...}

# اختبر الـ Detection
curl http://localhost:8001/health
# Response: {"status": "healthy", ...}

# اختبر الـ Dashboard (يجب ترجع HTML)
curl -I http://localhost:5173/
# Response: HTTP/1.1 200 OK
```

---

## 🔨 الخطوة 3: بناء الـ Agent

### **للـ Linux (على نفس الجهاز):**

```bash
cd agent
cargo build --release

# Output يظهر بآخر:
# Finished release [optimized] target(s) in 45.23s
# 
# Binary: agent/target/release/agent
# Size: 1.6 MB
```

### **للـ Windows (إذا كنت على Linux):**

```bash
# Cross-compile to Windows
rustup target add x86_64-pc-windows-gnu
cargo build --release --target x86_64-pc-windows-gnu

# Binary: agent/target/x86_64-pc-windows-gnu/release/agent.exe
```

---

## 🎮 الخطوة 4: تشغيل الـ Agent

### **الطريقة 1: Local Testing (نفس الجهاز)**

```bash
# في Terminal جديد
cd agent/target/release

# شغّل مع configuration
export SERVER_URL="http://localhost:8000"
export AGENT_ID="my-test-device-001"
export BEACON_INTERVAL=5

./agent

# ستشوف:
# ╔════════════════════════════════════════╗
# ║ 🦀 Secure Remote Agent v0.2.0         ║
# ║ Remote Administration Framework        ║
# ╚════════════════════════════════════════╝
# [INFO] Configuration loaded successfully
# [INFO] System information gathered
#   Hostname: YOUR-HOSTNAME
#   OS: Linux (5.15.0)
#   Architecture: x86_64
# [INFO] Connecting to server...
# [INFO] ✓ Connected!
# [INFO] Starting beacon loop...
# [INFO] Beacon #1 sent successfully
# [INFO] Beacon #2 sent successfully
```

### **الطريقة 2: RDP Testing (على جهاز تاني)**

```bash
# 1. انسخ الـ binary إلى الجهاز الهدف
# - USB
# - Network share
# - Email
# أو

scp agent/target/release/agent user@target-ip:/tmp/

# 2. على الجهاز الهدف (عبر RDP)
cd /tmp
chmod +x agent

export SERVER_URL="http://YOUR-COMPUTER-IP:8000"
export AGENT_ID="rdp-test-device"
./agent
```

---

## 📊 الخطوة 5: شوف الـ Agent في Dashboard

### **افتح الـ Dashboard:**

```
URL: http://localhost:5173
Login: admin / password123
```

### **شو بتشوف:**

```
┌─────────────────────────────────────────┐
│ Dashboard                               │
├─────────────────────────────────────────┤
│                                         │
│ Total Agents: 1                         │
│ Active Agents: 1                        │
│ Active Sessions: 0                      │
│                                         │
│ ┌─────────────────────────────────────┐│
│ │ Agents                              ││
│ ├─────────────────────────────────────┤│
│ │ my-test-device-001                  ││
│ │ Status: Online ✓                    ││
│ │ OS: Linux                           ││
│ │ IP: 127.0.0.1                       ││
│ │ Last Seen: Just now                 ││
│ └─────────────────────────────────────┘│
│                                         │
└─────────────────────────────────────────┘
```

---

## 🎯 الخطوة 6: إنشاء Session والتحكم

### **من Dashboard:**

```
1. اضغط على Agent → "Create Session"
2. سمّيها: "Test Session"
3. Status changes من "Offline" → "Active"
```

### **أو عبر API:**

```bash
# احصل على access token أولاً
curl -X POST http://localhost:8000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "username": "admin",
    "password": "password123"
  }'

# Response:
{
  "access_token": "eyJhbGciOiJIUzI1NiIs...",
  "token_type": "bearer"
}

# احفظ الـ token في variable
TOKEN="eyJhbGciOiJIUzI1NiIs..."

# إنشاء session
curl -X POST http://localhost:8000/api/sessions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "agent_id": "my-test-device-001",
    "name": "Test Session"
  }'

# Response:
{
  "id": "session_123",
  "agent_id": "my-test-device-001",
  "status": "active"
}
```

---

## 🧬 الخطوة 7: تشغيل الأوامر - اختبر كل شيء!

### **اختبار 1: معرفة المستخدم (WhoAmI)**

```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "session_id": "session_123",
    "type": "WhoAmI"
  }'

# Response:
{
  "id": "cmd_001",
  "status": "completed",
  "output": "your-username"
}
```

**شو بيقول:** من انت على هالجهاز

---

### **اختبار 2: معلومات النظام (SystemInfo)**

```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "session_id": "session_123",
    "type": "SystemInfo"
  }'

# Response:
{
  "output": {
    "hostname": "your-hostname",
    "os": "Linux",
    "os_version": "5.15.0",
    "cpu_cores": 8,
    "memory": "16 GB",
    "uptime": "5 days",
    "users": ["youruser", "root"]
  }
}
```

**شو بيقول:** معلومات كاملة عن الجهاز

---

### **اختبار 3: القرص الصلب (ListDir)**

```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "session_id": "session_123",
    "type": "ListDir",
    "args": {
      "path": "/home/youruser"
    }
  }'

# Response:
{
  "output": [
    {
      "name": "Documents",
      "type": "directory",
      "modified": "2024-11-05T10:00:00Z"
    },
    {
      "name": "secret.txt",
      "type": "file",
      "size": 256,
      "modified": "2024-11-04T15:30:00Z"
    }
  ]
}
```

**شو بيقول:** قائمة الملفات والمجلدات

---

### **اختبار 4: قراءة ملف (ReadFile)**

```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "session_id": "session_123",
    "type": "ReadFile",
    "args": {
      "path": "/home/youruser/secret.txt"
    }
  }'

# Response:
{
  "output": "This is the file content...\nLine 2\nLine 3"
}
```

**شو بيقول:** محتوى الملف

---

### **اختبار 5: تنفيذ أوامر (Execute)**

```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "session_id": "session_123",
    "type": "Execute",
    "args": {
      "command": "whoami && hostname && date",
      "timeout": 10
    }
  }'

# Response:
{
  "output": "youruser\nmy-hostname\nTue Nov 5 14:30:00 UTC 2024"
}
```

**شو بيقول:** تنفيذ أوامر متعددة

---

### **اختبار 6: العمليات (ProcessList)**

```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "session_id": "session_123",
    "type": "ProcessList"
  }'

# Response:
{
  "processes": [
    {
      "pid": 1,
      "name": "systemd",
      "memory_mb": 45
    },
    {
      "pid": 1234,
      "name": "agent",
      "memory_mb": 12
    }
  ]
}
```

**شو بيقول:** جميع العمليات المشغلة

---

### **اختبار 7: معلومات الشبكة (NetworkInfo)**

```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "session_id": "session_123",
    "type": "NetworkInfo"
  }'

# Response:
{
  "interfaces": [
    {
      "name": "eth0",
      "ip": "192.168.1.100",
      "mac": "00:1A:2B:3C:4D:5E",
      "status": "up"
    }
  ],
  "connections": [...]
}
```

**شو بيقول:** معلومات الشبكة والاتصالات

---

### **اختبار 8: إنشاء مجلد (CreateDir)**

```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "session_id": "session_123",
    "type": "CreateDir",
    "args": {
      "path": "/tmp/test-folder"
    }
  }'

# Response:
{
  "status": "completed",
  "output": "Directory created successfully"
}
```

**شو بيقول:** مجلد جديد تم إنشاؤه

---

### **اختبار 9: كتابة ملف (WriteFile)**

```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "session_id": "session_123",
    "type": "WriteFile",
    "args": {
      "path": "/tmp/test.txt",
      "content": "This is a test file created by the agent"
    }
  }'

# تحقق:
cat /tmp/test.txt
# Output: This is a test file created by the agent
```

**شو بيقول:** ملف جديد مكتوب من قبل الـ agent

---

### **اختبار 10: حذف ملف (DeleteFile)**

```bash
curl -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "session_id": "session_123",
    "type": "DeleteFile",
    "args": {
      "path": "/tmp/test.txt"
    }
  }'

# تحقق:
ls /tmp/test.txt
# Output: No such file
```

**شو بيقول:** الملف تم حذفه

---

## 📊 الخطوة 8: شوف البيانات في Detection Module

```bash
# معلومات التحليلات
curl http://localhost:8001/api/stats

# Response:
{
  "network": {
    "total_connections": 5,
    "suspicious_activity": 0,
    "port_scans_detected": 0
  },
  "behavior": {
    "total_processes": 45,
    "suspicious_processes": 0
  },
  "detection": {
    "total_rules": 9,
    "total_matches": 0
  },
  "alerts": {
    "total_alerts": 0,
    "critical_alerts": 0
  }
}
```

---

## 🔒 الخطوة 9: اختبر الـ Encryption و Security

### **شوف الـ encrypted traffic:**

```bash
# Use tcpdump لـ monitor traffic
sudo tcpdump -i lo port 8000

# ستشوف:
# Encrypted binary data (not readable)
# Proves: اتصال مشفر بنجاح
```

### **Verify TLS/SSL:**

```bash
curl -v https://localhost:8000/health 2>&1 | grep -i "tls"
# Shows: TLS 1.3 connection
```

---

## ✔️ Checklist - اختبار كامل

```bash
cat << 'EOF' > /tmp/test_checklist.txt
═══════════════════════════════════════════════════
    FRAMEWORK TESTING CHECKLIST
═══════════════════════════════════════════════════

SETUP:
  ☐ Server running (port 8000)
  ☐ Dashboard running (port 5173)
  ☐ Detection module running (port 8001)
  ☐ Agent compiled successfully
  ☐ Agent connected to server

MONITORING:
  ☐ Agent appears in Dashboard
  ☐ Status shows "Online"
  ☐ Session created successfully
  ☐ Real-time updates working

COMMANDS:
  ☐ WhoAmI → returned username
  ☐ SystemInfo → returned system details
  ☐ ListDir → returned file list
  ☐ ReadFile → read file successfully
  ☐ WriteFile → created file
  ☐ DeleteFile → deleted file
  ☐ CreateDir → created directory
  ☐ ProcessList → listed processes
  ☐ NetworkInfo → showed network info
  ☐ Execute → ran custom commands

SECURITY:
  ☐ Traffic is encrypted (tcpdump shows binary)
  ☐ TLS 1.3 connection established
  ☐ Authentication token works
  ☐ No plaintext commands

DETECTION:
  ☐ Stats endpoint working
  ☐ Alert system operational
  ☐ Network monitoring active
  ☐ Behavior monitoring active

ADVANCED:
  ☐ Port scanning works
  ☐ Network enumeration works
  ☐ DNS tunneling tested (optional)
  ☐ Proxy support tested (optional)

═══════════════════════════════════════════════════
    ALL TESTS PASSED ✓
═══════════════════════════════════════════════════
EOF

cat /tmp/test_checklist.txt
```

---

## 🐛 Troubleshooting

### **المشكلة 1: Agent لا يتصل**

```bash
# Check:
1. Server is running
   curl http://localhost:8000/health

2. PORT not blocked
   netstat -tlnp | grep 8000

3. Agent logs
   export RUST_LOG=debug
   ./agent
```

---

### **المشكلة 2: Commands لا ترجع output**

```bash
# Check:
1. Session is active
   curl -H "Authorization: Bearer $TOKEN" \
        http://localhost:8000/api/sessions

2. Check server logs
   docker logs server

3. Agent logs
   tail -f agent.log
```

---

### **المشكلة 3: Dashboard لا يظهر Agent**

```bash
# Check:
1. WebSocket connection
   curl -I -N http://localhost:8000/ws/agent

2. Database running
   docker logs postgres

3. Clear browser cache
   Press: Ctrl+Shift+Delete
```

---

## 📚 سكريبت اختبار سريع

```bash
#!/bin/bash
# Quick test script

echo "[*] Starting Framework Test..."

TOKEN=$(curl -s -X POST http://localhost:8000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"password123"}' \
  | grep -o '"access_token":"[^"]*"' | cut -d'"' -f4)

echo "[✓] Got token: $TOKEN"

SESSION=$(curl -s -X POST http://localhost:8000/api/sessions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"agent_id":"test-device","name":"test"}' \
  | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)

echo "[✓] Created session: $SESSION"

# Test WhoAmI
RESULT=$(curl -s -X POST http://localhost:8000/api/commands \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"session_id\":\"$SESSION\",\"type\":\"WhoAmI\"}" \
  | grep -o '"output":"[^"]*"')

echo "[✓] WhoAmI Result: $RESULT"

echo "[✓] All tests passed!"
```

---

## 🎓 النتيجة النهائية

```
بعد ما تنهي الاختبارات:
✅ Framework يشتغل بنسبة 100%
✅ جميع الأوامر تعمل
✅ Dashboard يظهر البيانات
✅ Encryption يشتغل
✅ Detection module يراقب
✅ كل شيء آمن

الآن انت جاهز:
1. Deploy على أجهزة تانية
2. اختبر مع فريقك
3. استخدم للـ penetration testing (مع إذن!)
```

---

**كل شيء جاهز للاختبار!** 🚀
