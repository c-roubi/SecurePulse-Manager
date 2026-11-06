"""Quick test server for credentials API"""

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
import json

app = FastAPI(title="C2 Test Server")

# CORS
origins = ["*"]
app.add_middleware(
    CORSMiddleware,
    allow_origins=origins,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Test credentials data
CREDENTIALS = {
    "agent1": [
        {
            "source": "🌐 Chrome",
            "url": "https://github.com",
            "username": "developer@github.com",
            "password": "ghp_SuperSecretToken2024!",
            "encrypted": True,
            "timestamp": "2024-11-06T12:00:00"
        },
        {
            "source": "🌐 Chrome",
            "url": "https://mail.google.com",
            "username": "myemail@gmail.com",
            "password": "Gmail@AppPassword#2024",
            "encrypted": True,
            "timestamp": "2024-11-06T12:00:00"
        },
        {
            "source": "🌐 Chrome",
            "url": "https://aws.amazon.com",
            "username": "aws_user@company.com",
            "password": "AWS_SecurePass_123456",
            "encrypted": True,
            "timestamp": "2024-11-06T12:00:00"
        },
        {
            "source": "🌐 Edge",
            "url": "https://outlook.office.com",
            "username": "user@company.onmicrosoft.com",
            "password": "Office365_Pass!234",
            "encrypted": True,
            "timestamp": "2024-11-06T12:00:00"
        },
        {
            "source": "🔒 Windows Vault",
            "username": "DOMAIN\\\\Administrator",
            "password": "P@ssw0rd_Secure_123!",
            "encrypted": True,
            "timestamp": "2024-11-06T12:00:00"
        }
    ]
}

@app.get("/api/live/agents")
async def list_agents():
    """List connected agents"""
    return {
        "agents": [
            {"id": "agent1", "hostname": "WORKSTATION-01", "os": "Windows", "status": "online"},
            {"id": "agent2", "hostname": "SERVER-02", "os": "Linux", "status": "online"},
        ]
    }

@app.post("/api/credentials/extract/browser/{agent_id}")
async def extract_browser(agent_id: str):
    """Extract browser credentials"""
    return {
        "status": "success",
        "agent_id": agent_id,
        "type": "browser_credentials",
        "data": "Browser credentials extracted"
    }

@app.post("/api/credentials/extract/system/{agent_id}")
async def extract_system(agent_id: str):
    """Extract system credentials"""
    return {
        "status": "success",
        "agent_id": agent_id,
        "type": "system_credentials",
        "data": "System credentials extracted"
    }

@app.post("/api/credentials/extract/all/{agent_id}")
async def extract_all(agent_id: str):
    """Extract all credentials"""
    return {
        "status": "success",
        "agent_id": agent_id,
        "type": "all_credentials",
        "data": "All credentials extracted"
    }

@app.get("/api/credentials/stats/{agent_id}")
async def get_stats(agent_id: str):
    """Get credential statistics"""
    if agent_id not in CREDENTIALS:
        return {"agent_id": agent_id, "total": 0, "by_source": {}}

    creds = CREDENTIALS[agent_id]
    stats = {}
    for cred in creds:
        source = cred.get('source', 'unknown')
        stats[source] = stats.get(source, 0) + 1

    return {
        "agent_id": agent_id,
        "total": len(creds),
        "by_source": stats
    }

@app.get("/api/docs", tags=["docs"])
async def docs():
    """API Documentation"""
    return {"message": "API is working"}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8000)
