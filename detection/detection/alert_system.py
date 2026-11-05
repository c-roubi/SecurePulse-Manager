"""Alert system for managing and responding to detections"""

import logging
import json
from dataclasses import dataclass, field
from typing import List, Dict, Callable, Optional
from enum import Enum
from datetime import datetime
import asyncio

logger = logging.getLogger(__name__)


class AlertSeverity(str, Enum):
    """Alert severity levels"""
    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


class AlertStatus(str, Enum):
    """Alert statuses"""
    NEW = "NEW"
    ACKNOWLEDGED = "ACKNOWLEDGED"
    INVESTIGATING = "INVESTIGATING"
    RESOLVED = "RESOLVED"
    FALSE_POSITIVE = "FALSE_POSITIVE"


@dataclass
class Alert:
    """Detection alert"""
    id: str
    type: str
    severity: AlertSeverity
    title: str
    description: str
    source: str
    timestamp: datetime = field(default_factory=datetime.utcnow)
    status: AlertStatus = AlertStatus.NEW
    indicators: List[str] = field(default_factory=list)
    metadata: Dict = field(default_factory=dict)
    acknowledged_at: Optional[datetime] = None
    acknowledged_by: Optional[str] = None
    notes: List[str] = field(default_factory=list)

    def to_dict(self) -> Dict:
        """Convert to dictionary"""
        return {
            "id": self.id,
            "type": self.type,
            "severity": self.severity.value,
            "title": self.title,
            "description": self.description,
            "source": self.source,
            "timestamp": self.timestamp.isoformat(),
            "status": self.status.value,
            "indicators": self.indicators,
            "metadata": self.metadata,
        }


@dataclass
class AlertRule:
    """Rule for auto-responding to alerts"""
    name: str
    description: str
    condition: Callable  # Function that returns bool given an Alert
    actions: List[str]  # Actions to take (e.g., "notify", "isolate", "block")
    enabled: bool = True


class AlertSystem:
    """Manages alerts and alert handling"""

    def __init__(self):
        self.alerts: List[Alert] = []
        self.alert_rules: Dict[str, AlertRule] = {}
        self.handlers: Dict[str, List[Callable]] = {
            "critical": [],
            "high": [],
            "medium": [],
            "low": [],
        }
        self.action_queue: asyncio.Queue = asyncio.Queue()
        self._setup_default_rules()

    def _setup_default_rules(self):
        """Setup default alert rules"""

        # Rule 1: Auto-acknowledge low severity alerts
        def low_severity_rule(alert: Alert) -> bool:
            return alert.severity == AlertSeverity.LOW

        self.add_rule(AlertRule(
            name="auto_acknowledge_low",
            description="Auto-acknowledge low severity alerts",
            condition=low_severity_rule,
            actions=["acknowledge"],
            enabled=True
        ))

        # Rule 2: Escalate critical alerts
        def critical_escalation_rule(alert: Alert) -> bool:
            return alert.severity == AlertSeverity.CRITICAL

        self.add_rule(AlertRule(
            name="escalate_critical",
            description="Escalate critical alerts",
            condition=critical_escalation_rule,
            actions=["notify", "escalate", "investigate"],
            enabled=True
        ))

    def create_alert(
        self,
        alert_type: str,
        severity: AlertSeverity,
        title: str,
        description: str,
        source: str,
        indicators: Optional[List[str]] = None,
        metadata: Optional[Dict] = None
    ) -> Alert:
        """Create and process an alert"""

        alert_id = f"alert_{len(self.alerts)}_{int(datetime.utcnow().timestamp())}"

        alert = Alert(
            id=alert_id,
            type=alert_type,
            severity=severity,
            title=title,
            description=description,
            source=source,
            indicators=indicators or [],
            metadata=metadata or {}
        )

        self.alerts.append(alert)
        logger.warning(f"[{severity.value}] {title}: {description}")

        # Process alert through rules
        self._process_alert_rules(alert)

        # Notify handlers
        self._notify_handlers(alert)

        return alert

    def _process_alert_rules(self, alert: Alert):
        """Process alert through matching rules"""
        for rule in self.alert_rules.values():
            if not rule.enabled:
                continue

            try:
                if rule.condition(alert):
                    self._execute_actions(alert, rule.actions)
            except Exception as e:
                logger.error(f"Error processing rule {rule.name}: {e}")

    def _execute_actions(self, alert: Alert, actions: List[str]):
        """Execute actions for an alert"""
        for action in actions:
            if action == "acknowledge":
                self.acknowledge_alert(alert.id, "system")
            elif action == "notify":
                self._send_notification(alert)
            elif action == "escalate":
                self._escalate_alert(alert)
            elif action == "investigate":
                self._start_investigation(alert)
            elif action == "isolate":
                self._isolate_host(alert)
            elif action == "block":
                self._block_indicator(alert)

    def _notify_handlers(self, alert: Alert):
        """Notify registered handlers"""
        severity_key = alert.severity.value.lower()

        if severity_key in self.handlers:
            for handler in self.handlers[severity_key]:
                try:
                    handler(alert)
                except Exception as e:
                    logger.error(f"Error in alert handler: {e}")

    def _send_notification(self, alert: Alert):
        """Send alert notification"""
        logger.info(f"Sending notification for alert {alert.id}")
        # In production: send email, Slack message, webhook, etc.

    def _escalate_alert(self, alert: Alert):
        """Escalate alert to higher priority"""
        logger.info(f"Escalating alert {alert.id} to management")
        # In production: create incident ticket, notify on-call, etc.

    def _start_investigation(self, alert: Alert):
        """Start automated investigation"""
        logger.info(f"Starting investigation for alert {alert.id}")
        # In production: run IR playbooks, collect forensics, etc.

    def _isolate_host(self, alert: Alert):
        """Isolate affected host"""
        logger.info(f"Isolating host for alert {alert.id}")
        # In production: disconnect network, kill processes, etc.

    def _block_indicator(self, alert: Alert):
        """Block indicators of compromise"""
        logger.info(f"Blocking indicators from alert {alert.id}")
        for indicator in alert.indicators:
            logger.info(f"  - Blocking: {indicator}")
        # In production: update firewall, EDR, WAF, etc.

    def add_rule(self, rule: AlertRule):
        """Add alert rule"""
        self.alert_rules[rule.name] = rule

    def acknowledge_alert(self, alert_id: str, acknowledged_by: str):
        """Acknowledge an alert"""
        for alert in self.alerts:
            if alert.id == alert_id:
                alert.status = AlertStatus.ACKNOWLEDGED
                alert.acknowledged_at = datetime.utcnow()
                alert.acknowledged_by = acknowledged_by
                logger.info(f"Alert {alert_id} acknowledged by {acknowledged_by}")
                return

    def add_note_to_alert(self, alert_id: str, note: str):
        """Add investigative note to alert"""
        for alert in self.alerts:
            if alert.id == alert_id:
                alert.notes.append(f"[{datetime.utcnow().isoformat()}] {note}")
                return

    def resolve_alert(self, alert_id: str, resolution: str):
        """Mark alert as resolved"""
        for alert in self.alerts:
            if alert.id == alert_id:
                alert.status = AlertStatus.RESOLVED
                self.add_note_to_alert(alert_id, f"Resolved: {resolution}")
                logger.info(f"Alert {alert_id} resolved")
                return

    def register_handler(self, severity: str, handler: Callable):
        """Register handler for specific severity"""
        if severity.lower() in self.handlers:
            self.handlers[severity.lower()].append(handler)

    def get_alerts(
        self,
        severity: Optional[AlertSeverity] = None,
        status: Optional[AlertStatus] = None,
        limit: int = 100
    ) -> List[Alert]:
        """Get alerts with optional filtering"""
        alerts = self.alerts

        if severity:
            alerts = [a for a in alerts if a.severity == severity]
        if status:
            alerts = [a for a in alerts if a.status == status]

        return alerts[-limit:]

    def get_statistics(self) -> Dict:
        """Get alert statistics"""
        severity_counts = {}
        status_counts = {}

        for alert in self.alerts:
            severity_counts[alert.severity.value] = severity_counts.get(alert.severity.value, 0) + 1
            status_counts[alert.status.value] = status_counts.get(alert.status.value, 0) + 1

        return {
            "total_alerts": len(self.alerts),
            "severity_breakdown": severity_counts,
            "status_breakdown": status_counts,
            "open_alerts": len([a for a in self.alerts if a.status == AlertStatus.NEW]),
            "critical_alerts": len([a for a in self.alerts if a.severity == AlertSeverity.CRITICAL]),
        }

    def export_alerts(self, format: str = "json") -> str:
        """Export alerts in specified format"""
        if format == "json":
            return json.dumps([a.to_dict() for a in self.alerts], indent=2)
        elif format == "csv":
            # CSV format
            lines = ["id,type,severity,title,status,timestamp"]
            for alert in self.alerts:
                lines.append(f"{alert.id},{alert.type},{alert.severity.value},{alert.title},{alert.status.value},{alert.timestamp.isoformat()}")
            return "\n".join(lines)
        else:
            raise ValueError(f"Unsupported format: {format}")
