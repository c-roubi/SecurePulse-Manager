"""Monitoring and dashboard routes"""

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select, func
from app.database import get_session
from app.models import Agent, Session, Command, DetectionEvent, AgentStatus, CommandStatus, Severity
from app.schemas import DashboardStats, DashboardResponse, DetectionEventResponse
from app.security import get_current_user
from app.agent_manager import agent_manager
from datetime import datetime, timedelta
from typing import List

router = APIRouter(prefix="/monitoring", tags=["monitoring"])


@router.get("/dashboard", response_model=DashboardResponse)
async def get_dashboard(
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Get dashboard statistics - includes live WebSocket agents"""
    live_count = len(agent_manager.active_connections)

    # Count agents from DB
    total_agents_db = await db.execute(select(func.count(Agent.id)))
    total_agents_db = total_agents_db.scalar() or 0

    total_agents = max(total_agents_db, live_count)
    active_agents = live_count

    # Count sessions
    total_sessions = await db.execute(select(func.count(Session.id)))
    total_sessions = total_sessions.scalar() or 0

    active_sessions = await db.execute(
        select(func.count(Session.id)).where(Session.status == "ACTIVE")
    )
    active_sessions = active_sessions.scalar() or 0
    active_sessions = max(active_sessions, live_count)

    # Count commands
    total_commands = await db.execute(select(func.count(Command.id)))
    total_commands = total_commands.scalar() or 0

    completed_commands = await db.execute(
        select(func.count(Command.id)).where(Command.status == CommandStatus.COMPLETED)
    )
    completed_commands = completed_commands.scalar() or 0

    # Count events
    total_events = await db.execute(select(func.count(DetectionEvent.id)))
    total_events = total_events.scalar() or 0

    critical_events = await db.execute(
        select(func.count(DetectionEvent.id)).where(DetectionEvent.severity == Severity.CRITICAL)
    )
    critical_events = critical_events.scalar() or 0

    stats = DashboardStats(
        total_agents=total_agents,
        active_agents=active_agents,
        total_sessions=total_sessions,
        active_sessions=active_sessions,
        total_commands=total_commands,
        completed_commands=completed_commands,
        total_events=total_events,
        critical_events=critical_events
    )

    # Get recent events
    result = await db.execute(
        select(DetectionEvent)
        .order_by(DetectionEvent.detected_at.desc())
        .limit(10)
    )
    recent_events = result.scalars().all()

    # Get recent commands
    result = await db.execute(
        select(Command)
        .order_by(Command.completed_at.desc())
        .limit(10)
    )
    recent_commands = result.scalars().all()

    return DashboardResponse(
        stats=stats,
        recent_events=recent_events,
        recent_commands=recent_commands
    )


@router.get("/agents/active", response_model=int)
async def get_active_agents_count(
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Get active agents count"""
    result = await db.execute(
        select(func.count(Agent.id)).where(Agent.status == AgentStatus.ACTIVE)
    )
    count = result.scalar() or 0
    return count


@router.get("/sessions/active", response_model=int)
async def get_active_sessions_count(
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Get active sessions count"""
    result = await db.execute(
        select(func.count(Session.id)).where(Session.status == "ACTIVE")
    )
    count = result.scalar() or 0
    return count


@router.get("/events/critical", response_model=List[DetectionEventResponse])
async def get_critical_events(
    limit: int = 50,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Get critical detection events"""
    result = await db.execute(
        select(DetectionEvent)
        .where(DetectionEvent.severity == Severity.CRITICAL)
        .order_by(DetectionEvent.detected_at.desc())
        .limit(limit)
    )
    events = result.scalars().all()
    return events


@router.get("/commands/pending", response_model=int)
async def get_pending_commands_count(
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Get pending commands count"""
    result = await db.execute(
        select(func.count(Command.id)).where(
            Command.status.in_([CommandStatus.QUEUED, CommandStatus.EXECUTING])
        )
    )
    count = result.scalar() or 0
    return count
