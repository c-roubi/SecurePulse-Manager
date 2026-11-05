-- Users Table
CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    username VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    role VARCHAR(50) DEFAULT 'operator' CHECK (role IN ('admin', 'operator')),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_login TIMESTAMP,
    is_active BOOLEAN DEFAULT TRUE
);

-- Agents Table
CREATE TABLE IF NOT EXISTS agents (
    id SERIAL PRIMARY KEY,
    agent_id UUID UNIQUE NOT NULL,
    hostname VARCHAR(255),
    os_type VARCHAR(50),
    os_version VARCHAR(50),
    architecture VARCHAR(50),
    ip_address VARCHAR(45),
    mac_address VARCHAR(17),
    "user" VARCHAR(255),
    privileges VARCHAR(50),
    first_seen TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_seen TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    status VARCHAR(20) DEFAULT 'INACTIVE' CHECK (status IN ('ACTIVE', 'INACTIVE', 'DEAD')),
    metadata JSONB
);

-- Sessions Table
CREATE TABLE IF NOT EXISTS sessions (
    id SERIAL PRIMARY KEY,
    agent_id INTEGER NOT NULL REFERENCES agents(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    session_token VARCHAR(255) UNIQUE NOT NULL,
    started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    ended_at TIMESTAMP,
    status VARCHAR(20) DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE', 'CLOSED', 'SUSPENDED')),
    command_count INTEGER DEFAULT 0,
    last_activity TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Commands Table
CREATE TABLE IF NOT EXISTS commands (
    id SERIAL PRIMARY KEY,
    session_id INTEGER NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    command_text TEXT NOT NULL,
    status VARCHAR(20) DEFAULT 'QUEUED' CHECK (
        status IN ('QUEUED', 'EXECUTING', 'COMPLETED', 'FAILED', 'CANCELLED')
    ),
    issued_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    executed_at TIMESTAMP,
    completed_at TIMESTAMP,
    output TEXT,
    error_message TEXT,
    execution_time INTEGER  -- milliseconds
);

-- Logs Table
CREATE TABLE IF NOT EXISTS logs (
    id SERIAL PRIMARY KEY,
    agent_id INTEGER REFERENCES agents(id) ON DELETE CASCADE,
    session_id INTEGER REFERENCES sessions(id) ON DELETE CASCADE,
    log_type VARCHAR(50) CHECK (log_type IN ('COMMAND', 'ACTIVITY', 'ERROR', 'INFO')),
    message TEXT NOT NULL,
    "timestamp" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    level VARCHAR(20) DEFAULT 'INFO' CHECK (level IN ('DEBUG', 'INFO', 'WARNING', 'ERROR', 'CRITICAL')),
    metadata JSONB
);

-- Detection Events Table
CREATE TABLE IF NOT EXISTS detection_events (
    id SERIAL PRIMARY KEY,
    agent_id INTEGER REFERENCES agents(id) ON DELETE CASCADE,
    event_type VARCHAR(100),
    description TEXT,
    severity VARCHAR(20) DEFAULT 'MEDIUM' CHECK (severity IN ('LOW', 'MEDIUM', 'HIGH', 'CRITICAL')),
    detected_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    rule_matched VARCHAR(255),
    action_taken VARCHAR(100),
    metadata JSONB
);

-- Indices for Performance
CREATE INDEX idx_agents_status ON agents(status);
CREATE INDEX idx_agents_last_seen ON agents(last_seen);
CREATE INDEX idx_sessions_agent_id ON sessions(agent_id);
CREATE INDEX idx_sessions_user_id ON sessions(user_id);
CREATE INDEX idx_sessions_status ON sessions(status);
CREATE INDEX idx_commands_session_id ON commands(session_id);
CREATE INDEX idx_commands_status ON commands(status);
CREATE INDEX idx_logs_agent_id ON logs(agent_id);
CREATE INDEX idx_logs_timestamp ON logs("timestamp");
CREATE INDEX idx_detection_events_severity ON detection_events(severity);
CREATE INDEX idx_detection_events_detected_at ON detection_events(detected_at);

-- Create User for Application
CREATE USER IF NOT EXISTS c2_app WITH PASSWORD 'c2_password';
GRANT CONNECT ON DATABASE c2_db TO c2_app;
GRANT USAGE ON SCHEMA public TO c2_app;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO c2_app;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO c2_app;
