"""File management routes"""

from fastapi import APIRouter, UploadFile, File, HTTPException, status, Depends, Query
from fastapi.responses import FileResponse
import os
import json
from datetime import datetime
from app.security import get_current_user
from app.agent_manager import agent_manager

router = APIRouter(prefix="/files", tags=["files"])

# Base directory for file storage
FILES_DIR = "/tmp/securepulse_files"
os.makedirs(FILES_DIR, exist_ok=True)


@router.post("/upload/{agent_id}")
async def upload_file(
    agent_id: str,
    file: UploadFile = File(...),
    username: str = Depends(get_current_user)
):
    """Upload file to agent"""
    if not agent_manager.is_connected(agent_id):
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Agent not connected"
        )

    try:
        # Save uploaded file
        file_path = os.path.join(FILES_DIR, file.filename)
        os.makedirs(os.path.dirname(file_path), exist_ok=True)

        contents = await file.read()
        with open(file_path, "wb") as f:
            f.write(contents)

        file_size = len(contents)

        # Queue file transfer command
        cmd = {
            "shell": {
                "command": f"cat > /tmp/{file.filename}"
            }
        }
        agent_manager.queue_command(agent_id, json.dumps(cmd))

        return {
            "status": "success",
            "filename": file.filename,
            "size": file_size,
            "uploaded_at": datetime.utcnow().isoformat(),
            "agent_id": agent_id
        }
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=str(e)
        )


@router.get("/download/{agent_id}/{filename}")
async def download_file(
    agent_id: str,
    filename: str,
    username: str = Depends(get_current_user)
):
    """Download file from agent"""
    if not agent_manager.is_connected(agent_id):
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Agent not connected"
        )

    file_path = os.path.join(FILES_DIR, filename)

    if not os.path.exists(file_path):
        # Queue command to fetch file from agent
        cmd = {
            "shell": {
                "command": f"cat /tmp/{filename}"
            }
        }
        agent_manager.queue_command(agent_id, json.dumps(cmd))

        raise HTTPException(
            status_code=status.HTTP_202_ACCEPTED,
            detail="File transfer initiated, check beacons for output"
        )

    return FileResponse(file_path, filename=filename)


@router.get("/list/{agent_id}")
async def list_files(
    agent_id: str,
    path: str = Query("/tmp"),
    username: str = Depends(get_current_user)
):
    """List files on agent"""
    if not agent_manager.is_connected(agent_id):
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Agent not connected"
        )

    # Queue ls command
    cmd = {
        "shell": {
            "command": f"ls -lah {path}"
        }
    }
    agent_manager.queue_command(agent_id, json.dumps(cmd))

    return {
        "status": "pending",
        "command": "ls -lah " + path,
        "agent_id": agent_id,
        "message": "File listing command queued, check agent beacons for output"
    }


@router.delete("/delete/{agent_id}/{filename}")
async def delete_file(
    agent_id: str,
    filename: str,
    username: str = Depends(get_current_user)
):
    """Delete file on agent"""
    if not agent_manager.is_connected(agent_id):
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Agent not connected"
        )

    # Queue rm command
    cmd = {
        "shell": {
            "command": f"rm -f /tmp/{filename}"
        }
    }
    agent_manager.queue_command(agent_id, json.dumps(cmd))

    return {
        "status": "success",
        "filename": filename,
        "deleted_at": datetime.utcnow().isoformat(),
        "agent_id": agent_id,
        "message": "Delete command queued"
    }


@router.get("/info/{agent_id}")
async def get_file_info(
    agent_id: str,
    path: str = Query("/tmp"),
    username: str = Depends(get_current_user)
):
    """Get file system info"""
    if not agent_manager.is_connected(agent_id):
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Agent not connected"
        )

    cmd = {
        "shell": {
            "command": f"df -h {path} && echo '---' && du -sh {path}"
        }
    }
    agent_manager.queue_command(agent_id, json.dumps(cmd))

    return {
        "status": "pending",
        "agent_id": agent_id,
        "command": "df and du",
        "message": "File system info command queued"
    }
