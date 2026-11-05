"""Command execution routes"""

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select
from typing import List
from app.database import get_session
from app.models import Command, Session as SessionModel, CommandStatus
from app.schemas import CommandCreate, CommandResponse, CommandUpdate
from app.security import get_current_user
from datetime import datetime
import json

router = APIRouter(prefix="/commands", tags=["commands"])


@router.post("/", response_model=CommandResponse)
async def create_command(
    command_create: CommandCreate,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Create and queue a command"""
    # Verify session exists
    result = await db.execute(
        select(SessionModel).where(SessionModel.id == command_create.session_id)
    )
    session = result.scalar_one_or_none()

    if not session:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Session not found"
        )

    # Create command
    db_command = Command(
        session_id=command_create.session_id,
        command_text=command_create.command_text,
        status=CommandStatus.QUEUED
    )

    db.add(db_command)
    session.command_count += 1
    await db.commit()
    await db.refresh(db_command)

    return db_command


@router.get("/session/{session_id}", response_model=List[CommandResponse])
async def list_session_commands(
    session_id: int,
    skip: int = 0,
    limit: int = 100,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """List commands for a session"""
    result = await db.execute(
        select(Command)
        .where(Command.session_id == session_id)
        .offset(skip)
        .limit(limit)
        .order_by(Command.id.desc())
    )
    commands = result.scalars().all()

    return commands


@router.get("/{command_id}", response_model=CommandResponse)
async def get_command(
    command_id: int,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Get command details"""
    result = await db.execute(
        select(Command).where(Command.id == command_id)
    )
    command = result.scalar_one_or_none()

    if not command:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Command not found"
        )

    return command


@router.patch("/{command_id}", response_model=CommandResponse)
async def update_command(
    command_id: int,
    command_update: CommandUpdate,
    username: str = Depends(get_current_user),
    db: AsyncSession = Depends(get_session)
):
    """Update command execution result"""
    result = await db.execute(
        select(Command).where(Command.id == command_id)
    )
    command = result.scalar_one_or_none()

    if not command:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Command not found"
        )

    if command_update.status:
        command.status = command_update.status
    if command_update.output is not None:
        command.output = command_update.output
    if command_update.error_message is not None:
        command.error_message = command_update.error_message
    if command_update.execution_time is not None:
        command.execution_time = command_update.execution_time

    if command.status == CommandStatus.EXECUTING and not command.executed_at:
        command.executed_at = datetime.utcnow()
    elif command.status == CommandStatus.COMPLETED and not command.completed_at:
        command.completed_at = datetime.utcnow()

    await db.commit()
    await db.refresh(command)

    return command
