"""Session management routes"""

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select
from typing import List
from app.database import get_session
from app.models import Session, Agent, User, SessionStatus
from app.schemas import SessionCreate, SessionResponse, SessionUpdate
from app.security import get_current_user, generate_session_token
import uuid

router = APIRouter(prefix="/sessions", tags=["sessions"])


@router.post("/", response_model=SessionResponse)
async def create_session(
    session_create: SessionCreate,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Create a new session"""
    # Verify agent exists
    agent_result = await db.execute(
        select(Agent).where(Agent.id == session_create.agent_id)
    )
    agent = agent_result.scalar_one_or_none()

    if not agent:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Agent not found"
        )

    # Verify user exists
    user_result = await db.execute(
        select(User).where(User.username == username)
    )
    user = user_result.scalar_one_or_none()

    if not user:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="User not found"
        )

    # Create session
    session_token = str(uuid.uuid4())
    db_session = Session(
        agent_id=session_create.agent_id,
        user_id=user.id,
        session_token=session_token,
        status=SessionStatus.ACTIVE
    )

    db.add(db_session)
    await db.commit()
    await db.refresh(db_session)

    return db_session


@router.get("/", response_model=List[SessionResponse])
async def list_sessions(
    skip: int = 0,
    limit: int = 100,
    status_filter: str = None,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """List all sessions"""
    query = select(Session).offset(skip).limit(limit)

    if status_filter:
        try:
            status_enum = SessionStatus(status_filter)
            query = query.where(Session.status == status_enum)
        except ValueError:
            raise HTTPException(400, detail=f"Invalid status: {status_filter}")

    result = await db.execute(query)
    sessions = result.scalars().all()

    return sessions


@router.get("/{session_id}", response_model=SessionResponse)
async def get_session(
    session_id: int,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Get session details"""
    result = await db.execute(
        select(Session).where(Session.id == session_id)
    )
    session = result.scalar_one_or_none()

    if not session:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Session not found"
        )

    return session


@router.patch("/{session_id}", response_model=SessionResponse)
async def update_session(
    session_id: int,
    session_update: SessionUpdate,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Update session"""
    result = await db.execute(
        select(Session).where(Session.id == session_id)
    )
    session = result.scalar_one_or_none()

    if not session:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Session not found"
        )

    if session_update.status:
        session.status = session_update.status

    if session_update.last_activity:
        session.last_activity = session_update.last_activity

    await db.commit()
    await db.refresh(session)

    return session


@router.delete("/{session_id}", status_code=status.HTTP_204_NO_CONTENT)
async def close_session(
    session_id: int,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Close/delete session"""
    result = await db.execute(
        select(Session).where(Session.id == session_id)
    )
    session = result.scalar_one_or_none()

    if not session:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Session not found"
        )

    session.status = SessionStatus.CLOSED
    await db.commit()

    return None
