"""Behavioral monitoring for detecting malicious activities"""

import logging
from dataclasses import dataclass, field
from typing import List, Dict, Set, Optional
from datetime import datetime, timedelta
from enum import Enum

logger = logging.getLogger(__name__)


class ProcessState(Enum):
    """Process states"""
    NORMAL = "NORMAL"
    SUSPICIOUS = "SUSPICIOUS"
    MALICIOUS = "MALICIOUS"


@dataclass
class Process:
    """Represents a process"""
    pid: int
    name: str
    exe: str
    cmdline: List[str]
    parent_pid: int
    create_time: datetime
    create_child_processes: List[int] = field(default_factory=list)
    network_connections: List[str] = field(default_factory=list)
    file_operations: List[str] = field(default_factory=list)
    registry_operations: List[str] = field(default_factory=list)


@dataclass
class BehavioralAlert:
    """Behavioral alert"""
    alert_type: str
    process_name: str
    pid: int
    severity: str
    description: str
    timestamp: datetime = field(default_factory=datetime.utcnow)
    indicators: List[str] = field(default_factory=list)


class BehaviorMonitor:
    """Monitors system behavior for suspicious activities"""

    def __init__(self):
        self.processes: Dict[int, Process] = {}
        self.alerts: List[BehavioralAlert] = []
        self.baseline: Dict[str, int] = {}
        self.suspicious_processes: Set[str] = {
            'powershell.exe',
            'cmd.exe',
            'wmic.exe',
            'tasklist.exe',
            'ipconfig.exe',
            'whoami.exe',
            'net.exe',
            'systeminfo.exe',
            'reg.exe',
            'taskkill.exe',
        }
        self.suspicious_keywords = [
            'base64',
            'encoded',
            '-NoProfile',
            '-ExecutionPolicy Bypass',
            'Start-Process',
            'New-Item',
            'Remove-Item',
            'Invoke-WebRequest',
            'curl',
            'wget',
        ]

    def monitor_process(self, process: Process) -> Optional[BehavioralAlert]:
        """Monitor a process for suspicious behavior"""
        self.processes[process.pid] = process

        alert = None

        # Check for suspicious process names
        if process.name.lower() in self.suspicious_processes:
            alert = self._analyze_suspicious_process(process)
        # Check for suspicious command line
        elif self._has_suspicious_cmdline(process.cmdline):
            alert = BehavioralAlert(
                alert_type="SUSPICIOUS_CMDLINE",
                process_name=process.name,
                pid=process.pid,
                severity="MEDIUM",
                description=f"Suspicious command line in {process.name}",
                indicators=[f"Command: {' '.join(process.cmdline[:3])}"]
            )
        # Check for suspicious parent-child relationships
        elif self._has_suspicious_parent_relationship(process):
            alert = BehavioralAlert(
                alert_type="SUSPICIOUS_PARENT",
                process_name=process.name,
                pid=process.pid,
                severity="HIGH",
                description=f"Suspicious parent-child relationship for {process.name}",
                indicators=[f"Parent PID: {process.parent_pid}"]
            )

        if alert:
            self.alerts.append(alert)

        return alert

    def _analyze_suspicious_process(self, process: Process) -> Optional[BehavioralAlert]:
        """Analyze a suspicious process in detail"""
        indicators = []

        # Check context
        if process.parent_pid not in [0, 1]:  # Not system or init
            parent = self.processes.get(process.parent_pid)
            if parent and parent.name.lower() in ['explorer.exe', 'svchost.exe']:
                indicators.append(f"Spawned by {parent.name}")

        # Check command line
        if self._has_suspicious_cmdline(process.cmdline):
            indicators.append("Suspicious command line arguments")

        # Check for obfuscation
        if self._has_obfuscation(process.cmdline):
            indicators.append("Command line appears obfuscated")

        severity = "MEDIUM"
        if len(indicators) > 2:
            severity = "HIGH"

        return BehavioralAlert(
            alert_type="SUSPICIOUS_PROCESS",
            process_name=process.name,
            pid=process.pid,
            severity=severity,
            description=f"Suspicious process: {process.name}",
            indicators=indicators
        )

    def _has_suspicious_cmdline(self, cmdline: List[str]) -> bool:
        """Check if command line has suspicious keywords"""
        cmdline_str = ' '.join(cmdline).lower()
        return any(keyword in cmdline_str for keyword in self.suspicious_keywords)

    def _has_suspicious_parent_relationship(self, process: Process) -> bool:
        """Detect suspicious parent-child relationships"""
        suspicious_parents = {
            'explorer.exe': {'powershell.exe', 'cmd.exe', 'wmic.exe'},
            'notepad.exe': {'cmd.exe', 'powershell.exe'},
            'iexplore.exe': {'cmd.exe', 'powershell.exe', 'calc.exe'},
        }

        parent = self.processes.get(process.parent_pid)
        if not parent:
            return False

        parent_name = parent.name.lower()
        process_name = process.name.lower()

        if parent_name in suspicious_parents:
            return process_name in {p.lower() for p in suspicious_parents[parent_name]}

        return False

    def _has_obfuscation(self, cmdline: List[str]) -> bool:
        """Detect command line obfuscation"""
        cmdline_str = ' '.join(cmdline)

        # Check for base64 encoding
        if 'base64' in cmdline_str.lower():
            return True

        # Check for unusual escaping
        if cmdline_str.count('\\') > 5:
            return True

        # Check for caret escaping (Windows obfuscation)
        if '^' in cmdline_str:
            return True

        return False

    def detect_process_injection(self, source_process: Process, target_process: Process) -> Optional[BehavioralAlert]:
        """Detect process injection attempts"""
        suspicious_apis = [
            'CreateRemoteThread',
            'WriteProcessMemory',
            'VirtualAllocEx',
            'SetWindowsHookEx',
        ]

        # Check if source process has suspicious APIs
        for api in suspicious_apis:
            if api in (source_process.exe or ''):
                return BehavioralAlert(
                    alert_type="PROCESS_INJECTION",
                    process_name=source_process.name,
                    pid=source_process.pid,
                    severity="CRITICAL",
                    description=f"Possible process injection from {source_process.name} to {target_process.name}",
                    indicators=[f"Target PID: {target_process.pid}", f"Suspicious API: {api}"]
                )

        return None

    def detect_lateral_movement(self, process: Process) -> Optional[BehavioralAlert]:
        """Detect lateral movement attempts"""
        lateral_movement_indicators = {
            'psexec': 'PsExec execution',
            'wmic': 'WMI command execution',
            'winrm': 'WinRM lateral movement',
            'pass-the-hash': 'NTLM authentication pass-the-hash',
            'kerberos': 'Kerberos authentication',
        }

        for indicator, description in lateral_movement_indicators.items():
            if indicator.lower() in process.exe.lower() or indicator.lower() in ' '.join(process.cmdline).lower():
                return BehavioralAlert(
                    alert_type="LATERAL_MOVEMENT",
                    process_name=process.name,
                    pid=process.pid,
                    severity="HIGH",
                    description=f"Possible lateral movement: {description}",
                    indicators=[f"Process: {process.name}"]
                )

        return None

    def get_statistics(self) -> Dict:
        """Get behavioral monitoring statistics"""
        suspicious_count = len([p for p in self.processes.values()
                               if p.name.lower() in self.suspicious_processes])

        return {
            "total_processes": len(self.processes),
            "suspicious_processes": suspicious_count,
            "total_alerts": len(self.alerts),
            "critical_alerts": len([a for a in self.alerts if a.severity == "CRITICAL"]),
        }

    def get_alerts(self, severity: Optional[str] = None, limit: int = 100) -> List[BehavioralAlert]:
        """Get behavioral alerts"""
        alerts = self.alerts
        if severity:
            alerts = [a for a in alerts if a.severity == severity]
        return alerts[-limit:]
