"""Pydantic schemas for request/response validation"""

from pydantic import BaseModel, EmailStr, Field
from typing import Optional, List, Dict, Any
from datetime import datetime
from app.models import UserRole, AgentStatus, SessionStatus, CommandStatus


# ============ User Schemas ============

class UserBase(BaseModel):
    """Base user schema"""
    username: str = Field(..., min_length=3, max_length=255)
    email: EmailStr
    role: UserRole = UserRole.OPERATOR


class UserCreate(UserBase):
    """User creation schema"""
    password: str = Field(..., min_length=8, max_length=255)


class UserUpdate(BaseModel):
    """User update schema"""
    email: Optional[EmailStr] = None
    role: Optional[UserRole] = None
    password: Optional[str] = Field(None, min_length=8)


class UserResponse(UserBase):
    """User response schema"""
    id: int
    created_at: datetime
    last_login: Optional[datetime] = None
    is_active: bool

    class Config:
        from_attributes = True


# ============ Agent Schemas ============

class AgentBase(BaseModel):
    """Base agent schema"""
    agent_id: str
    hostname: str
    os_type: str
    os_version: str
    architecture: str


class AgentCreate(AgentBase):
    """Agent creation schema"""
    ip_address: Optional[str] = None
    mac_address: Optional[str] = None
    user: Optional[str] = None
    privileges: Optional[str] = None


class AgentUpdate(BaseModel):
    """Agent update schema"""
    last_seen: datetime = Field(default_factory=datetime.utcnow)
    status: Optional[AgentStatus] = None
    metadata: Optional[Dict[str, Any]] = None


class AgentResponse(AgentBase):
    """Agent response schema"""
    id: int
    ip_address: Optional[str] = None
    mac_address: Optional[str] = None
    user: Optional[str] = None
    privileges: Optional[str] = None
    first_seen: datetime
    last_seen: datetime
    status: AgentStatus
    metadata: Dict[str, Any]

    class Config:
        from_attributes = True


# ============ Session Schemas ============

class SessionBase(BaseModel):
    """Base session schema"""
    agent_id: int
    user_id: int


class SessionCreate(SessionBase):
    """Session creation schema"""
    pass


class SessionUpdate(BaseModel):
    """Session update schema"""
    status: Optional[SessionStatus] = None
    last_activity: datetime = Field(default_factory=datetime.utcnow)


class SessionResponse(SessionBase):
    """Session response schema"""
    id: int
    session_token: str
    started_at: datetime
    ended_at: Optional[datetime] = None
    status: SessionStatus
    command_count: int
    last_activity: datetime

    class Config:
        from_attributes = True


# ============ Command Schemas ============

class CommandBase(BaseModel):
    """Base command schema"""
    command_text: str = Field(..., min_length=1, max_length=10000)
    session_id: int


class CommandCreate(CommandBase):
    """Command creation schema"""
    pass


class CommandUpdate(BaseModel):
    """Command update schema"""
    status: Optional[CommandStatus] = None
    output: Optional[str] = None
    error_message: Optional[str] = None
    execution_time: Optional[int] = None


class CommandResponse(CommandBase):
    """Command response schema"""
    id: int
    status: CommandStatus
    issued_at: datetime
    executed_at: Optional[datetime] = None
    completed_at: Optional[datetime] = None
    output: Optional[str] = None
    error_message: Optional[str] = None
    execution_time: Optional[int] = None

    class Config:
        from_attributes = True


# ============ Authentication Schemas ============

class TokenRequest(BaseModel):
    """Token request schema"""
    username: str
    password: str


class TokenResponse(BaseModel):
    """Token response schema"""
    access_token: str
    token_type: str = "bearer"
    expires_in: int  # seconds


class TokenPayload(BaseModel):
    """Token payload schema"""
    sub: str  # username
    exp: datetime


# ============ Log Schemas ============

class LogCreate(BaseModel):
    """Log creation schema"""
    agent_id: Optional[int] = None
    session_id: Optional[int] = None
    log_type: str
    message: str
    level: str = "INFO"
    metadata: Optional[Dict[str, Any]] = None


class LogResponse(BaseModel):
    """Log response schema"""
    id: int
    agent_id: Optional[int] = None
    session_id: Optional[int] = None
    log_type: str
    message: str
    timestamp: datetime
    level: str
    metadata: Dict[str, Any]

    class Config:
        from_attributes = True


# ============ Detection Event Schemas ============

class DetectionEventCreate(BaseModel):
    """Detection event creation schema"""
    agent_id: int
    event_type: str
    description: Optional[str] = None
    severity: str = "MEDIUM"
    rule_matched: Optional[str] = None
    action_taken: Optional[str] = None
    metadata: Optional[Dict[str, Any]] = None


class DetectionEventResponse(BaseModel):
    """Detection event response schema"""
    id: int
    agent_id: int
    event_type: str
    description: Optional[str] = None
    severity: str
    detected_at: datetime
    rule_matched: Optional[str] = None
    action_taken: Optional[str] = None
    metadata: Dict[str, Any]

    class Config:
        from_attributes = True


# ============ Dashboard Schemas ============

class DashboardStats(BaseModel):
    """Dashboard statistics schema"""
    total_agents: int
    active_agents: int
    total_sessions: int
    active_sessions: int
    total_commands: int
    completed_commands: int
    total_events: int
    critical_events: int


class DashboardResponse(BaseModel):
    """Dashboard response schema"""
    stats: DashboardStats
    recent_events: List[DetectionEventResponse]
    recent_commands: List[CommandResponse]
