"""Database models for C2 Server"""

from sqlalchemy import Column, Integer, String, DateTime, Enum, Boolean, Text, JSON, ForeignKey
from sqlalchemy.ext.declarative import declarative_base
from sqlalchemy.orm import relationship
from datetime import datetime
import enum

Base = declarative_base()


class UserRole(str, enum.Enum):
    """User roles"""
    ADMIN = "admin"
    OPERATOR = "operator"
    VIEWER = "viewer"


class AgentStatus(str, enum.Enum):
    """Agent statuses"""
    ACTIVE = "ACTIVE"
    INACTIVE = "INACTIVE"
    DEAD = "DEAD"


class SessionStatus(str, enum.Enum):
    """Session statuses"""
    ACTIVE = "ACTIVE"
    CLOSED = "CLOSED"
    SUSPENDED = "SUSPENDED"


class CommandStatus(str, enum.Enum):
    """Command execution statuses"""
    QUEUED = "QUEUED"
    EXECUTING = "EXECUTING"
    COMPLETED = "COMPLETED"
    FAILED = "FAILED"
    CANCELLED = "CANCELLED"


class LogLevel(str, enum.Enum):
    """Log levels"""
    DEBUG = "DEBUG"
    INFO = "INFO"
    WARNING = "WARNING"
    ERROR = "ERROR"
    CRITICAL = "CRITICAL"


class Severity(str, enum.Enum):
    """Detection severity levels"""
    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


class User(Base):
    """User model"""
    __tablename__ = "users"

    id = Column(Integer, primary_key=True, index=True)
    username = Column(String(255), unique=True, nullable=False, index=True)
    password_hash = Column(String(255), nullable=False)
    email = Column(String(255), unique=True, nullable=False)
    role = Column(Enum(UserRole), default=UserRole.OPERATOR)
    created_at = Column(DateTime, default=datetime.utcnow)
    last_login = Column(DateTime)
    is_active = Column(Boolean, default=True)

    # Relationships
    sessions = relationship("Session", back_populates="user")

    def __repr__(self):
        return f"<User {self.username} ({self.role})>"


class Agent(Base):
    """Agent model"""
    __tablename__ = "agents"

    id = Column(Integer, primary_key=True, index=True)
    agent_id = Column(String(36), unique=True, nullable=False, index=True)  # UUID
    hostname = Column(String(255), index=True)
    os_type = Column(String(50))
    os_version = Column(String(50))
    architecture = Column(String(50))
    ip_address = Column(String(45), index=True)
    mac_address = Column(String(17))
    user = Column(String(255))
    privileges = Column(String(50))
    first_seen = Column(DateTime, default=datetime.utcnow)
    last_seen = Column(DateTime, default=datetime.utcnow, index=True)
    status = Column(Enum(AgentStatus), default=AgentStatus.INACTIVE, index=True)
    agent_metadata = Column(JSON, default=dict)

    # Relationships
    sessions = relationship("Session", back_populates="agent")
    logs = relationship("Log", back_populates="agent")
    detection_events = relationship("DetectionEvent", back_populates="agent")

    def __repr__(self):
        return f"<Agent {self.agent_id} ({self.hostname})>"


class Session(Base):
    """Session model"""
    __tablename__ = "sessions"

    id = Column(Integer, primary_key=True, index=True)
    agent_id = Column(Integer, ForeignKey("agents.id"), nullable=False, index=True)
    user_id = Column(Integer, ForeignKey("users.id"), nullable=False, index=True)
    session_token = Column(String(255), unique=True, nullable=False)
    started_at = Column(DateTime, default=datetime.utcnow)
    ended_at = Column(DateTime)
    status = Column(Enum(SessionStatus), default=SessionStatus.ACTIVE, index=True)
    command_count = Column(Integer, default=0)
    last_activity = Column(DateTime, default=datetime.utcnow)

    # Relationships
    agent = relationship("Agent", back_populates="sessions")
    user = relationship("User", back_populates="sessions")
    commands = relationship("Command", back_populates="session")

    def __repr__(self):
        return f"<Session {self.session_token[:8]}... ({self.status})>"


class Command(Base):
    """Command model"""
    __tablename__ = "commands"

    id = Column(Integer, primary_key=True, index=True)
    session_id = Column(Integer, ForeignKey("sessions.id"), nullable=False, index=True)
    command_text = Column(Text, nullable=False)
    status = Column(Enum(CommandStatus), default=CommandStatus.QUEUED, index=True)
    issued_at = Column(DateTime, default=datetime.utcnow)
    executed_at = Column(DateTime)
    completed_at = Column(DateTime)
    output = Column(Text)
    error_message = Column(Text)
    execution_time = Column(Integer)  # milliseconds

    # Relationships
    session = relationship("Session", back_populates="commands")

    def __repr__(self):
        return f"<Command {self.id} ({self.status})>"


class Log(Base):
    """Log model"""
    __tablename__ = "logs"

    id = Column(Integer, primary_key=True, index=True)
    agent_id = Column(Integer, ForeignKey("agents.id"), index=True)
    session_id = Column(Integer, ForeignKey("sessions.id"), index=True)
    log_type = Column(String(50), nullable=False)
    message = Column(Text, nullable=False)
    timestamp = Column(DateTime, default=datetime.utcnow, index=True)
    level = Column(Enum(LogLevel), default=LogLevel.INFO)
    agent_metadata = Column(JSON, default=dict)

    # Relationships
    agent = relationship("Agent", back_populates="logs")

    def __repr__(self):
        return f"<Log {self.level} - {self.message[:50]}>"


class DetectionEvent(Base):
    """Detection event model"""
    __tablename__ = "detection_events"

    id = Column(Integer, primary_key=True, index=True)
    agent_id = Column(Integer, ForeignKey("agents.id"), index=True)
    event_type = Column(String(100), nullable=False)
    description = Column(Text)
    severity = Column(Enum(Severity), default=Severity.MEDIUM, index=True)
    detected_at = Column(DateTime, default=datetime.utcnow, index=True)
    rule_matched = Column(String(255))
    action_taken = Column(String(100))
    agent_metadata = Column(JSON, default=dict)

    # Relationships
    agent = relationship("Agent", back_populates="detection_events")

    def __repr__(self):
        return f"<DetectionEvent {self.event_type} ({self.severity})>"
