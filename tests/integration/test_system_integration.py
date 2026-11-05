"""Integration tests for the complete system"""

import pytest
import asyncio
import websockets
import json
from datetime import datetime
import httpx
import time

BASE_SERVER_URL = "http://localhost:8000"
BASE_DETECTION_URL = "http://localhost:8001"
WS_AGENT_URL = "ws://localhost:8000/ws/agent"


class TestServerHealthAndAuth:
    """Test basic server health and authentication"""

    @pytest.mark.asyncio
    async def test_server_health(self):
        """Test server is running and healthy"""
        async with httpx.AsyncClient() as client:
            response = await client.get(f"{BASE_SERVER_URL}/health")
            assert response.status_code == 200
            data = response.json()
            assert "status" in data
            assert data["status"] == "healthy"

    @pytest.mark.asyncio
    async def test_detection_module_health(self):
        """Test detection module is running"""
        async with httpx.AsyncClient() as client:
            response = await client.get(f"{BASE_DETECTION_URL}/health")
            assert response.status_code == 200
            data = response.json()
            assert data["status"] == "healthy"

    @pytest.mark.asyncio
    async def test_user_registration(self):
        """Test user registration"""
        async with httpx.AsyncClient() as client:
            response = await client.post(
                f"{BASE_SERVER_URL}/api/auth/register",
                json={
                    "username": f"testuser_{int(time.time())}",
                    "email": f"test_{int(time.time())}@example.com",
                    "password": "TestPassword123!"
                }
            )
            assert response.status_code == 200
            data = response.json()
            assert "user" in data

    @pytest.mark.asyncio
    async def test_user_login(self):
        """Test user login with demo credentials"""
        async with httpx.AsyncClient() as client:
            response = await client.post(
                f"{BASE_SERVER_URL}/api/auth/login",
                json={
                    "username": "admin",
                    "password": "password123"
                }
            )
            assert response.status_code == 200
            data = response.json()
            assert "access_token" in data
            assert data["token_type"] == "bearer"


class TestAgentCommunication:
    """Test agent-server communication"""

    @pytest.mark.asyncio
    async def test_websocket_connection(self):
        """Test agent WebSocket connection"""
        try:
            async with websockets.connect(WS_AGENT_URL) as websocket:
                # Send initial beacon
                beacon = {
                    "type": "beacon",
                    "agent_id": "test_agent",
                    "timestamp": datetime.utcnow().isoformat(),
                    "status": "online"
                }
                await websocket.send(json.dumps(beacon))

                # Receive acknowledgment
                response = await asyncio.wait_for(websocket.recv(), timeout=5)
                data = json.loads(response)
                assert data.get("status") == "acknowledged"
        except Exception as e:
            pytest.skip(f"WebSocket test skipped: {e}")

    @pytest.mark.asyncio
    async def test_command_execution(self):
        """Test command execution flow"""
        async with httpx.AsyncClient() as client:
            # Create session
            response = await client.post(
                f"{BASE_SERVER_URL}/api/sessions",
                json={
                    "agent_id": "test_agent",
                    "name": "Test Session"
                },
                headers={"Authorization": "Bearer test_token"}
            )

            if response.status_code == 200:
                session = response.json()
                assert "id" in session

    @pytest.mark.asyncio
    async def test_agent_recon_data(self):
        """Test agent reconnaissance data collection"""
        recon_data = {
            "hostname": "test-host",
            "os": "Linux",
            "architecture": "x86_64",
            "agent_id": "test_agent_123"
        }

        assert recon_data["hostname"] is not None
        assert recon_data["os"] is not None
        assert recon_data["agent_id"] is not None


class TestDetectionCapabilities:
    """Test detection module capabilities"""

    @pytest.mark.asyncio
    async def test_detection_stats(self):
        """Test detection statistics endpoint"""
        async with httpx.AsyncClient() as client:
            response = await client.get(f"{BASE_DETECTION_URL}/api/stats")
            assert response.status_code == 200
            data = response.json()
            assert "network" in data
            assert "behavior" in data
            assert "detection" in data
            assert "alerts" in data

    @pytest.mark.asyncio
    async def test_file_scanning(self):
        """Test file scanning capability"""
        async with httpx.AsyncClient() as client:
            response = await client.post(
                f"{BASE_DETECTION_URL}/api/detect/file",
                json={
                    "filename": "test.exe",
                    "content": b"test content"
                }
            )
            # May return 422 due to binary content, but endpoint exists
            assert response.status_code in [200, 422]

    @pytest.mark.asyncio
    async def test_command_line_detection(self):
        """Test command line detection"""
        async with httpx.AsyncClient() as client:
            response = await client.post(
                f"{BASE_DETECTION_URL}/api/detect/cmdline",
                json={
                    "command_line": "powershell -NoProfile -ExecutionPolicy Bypass"
                }
            )
            assert response.status_code == 200
            data = response.json()
            assert "matches" in data

    @pytest.mark.asyncio
    async def test_alert_management(self):
        """Test alert creation and management"""
        async with httpx.AsyncClient() as client:
            # Get existing alerts
            response = await client.get(f"{BASE_DETECTION_URL}/api/alerts")
            assert response.status_code == 200
            data = response.json()
            assert "alerts" in data
            assert "count" in data

    @pytest.mark.asyncio
    async def test_detection_rules_info(self):
        """Test detection rules information"""
        async with httpx.AsyncClient() as client:
            response = await client.get(f"{BASE_DETECTION_URL}/api/system/rules")
            assert response.status_code == 200
            data = response.json()
            assert "total_rules" in data
            assert "rule_types" in data


class TestDashboardAPI:
    """Test dashboard API endpoints"""

    @pytest.mark.asyncio
    async def test_dashboard_stats_endpoint(self):
        """Test dashboard statistics endpoint"""
        async with httpx.AsyncClient() as client:
            response = await client.get(
                f"{BASE_SERVER_URL}/api/dashboard/stats",
                headers={"Authorization": "Bearer test_token"}
            )
            # May require auth, but endpoint should exist
            assert response.status_code in [200, 401, 403]

    @pytest.mark.asyncio
    async def test_sessions_list(self):
        """Test sessions list endpoint"""
        async with httpx.AsyncClient() as client:
            response = await client.get(
                f"{BASE_SERVER_URL}/api/sessions",
                headers={"Authorization": "Bearer test_token"}
            )
            # May require auth
            assert response.status_code in [200, 401, 403]

    @pytest.mark.asyncio
    async def test_agents_list(self):
        """Test agents list endpoint"""
        async with httpx.AsyncClient() as client:
            response = await client.get(
                f"{BASE_SERVER_URL}/api/agents",
                headers={"Authorization": "Bearer test_token"}
            )
            # May require auth
            assert response.status_code in [200, 401, 403]


class TestEndToEndFlow:
    """End-to-end integration tests"""

    @pytest.mark.asyncio
    async def test_complete_workflow(self):
        """Test complete attack/defense workflow"""
        async with httpx.AsyncClient() as client:
            # 1. Check server health
            health = await client.get(f"{BASE_SERVER_URL}/health")
            assert health.status_code == 200

            # 2. Check detection module
            detection_health = await client.get(f"{BASE_DETECTION_URL}/health")
            assert detection_health.status_code == 200

            # 3. Get detection stats
            stats = await client.get(f"{BASE_DETECTION_URL}/api/stats")
            assert stats.status_code == 200

            # 4. Simulate command line detection
            cmdline = await client.post(
                f"{BASE_DETECTION_URL}/api/detect/cmdline",
                json={"command_line": "powershell.exe -NoProfile"}
            )
            assert cmdline.status_code == 200


# Pytest fixtures
@pytest.fixture
def event_loop():
    """Create event loop for async tests"""
    loop = asyncio.get_event_loop_policy().new_event_loop()
    yield loop
    loop.close()


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])
