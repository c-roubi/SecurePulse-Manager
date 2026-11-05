"""C2 Server Main Application"""

from fastapi import FastAPI, WebSocket, status
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from contextlib import asynccontextmanager
import logging
from datetime import datetime

from app.database import init_db, close_db
from app.api import auth, sessions, agents, commands, monitoring

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application lifespan management"""
    # Startup
    logger.info("🚀 C2 Server starting...")
    try:
        await init_db()
        logger.info("✅ Database initialized")
    except Exception as e:
        logger.error(f"❌ Database initialization failed: {e}")
    yield
    # Shutdown
    logger.info("🛑 C2 Server shutting down...")
    try:
        await close_db()
        logger.info("✅ Database connections closed")
    except Exception as e:
        logger.error(f"❌ Shutdown error: {e}")

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




# ============ Health & Status Endpoints ============

@app.get("/health")
async def health_check():
    """Health check endpoint"""
    return {
        "status": "healthy",
        "version": "0.1.0",
        "timestamp": datetime.utcnow().isoformat()
    }


@app.get("/")
async def root():
    """Root endpoint"""
    return {
        "message": "C2 Server is operational",
        "docs": "/api/docs",
        "api_version": "0.1.0"
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
            "sessions": "operational"
        }
    }


# ============ WebSocket Endpoints ============

@app.websocket("/ws/agent/{agent_id}")
async def websocket_agent_endpoint(websocket: WebSocket, agent_id: str):
    """WebSocket endpoint for agent communication"""
    await websocket.accept()
    logger.info(f"🔗 Agent {agent_id} connected via WebSocket")

    try:
        while True:
            # Receive data from agent
            data = await websocket.receive_text()
            logger.debug(f"📥 Received from {agent_id}: {data[:100]}...")

            # Echo back (in production, process the data)
            await websocket.send_text(f"ACK:{data}")
    except Exception as e:
        logger.error(f"❌ WebSocket error for {agent_id}: {e}")
    finally:
        logger.info(f"🔌 Agent {agent_id} disconnected")


@app.websocket("/ws/monitoring")
async def websocket_monitoring_endpoint(websocket: WebSocket):
    """WebSocket endpoint for real-time monitoring"""
    await websocket.accept()
    logger.info("🔗 Monitoring client connected")

    try:
        while True:
            # Receive subscription info
            data = await websocket.receive_text()
            logger.debug(f"📥 Monitoring request: {data[:100]}...")

            # Send mock data (in production, send actual events)
            await websocket.send_json({
                "type": "event",
                "timestamp": datetime.utcnow().isoformat(),
                "data": {"mock": "data"}
            })
    except Exception as e:
        logger.error(f"❌ Monitoring WebSocket error: {e}")
    finally:
        logger.info("🔌 Monitoring client disconnected")


# ============ Error Handlers ============

@app.exception_handler(Exception)
async def global_exception_handler(request, exc):
    """Global exception handler"""
    logger.error(f"❌ Unhandled exception: {exc}")
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
