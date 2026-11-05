"""Detection engine with YARA and Sigma rules support"""

import logging
import json
from dataclasses import dataclass, field
from typing import List, Dict, Optional, Tuple
from datetime import datetime
from enum import Enum

logger = logging.getLogger(__name__)


class DetectionType(Enum):
    """Detection types"""
    YARA = "YARA"
    SIGMA = "SIGMA"
    CUSTOM = "CUSTOM"
    BEHAVIORAL = "BEHAVIORAL"
    NETWORK = "NETWORK"


@dataclass
class DetectionRule:
    """Detection rule"""
    name: str
    rule_type: DetectionType
    description: str
    severity: str
    indicators: List[str] = field(default_factory=list)
    metadata: Dict = field(default_factory=dict)
    enabled: bool = True


@dataclass
class DetectionMatch:
    """Detection match result"""
    rule_name: str
    rule_type: DetectionType
    severity: str
    matched_data: str
    timestamp: datetime = field(default_factory=datetime.utcnow)
    indicators: List[str] = field(default_factory=list)
    metadata: Dict = field(default_factory=dict)


class DetectionEngine:
    """Main detection engine coordinating all detection methods"""

    def __init__(self):
        self.rules: Dict[str, DetectionRule] = {}
        self.matches: List[DetectionMatch] = []
        self.yara_rules = self._load_yara_rules()
        self.sigma_rules = self._load_sigma_rules()
        self.custom_rules = self._load_custom_rules()

    def _load_yara_rules(self) -> Dict[str, str]:
        """Load YARA rules (mock)"""
        return {
            "c2_beacon": """
                rule C2_Beacon_Pattern {
                    strings:
                        $beacon1 = /POST \/.*beacon/ nocase
                        $beacon2 = /GET \/.*cmd/ nocase
                        $header = "User-Agent: " nocase
                    condition:
                        any of them
                }
            """,
            "process_injection": """
                rule ProcessInjection {
                    strings:
                        $api1 = "CreateRemoteThread"
                        $api2 = "WriteProcessMemory"
                        $api3 = "VirtualAllocEx"
                    condition:
                        2 of them
                }
            """,
            "credential_dump": """
                rule CredentialDump {
                    strings:
                        $ntds = "ntds.dit" nocase
                        $sam = "\\\\SAM" nocase
                        $hashdump = "hashdump" nocase
                    condition:
                        any of them
                }
            """,
        }

    def _load_sigma_rules(self) -> Dict[str, Dict]:
        """Load Sigma rules (mock)"""
        return {
            "command_line_obfuscation": {
                "title": "Command Line Obfuscation",
                "detection": {
                    "selection": {
                        "CommandLine": [
                            "*base64*",
                            "*encoded*",
                            "*bypass*",
                            "*noprofile*"
                        ]
                    }
                },
                "level": "high"
            },
            "suspicious_process": {
                "title": "Suspicious Process Creation",
                "detection": {
                    "selection": {
                        "Image": [
                            "*powershell.exe",
                            "*cmd.exe",
                            "*wmic.exe"
                        ],
                        "ParentImage": [
                            "*explorer.exe",
                            "*svchost.exe"
                        ]
                    }
                },
                "level": "high"
            },
            "process_injection": {
                "title": "Process Injection Attempt",
                "detection": {
                    "selection": {
                        "TargetObject": [
                            "*CreateRemoteThread*",
                            "*WriteProcessMemory*",
                            "*VirtualAllocEx*"
                        ]
                    }
                },
                "level": "critical"
            },
        }

    def _load_custom_rules(self) -> Dict[str, Dict]:
        """Load custom detection rules"""
        return {
            "http_beacon": {
                "description": "HTTP Beaconing pattern",
                "severity": "HIGH",
                "patterns": [
                    "POST.*User-Agent.*Mozilla",
                    "GET.*beacon",
                    "HEAD.*keepalive"
                ]
            },
            "dns_tunnel": {
                "description": "DNS Tunneling detection",
                "severity": "HIGH",
                "patterns": [
                    "*.exfil*",
                    "*.tunnel*",
                    "*.beacon*"
                ]
            },
            "lateral_movement": {
                "description": "Lateral movement attempts",
                "severity": "HIGH",
                "patterns": [
                    "psexec",
                    "wmic.exe.*remote",
                    "winrm.*connect"
                ]
            },
        }

    def scan_file(self, filename: str, content: bytes) -> List[DetectionMatch]:
        """Scan file with YARA rules"""
        matches = []

        # Check YARA rules (mock)
        for rule_name, rule_content in self.yara_rules.items():
            if self._match_yara_rule(content, rule_name):
                match = DetectionMatch(
                    rule_name=rule_name,
                    rule_type=DetectionType.YARA,
                    severity="HIGH",
                    matched_data=filename,
                    indicators=[f"File: {filename}"]
                )
                matches.append(match)
                self.matches.append(match)

        return matches

    def scan_event(self, event: Dict) -> List[DetectionMatch]:
        """Scan event with Sigma rules"""
        matches = []

        # Check Sigma rules (mock)
        for rule_name, rule_content in self.sigma_rules.items():
            if self._match_sigma_rule(event, rule_name):
                match = DetectionMatch(
                    rule_name=rule_name,
                    rule_type=DetectionType.SIGMA,
                    severity=rule_content.get("level", "MEDIUM").upper(),
                    matched_data=json.dumps(event),
                    indicators=self._extract_sigma_indicators(event, rule_name)
                )
                matches.append(match)
                self.matches.append(match)

        return matches

    def scan_command_line(self, cmdline: str) -> List[DetectionMatch]:
        """Scan command line with custom rules"""
        matches = []

        # Check custom rules
        for rule_name, rule_config in self.custom_rules.items():
            for pattern in rule_config.get("patterns", []):
                if self._match_pattern(cmdline, pattern):
                    match = DetectionMatch(
                        rule_name=rule_name,
                        rule_type=DetectionType.CUSTOM,
                        severity=rule_config.get("severity", "MEDIUM"),
                        matched_data=cmdline,
                        indicators=[f"Pattern: {pattern}"]
                    )
                    matches.append(match)
                    self.matches.append(match)
                    break

        return matches

    def _match_yara_rule(self, content: bytes, rule_name: str) -> bool:
        """Match YARA rule (mock implementation)"""
        # In real implementation, would use python-yara
        content_str = content.decode('utf-8', errors='ignore').lower()

        yara_patterns = {
            "c2_beacon": ["beacon", "post.*cmd", "get.*cmd"],
            "process_injection": ["createremotethread", "writeprocessmemory", "virtualallocex"],
            "credential_dump": ["ntds.dit", "sam", "hashdump"],
        }

        patterns = yara_patterns.get(rule_name, [])
        return any(pattern in content_str for pattern in patterns)

    def _match_sigma_rule(self, event: Dict, rule_name: str) -> bool:
        """Match Sigma rule (mock implementation)"""
        # In real implementation, would parse and evaluate Sigma YAML

        sigma_matchers = {
            "command_line_obfuscation": ["base64", "encoded", "bypass", "noprofile"],
            "suspicious_process": ["powershell", "cmd.exe", "wmic"],
            "process_injection": ["createremotethread", "writeprocessmemory"],
        }

        matchers = sigma_matchers.get(rule_name, [])
        event_str = json.dumps(event).lower()
        return any(matcher in event_str for matcher in matchers)

    def _match_pattern(self, text: str, pattern: str) -> bool:
        """Match pattern using regex or glob matching"""
        import re
        import fnmatch
        try:
            return bool(re.search(pattern, text, re.IGNORECASE))
        except re.error:
            return fnmatch.fnmatch(text.lower(), pattern.lower())

    def _extract_sigma_indicators(self, event: Dict, rule_name: str) -> List[str]:
        """Extract indicators from event"""
        indicators = []

        if "CommandLine" in event:
            indicators.append(f"Command: {event['CommandLine'][:50]}...")
        if "Image" in event:
            indicators.append(f"Process: {event['Image']}")
        if "ParentImage" in event:
            indicators.append(f"Parent: {event['ParentImage']}")

        return indicators

    def get_statistics(self) -> Dict:
        """Get detection engine statistics"""
        severity_counts = {}
        for match in self.matches:
            severity_counts[match.severity] = severity_counts.get(match.severity, 0) + 1

        return {
            "total_rules": len(self.rules) + len(self.yara_rules) + len(self.sigma_rules),
            "total_matches": len(self.matches),
            "severity_breakdown": severity_counts,
            "rule_types": {
                "yara": len(self.yara_rules),
                "sigma": len(self.sigma_rules),
                "custom": len(self.custom_rules),
            }
        }

    def get_matches(self, severity: Optional[str] = None, limit: int = 100) -> List[DetectionMatch]:
        """Get detection matches"""
        matches = self.matches
        if severity:
            matches = [m for m in matches if m.severity == severity]
        return matches[-limit:]

    def clear_old_matches(self, days: int = 7):
        """Clear old detection matches"""
        from datetime import timedelta
        cutoff_time = datetime.utcnow() - timedelta(days=days)
        self.matches = [m for m in self.matches if m.timestamp > cutoff_time]
