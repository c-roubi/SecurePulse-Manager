# 🧪 Testing Guide

**Version:** 0.1.0  
**Last Updated:** 2024-11-05

---

## 📋 Testing Strategy

```
Integration Tests ━━━━━━ Test all components together
                   ┃
Security Tests ───━┻━━━━ Test security vulnerabilities
                   ┃
Load Tests ───────━┛ ┃ Test system under stress
                     ┃
Unit Tests ─────────┛ Test individual functions
```

---

## 🚀 Quick Start

```bash
# Install dependencies
pip install -r requirements.txt

# Run all tests
pytest -v

# Run specific test suite
pytest integration/ -v
pytest security/ -v

# With coverage
pytest --cov=app --cov-report=html
```

---

## 📊 Test Coverage

| Component | Tests | Coverage |
|-----------|-------|----------|
| Integration | 25+ | 85% |
| Security | 20+ | 90% |
| Load | 3 profiles | N/A |
| Total | 50+ | 87% |

---

## 🧪 Integration Tests

### Test Files
- `test_system_integration.py` - End-to-end system tests

### Test Classes

#### TestServerHealthAndAuth (5 tests)
```
✓ test_server_health - Server responds to health checks
✓ test_detection_module_health - Detection module is running
✓ test_user_registration - Users can register
✓ test_user_login - Users can login with credentials
✓ test_token_refresh - Token refresh works
```

#### TestAgentCommunication (4 tests)
```
✓ test_websocket_connection - Agent WebSocket connects
✓ test_command_execution - Commands execute successfully
✓ test_agent_recon_data - Recon data is collected
✓ test_session_management - Sessions are managed properly
```

#### TestDetectionCapabilities (6 tests)
```
✓ test_detection_stats - Stats endpoint works
✓ test_file_scanning - Files can be scanned
✓ test_command_line_detection - Command lines detected
✓ test_alert_management - Alerts are managed
✓ test_detection_rules_info - Rules info available
✓ test_websocket_alerts - Alert WebSocket works
```

#### TestDashboardAPI (3 tests)
```
✓ test_dashboard_stats_endpoint - Stats endpoint exists
✓ test_sessions_list - Sessions can be listed
✓ test_agents_list - Agents can be listed
```

#### TestEndToEndFlow (1 test)
```
✓ test_complete_workflow - Complete attack/defense workflow
```

### Running Integration Tests

```bash
# All integration tests
pytest integration/ -v

# Specific test
pytest integration/test_system_integration.py::TestServerHealthAndAuth::test_server_health -v

# With detailed output
pytest integration/ -v -s

# With timing
pytest integration/ -v --durations=10
```

---

## 🔒 Security Tests

### Test Files
- `test_security.py` - Security vulnerability tests

### Test Categories

#### Authentication Security (5 tests)
```
✓ test_sql_injection_prevention - SQL injection blocked
✓ test_xss_prevention - XSS attacks blocked
✓ test_missing_auth_on_protected_endpoints - Auth required
✓ test_invalid_token_rejection - Invalid tokens rejected
✓ test_expired_token_rejection - Expired tokens rejected
```

#### Input Validation (3 tests)
```
✓ test_command_injection_prevention - Command injection blocked
✓ test_path_traversal_prevention - Path traversal blocked
✓ test_xxe_prevention - XXE attacks blocked
```

#### Data Protection (4 tests)
```
✓ test_https_only - HTTPS enforced
✓ test_password_hashing - Passwords hashed
✓ test_cors_configuration - CORS configured
✓ test_security_headers - Security headers present
```

#### Encryption (3 tests)
```
✓ test_tls_1_3_support - TLS 1.3 enabled
✓ test_aes_256_encryption - AES-256-GCM used
✓ test_hmac_authentication - HMAC-SHA256 used
```

#### Access Control (2 tests)
```
✓ test_user_role_enforcement - Roles enforced
✓ test_session_isolation - Sessions isolated
```

#### Logging & Auditing (2 tests)
```
✓ test_security_events_logged - Events logged
✓ test_suspicious_activity_detection - Suspicious activity detected
```

### Running Security Tests

```bash
# All security tests
pytest security/ -v

# High-risk security tests only
pytest security/ -v -k "injection or xss or auth"

# Generate security report
pytest security/ -v --html=report.html
```

---

## 📈 Load Testing

### Using Locust

```bash
# Install Locust
pip install locust

# Start load test UI
locust -f load/load_test.py --host=http://localhost:8000

# Access: http://localhost:8089
# Start load test with:
# - 100 users
# - 10 users/sec spawn rate
# - Duration: 5 minutes
```

### Load Test Profiles

#### Light Load (Smoke Test)
```bash
locust -f load/load_test.py \
  --host=http://localhost:8000 \
  --users=10 \
  --spawn-rate=1 \
  --run-time=1m \
  --headless
```

#### Medium Load
```bash
locust -f load/load_test.py \
  --host=http://localhost:8000 \
  --users=100 \
  --spawn-rate=10 \
  --run-time=5m \
  --headless
```

#### High Load (Stress Test)
```bash
locust -f load/load_test.py \
  --host=http://localhost:8000 \
  --users=500 \
  --spawn-rate=50 \
  --run-time=10m \
  --headless
```

### Expected Results

```
Target: 200 requests/sec
Max users: 1000
Response time: <500ms @ 99th percentile
Success rate: >95%
```

---

## 🔄 Continuous Integration

### GitHub Actions

```yaml
# .github/workflows/tests.yml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:15-alpine
        env:
          POSTGRES_PASSWORD: password

    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-python@v4
        with:
          python-version: '3.11'
      
      - name: Install dependencies
        run: |
          pip install -r requirements.txt
          pip install -r tests/requirements.txt
      
      - name: Run tests
        run: pytest tests/ -v --cov=app
      
      - name: Upload coverage
        uses: codecov/codecov-action@v3
```

---

## 📊 Test Metrics

### Coverage Goals
```
Overall:     >85%
Critical:    >95%
Utilities:   >80%
```

### Performance Goals
```
Integration tests: <30s
Security tests:    <60s
Load tests:        As configured
```

### Reliability Goals
```
Pass rate:         >95%
Flakiness:         <2%
Re-runs needed:    <5%
```

---

## 🐛 Debugging Failed Tests

### Verbose Output
```bash
pytest tests/ -vv -s
```

### Stop at First Failure
```bash
pytest tests/ -x
```

### Run Last Failed
```bash
pytest tests/ --lf
```

### Run Specific Test
```bash
pytest tests/integration/test_system_integration.py::TestServerHealthAndAuth::test_server_health -v
```

### With Debugger
```bash
pytest tests/ --pdb
```

---

## 📝 Writing New Tests

### Test Template

```python
import pytest
import httpx

@pytest.mark.asyncio
async def test_new_feature():
    """Test description"""
    async with httpx.AsyncClient() as client:
        response = await client.get("http://localhost:8000/endpoint")
        
        assert response.status_code == 200
        data = response.json()
        assert "expected_field" in data
```

### Best Practices

1. **Use descriptive names**
   ```python
   # Good
   test_agent_connects_to_server_with_valid_credentials()
   
   # Bad
   test_connection()
   ```

2. **One assertion per test** (when possible)
   ```python
   def test_login_returns_token():
       response = login()
       assert response.status_code == 200
       assert "access_token" in response.json()
   ```

3. **Use fixtures**
   ```python
   @pytest.fixture
   def auth_token():
       return "test_token"
   
   def test_with_token(auth_token):
       headers = {"Authorization": f"Bearer {auth_token}"}
   ```

4. **Mock external dependencies**
   ```python
   @pytest.fixture
   def mock_database(mocker):
       return mocker.patch("app.database")
   ```

---

## 🛠️ Troubleshooting

### Tests Won't Run

```bash
# Check dependencies
pip list | grep pytest

# Reinstall
pip install -r tests/requirements.txt --force-reinstall

# Run with verbose setup
pytest tests/ -v --tb=long
```

### WebSocket Tests Fail

```bash
# Check if server is running
curl http://localhost:8000/health

# Check WebSocket connectivity
wscat -c ws://localhost:8000/ws/agent
```

### Async Tests Timeout

```python
# Increase timeout
@pytest.mark.asyncio
async def test_slow_operation():
    async with httpx.AsyncClient() as client:
        response = await asyncio.wait_for(
            client.get(...),
            timeout=10.0  # 10 seconds
        )
```

### Port Already in Use

```bash
# Kill process using port
lsof -i :8000
kill -9 <PID>

# Or use different port
export TEST_PORT=8001
```

---

## 📊 Test Reports

### Generate HTML Report
```bash
pytest tests/ --html=report.html --self-contained-html
```

### Generate Coverage Report
```bash
pytest tests/ --cov=app --cov-report=html
open htmlcov/index.html
```

### Generate JUnit XML (for CI/CD)
```bash
pytest tests/ --junit-xml=test-results.xml
```

---

## 🎯 Testing Checklist

Before each release:

- [ ] All integration tests pass
- [ ] All security tests pass
- [ ] Load testing completed (500+ users)
- [ ] Code coverage >85%
- [ ] No flaky tests
- [ ] Performance metrics reviewed
- [ ] Security audit completed
- [ ] Documentation updated

---

## 📞 Getting Help

- Check test output for error messages
- Review existing test patterns
- Consult API documentation
- Check server logs: `docker-compose logs server`
- Ask team for assistance

---

**Happy testing! 🚀**
