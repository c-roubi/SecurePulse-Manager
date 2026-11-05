# 📋 خطة العمل التفصيلية والجدول الزمني

---

## المرحلة الأولى: الإعداد والتخطيط (الأسبوع 1-2)

### المهام:

#### 1.1 إعداد المستودع والبيئة
```
المدة: 3 أيام
الأولويات:
- [ ] إنشاء مستودع GitHub مع .gitignore
- [ ] إنشاء branches رئيسية (main, develop)
- [ ] إعداد ملف CONTRIBUTING.md
- [ ] إعداد ملف CODE_OF_CONDUCT.md

الملفات المطلوبة:
├── .gitignore          # استبعاد الملفات الحساسة
├── .env.example        # نموذج المتغيرات البيئية
├── docker-compose.yml  # تشغيل البيئة المحلية
└── README.md           # دليل المشروع

الأوامر:
git init
git branch develop
git branch agent-dev
git branch server-dev
git branch dashboard-dev
git branch detection-dev
```

#### 1.2 تصميم قاعدة البيانات
```
المدة: 4 أيام
الجداول المطلوبة:

1. Users Table
   - id (PK)
   - username (UNIQUE)
   - password_hash
   - email
   - role (ADMIN/OPERATOR)
   - created_at
   - last_login

2. Agents Table
   - id (PK)
   - agent_id (UNIQUE)
   - hostname
   - os_type
   - os_version
   - architecture
   - ip_address
   - mac_address
   - user
   - privileges
   - first_seen
   - last_seen
   - status (ACTIVE/INACTIVE)

3. Sessions Table
   - id (PK)
   - agent_id (FK)
   - user_id (FK)
   - session_token
   - started_at
   - ended_at
   - status (ACTIVE/CLOSED)
   - command_count

4. Commands Table
   - id (PK)
   - session_id (FK)
   - command_text
   - status (QUEUED/EXECUTING/COMPLETED/FAILED)
   - issued_at
   - executed_at
   - completed_at
   - output
   - error_message

5. Logs Table
   - id (PK)
   - agent_id (FK)
   - log_type (COMMAND/ACTIVITY/ERROR)
   - message
   - timestamp
   - level (INFO/WARNING/ERROR/DEBUG)

6. Detection_Events Table
   - id (PK)
   - agent_id (FK)
   - event_type
   - description
   - severity (LOW/MEDIUM/HIGH/CRITICAL)
   - detected_at
   - rule_matched
   - action_taken

ملف: database/schema.sql
```

#### 1.3 كتابة وثائق API
```
المدة: 3 أيام
الأدوات: OpenAPI/Swagger

الـ Endpoints الرئيسية:

Authentication:
POST   /api/auth/login
POST   /api/auth/refresh
POST   /api/auth/logout

Sessions:
GET    /api/sessions              - قائمة الجلسات
GET    /api/sessions/{id}         - تفاصيل جلسة
POST   /api/sessions/{id}/connect - الاتصال بالعميل
DELETE /api/sessions/{id}         - إغلاق جلسة

Commands:
POST   /api/sessions/{id}/commands      - إرسال أمر
GET    /api/sessions/{id}/commands      - سجل الأوامر
GET    /api/commands/{cmd_id}           - تفاصيل الأمر
GET    /api/commands/{cmd_id}/output    - مخرجات الأمر

Agents:
GET    /api/agents              - قائمة العملاء
GET    /api/agents/{id}         - معلومات العميل
GET    /api/agents/{id}/status  - حالة العميل

Monitoring:
GET    /api/monitoring/dashboard        - بيانات لوحة المراقبة
GET    /api/monitoring/events           - الأحداث
GET    /api/monitoring/statistics       - الإحصائيات

WebSocket:
WS     /ws/sessions/{id}/stream - بث البيانات المباشر
WS     /ws/monitoring           - مراقبة مباشرة

ملف: docs/api.openapi.yaml
```

#### 1.4 إعداد Docker والبيئة
```
المدة: 3 أيام

docker-compose.yml:
- PostgreSQL 15
- Redis 7 (optional for caching)
- Python FastAPI service
- Node.js React service
- Python Detection service

Dockerfiles:
├── agent/Dockerfile
├── server/Dockerfile
├── dashboard/Dockerfile
└── detection/Dockerfile

.env variables:
DATABASE_URL=postgresql://user:pass@postgres:5432/c2db
JWT_SECRET=<random-key>
AGENT_TOKEN=<pre-shared-key>
REDIS_URL=redis://redis:6379
DEBUG=False
```

#### 1.5 إعداد CI/CD Pipeline
```
المدة: 2 أيام

GitHub Actions:
├── Test Pipeline
│   ├── Unit Tests (pytest + cargo test)
│   ├── Integration Tests
│   └── Code Coverage Report
├── Security Checks
│   ├── Dependency Scanning
│   ├── SAST Analysis
│   └── Secret Detection
└── Build Pipeline
    ├── Docker Image Build
    ├── Image Registry Push
    └── Deployment (if applicable)

ملف: .github/workflows/ci.yml
```

---

## المرحلة الثانية: تطوير العميل (الأسبوع 3-4)

### 2.1 مشروع Rust الأساسي
```
المدة: 2 أيام

cargo new secure-agent
cd secure-agent

Cargo.toml:
[dependencies]
tokio = { version = "1.35", features = ["full"] }
rustls = "0.21"
tungstenite = "0.20"
tokio-tungstenite = "0.21"
serde = { version = "1.0", features = ["derive"] }
serde_json = "1.0"
base64 = "0.21"
aes-gcm = "0.10"
hmac = "0.12"
sha2 = "0.10"
clap = { version = "4.4", features = ["derive"] }
chrono = "0.4"
uuid = { version = "1.6", features = ["v4"] }
log = "0.4"
env_logger = "0.11"
thiserror = "1.0"

هيكل المشروع:
secure-agent/
├── src/
│   ├── main.rs
│   ├── config.rs
│   ├── communicator/
│   │   ├── mod.rs
│   │   ├── tls.rs
│   │   └── websocket.rs
│   ├── executor/
│   │   ├── mod.rs
│   │   ├── commands.rs
│   │   └── process.rs
│   ├── recon/
│   │   ├── mod.rs
│   │   ├── system.rs
│   │   └── network.rs
│   ├── crypto/
│   │   ├── mod.rs
│   │   ├── encrypt.rs
│   │   └── hash.rs
│   └── lib.rs
└── Cargo.toml
```

### 2.2 وحدة الاتصالات (TLS + WebSocket)
```
المدة: 5 أيام

العناصر الرئيسية:

1. TLS Configuration:
   - Load client certificate & key
   - Custom certificate validation
   - Cipher suite configuration
   - Session resumption

ملف: src/communicator/tls.rs
```rust
use rustls::{ClientConfig, OwnedCertificate, PrivateKey};
use std::sync::Arc;

pub struct TlsConfig {
    config: Arc<ClientConfig>,
    cert: OwnedCertificate,
    key: PrivateKey,
}

impl TlsConfig {
    pub fn new(cert_path: &str, key_path: &str) -> Result<Self, Box<dyn std::error::Error>> {
        let cert = load_certificate(cert_path)?;
        let key = load_private_key(key_path)?;
        let config = create_client_config(&cert, &key)?;
        Ok(Self { config: Arc::new(config), cert, key })
    }

    pub fn get_config(&self) -> Arc<ClientConfig> {
        self.config.clone()
    }
}
```

2. WebSocket Handler:
   - Connection management
   - Heartbeat mechanism
   - Automatic reconnection
   - Message queuing

ملف: src/communicator/websocket.rs
```rust
use tokio_tungstenite::{connect_async, tungstenite::Message};
use tokio::sync::mpsc;

pub struct WebSocketClient {
    url: String,
    tx: mpsc::Sender<Message>,
    rx: mpsc::Receiver<Message>,
}

impl WebSocketClient {
    pub async fn connect(url: &str) -> Result<Self> {
        let (ws_stream, _) = connect_async(url).await?;
        let (write, read) = ws_stream.split();
        let (tx, rx) = mpsc::channel(100);
        
        // Spawn reader task
        tokio::spawn(async move {
            // Read messages
        });
        
        Ok(Self { url: url.to_string(), tx, rx })
    }

    pub async fn send_command(&mut self, cmd: &str) -> Result<()> {
        let msg = Message::Text(cmd.to_string());
        self.tx.send(msg).await?;
        Ok(())
    }

    pub async fn recv_response(&mut self) -> Option<Message> {
        self.rx.recv().await
    }
}
```

3. Message Encryption/Decryption:
   - AES-256-GCM encryption
   - HMAC authentication
   - IV generation

ملف: src/crypto/encrypt.rs
```rust
use aes_gcm::{Aes256Gcm, Key, Nonce};
use rand::Rng;

pub struct CryptoManager {
    cipher: Aes256Gcm,
    key: Key<Aes256Gcm>,
}

impl CryptoManager {
    pub fn new(key: &[u8; 32]) -> Self {
        let key = Key::<Aes256Gcm>::from(*key);
        let cipher = Aes256Gcm::new(&key);
        Self { cipher, key }
    }

    pub fn encrypt(&self, plaintext: &[u8]) -> Result<Vec<u8>, String> {
        let nonce = Nonce::from_slice(b"unique nonce");
        let ciphertext = self.cipher
            .encrypt(nonce, plaintext)
            .map_err(|e| e.to_string())?;
        Ok(ciphertext)
    }

    pub fn decrypt(&self, ciphertext: &[u8]) -> Result<Vec<u8>, String> {
        let nonce = Nonce::from_slice(b"unique nonce");
        let plaintext = self.cipher
            .decrypt(nonce, ciphertext)
            .map_err(|e| e.to_string())?;
        Ok(plaintext)
    }
}
```
```

### 2.3 وحدة جمع المعلومات (System Fingerprinting)
```
المدة: 3 أيام

العناصر المطلوبة:
- OS info (name, version, arch)
- Hostname and domain
- IP addresses (local & external)
- MAC addresses
- User information
- Process list
- Installed software
- Network interfaces
- Disk information

ملف: src/recon/system.rs
```rust
use sysinfo::{System, SystemExt, ProcessExt, NetworkExt};
use std::net::IpAddr;

pub struct SystemInfo {
    pub hostname: String,
    pub os: String,
    pub os_version: String,
    pub architecture: String,
    pub users: Vec<String>,
    pub processes: Vec<ProcessInfo>,
    pub network_interfaces: Vec<NetworkInterface>,
    pub disk_info: Vec<DiskInfo>,
}

impl SystemInfo {
    pub fn gather() -> Result<Self, Box<dyn std::error::Error>> {
        let mut sys = System::new_all();
        sys.refresh_all();

        Ok(Self {
            hostname: gethostname::gethostname().to_string_lossy().to_string(),
            os: System::name().unwrap_or_default(),
            os_version: System::os_version().unwrap_or_default(),
            architecture: std::env::consts::ARCH.to_string(),
            users: gather_users()?,
            processes: gather_processes(&sys)?,
            network_interfaces: gather_network_info(&sys)?,
            disk_info: gather_disk_info(&sys)?,
        })
    }
}
```

### 2.4 وحدة تنفيذ الأوامر
```
المدة: 3 أيام

الأوامر المدعومة:
- Basic: whoami, pwd, ls/dir, cd, cat, echo
- Process: ps, tasklist, kill, taskkill
- Network: ipconfig, ifconfig, netstat, ping
- File: copy, move, delete, mkdir, chmod
- Registry (Windows): reg query, reg add, reg delete
- System: systeminfo, date, uname, lsb_release

ملف: src/executor/commands.rs
```rust
pub enum Command {
    Execute { program: String, args: Vec<String> },
    GetProcessList,
    KillProcess { pid: u32 },
    GetFileList { path: String },
    ReadFile { path: String },
    WriteFile { path: String, content: String },
    DeleteFile { path: String },
    GetSystemInfo,
    GetNetworkInfo,
}

pub async fn execute_command(cmd: Command) -> Result<String> {
    match cmd {
        Command::Execute { program, args } => {
            let output = tokio::process::Command::new(program)
                .args(args)
                .output()
                .await?;
            Ok(String::from_utf8_lossy(&output.stdout).to_string())
        },
        Command::GetProcessList => {
            // Use psutil equivalent or /proc
            Ok(get_processes()?)
        },
        // ... other commands
    }
}
```

### 2.5 الحلقة الرئيسية (Main Event Loop)
```
المدة: 2 أيام

ملف: src/main.rs
```rust
#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    env_logger::init();
    
    let config = Config::load("config.json")?;
    
    // Initialize crypto
    let crypto = CryptoManager::new(&config.encryption_key);
    
    // Connect to C2 server
    let mut client = WebSocketClient::connect(&config.c2_url).await?;
    
    // Gather system info
    let sys_info = SystemInfo::gather()?;
    
    // Register agent
    client.register_agent(&sys_info).await?;
    
    // Main loop
    loop {
        // Send heartbeat
        client.heartbeat().await?;
        
        // Check for commands
        if let Some(cmd) = client.recv_command().await {
            let result = execute_command(cmd).await;
            client.send_result(result).await?;
        }
        
        // Sleep
        tokio::time::sleep(Duration::from_secs(config.beacon_interval)).await;
    }
}
```

### 2.6 الاختبارات (Unit Tests)
```
المدة: 1 يوم

ملف: src/tests/mod.rs
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encryption_decryption() {
        let key = [0u8; 32];
        let crypto = CryptoManager::new(&key);
        let plaintext = b"test message";
        let encrypted = crypto.encrypt(plaintext).unwrap();
        let decrypted = crypto.decrypt(&encrypted).unwrap();
        assert_eq!(plaintext, &decrypted[..]);
    }

    #[test]
    fn test_command_parsing() {
        let cmd_str = "execute|whoami";
        let cmd = parse_command(cmd_str).unwrap();
        assert!(matches!(cmd, Command::Execute { .. }));
    }
}
```

---

## المرحلة الثالثة: تطوير الخادم (الأسبوع 5-6)

### 3.1 إنشاء تطبيق FastAPI
```
المدة: 2 أيام

ملف: server/app/main.py
```python
from fastapi import FastAPI, WebSocket, Depends, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from sqlalchemy.ext.asyncio import create_async_engine, AsyncSession
from contextlib import asynccontextmanager
import logging

# Initialize logging
logger = logging.getLogger(__name__)

# Database setup
DATABASE_URL = "postgresql+asyncpg://user:pass@localhost/c2db"
engine = create_async_engine(DATABASE_URL, echo=True, future=True)

async def get_db():
    async with AsyncSession(engine) as session:
        yield session

@asynccontextmanager
async def lifespan(app: FastAPI):
    # Startup
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    # Shutdown
    await engine.dispose()

app = FastAPI(
    title="C2 Server API",
    description="Remote Administration Framework",
    version="1.0.0",
    lifespan=lifespan
)

# CORS
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:3000"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Include routers
from app.api import auth, sessions, commands, agents, monitoring
app.include_router(auth.router, prefix="/api/auth", tags=["auth"])
app.include_router(sessions.router, prefix="/api/sessions", tags=["sessions"])
app.include_router(commands.router, prefix="/api/commands", tags=["commands"])
app.include_router(agents.router, prefix="/api/agents", tags=["agents"])
app.include_router(monitoring.router, prefix="/api/monitoring", tags=["monitoring"])

@app.get("/health")
async def health_check():
    return {"status": "healthy"}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
```

### 3.2 نماذج قاعدة البيانات (ORM)
```
المدة: 3 أيام

ملف: server/app/models/user.py
```python
from sqlalchemy import Column, Integer, String, DateTime, Enum
from sqlalchemy.ext.declarative import declarative_base
from datetime import datetime
import enum

Base = declarative_base()

class UserRole(str, enum.Enum):
    ADMIN = "admin"
    OPERATOR = "operator"

class User(Base):
    __tablename__ = "users"

    id = Column(Integer, primary_key=True)
    username = Column(String(255), unique=True, nullable=False)
    password_hash = Column(String(255), nullable=False)
    email = Column(String(255), unique=True, nullable=False)
    role = Column(Enum(UserRole), default=UserRole.OPERATOR)
    created_at = Column(DateTime, default=datetime.utcnow)
    last_login = Column(DateTime)

    def __repr__(self):
        return f"<User {self.username}>"
```

ملف: server/app/models/agent.py
```python
class Agent(Base):
    __tablename__ = "agents"

    id = Column(Integer, primary_key=True)
    agent_id = Column(String(36), unique=True, nullable=False)  # UUID
    hostname = Column(String(255))
    os_type = Column(String(50))
    os_version = Column(String(50))
    architecture = Column(String(50))
    ip_address = Column(String(45))
    mac_address = Column(String(17))
    user = Column(String(255))
    privileges = Column(String(50))
    first_seen = Column(DateTime, default=datetime.utcnow)
    last_seen = Column(DateTime, default=datetime.utcnow)
    status = Column(String(20), default="INACTIVE")
    
    sessions = relationship("Session", back_populates="agent")
    logs = relationship("Log", back_populates="agent")
```

### 3.3 واجهات API
```
المدة: 4 أيام

ملف: server/app/api/sessions.py
```python
from fastapi import APIRouter, WebSocket, Depends, HTTPException
from sqlalchemy.ext.asyncio import AsyncSession
from app.models import Session, Agent, Command
from app.schemas import SessionCreate, SessionResponse
import json
from typing import Dict, List

router = APIRouter()

active_connections: Dict[str, WebSocket] = {}

@router.post("/", response_model=SessionResponse)
async def create_session(
    session_create: SessionCreate,
    db: AsyncSession = Depends(get_db)
):
    # Verify agent exists
    agent = await db.execute(
        select(Agent).where(Agent.agent_id == session_create.agent_id)
    )
    agent = agent.scalar()
    
    if not agent:
        raise HTTPException(status_code=404, detail="Agent not found")
    
    # Create session
    session = Session(
        agent_id=agent.id,
        user_id=session_create.user_id,
        status="ACTIVE"
    )
    db.add(session)
    await db.commit()
    
    return session

@router.websocket("/ws/{session_id}")
async def websocket_endpoint(session_id: str, websocket: WebSocket):
    await websocket.accept()
    active_connections[session_id] = websocket
    
    try:
        while True:
            # Receive command from client
            data = await websocket.receive_text()
            command_data = json.loads(data)
            
            # Store command in database
            # ... implementation
            
            # Broadcast to agent
            # ... implementation
    except Exception as e:
        logger.error(f"WebSocket error: {e}")
    finally:
        del active_connections[session_id]

@router.get("/", response_model=List[SessionResponse])
async def list_sessions(db: AsyncSession = Depends(get_db)):
    result = await db.execute(select(Session))
    return result.scalars().all()

@router.get("/{session_id}", response_model=SessionResponse)
async def get_session(session_id: int, db: AsyncSession = Depends(get_db)):
    session = await db.get(Session, session_id)
    if not session:
        raise HTTPException(status_code=404, detail="Session not found")
    return session
```

### 3.4 المصادقة والتفويض
```
المدة: 2 أيام

ملف: server/app/api/auth.py
```python
from fastapi import APIRouter, Depends, HTTPException, status
from fastapi.security import HTTPBearer, HTTPAuthCredentials
from sqlalchemy.ext.asyncio import AsyncSession
from datetime import datetime, timedelta
from jose import JWTError, jwt
from passlib.context import CryptContext
from app.models import User
from app.schemas import TokenResponse, UserLogin

router = APIRouter()

# Configuration
SECRET_KEY = "your-secret-key-change-in-production"
ALGORITHM = "HS256"
ACCESS_TOKEN_EXPIRE_MINUTES = 30

# Password hashing
pwd_context = CryptContext(schemes=["bcrypt"], deprecated="auto")

# Security scheme
security = HTTPBearer()

def verify_password(plain_password: str, hashed_password: str) -> bool:
    return pwd_context.verify(plain_password, hashed_password)

def get_password_hash(password: str) -> str:
    return pwd_context.hash(password)

def create_access_token(data: dict, expires_delta: timedelta = None):
    to_encode = data.copy()
    if expires_delta:
        expire = datetime.utcnow() + expires_delta
    else:
        expire = datetime.utcnow() + timedelta(minutes=15)
    to_encode.update({"exp": expire})
    encoded_jwt = jwt.encode(to_encode, SECRET_KEY, algorithm=ALGORITHM)
    return encoded_jwt

async def get_current_user(
    credentials: HTTPAuthCredentials = Depends(security),
    db: AsyncSession = Depends(get_db)
):
    try:
        payload = jwt.decode(credentials.credentials, SECRET_KEY, algorithms=[ALGORITHM])
        username: str = payload.get("sub")
        if username is None:
            raise HTTPException(status_code=401, detail="Invalid token")
    except JWTError:
        raise HTTPException(status_code=401, detail="Invalid token")
    
    user = await db.execute(select(User).where(User.username == username))
    user = user.scalar()
    if user is None:
        raise HTTPException(status_code=401, detail="User not found")
    return user

@router.post("/login", response_model=TokenResponse)
async def login(credentials: UserLogin, db: AsyncSession = Depends(get_db)):
    user = await db.execute(select(User).where(User.username == credentials.username))
    user = user.scalar()
    
    if not user or not verify_password(credentials.password, user.password_hash):
        raise HTTPException(status_code=401, detail="Invalid credentials")
    
    access_token = create_access_token(
        data={"sub": user.username},
        expires_delta=timedelta(minutes=ACCESS_TOKEN_EXPIRE_MINUTES)
    )
    
    user.last_login = datetime.utcnow()
    await db.commit()
    
    return {
        "access_token": access_token,
        "token_type": "bearer",
        "expires_in": ACCESS_TOKEN_EXPIRE_MINUTES * 60
    }
```

### 3.5 Service Layer
```
المدة: 2 أيام

ملف: server/app/services/session_manager.py
```python
from sqlalchemy.ext.asyncio import AsyncSession
from app.models import Session, Agent, Command
from typing import Optional, List

class SessionManager:
    def __init__(self, db: AsyncSession):
        self.db = db
    
    async def create_session(self, agent_id: int, user_id: int) -> Session:
        session = Session(agent_id=agent_id, user_id=user_id)
        self.db.add(session)
        await self.db.commit()
        return session
    
    async def get_active_sessions(self) -> List[Session]:
        result = await self.db.execute(
            select(Session).where(Session.status == "ACTIVE")
        )
        return result.scalars().all()
    
    async def queue_command(self, session_id: int, command_text: str) -> Command:
        cmd = Command(
            session_id=session_id,
            command_text=command_text,
            status="QUEUED"
        )
        self.db.add(cmd)
        await self.db.commit()
        return cmd
    
    async def close_session(self, session_id: int) -> Session:
        session = await self.db.get(Session, session_id)
        session.status = "CLOSED"
        session.ended_at = datetime.utcnow()
        await self.db.commit()
        return session
```
```

---

## المرحلة الرابعة: لوحة التحكم (الأسبوع 7-8)

### 4.1 إعداد مشروع React
```
المدة: 1 يوم

npm create vite@latest dashboard -- --template react
cd dashboard
npm install
npm install -D typescript @types/react @types/node
npm install axios zustand recharts react-router-dom

tsconfig.json:
{
  "compilerOptions": {
    "target": "ES2020",
    "useDefineForModule": true,
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "jsx": "react-jsx",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "resolveJsonModule": true,
    "allowImportingTsExtensions": true,
    "strict": true,
    "declaration": true,
    "declarationMap": true,
    "sourceMap": true,
    "outDir": "./dist",
    "skipLibCheck": true
  }
}
```

### 4.2 هيكل المشروع
```
dashboard/
├── src/
│   ├── App.tsx
│   ├── components/
│   │   ├── Auth/
│   │   │   ├── Login.tsx
│   │   │   └── PrivateRoute.tsx
│   │   ├── Layout/
│   │   │   ├── Sidebar.tsx
│   │   │   ├── Header.tsx
│   │   │   └── MainLayout.tsx
│   │   ├── Dashboard/
│   │   │   ├── Dashboard.tsx
│   │   │   ├── StatsCard.tsx
│   │   │   └── Charts.tsx
│   │   ├── Sessions/
│   │   │   ├── SessionList.tsx
│   │   │   ├── SessionDetails.tsx
│   │   │   └── SessionCreate.tsx
│   │   ├── Commands/
│   │   │   ├── CommandExecutor.tsx
│   │   │   ├── CommandHistory.tsx
│   │   │   └── OutputConsole.tsx
│   │   └── Monitoring/
│   │       ├── AlertList.tsx
│   │       ├── EventLog.tsx
│   │       └── NetworkMonitor.tsx
│   ├── services/
│   │   ├── api.ts
│   │   ├── websocket.ts
│   │   ├── auth.ts
│   │   └── storage.ts
│   ├── store/
│   │   ├── authStore.ts
│   │   ├── sessionStore.ts
│   │   └── appStore.ts
│   ├── types/
│   │   ├── index.ts
│   │   ├── api.ts
│   │   └── models.ts
│   ├── utils/
│   │   ├── formatters.ts
│   │   ├── validators.ts
│   │   └── constants.ts
│   └── styles/
│       └── globals.css
└── package.json
```

### 4.3 خدمات API
```
المدة: 3 أيام

ملف: src/services/api.ts
```typescript
import axios, { AxiosInstance, AxiosError } from 'axios';

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:8000/api';

class ApiClient {
    private client: AxiosInstance;
    
    constructor() {
        this.client = axios.create({
            baseURL: API_BASE_URL,
            headers: {
                'Content-Type': 'application/json',
            },
        });
        
        this.client.interceptors.request.use((config) => {
            const token = localStorage.getItem('access_token');
            if (token) {
                config.headers.Authorization = `Bearer ${token}`;
            }
            return config;
        });
    }
    
    async login(username: string, password: string) {
        try {
            const response = await this.client.post('/auth/login', {
                username,
                password,
            });
            return response.data;
        } catch (error) {
            throw this.handleError(error);
        }
    }
    
    async getSessions() {
        try {
            const response = await this.client.get('/sessions');
            return response.data;
        } catch (error) {
            throw this.handleError(error);
        }
    }
    
    async getSession(id: number) {
        try {
            const response = await this.client.get(`/sessions/${id}`);
            return response.data;
        } catch (error) {
            throw this.handleError(error);
        }
    }
    
    async executeCommand(sessionId: number, command: string) {
        try {
            const response = await this.client.post(
                `/sessions/${sessionId}/commands`,
                { command_text: command }
            );
            return response.data;
        } catch (error) {
            throw this.handleError(error);
        }
    }
    
    async getMonitoringData() {
        try {
            const response = await this.client.get('/monitoring/dashboard');
            return response.data;
        } catch (error) {
            throw this.handleError(error);
        }
    }
    
    private handleError(error: unknown): Error {
        if (axios.isAxiosError(error)) {
            return new Error(
                error.response?.data?.detail || 
                error.message || 
                'An error occurred'
            );
        }
        return error as Error;
    }
}

export const apiClient = new ApiClient();
```

ملف: src/services/websocket.ts
```typescript
import { useCallback } from 'react';

export class WebSocketService {
    private ws: WebSocket | null = null;
    private url: string;
    private reconnectAttempts = 0;
    private maxReconnectAttempts = 5;
    private listeners: Map<string, Set<Function>> = new Map();
    
    constructor(url: string) {
        this.url = url;
    }
    
    connect(): Promise<void> {
        return new Promise((resolve, reject) => {
            try {
                const token = localStorage.getItem('access_token');
                this.ws = new WebSocket(
                    `${this.url}?token=${token}`
                );
                
                this.ws.onopen = () => {
                    console.log('WebSocket connected');
                    this.reconnectAttempts = 0;
                    this.emit('connected');
                    resolve();
                };
                
                this.ws.onmessage = (event) => {
                    const data = JSON.parse(event.data);
                    this.emit('message', data);
                    this.emit(data.type, data);
                };
                
                this.ws.onerror = (error) => {
                    console.error('WebSocket error:', error);
                    this.emit('error', error);
                    reject(error);
                };
                
                this.ws.onclose = () => {
                    console.log('WebSocket closed');
                    this.attemptReconnect();
                };
            } catch (error) {
                reject(error);
            }
        });
    }
    
    send(type: string, data: any): void {
        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
            this.ws.send(JSON.stringify({ type, ...data }));
        }
    }
    
    on(event: string, callback: Function): void {
        if (!this.listeners.has(event)) {
            this.listeners.set(event, new Set());
        }
        this.listeners.get(event)!.add(callback);
    }
    
    off(event: string, callback: Function): void {
        if (this.listeners.has(event)) {
            this.listeners.get(event)!.delete(callback);
        }
    }
    
    private emit(event: string, data?: any): void {
        if (this.listeners.has(event)) {
            this.listeners.get(event)!.forEach(callback => {
                callback(data);
            });
        }
    }
    
    private attemptReconnect(): void {
        if (this.reconnectAttempts < this.maxReconnectAttempts) {
            this.reconnectAttempts++;
            const delay = Math.pow(2, this.reconnectAttempts) * 1000;
            console.log(`Reconnecting in ${delay}ms...`);
            setTimeout(() => this.connect().catch(console.error), delay);
        }
    }
    
    disconnect(): void {
        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }
    }
}

const WS_URL = import.meta.env.VITE_WS_URL || 'ws://localhost:8000/ws';
export const wsService = new WebSocketService(WS_URL);
```

### 4.4 State Management
```
المدة: 2 أيام

ملف: src/store/authStore.ts
```typescript
import { create } from 'zustand';
import { apiClient } from '../services/api';

interface User {
    id: number;
    username: string;
    email: string;
    role: 'admin' | 'operator';
}

interface AuthStore {
    user: User | null;
    token: string | null;
    isAuthenticated: boolean;
    isLoading: boolean;
    error: string | null;
    
    login: (username: string, password: string) => Promise<void>;
    logout: () => void;
    restoreSession: () => void;
}

export const useAuthStore = create<AuthStore>((set) => ({
    user: null,
    token: localStorage.getItem('access_token'),
    isAuthenticated: !!localStorage.getItem('access_token'),
    isLoading: false,
    error: null,
    
    login: async (username: string, password: string) => {
        set({ isLoading: true, error: null });
        try {
            const response = await apiClient.login(username, password);
            localStorage.setItem('access_token', response.access_token);
            set({
                token: response.access_token,
                isAuthenticated: true,
                user: response.user,
                isLoading: false,
            });
        } catch (error) {
            set({
                error: error instanceof Error ? error.message : 'Login failed',
                isLoading: false,
            });
            throw error;
        }
    },
    
    logout: () => {
        localStorage.removeItem('access_token');
        set({
            user: null,
            token: null,
            isAuthenticated: false,
        });
    },
    
    restoreSession: () => {
        const token = localStorage.getItem('access_token');
        if (token) {
            set({ token, isAuthenticated: true });
        }
    },
}));
```

ملف: src/store/sessionStore.ts
```typescript
import { create } from 'zustand';
import { apiClient } from '../services/api';

interface Session {
    id: number;
    agent_id: number;
    status: string;
    created_at: string;
    command_count: number;
}

interface SessionStore {
    sessions: Session[];
    currentSession: Session | null;
    isLoading: boolean;
    error: string | null;
    
    fetchSessions: () => Promise<void>;
    setCurrentSession: (session: Session) => void;
    executeCommand: (sessionId: number, command: string) => Promise<void>;
}

export const useSessionStore = create<SessionStore>((set, get) => ({
    sessions: [],
    currentSession: null,
    isLoading: false,
    error: null,
    
    fetchSessions: async () => {
        set({ isLoading: true });
        try {
            const sessions = await apiClient.getSessions();
            set({ sessions, isLoading: false });
        } catch (error) {
            set({
                error: error instanceof Error ? error.message : 'Failed to fetch sessions',
                isLoading: false,
            });
        }
    },
    
    setCurrentSession: (session: Session) => {
        set({ currentSession: session });
    },
    
    executeCommand: async (sessionId: number, command: string) => {
        try {
            await apiClient.executeCommand(sessionId, command);
            get().fetchSessions();
        } catch (error) {
            set({
                error: error instanceof Error ? error.message : 'Failed to execute command',
            });
        }
    },
}));
```

### 4.5 مكونات واجهة المستخدم
```
المدة: 4 أيام

ملف: src/components/Auth/Login.tsx
```typescript
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuthStore } from '../../store/authStore';

export const Login: React.FC = () => {
    const [username, setUsername] = useState('');
    const [password, setPassword] = useState('');
    const navigate = useNavigate();
    const { login, isLoading, error } = useAuthStore();
    
    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            await login(username, password);
            navigate('/dashboard');
        } catch (err) {
            // Error is handled by store
        }
    };
    
    return (
        <div className="min-h-screen flex items-center justify-center bg-gray-50">
            <div className="max-w-md w-full space-y-8">
                <div>
                    <h2 className="mt-6 text-center text-3xl font-extrabold text-gray-900">
                        Sign in to Dashboard
                    </h2>
                </div>
                <form className="mt-8 space-y-6" onSubmit={handleSubmit}>
                    {error && (
                        <div className="rounded-md bg-red-50 p-4">
                            <p className="text-sm font-medium text-red-800">{error}</p>
                        </div>
                    )}
                    <div className="rounded-md shadow-sm -space-y-px">
                        <div>
                            <input
                                type="text"
                                placeholder="Username"
                                value={username}
                                onChange={(e) => setUsername(e.target.value)}
                                className="appearance-none rounded-none relative block w-full px-3 py-2 border border-gray-300 placeholder-gray-500 text-gray-900 rounded-t-md focus:outline-none focus:ring-blue-500 focus:border-blue-500 focus:z-10 sm:text-sm"
                            />
                        </div>
                        <div>
                            <input
                                type="password"
                                placeholder="Password"
                                value={password}
                                onChange={(e) => setPassword(e.target.value)}
                                className="appearance-none rounded-none relative block w-full px-3 py-2 border border-gray-300 placeholder-gray-500 text-gray-900 rounded-b-md focus:outline-none focus:ring-blue-500 focus:border-blue-500 focus:z-10 sm:text-sm"
                            />
                        </div>
                    </div>
                    <div>
                        <button
                            type="submit"
                            disabled={isLoading}
                            className="group relative w-full flex justify-center py-2 px-4 border border-transparent text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-50"
                        >
                            {isLoading ? 'Signing in...' : 'Sign in'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
};
```

ملف: src/components/Dashboard/Dashboard.tsx
```typescript
import React, { useEffect, useState } from 'react';
import { useSessionStore } from '../../store/sessionStore';
import { apiClient } from '../../services/api';

interface DashboardStats {
    totalAgents: number;
    activeSessions: number;
    totalCommands: number;
    detectionEvents: number;
}

export const Dashboard: React.FC = () => {
    const [stats, setStats] = useState<DashboardStats | null>(null);
    const { fetchSessions, sessions } = useSessionStore();
    
    useEffect(() => {
        const loadData = async () => {
            try {
                const data = await apiClient.getMonitoringData();
                setStats(data.stats);
                await fetchSessions();
            } catch (error) {
                console.error('Failed to load dashboard data:', error);
            }
        };
        
        loadData();
        const interval = setInterval(loadData, 5000);
        return () => clearInterval(interval);
    }, [fetchSessions]);
    
    if (!stats) return <div>Loading...</div>;
    
    return (
        <div className="p-6">
            <h1 className="text-3xl font-bold text-gray-900 mb-8">Dashboard</h1>
            
            <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-8">
                <StatCard
                    title="Total Agents"
                    value={stats.totalAgents}
                    icon="🖥️"
                />
                <StatCard
                    title="Active Sessions"
                    value={stats.activeSessions}
                    icon="🔗"
                />
                <StatCard
                    title="Total Commands"
                    value={stats.totalCommands}
                    icon="⌨️"
                />
                <StatCard
                    title="Detection Events"
                    value={stats.detectionEvents}
                    icon="🚨"
                />
            </div>
            
            <div className="bg-white rounded-lg shadow p-6">
                <h2 className="text-2xl font-bold mb-4">Active Sessions</h2>
                <SessionsList sessions={sessions} />
            </div>
        </div>
    );
};

const StatCard: React.FC<{ title: string; value: number; icon: string }> = ({
    title,
    value,
    icon,
}) => (
    <div className="bg-white rounded-lg shadow p-6">
        <div className="flex items-center">
            <span className="text-4xl mr-4">{icon}</span>
            <div>
                <p className="text-gray-500 text-sm">{title}</p>
                <p className="text-2xl font-bold text-gray-900">{value}</p>
            </div>
        </div>
    </div>
);

const SessionsList: React.FC<{ sessions: any[] }> = ({ sessions }) => (
    <div className="overflow-x-auto">
        <table className="min-w-full">
            <thead className="bg-gray-50">
                <tr>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Agent ID
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Status
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Commands
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Created
                    </th>
                </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
                {sessions.map((session) => (
                    <tr key={session.id} className="hover:bg-gray-50">
                        <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                            {session.agent_id}
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                            <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${
                                session.status === 'ACTIVE'
                                    ? 'bg-green-100 text-green-800'
                                    : 'bg-gray-100 text-gray-800'
                            }`}>
                                {session.status}
                            </span>
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                            {session.command_count}
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                            {new Date(session.created_at).toLocaleDateString()}
                        </td>
                    </tr>
                ))}
            </tbody>
        </table>
    </div>
);
```
```

---

## المرحلة الخامسة: وحدة الكشف (الأسبوع 9-10)

### 5.1 إعداد مشروع Python
```
المدة: 1 يوم

mkdir detection
cd detection
python -m venv venv
source venv/bin/activate  # Linux/Mac
# or
venv\Scripts\activate  # Windows

pip install -r requirements.txt
```

### 5.2 محلل حركة الشبكة
```
المدة: 3 أيام

ملف: detection/detection/network_analyzer.py
```python
import scapy.all as scapy
from scapy.layers.inet import IP, TCP, UDP
from collections import defaultdict
import logging

logger = logging.getLogger(__name__)

class NetworkAnalyzer:
    def __init__(self):
        self.connections = defaultdict(list)
        self.suspicious_patterns = []
    
    def start_sniffing(self, interface: str, packet_count: int = 0):
        logger.info(f"Starting packet capture on {interface}")
        scapy.sniff(
            iface=interface,
            prn=self.analyze_packet,
            store=False,
            count=packet_count if packet_count > 0 else None
        )
    
    def analyze_packet(self, packet):
        if IP in packet:
            ip_src = packet[IP].src
            ip_dst = packet[IP].dst
            
            if TCP in packet:
                sport = packet[TCP].sport
                dport = packet[TCP].dport
                flags = packet[TCP].flags
                
                self.detect_port_scan(ip_src, dport)
                self.detect_suspicious_connections(ip_src, ip_dst, dport)
            
            elif UDP in packet:
                sport = packet[UDP].sport
                dport = packet[UDP].dport
                
                self.detect_dns_exfiltration(packet)
    
    def detect_port_scan(self, src: str, dst_port: int):
        """Detect potential port scanning activity"""
        if src not in self.connections:
            self.connections[src] = []
        
        self.connections[src].append(dst_port)
        
        # If more than 50 different ports in 10 seconds = likely port scan
        if len(set(self.connections[src][-50:])) > 20:
            self.report_alert(
                "PORT_SCAN_DETECTED",
                f"Possible port scan from {src}",
                "HIGH"
            )
    
    def detect_suspicious_connections(self, src: str, dst: str, port: int):
        """Detect unusual outbound connections"""
        # Blacklist of suspicious ports/protocols
        suspicious_ports = {
            445: "SMB",
            3389: "RDP",
            5985: "WinRM",
            4444: "Custom",
            5555: "Custom",
            8080: "Proxy",
        }
        
        if port in suspicious_ports:
            self.report_alert(
                "SUSPICIOUS_CONNECTION",
                f"Outbound connection to {dst}:{port} ({suspicious_ports[port]})",
                "MEDIUM"
            )
    
    def detect_dns_exfiltration(self, packet):
        """Detect potential DNS-based data exfiltration"""
        from scapy.layers.dns import DNS, DNSQR
        
        if DNS in packet:
            dns_query = packet[DNS].qd
            if dns_query:
                domain = dns_query.qname.decode('utf-8', errors='ignore')
                
                # Check for unusual subdomain patterns
                parts = domain.split('.')
                if len(parts[0]) > 20:  # Unusually long subdomain
                    self.report_alert(
                        "DNS_EXFILTRATION_DETECTED",
                        f"Suspicious DNS query: {domain}",
                        "HIGH"
                    )
    
    def report_alert(self, alert_type: str, message: str, severity: str):
        logger.warning(f"[{severity}] {alert_type}: {message}")
        self.suspicious_patterns.append({
            "type": alert_type,
            "message": message,
            "severity": severity,
        })
```

### 5.3 مراقب السلوك
```
المدة: 3 أيام

ملف: detection/detection/behavior_monitor.py
```python
import psutil
import logging
from collections import defaultdict
import time

logger = logging.getLogger(__name__)

class BehaviorMonitor:
    def __init__(self):
        self.process_baseline = {}
        self.suspicious_processes = []
        self.baseline_created = False
    
    def create_baseline(self):
        """Capture baseline process activity"""
        logger.info("Creating baseline process metrics...")
        for proc in psutil.process_iter(['pid', 'name', 'exe', 'cmdline', 'ppid']):
            try:
                self.process_baseline[proc.info['pid']] = {
                    'name': proc.info['name'],
                    'exe': proc.info['exe'],
                    'cmdline': proc.info['cmdline'],
                    'ppid': proc.info['ppid'],
                }
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                pass
        self.baseline_created = True
    
    def monitor_processes(self, check_interval: int = 5):
        """Monitor for new/suspicious processes"""
        while True:
            try:
                current_pids = set(psutil.pids())
                baseline_pids = set(self.process_baseline.keys())
                
                new_pids = current_pids - baseline_pids
                
                for pid in new_pids:
                    try:
                        proc = psutil.Process(pid)
                        self.analyze_process(proc)
                    except (psutil.NoSuchProcess, psutil.AccessDenied):
                        pass
                
                time.sleep(check_interval)
            except Exception as e:
                logger.error(f"Error in process monitoring: {e}")
    
    def analyze_process(self, proc: psutil.Process):
        """Analyze a process for suspicious behavior"""
        try:
            name = proc.name()
            exe = proc.exe()
            cmdline = proc.cmdline()
            ppid = proc.ppid()
            
            # Check for suspicious patterns
            if self.is_suspicious(name, exe, cmdline, ppid):
                self.report_suspicious_process(proc)
        except Exception as e:
            logger.error(f"Error analyzing process {proc.pid}: {e}")
    
    def is_suspicious(self, name: str, exe: str, cmdline: list, ppid: int) -> bool:
        """Determine if a process is suspicious"""
        suspicious_indicators = [
            # Suspicious process names
            ('powershell', 'child of explorer'),
            ('cmd.exe', 'child of non-standard parent'),
            ('wmic.exe', 'executing WMI commands'),
            ('tasklist.exe', 'process enumeration'),
            ('ipconfig.exe', 'network reconnaissance'),
            ('whoami.exe', 'privilege enumeration'),
            ('net.exe', 'network enumeration'),
            ('systeminfo.exe', 'system reconnaissance'),
            ('reg.exe', 'registry access'),
        ]
        
        # Check process name
        for suspicious_name, context in suspicious_indicators:
            if suspicious_name in name.lower():
                # Additional context checks
                if context == 'child of explorer' and ppid == psutil.Process(ppid).ppid():
                    return True
        
        # Check command line for suspicious patterns
        if cmdline:
            cmd_str = ' '.join(cmdline).lower()
            if any(pattern in cmd_str for pattern in [
                'base64',
                'encoded',
                '-NoProfile',
                '-ExecutionPolicy Bypass',
                'Start-Process',
                'New-Item',
            ]):
                return True
        
        return False
    
    def report_suspicious_process(self, proc: psutil.Process):
        """Report a suspicious process"""
        logger.warning(f"Suspicious process detected: {proc.name()} (PID: {proc.pid})")
        try:
            alert = {
                'pid': proc.pid,
                'name': proc.name(),
                'exe': proc.exe(),
                'cmdline': proc.cmdline(),
                'parent_pid': proc.ppid(),
                'create_time': proc.create_time(),
            }
            self.suspicious_processes.append(alert)
        except Exception as e:
            logger.error(f"Error reporting suspicious process: {e}")
```

### 5.4 محرك YARA والقوانين
```
المدة: 2 أيام

ملف: detection/rules/yara_rules/c2_detection.yar
```yara
rule C2_WebSocket_Communication
{
    meta:
        description = "Detects C2 communication over WebSocket"
        author = "Security Team"
        date = "2024-11-05"
    strings:
        $ws1 = "WebSocket" nocase
        $ws2 = "ws://" nocase
        $ws3 = "wss://" nocase
        $beacon = "beacon" nocase
        $agent = "agent" nocase
    condition:
        any of ($ws*) and any of ($beacon, $agent)
}

rule C2_Reverse_Shell
{
    meta:
        description = "Detects reverse shell patterns"
        author = "Security Team"
        date = "2024-11-05"
    strings:
        $ps1 = "$client = New-Object System.Net.Sockets.TCPClient"
        $ps2 = "$stream = $client.GetStream()"
        $sh1 = "/bin/bash -i >& /dev/tcp/"
        $sh2 = "/bin/sh -c '/bin/bash -i >& /dev/tcp/"
    condition:
        any of them
}

rule C2_Process_Injection
{
    meta:
        description = "Detects process injection attempts"
        author = "Security Team"
        date = "2024-11-05"
    strings:
        $api1 = "CreateRemoteThread"
        $api2 = "WriteProcessMemory"
        $api3 = "VirtualAllocEx"
        $api4 = "SetWindowsHookEx"
    condition:
        3 of them
}

rule C2_Encrypted_Traffic
{
    meta:
        description = "Detects potential encrypted C2 traffic"
        author = "Security Team"
        date = "2024-11-05"
    strings:
        $tls1 = { 16 03 01 }  // TLS Handshake
        $tls2 = { 16 03 03 }  // TLS 1.2
    condition:
        any of them at 0
}
```

ملف: detection/rules/sigma_rules/c2_sigma.yml
```yaml
title: C2 Agent Communication Pattern
id: c2-agent-beaconing
status: experimental
date: 2024-11-05
description: Detects periodic beaconing behavior common in C2 agents
logsource:
    product: network
    service: firewall
detection:
    selection:
        - dst_port|in:
            - 443
            - 8080
            - 8443
        - dst_ip|startswith:
            - 185.
            - 178.
            - 95.
        - protocol: tcp
        - bytes_out: '>1000'
        - bytes_out: '<10000'
    timeframe: selection | 60s
    condition: selection | count > 10
falsepositives:
    - Legitimate HTTPS traffic
    - VPN connections
level: medium
tags:
    - c2
    - beaconing
    - network
```

### 5.5 محرك الكشف
```
المدة: 2 أيام

ملف: detection/detection/detection_engine.py
```python
import yara
import logging
from typing import List, Dict

logger = logging.getLogger(__name__)

class DetectionEngine:
    def __init__(self, yara_rules_dir: str):
        self.rules = self.load_yara_rules(yara_rules_dir)
        self.matches = []
    
    def load_yara_rules(self, rules_dir: str) -> yara.Rules:
        """Load all YARA rules from directory"""
        try:
            filepaths = {}
            import os
            for root, dirs, files in os.walk(rules_dir):
                for file in files:
                    if file.endswith('.yar'):
                        rule_path = os.path.join(root, file)
                        filepaths[file] = rule_path
            
            return yara.compile(filepaths=filepaths)
        except Exception as e:
            logger.error(f"Error loading YARA rules: {e}")
            return None
    
    def scan_file(self, file_path: str) -> List[Dict]:
        """Scan a file with YARA rules"""
        if not self.rules:
            return []
        
        try:
            matches = self.rules.match(file_path)
            alerts = []
            
            for match in matches:
                alert = {
                    'rule': match.rule,
                    'tags': match.tags,
                    'meta': match.meta,
                    'strings': [(str(s[0]), str(s[1]), s[2][:20]) for s in match.strings],
                }
                alerts.append(alert)
                logger.warning(f"YARA match: {match.rule}")
            
            return alerts
        except Exception as e:
            logger.error(f"Error scanning file {file_path}: {e}")
            return []
    
    def scan_memory(self, pid: int) -> List[Dict]:
        """Scan process memory with YARA rules"""
        try:
            # Implementation would use psutil and ctypes
            # to read process memory and scan with YARA
            pass
        except Exception as e:
            logger.error(f"Error scanning memory of PID {pid}: {e}")
            return []
```

### 5.6 نظام التنبيهات
```
المدة: 2 أيام

ملف: detection/detection/alert_system.py
```python
import logging
from datetime import datetime
from enum import Enum
from typing import Dict, List

logger = logging.getLogger(__name__)

class Severity(str, Enum):
    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"

class Alert:
    def __init__(
        self,
        alert_type: str,
        message: str,
        severity: Severity,
        source: str,
        metadata: Dict = None
    ):
        self.id = None
        self.type = alert_type
        self.message = message
        self.severity = severity
        self.source = source
        self.timestamp = datetime.utcnow()
        self.metadata = metadata or {}
        self.acknowledged = False
    
    def to_dict(self):
        return {
            'type': self.type,
            'message': self.message,
            'severity': self.severity.value,
            'source': self.source,
            'timestamp': self.timestamp.isoformat(),
            'metadata': self.metadata,
            'acknowledged': self.acknowledged,
        }

class AlertSystem:
    def __init__(self, db_connection=None):
        self.db = db_connection
        self.alerts: List[Alert] = []
        self.thresholds = {
            'port_scan': 50,
            'failed_logins': 10,
            'privilege_escalation': 1,
        }
    
    def create_alert(
        self,
        alert_type: str,
        message: str,
        severity: Severity,
        source: str,
        metadata: Dict = None
    ) -> Alert:
        """Create and store an alert"""
        alert = Alert(alert_type, message, severity, source, metadata)
        self.alerts.append(alert)
        
        logger.warning(
            f"[{severity.value}] {alert_type}: {message} (source: {source})"
        )
        
        # Store in database if available
        if self.db:
            self.store_alert(alert)
        
        # Notify handlers
        self.notify_handlers(alert)
        
        return alert
    
    def store_alert(self, alert: Alert):
        """Store alert in database"""
        try:
            # Implementation depends on database choice
            pass
        except Exception as e:
            logger.error(f"Error storing alert: {e}")
    
    def notify_handlers(self, alert: Alert):
        """Notify alert handlers (email, webhook, etc)"""
        if alert.severity in [Severity.HIGH, Severity.CRITICAL]:
            self.send_webhook(alert)
            self.send_notification(alert)
    
    def send_webhook(self, alert: Alert):
        """Send alert via webhook"""
        # Implementation
        pass
    
    def send_notification(self, alert: Alert):
        """Send notification (email, Slack, etc)"""
        # Implementation
        pass
    
    def get_alerts(self, severity: Severity = None, limit: int = 100) -> List[Dict]:
        """Retrieve alerts"""
        alerts = self.alerts
        if severity:
            alerts = [a for a in alerts if a.severity == severity]
        
        return [a.to_dict() for a in alerts[-limit:]]
    
    def acknowledge_alert(self, alert_id: str):
        """Mark alert as acknowledged"""
        for alert in self.alerts:
            if alert.id == alert_id:
                alert.acknowledged = True
                break
```
```

---

## المرحلة السادسة: الاختبار والتوثيق (الأسبوع 11-12)

### 6.1 الاختبارات
```
المدة: 5 أيام

Unit Tests (Rust):
cargo test --all

Integration Tests:
pytest tests/integration/

Load Tests:
locust -f tests/load/locustfile.py

Security Tests:
- OWASP Top 10 review
- SQL Injection testing
- XSS testing
- CSRF protection verification
```

### 6.2 التوثيق
```
المدة: 3 أيام

├── README.md                     # دليل البدء
├── ARCHITECTURE.md               # الهندسة المعمارية
├── API.md                        # توثيق API
├── INSTALLATION.md               # التثبيت
├── DEPLOYMENT.md                 # النشر
├── SECURITY.md                   # اعتبارات الأمان
├── CONTRIBUTING.md               # المساهمة
└── docs/
    ├── agent-guide.md
    ├── server-guide.md
    ├── dashboard-guide.md
    └── detection-guide.md
```

### 6.3 العرض النهائي
```
المدة: يومان

السيناريوهات:
1. تسجيل الدخول والمصادقة
2. تسجيل عميل جديد
3. تنفيذ أوامر أساسية
4. مراقبة البيانات المباشرة
5. كشف السلوك المريب
6. إغلاق جلسة بشكل آمن
```

---

## ملخص الجدول الزمني

| المرحلة | المدة | المكونات |
|--------|------|---------|
| الإعداد | أسبوع | البيئة، DB، API، CI/CD |
| العميل | أسبوعان | Rust Agent + التشفير |
| الخادم | أسبوعان | FastAPI + Database |
| اللوحة | أسبوعان | React Dashboard |
| الكشف | أسبوعان | Detection Engine |
| الاختبار | أسبوعان | QA + التوثيق |
| **المجموع** | **12 أسبوع** | **النظام الكامل** |

---

## المتطلبات الأساسية

### المهارات المطلوبة:
- Rust (intermediate)
- Python (intermediate)
- TypeScript/React (intermediate)
- Network Security (basics)
- Database Design (basics)
- Linux/Windows Administration

### الأدوات:
- Git
- Docker & Docker Compose
- Visual Studio Code / JetBrains IDE
- PostgreSQL 15
- Node.js 18+
- Python 3.11+
- Rust 1.70+

---

✅ **هذه الخطة توفر رودمابًا شاملاً وقابلاً للتنفيذ لمشروع تخرج احترافي وآمن**
