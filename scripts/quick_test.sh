#!/bin/bash

# Quick testing script for Framework

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo -e "${BLUE}╔════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║  🧪 Framework Quick Testing Script                ║${NC}"
echo -e "${BLUE}║  Secure Remote Administration Framework           ║${NC}"
echo -e "${BLUE}╚════════════════════════════════════════════════════╝${NC}"
echo ""

# Test 1: Server Health
echo -e "${YELLOW}[TEST 1] Server Health Check${NC}"
if curl -s http://localhost:8000/health | grep -q "healthy"; then
    echo -e "${GREEN}[✓] Server is healthy${NC}"
else
    echo -e "${RED}[✗] Server not responding${NC}"
    exit 1
fi

# Test 2: Detection Module
echo -e "${YELLOW}[TEST 2] Detection Module${NC}"
if curl -s http://localhost:8001/health | grep -q "healthy"; then
    echo -e "${GREEN}[✓] Detection module is healthy${NC}"
else
    echo -e "${RED}[✗] Detection module not responding${NC}"
    exit 1
fi

# Test 3: Get Authentication Token
echo -e "${YELLOW}[TEST 3] Authentication${NC}"
TOKEN=$(curl -s -X POST http://localhost:8000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"password123"}' \
  | grep -o '"access_token":"[^"]*"' | cut -d'"' -f4)

if [ -z "$TOKEN" ]; then
    echo -e "${RED}[✗] Failed to get token${NC}"
    exit 1
fi
echo -e "${GREEN}[✓] Authentication successful${NC}"
echo -e "${BLUE}    Token: ${TOKEN:0:20}...${NC}"

# Test 4: List Agents
echo -e "${YELLOW}[TEST 4] List Agents${NC}"
AGENTS=$(curl -s -X GET http://localhost:8000/api/agents \
  -H "Authorization: Bearer $TOKEN" 2>/dev/null)

if echo "$AGENTS" | grep -q "agents"; then
    echo -e "${GREEN}[✓] Agents endpoint working${NC}"
    AGENT_COUNT=$(echo "$AGENTS" | grep -o '"agent_id"' | wc -l)
    echo -e "${BLUE}    Agents found: $AGENT_COUNT${NC}"
else
    echo -e "${YELLOW}[!] No agents found (expected on first run)${NC}"
fi

# Test 5: Create Session (if agents exist)
echo -e "${YELLOW}[TEST 5] Session Management${NC}"

# Try to get first agent
AGENT_ID=$(curl -s -X GET http://localhost:8000/api/agents \
  -H "Authorization: Bearer $TOKEN" 2>/dev/null \
  | grep -o '"agent_id":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -n "$AGENT_ID" ]; then
    SESSION=$(curl -s -X POST http://localhost:8000/api/sessions \
      -H "Authorization: Bearer $TOKEN" \
      -H "Content-Type: application/json" \
      -d "{\"agent_id\":\"$AGENT_ID\",\"name\":\"test-session\"}" \
      | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)

    if [ -n "$SESSION" ]; then
        echo -e "${GREEN}[✓] Session created successfully${NC}"
        echo -e "${BLUE}    Session ID: $SESSION${NC}"
        echo -e "${BLUE}    Agent ID: $AGENT_ID${NC}"

        # Test command execution
        echo -e "${YELLOW}[TEST 6] Command Execution${NC}"
        CMD_RESULT=$(curl -s -X POST http://localhost:8000/api/commands \
          -H "Authorization: Bearer $TOKEN" \
          -H "Content-Type: application/json" \
          -d "{\"session_id\":\"$SESSION\",\"type\":\"WhoAmI\"}" \
          2>/dev/null)

        if echo "$CMD_RESULT" | grep -q "completed"; then
            echo -e "${GREEN}[✓] Command executed successfully${NC}"
            OUTPUT=$(echo "$CMD_RESULT" | grep -o '"output":"[^"]*"' | head -1 | cut -d'"' -f4)
            echo -e "${BLUE}    Output: $OUTPUT${NC}"
        else
            echo -e "${YELLOW}[!] Command still processing or error${NC}"
        fi
    else
        echo -e "${YELLOW}[!] Could not create session${NC}"
    fi
else
    echo -e "${YELLOW}[!] No agents available - connect an agent first${NC}"
fi

# Test 7: Detection Module Stats
echo -e "${YELLOW}[TEST 7] Detection Module Statistics${NC}"
STATS=$(curl -s http://localhost:8001/api/stats)

if echo "$STATS" | grep -q "network"; then
    echo -e "${GREEN}[✓] Detection stats available${NC}"
    ALERTS=$(echo "$STATS" | grep -o '"total_alerts":[0-9]*' | cut -d':' -f2)
    echo -e "${BLUE}    Total alerts: $ALERTS${NC}"
else
    echo -e "${YELLOW}[!] Could not fetch stats${NC}"
fi

# Test 8: Port Availability
echo -e "${YELLOW}[TEST 8] Port Availability${NC}"
PORTS=(8000 8001 5173)
for PORT in "${PORTS[@]}"; do
    if nc -z localhost $PORT 2>/dev/null; then
        echo -e "${GREEN}[✓] Port $PORT is open${NC}"
    else
        echo -e "${RED}[✗] Port $PORT is closed${NC}"
    fi
done

# Summary
echo ""
echo -e "${BLUE}╔════════════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║  ✓ FRAMEWORK TESTING COMPLETE                     ║${NC}"
echo -e "${BLUE}╚════════════════════════════════════════════════════╝${NC}"
echo ""
echo -e "${YELLOW}Next Steps:${NC}"
echo "1. Open Dashboard: http://localhost:5173"
echo "2. Connect an agent:"
echo "   export SERVER_URL='http://localhost:8000'"
echo "   ./agent/target/release/agent"
echo "3. Run commands through Dashboard"
echo ""
echo -e "${GREEN}Ready to test!${NC}"
