"""Network traffic analyzer for detecting suspicious activity"""

import logging
from dataclasses import dataclass
from typing import List, Dict, Optional
from collections import defaultdict
from datetime import datetime, timedelta

logger = logging.getLogger(__name__)


@dataclass
class Connection:
    """Represents a network connection"""
    src_ip: str
    dst_ip: str
    dst_port: int
    protocol: str
    timestamp: datetime
    bytes_sent: int = 0
    bytes_received: int = 0


@dataclass
class SuspiciousActivity:
    """Represents suspicious network activity"""
    activity_type: str
    src_ip: str
    dst_ip: Optional[str] = None
    port: Optional[int] = None
    severity: str = "MEDIUM"
    description: str = ""
    timestamp: datetime = None

    def __post_init__(self):
        if self.timestamp is None:
            self.timestamp = datetime.utcnow()


class NetworkAnalyzer:
    """Analyzes network traffic for suspicious patterns"""

    def __init__(self):
        self.connections: Dict[str, List[Connection]] = defaultdict(list)
        self.alerts: List[SuspiciousActivity] = []
        self.suspicious_ips = set()
        self.suspicious_ports = {
            445: "SMB",
            3389: "RDP",
            5985: "WinRM",
            4444: "Custom C2",
            5555: "Custom C2",
            8080: "Proxy",
            9090: "Custom",
        }
        self.known_c2_ips = set()  # To be populated from threat intel

    def analyze_connection(self, connection: Connection) -> Optional[SuspiciousActivity]:
        """Analyze a single connection for suspicious patterns"""
        # Store connection
        self.connections[connection.src_ip].append(connection)

        alert = None

        # Check for suspicious patterns

        # 1. Port scan detection
        if self._detect_port_scan(connection.src_ip):
            alert = SuspiciousActivity(
                activity_type="PORT_SCAN",
                src_ip=connection.src_ip,
                severity="HIGH",
                description=f"Possible port scan from {connection.src_ip}"
            )
        # 2. Suspicious ports
        elif connection.dst_port in self.suspicious_ports:
            alert = SuspiciousActivity(
                activity_type="SUSPICIOUS_PORT",
                src_ip=connection.src_ip,
                dst_ip=connection.dst_ip,
                port=connection.dst_port,
                severity="MEDIUM",
                description=f"Outbound connection to suspicious port {connection.dst_port} ({self.suspicious_ports[connection.dst_port]})"
            )
        # 3. Data exfiltration detection
        elif connection.bytes_sent > 1000000:  # More than 1MB
            alert = SuspiciousActivity(
                activity_type="LARGE_DATA_TRANSFER",
                src_ip=connection.src_ip,
                dst_ip=connection.dst_ip,
                severity="HIGH",
                description=f"Large data transfer ({connection.bytes_sent / 1024 / 1024:.2f}MB) to {connection.dst_ip}"
            )
        # 4. Known C2 detection
        elif connection.dst_ip in self.known_c2_ips:
            alert = SuspiciousActivity(
                activity_type="C2_COMMUNICATION",
                src_ip=connection.src_ip,
                dst_ip=connection.dst_ip,
                severity="CRITICAL",
                description=f"Communication with known C2 server {connection.dst_ip}"
            )

        if alert:
            self.alerts.append(alert)

        return alert

    def _detect_port_scan(self, src_ip: str) -> bool:
        """Detect port scanning activity"""
        if src_ip not in self.connections:
            return False

        # Get connections from the last 10 seconds
        recent = [
            c for c in self.connections[src_ip]
            if (datetime.utcnow() - c.timestamp).total_seconds() < 10
        ]

        # If more than 20 different ports in 10 seconds = likely port scan
        unique_ports = len(set(c.dst_port for c in recent))
        return unique_ports > 20

    def _detect_dns_exfiltration(self, domain: str) -> bool:
        """Detect DNS-based data exfiltration"""
        # Check for unusually long subdomains
        parts = domain.split('.')
        if len(parts[0]) > 20:
            return True

        # Check for suspicious patterns
        suspicious_patterns = ['exfil', 'beacon', 'c2', 'cmd', 'shell']
        domain_lower = domain.lower()
        return any(pattern in domain_lower for pattern in suspicious_patterns)

    def get_statistics(self) -> Dict:
        """Get network statistics"""
        total_connections = sum(len(conns) for conns in self.connections.values())

        return {
            "total_connections": total_connections,
            "unique_sources": len(self.connections),
            "total_alerts": len(self.alerts),
            "high_severity_alerts": len([a for a in self.alerts if a.severity in ["HIGH", "CRITICAL"]]),
            "suspicious_ips": len(self.suspicious_ips),
        }

    def get_alerts(self, severity: Optional[str] = None, limit: int = 100) -> List[SuspiciousActivity]:
        """Get alerts, optionally filtered by severity"""
        alerts = self.alerts
        if severity:
            alerts = [a for a in alerts if a.severity == severity]
        return alerts[-limit:]


class DNSAnalyzer:
    """Analyzes DNS traffic for data exfiltration"""

    def __init__(self):
        self.dns_queries: Dict[str, List[str]] = defaultdict(list)
        self.alerts: List[SuspiciousActivity] = []

    def analyze_dns_query(self, src_ip: str, domain: str) -> Optional[SuspiciousActivity]:
        """Analyze DNS query"""
        self.dns_queries[src_ip].append(domain)

        # Check for exfiltration patterns
        parts = domain.split('.')
        if len(parts[0]) > 20:
            return SuspiciousActivity(
                activity_type="DNS_EXFILTRATION",
                src_ip=src_ip,
                severity="HIGH",
                description=f"Suspicious DNS query: {domain}"
            )

        # Check for suspicious domains
        suspicious_keywords = ['exfil', 'beacon', 'c2', 'malware', 'shell']
        domain_lower = domain.lower()
        if any(keyword in domain_lower for keyword in suspicious_keywords):
            return SuspiciousActivity(
                activity_type="SUSPICIOUS_DOMAIN",
                src_ip=src_ip,
                severity="MEDIUM",
                description=f"Query to suspicious domain: {domain}"
            )

        return None

    def detect_dns_tunneling(self, src_ip: str) -> Optional[SuspiciousActivity]:
        """Detect DNS tunneling (high volume of DNS queries)"""
        if src_ip not in self.dns_queries:
            return None

        # If more than 100 queries in recent time = likely DNS tunneling
        recent_queries = [
            q for q in self.dns_queries[src_ip]
            if q is not None
        ]

        if len(recent_queries) > 100:
            return SuspiciousActivity(
                activity_type="DNS_TUNNELING",
                src_ip=src_ip,
                severity="HIGH",
                description=f"Possible DNS tunneling: {len(recent_queries)} queries from {src_ip}"
            )

        return None
