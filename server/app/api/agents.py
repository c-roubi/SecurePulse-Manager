"""Agent management routes"""

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select
from typing import List
from app.database import get_session
from app.models import Agent, AgentStatus
from app.schemas import AgentResponse, AgentCreate, AgentUpdate
from app.security import get_current_user
from datetime import datetime

router = APIRouter(prefix="/agents", tags=["agents"])


@router.post("/", response_model=AgentResponse)
async def create_agent(
    agent_create: AgentCreate,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Register a new agent"""
    # Check if agent exists
    result = await db.execute(
        select(Agent).where(Agent.agent_id == agent_create.agent_id)
    )
    if result.scalar_one_or_none():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Agent already registered"
        )

    # Create agent
    db_agent = Agent(
        agent_id=agent_create.agent_id,
        hostname=agent_create.hostname,
        os_type=agent_create.os_type,
        os_version=agent_create.os_version,
        architecture=agent_create.architecture,
        ip_address=agent_create.ip_address,
        mac_address=agent_create.mac_address,
        user=agent_create.user,
        privileges=agent_create.privileges,
        status=AgentStatus.ACTIVE
    )

    db.add(db_agent)
    await db.commit()
    await db.refresh(db_agent)

    return db_agent


@router.get("/", response_model=List[AgentResponse])
async def list_agents(
    skip: int = 0,
    limit: int = 100,
    status_filter: str = None,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """List all agents"""
    query = select(Agent).offset(skip).limit(limit)

    if status_filter:
        try:
            status_enum = AgentStatus(status_filter)
            query = query.where(Agent.status == status_enum)
        except ValueError:
            raise HTTPException(400, detail=f"Invalid status: {status_filter}")

    result = await db.execute(query)
    agents = result.scalars().all()

    return agents


@router.get("/{agent_id}", response_model=AgentResponse)
async def get_agent(
    agent_id: int,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Get agent details"""
    result = await db.execute(
        select(Agent).where(Agent.id == agent_id)
    )
    agent = result.scalar_one_or_none()

    if not agent:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Agent not found"
        )

    return agent


@router.patch("/{agent_id}", response_model=AgentResponse)
async def update_agent(
    agent_id: int,
    agent_update: AgentUpdate,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Update agent status"""
    result = await db.execute(
        select(Agent).where(Agent.id == agent_id)
    )
    agent = result.scalar_one_or_none()

    if not agent:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Agent not found"
        )

    if agent_update.status:
        agent.status = agent_update.status

    agent.last_seen = agent_update.last_seen or datetime.utcnow()

    await db.commit()
    await db.refresh(agent)

    return agent
