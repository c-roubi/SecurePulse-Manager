# 🚀 Deployment Guide

**Version:** 0.1.0  
**Environment:** Production  
**Last Updated:** 2024-11-05

---

## 📋 Table of Contents

1. [Pre-Deployment Checklist](#pre-deployment-checklist)
2. [Local Development](#local-development)
3. [Docker Deployment](#docker-deployment)
4. [Cloud Deployment](#cloud-deployment)
5. [Configuration](#configuration)
6. [Security Hardening](#security-hardening)
7. [Monitoring & Logging](#monitoring--logging)
8. [Troubleshooting](#troubleshooting)

---

## ✅ Pre-Deployment Checklist

- [ ] All tests passing (unit, integration, security)
- [ ] Security review completed
- [ ] Load testing successful
- [ ] Environment variables configured
- [ ] Database migrations run
- [ ] SSL/TLS certificates ready
- [ ] Firewall rules configured
- [ ] Backup strategy in place
- [ ] Monitoring alerts configured
- [ ] Incident response plan documented

---

## 🏠 Local Development

### Prerequisites

- Docker & Docker Compose (v2.0+)
- Rust 1.70+
- Python 3.11+
- Node.js 18+
- PostgreSQL 15+ (or use Docker)

### Quick Start

```bash
# Clone repository
git clone <repo-url>
cd secure-remote-admin

# Copy environment template
cp .env.example .env

# Build all components
docker-compose build

# Start services
docker-compose up -d

# Initialize database
docker-compose exec server python -m alembic upgrade head

# Verify services
curl http://localhost:8000/health
curl http://localhost:8001/health
```

### Access Points

| Service | URL | Credentials |
|---------|-----|-------------|
| Dashboard | http://localhost:5173 | admin/password123 |
| API Docs | http://localhost:8000/api/docs | N/A |
| Detection | http://localhost:8001/api/docs | N/A |

---

## 🐳 Docker Deployment

### Docker Compose (Recommended for Production)

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:15-alpine
    environment:
      POSTGRES_DB: remote_admin
      POSTGRES_USER: admin
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U admin"]
      interval: 10s
      timeout: 5s
      retries: 5

  server:
    build:
      context: .
      dockerfile: server/Dockerfile
    environment:
      DATABASE_URL: postgresql://admin:${DB_PASSWORD}@postgres:5432/remote_admin
      SECRET_KEY: ${SECRET_KEY}
      ALLOWED_ORIGINS: ${ALLOWED_ORIGINS}
    ports:
      - "8000:8000"
    depends_on:
      postgres:
        condition: service_healthy
    volumes:
      - ./server:/app/server

  detection:
    build:
      context: .
      dockerfile: detection/Dockerfile
    environment:
      LOG_LEVEL: ${LOG_LEVEL}
    ports:
      - "8001:8001"
    volumes:
      - ./detection:/app/detection

  dashboard:
    build:
      context: .
      dockerfile: dashboard/Dockerfile
    environment:
      VITE_API_URL: ${API_URL}
    ports:
      - "5173:5173"
    volumes:
      - ./dashboard:/app/dashboard

volumes:
  postgres_data:
```

### Environment Variables

```bash
# .env.production
DATABASE_URL=postgresql://user:password@db-host:5432/remote_admin
SECRET_KEY=$(openssl rand -hex 32)
ALLOWED_ORIGINS=https://yourdomain.com
API_URL=https://api.yourdomain.com
LOG_LEVEL=INFO
WORKERS=4
```

### Build & Run

```bash
# Production build
docker-compose -f docker-compose.yml -f docker-compose.prod.yml build

# Start with production config
docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d

# View logs
docker-compose logs -f server

# Execute migrations
docker-compose exec server alembic upgrade head
```

---

## ☁️ Cloud Deployment

### AWS ECS (Elastic Container Service)

```bash
# 1. Create ECS cluster
aws ecs create-cluster --cluster-name remote-admin

# 2. Push images to ECR
aws ecr create-repository --repository-name remote-admin-server
aws ecr create-repository --repository-name remote-admin-detection
aws ecr create-repository --repository-name remote-admin-dashboard

# 3. Tag and push images
docker tag remote-admin:server $(aws sts get-caller-identity --query Account --output text).dkr.ecr.us-east-1.amazonaws.com/remote-admin-server:latest
aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin $(aws sts get-caller-identity --query Account --output text).dkr.ecr.us-east-1.amazonaws.com
docker push $(aws sts get-caller-identity --query Account --output text).dkr.ecr.us-east-1.amazonaws.com/remote-admin-server:latest
```

### Kubernetes Deployment

```yaml
# k8s/deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: remote-admin-server
spec:
  replicas: 3
  selector:
    matchLabels:
      app: remote-admin-server
  template:
    metadata:
      labels:
        app: remote-admin-server
    spec:
      containers:
      - name: server
        image: remote-admin:server
        ports:
        - containerPort: 8000
        env:
        - name: DATABASE_URL
          valueFrom:
            secretKeyRef:
              name: remote-admin-secrets
              key: database-url
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health
            port: 8000
          initialDelaySeconds: 10
          periodSeconds: 10
```

---

## ⚙️ Configuration

### Database Configuration

```python
# server/config.py
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker

DATABASE_URL = "postgresql://user:password@host:5432/remote_admin"

engine = create_engine(
    DATABASE_URL,
    pool_size=20,
    max_overflow=40,
    pool_pre_ping=True,
    connect_args={
        "connect_timeout": 10,
        "application_name": "remote_admin"
    }
)
```

### TLS/SSL Configuration

```nginx
# nginx.conf
server {
    listen 443 ssl http2;
    server_name api.yourdomain.com;

    ssl_certificate /etc/ssl/certs/yourdomain.com.crt;
    ssl_certificate_key /etc/ssl/private/yourdomain.com.key;
    ssl_protocols TLSv1.3 TLSv1.2;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;

    location / {
        proxy_pass http://server:8000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /ws {
        proxy_pass http://server:8000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }
}
```

### Firewall Rules

```bash
# Open only necessary ports
ufw allow 22/tcp      # SSH
ufw allow 443/tcp     # HTTPS
ufw allow 80/tcp      # HTTP (redirect to 443)
ufw allow 5432/tcp    # PostgreSQL (internal only)
ufw enable
```

---

## 🔒 Security Hardening

### Pre-Deployment Security

1. **Secrets Management**
```bash
# Use AWS Secrets Manager or similar
export SECRET_KEY=$(openssl rand -hex 32)
export DB_PASSWORD=$(openssl rand -hex 16)
```

2. **Database Security**
```sql
-- Create limited user
CREATE USER app_user WITH PASSWORD 'strong_password';
GRANT CONNECT ON DATABASE remote_admin TO app_user;
GRANT USAGE ON SCHEMA public TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO app_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_user;

-- Enable SSL
ALTER SYSTEM SET ssl = on;
```

3. **Network Security**
- Run services behind WAF (Web Application Firewall)
- Use VPC with private subnets for database
- Implement rate limiting
- Enable DDoS protection

4. **Application Security**
```python
# server/config.py
SECURITY_HEADERS = {
    "Strict-Transport-Security": "max-age=31536000; includeSubDomains",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "X-XSS-Protection": "1; mode=block",
    "Content-Security-Policy": "default-src 'self'",
}

# Enable HTTPS only
SESSION_COOKIE_SECURE = True
SESSION_COOKIE_HTTPONLY = True
SESSION_COOKIE_SAMESITE = "Strict"
```

### Regular Security Maintenance

- [ ] Monthly security updates
- [ ] Quarterly penetration testing
- [ ] Weekly log review
- [ ] Monthly credential rotation
- [ ] Annual infrastructure audit

---

## 📊 Monitoring & Logging

### Prometheus Metrics

```yaml
# prometheus.yml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: 'remote_admin_server'
    static_configs:
      - targets: ['localhost:8000']
  - job_name: 'remote_admin_detection'
    static_configs:
      - targets: ['localhost:8001']
```

### ELK Stack Integration

```bash
# docker-compose.yml (add to services)
elasticsearch:
  image: docker.elastic.co/elasticsearch/elasticsearch:8.0.0
  environment:
    - discovery.type=single-node

logstash:
  image: docker.elastic.co/logstash/logstash:8.0.0
  volumes:
    - ./logstash.conf:/usr/share/logstash/pipeline/logstash.conf

kibana:
  image: docker.elastic.co/kibana/kibana:8.0.0
  ports:
    - "5601:5601"
```

### Log Configuration

```python
# server/logging_config.py
import logging

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler('logs/server.log'),
        logging.StreamHandler()
    ]
)
```

### Alerting

```yaml
# alert.rules
groups:
  - name: remote_admin
    rules:
      - alert: HighErrorRate
        expr: rate(http_requests_total{status=~"5.."}[5m]) > 0.05
        for: 5m
        annotations:
          summary: "High error rate detected"

      - alert: DiskSpaceLow
        expr: node_filesystem_avail_bytes / node_filesystem_size_bytes < 0.1
        annotations:
          summary: "Disk space running low"
```

---

## 🔧 Troubleshooting

### Common Issues

**1. Database Connection Timeout**
```bash
# Check PostgreSQL is running
docker-compose logs postgres

# Test connection
psql -h localhost -U admin -d remote_admin
```

**2. SSL/TLS Certificate Issues**
```bash
# Verify certificate
openssl x509 -in certificate.crt -text -noout

# Check certificate expiration
openssl x509 -enddate -noout -in certificate.crt
```

**3. High Memory Usage**
```bash
# Check container resource usage
docker stats remote-admin-server-1

# Adjust limits in docker-compose.yml
resources:
  limits:
    memory: 1G
    cpus: '1'
```

**4. WebSocket Connection Failures**
```bash
# Check WebSocket endpoint
curl -i -N -H "Connection: Upgrade" -H "Upgrade: websocket" http://localhost:8000/ws/agent

# Verify firewall allows WebSocket
sudo ufw allow 8000/tcp
```

### Debug Mode

```bash
# Enable verbose logging
LOG_LEVEL=DEBUG docker-compose up

# Check logs for specific service
docker-compose logs --tail=100 server

# Interactive debugging
docker-compose exec server python -m pdb <script.py>
```

---

## 📈 Performance Tuning

### Database Optimization

```sql
-- Create indexes
CREATE INDEX idx_sessions_agent_id ON sessions(agent_id);
CREATE INDEX idx_commands_session_id ON commands(session_id);
CREATE INDEX idx_logs_timestamp ON logs(timestamp);

-- Analyze performance
ANALYZE sessions;
EXPLAIN ANALYZE SELECT * FROM commands WHERE session_id = '123';
```

### Caching Strategy

```python
# server/cache.py
from functools import lru_cache
import redis

redis_client = redis.Redis(host='localhost', port=6379)

@lru_cache(maxsize=128)
def get_agent(agent_id: str):
    # Check cache first
    cached = redis_client.get(f"agent:{agent_id}")
    if cached:
        return json.loads(cached)
    # Fetch from DB
    agent = db.query(Agent).filter(Agent.id == agent_id).first()
    redis_client.setex(f"agent:{agent_id}", 3600, json.dumps(agent))
    return agent
```

### Load Balancing

```nginx
# nginx.conf
upstream remote_admin_servers {
    least_conn;
    server server1:8000 weight=1;
    server server2:8000 weight=1;
    server server3:8000 weight=1;
}

server {
    listen 80;
    server_name api.yourdomain.com;

    location / {
        proxy_pass http://remote_admin_servers;
        proxy_set_header Host $host;
    }
}
```

---

## 🔄 Backup & Recovery

### Automated Backups

```bash
#!/bin/bash
# backup.sh
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
DB_NAME="remote_admin"
BACKUP_DIR="/backups"

# Database backup
docker-compose exec postgres pg_dump -U admin $DB_NAME | gzip > $BACKUP_DIR/db_$TIMESTAMP.sql.gz

# Upload to S3
aws s3 cp $BACKUP_DIR/db_$TIMESTAMP.sql.gz s3://backup-bucket/remote-admin/

# Cleanup old backups (keep 30 days)
find $BACKUP_DIR -name "db_*.sql.gz" -mtime +30 -delete
```

### Recovery Procedure

```bash
# Restore from backup
gunzip < /backups/db_20240101_120000.sql.gz | docker-compose exec -T postgres psql -U admin remote_admin

# Verify recovery
docker-compose exec postgres psql -U admin -d remote_admin -c "SELECT COUNT(*) FROM sessions;"
```

---

## 📝 Maintenance Schedule

| Task | Frequency | Owner |
|------|-----------|-------|
| Security updates | As needed | DevOps |
| Database optimization | Monthly | DBA |
| Log rotation | Weekly | DevOps |
| Backup verification | Monthly | DevOps |
| Performance review | Monthly | DevOps |
| Security audit | Quarterly | Security |
| Disaster recovery test | Quarterly | DevOps |

---

## 📞 Support & Escalation

**Level 1 Support:** Check logs and basic troubleshooting
**Level 2 Support:** Configuration and performance tuning
**Level 3 Support:** Security incidents and infrastructure

Contact: security-team@company.com
On-call: See incident management guide
