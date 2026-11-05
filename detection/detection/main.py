"""Detection module main application"""

import logging
import base64
from fastapi import FastAPI, WebSocket
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from datetime import datetime

from detection.network_analyzer import NetworkAnalyzer, DNSAnalyzer
from detection.behavior_monitor import BehaviorMonitor
from detection.detection_engine import DetectionEngine
from detection.alert_system import AlertSystem, AlertSeverity

# Request models
class FileScanRequest(BaseModel):
    filename: str
    content: str  # Base64 encoded

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Create FastAPI app
app = FastAPI(
    title="Detection Module",
    description="Network and Behavioral Detection System",
    version="0.1.0",
    docs_url="/api/docs",
    openapi_url="/api/openapi.json"
)

# CORS Configuration
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Initialize detection components
network_analyzer = NetworkAnalyzer()
dns_analyzer = DNSAnalyzer()
behavior_monitor = BehaviorMonitor()
detection_engine = DetectionEngine()
alert_system = AlertSystem()

logger.info("🔍 Detection Module initialized")


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
        "message": "Detection Module is operational",
        "docs": "/api/docs"
    }


# ============ Statistics Endpoints ============

@app.get("/api/stats")
async def get_statistics():
    """Get detection statistics"""
    return {
        "network": network_analyzer.get_statistics(),
        "behavior": behavior_monitor.get_statistics(),
        "detection": detection_engine.get_statistics(),
        "alerts": alert_system.get_statistics(),
        "timestamp": datetime.utcnow().isoformat()
    }


# ============ Alert Endpoints ============

@app.get("/api/alerts")
async def get_alerts(severity: str = None, status: str = None, limit: int = 100):
    """Get alerts"""
    alerts = alert_system.get_alerts(
        severity=severity,
        status=status,
        limit=limit
    )
    return {
        "alerts": [a.to_dict() for a in alerts],
        "count": len(alerts)
    }


@app.post("/api/alerts/{alert_id}/acknowledge")
async def acknowledge_alert(alert_id: str, acknowledged_by: str = "user"):
    """Acknowledge an alert"""
    alert_system.acknowledge_alert(alert_id, acknowledged_by)
    return {"status": "acknowledged"}


@app.post("/api/alerts/{alert_id}/note")
async def add_alert_note(alert_id: str, note: str):
    """Add note to alert"""
    alert_system.add_note_to_alert(alert_id, note)
    return {"status": "note_added"}


@app.post("/api/alerts/{alert_id}/resolve")
async def resolve_alert(alert_id: str, resolution: str):
    """Resolve an alert"""
    alert_system.resolve_alert(alert_id, resolution)
    return {"status": "resolved"}


# ============ Detection Endpoints ============

@app.post("/api/detect/file")
async def scan_file(request: FileScanRequest):
    """Scan file for detections"""
    try:
        content_bytes = base64.b64decode(request.content)
    except Exception as e:
        return {"error": f"Failed to decode base64 content: {e}"}

    matches = detection_engine.scan_file(request.filename, content_bytes)

    # Create alerts for matches
    for match in matches:
        alert_system.create_alert(
            alert_type=match.rule_type.value,
            severity=AlertSeverity(match.severity),
            title=f"Detection: {match.rule_name}",
            description=f"File matched detection rule: {match.rule_name}",
            source="detection_engine",
            indicators=match.indicators,
            metadata=match.metadata
        )

    return {
        "matches": len(matches),
        "detections": [
            {
                "rule": m.rule_name,
                "type": m.rule_type.value,
                "severity": m.severity
            }
            for m in matches
        ]
    }


@app.post("/api/detect/event")
async def scan_event(event: dict):
    """Scan event for detections"""
    matches = detection_engine.scan_event(event)

    # Create alerts for matches
    for match in matches:
        alert_system.create_alert(
            alert_type=match.rule_type.value,
            severity=AlertSeverity(match.severity),
            title=f"Detection: {match.rule_name}",
            description=f"Event matched detection rule: {match.rule_name}",
            source="detection_engine",
            indicators=match.indicators
        )

    return {
        "matches": len(matches),
        "detections": [
            {
                "rule": m.rule_name,
                "type": m.rule_type.value,
                "severity": m.severity
            }
            for m in matches
        ]
    }


@app.post("/api/detect/cmdline")
async def scan_cmdline(command_line: str):
    """Scan command line for detections"""
    matches = detection_engine.scan_command_line(command_line)

    # Create alerts for matches
    for match in matches:
        alert_system.create_alert(
            alert_type=match.rule_type.value,
            severity=AlertSeverity(match.severity),
            title=f"Detection: {match.rule_name}",
            description=f"Command line matched detection rule: {match.rule_name}",
            source="detection_engine",
            indicators=match.indicators
        )

    return {
        "matches": len(matches),
        "detections": [
            {
                "rule": m.rule_name,
                "type": m.rule_type.value,
                "severity": m.severity
            }
            for m in matches
        ]
    }


# ============ WebSocket Endpoints ============

@app.websocket("/ws/detections")
async def websocket_detections(websocket: WebSocket):
    """WebSocket endpoint for real-time detections"""
    await websocket.accept()
    logger.info("Detection WebSocket client connected")

    try:
        while True:
            # Receive detection data from client
            data = await websocket.receive_json()

            # Process detection
            detection_type = data.get("type")

            if detection_type == "network":
                # Network detection
                logger.debug(f"Processing network detection: {data}")
            elif detection_type == "behavior":
                # Behavioral detection
                logger.debug(f"Processing behavioral detection: {data}")
            elif detection_type == "signature":
                # Signature detection
                logger.debug(f"Processing signature detection: {data}")

            # Send acknowledgment
            await websocket.send_json({"status": "received"})
    except Exception as e:
        logger.error(f"WebSocket error: {e}")
    finally:
        logger.info("Detection WebSocket client disconnected")


@app.websocket("/ws/alerts")
async def websocket_alerts(websocket: WebSocket):
    """WebSocket endpoint for alert streaming"""
    await websocket.accept()
    logger.info("Alert WebSocket client connected")

    try:
        while True:
            # Send latest alerts
            recent_alerts = alert_system.get_alerts(limit=10)
            await websocket.send_json({
                "alerts": [a.to_dict() for a in recent_alerts],
                "timestamp": datetime.utcnow().isoformat()
            })

            # Wait before next update
            import asyncio
            await asyncio.sleep(5)
    except Exception as e:
        logger.error(f"WebSocket error: {e}")
    finally:
        logger.info("Alert WebSocket client disconnected")


# ============ System Endpoints ============

@app.get("/api/system/rules")
async def get_rules_info():
    """Get detection rules information"""
    return {
        "total_rules": detection_engine.get_statistics()["total_rules"],
        "rule_types": detection_engine.get_statistics()["rule_types"],
        "yara_rules": len(detection_engine.yara_rules),
        "sigma_rules": len(detection_engine.sigma_rules),
        "custom_rules": len(detection_engine.custom_rules),
    }


@app.get("/api/system/config")
async def get_configuration():
    """Get detection module configuration"""
    return {
        "version": "0.1.0",
        "enabled_modules": {
            "network_analyzer": True,
            "dns_analyzer": True,
            "behavior_monitor": True,
            "detection_engine": True,
            "alert_system": True
        },
        "alert_thresholds": {
            "critical": 1,
            "high": 5,
            "medium": 20,
            "low": 100
        }
    }


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        app,
        host="0.0.0.0",
        port=8001,
        reload=True,
        log_level="info"
    )
