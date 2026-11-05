"""Load testing for the system using Locust"""

from locust import HttpUser, task, between, events
import json
from datetime import datetime
import time


class AgentUser(HttpUser):
    """Simulates agent users"""
    wait_time = between(1, 3)

    def on_start(self):
        """Setup before tests"""
        self.agent_id = f"agent_{self.client.get_cookie('sessionid', 0)}"
        self.session_id = None

    @task(3)
    def health_check(self):
        """Check server health"""
        self.client.get("/health")

    @task(2)
    def get_detection_stats(self):
        """Get detection statistics"""
        self.client.get("http://localhost:8001/api/stats")

    @task(1)
    def scan_command_line(self):
        """Test command line scanning"""
        self.client.post(
            "http://localhost:8001/api/detect/cmdline",
            json={"command_line": "powershell -NoProfile"}
        )


class DashboardUser(HttpUser):
    """Simulates dashboard users"""
    wait_time = between(2, 5)

    def on_start(self):
        """Setup and login"""
        # Attempt login
        response = self.client.post(
            "/api/auth/login",
            json={
                "username": "admin",
                "password": "password123"
            }
        )
        if response.status_code == 200:
            data = response.json()
            self.token = data.get("access_token")
        else:
            self.token = None

    @task(3)
    def view_dashboard(self):
        """View main dashboard"""
        headers = {}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        self.client.get("/api/dashboard/stats", headers=headers)

    @task(2)
    def list_agents(self):
        """List all agents"""
        headers = {}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        self.client.get("/api/agents", headers=headers)

    @task(2)
    def list_sessions(self):
        """List active sessions"""
        headers = {}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        self.client.get("/api/sessions", headers=headers)


class DetectionUser(HttpUser):
    """Simulates detection monitoring users"""
    wait_time = between(1, 2)

    @task(4)
    def get_alerts(self):
        """Get current alerts"""
        self.client.get("http://localhost:8001/api/alerts")

    @task(2)
    def get_rules(self):
        """Get detection rules info"""
        self.client.get("http://localhost:8001/api/system/rules")

    @task(1)
    def get_config(self):
        """Get system config"""
        self.client.get("http://localhost:8001/api/system/config")


# Performance metrics collector
@events.test_start.add_listener
def on_test_start(environment, **kwargs):
    """Called when load test starts"""
    print("\n" + "="*60)
    print("🚀 Starting Load Testing")
    print("="*60)
    print(f"Time: {datetime.utcnow().isoformat()}")
    print(f"Spawn rate: {environment.spawn_rate} users/sec")
    print(f"Target users: {environment.target_user_count}")
    print("="*60 + "\n")


@events.test_stop.add_listener
def on_test_stop(environment, **kwargs):
    """Called when load test stops"""
    print("\n" + "="*60)
    print("📊 Load Test Results")
    print("="*60)

    stats = environment.stats
    print(f"\nTotal requests: {stats.total.num_requests}")
    print(f"Total failures: {stats.total.num_failures}")
    print(f"Success rate: {stats.total.failure_rate * 100:.2f}%")

    if stats.total.num_requests > 0:
        print(f"\nResponse time (ms):")
        print(f"  Min: {stats.total.min_response_time:.0f}")
        print(f"  Max: {stats.total.max_response_time:.0f}")
        print(f"  Mean: {stats.total.avg_response_time:.0f}")
        print(f"  Median: {stats.total.median_response_time:.0f}")

    print(f"\nRequests per second: {stats.total.current_rps:.2f}")
    print("="*60 + "\n")


@events.request.add_listener
def on_request(request_type, name, response_time, response_length, response, context, exception, **kwargs):
    """Log individual requests"""
    if exception:
        print(f"❌ {request_type} {name}: {exception}")
    elif response_time > 1000:  # Slow request
        print(f"⚠️  {request_type} {name}: {response_time:.0f}ms (slow)")
