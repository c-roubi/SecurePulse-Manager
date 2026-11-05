# SecurePulse Manager - C2 Framework

Advanced Command & Control (C2) Framework with Modern Dashboard & Payload Builder

## Project Overview

SecurePulse Manager is a comprehensive C2 framework designed for security research and educational purposes within isolated lab environments.

## Features

### 🎨 Admin Dashboard
- Real-time agent monitoring
- Active sessions management
- Command execution interface
- Live desktop streaming
- File manager integration
- Terminal access
- System monitoring & analytics
- Alert management

### 🔨 Payload Builder
- Simplified C2 configuration
- Dynamic server discovery
- Advanced encryption support
- Connection testing
- Auto-detection of target system info
- One-click payload generation

### 🔐 Security Features
- AES-256-GCM Encryption
- ChaCha20-Poly1305 Support
- Hybrid RSA+AES Encryption
- Secure Key Derivation (PBKDF2, Scrypt, Argon2, HKDF)
- TLS 1.3 Communication

### 📊 Monitoring Capabilities
- CPU & Memory Usage
- Network Statistics
- Disk Usage Monitoring
- Process Management
- Real-time System Metrics

## Directory Structure

```
SecurePulse-Manager/
├── Frontend/              # Web UI Components
│   ├── admin_dashboard.html
│   ├── admin_dashboard_pro.html
│   ├── payload_builder.html
│   └── ...
├── Screenshots/           # UI Screenshots & Demos
│   ├── admin_dashboard.png
│   ├── payload_builder.png
│   └── ...
├── Docs/                 # Documentation
│   ├── architecture_plan.md
│   ├── implementation_plan.md
│   └── ...
└── README.md
```

## Usage

### Admin Dashboard
1. Open `Frontend/admin_dashboard_pro.html` in a web browser
2. View connected agents
3. Select a device for live control
4. Access File Manager, Terminal, Desktop Stream, System Monitoring

### Payload Builder
1. Open `Frontend/payload_builder.html` in a web browser
2. Enter C2 Server IP/Domain
3. Configure encryption method & beacon interval
4. Test connection to C2 server
5. Generate & download payload
6. Deploy on target system

## Important Notes

⚠️ **DISCLAIMER**: This project is designed for:
- Educational purposes
- Security research
- Authorized penetration testing
- Isolated lab environments ONLY

**Unauthorized access to computer systems is illegal.**

This framework should ONLY be used in:
- Authorized lab environments
- Controlled testing scenarios
- With proper authorization documentation
- Under academic supervision

## Technical Stack

- **Frontend**: HTML5, CSS3, JavaScript
- **Backend**: FastAPI (Python)
- **Agent**: Rust with async/await
- **Communication**: WebSocket + TLS 1.3
- **Encryption**: AES-256-GCM, ChaCha20-Poly1305
- **State Management**: Zustand (React)
- **ORM**: SQLAlchemy (async)

## Project Status

✅ Phase 1: Admin Dashboard (Completed)
✅ Phase 2: Payload Builder (Completed)
✅ Phase 3: Advanced Encryption (Completed)
✅ Phase 4: Live Session Management (Completed)
🔄 Phase 5: Deployment & Testing (In Progress)

## Documentation

- `Docs/architecture_plan.md` - System architecture overview
- `Docs/implementation_plan.md` - Implementation details
- `Docs/FIXES_SUMMARY.md` - Bug fixes and improvements

## Screenshots

See `Screenshots/` directory for visual demos of:
- Dashboard interface
- Agent management
- Live desktop streaming
- File manager
- Terminal interface
- Payload builder

## License

Academic & Research Use Only

---

**Project by**: Khaled Roubi
**Supervisor**: Dr. Sami Fouad
**Institution**: Al-Quds Open University - Information Security Department
**Date**: November 2026
