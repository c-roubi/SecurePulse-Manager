# Security Telemetry & Automated Remote Agent Framework (Academic Research Project)

## 📌 Academic Information
* **Student Name:** Khaled Ropy (خالد روبي)
* **Supervisor:** Dr. Sami Al-Natsheh (الدكتور سامي النتشة)
* **Institution:** Al-Quds Open University (جامعة القدس المفتوحة)
* **Faculty:** Faculty of Technology and Applied Sciences - Cyber Security & Information Technology Department
* **Course:** Graduation Project (مشروع تخرج)

---

## 🛡️ Academic Authorization & Ethical Disclaimer

> **IMPORTANT NOTICE:**  
> This project has been developed strictly as a senior graduation thesis for academic assessment, system behavior analysis, and cybersecurity education. All testing, telemetry gathering, and control simulations are conducted in a **strictly controlled, isolated laboratory environment** at Al-Quds Open University under the direct authorization and supervision of **Dr. Sami Al-Natsheh**.
>
> **Ethical Compliance Statement:**
> - This software must NOT be deployed on unauthorized networks or systems.
> - The source code and associated research materials are intended solely for defensive cybersecurity research, detection logic evaluation, and software quality analysis.
> - The author (Khaled Ropy) and the university disclaim any responsibility for unauthorized or illegal use of this codebase.

---

## 🎯 Abstract & Objectives

This framework serves as an educational research prototype designed to demonstrate the architecture of remote endpoint monitoring, network telemetry processing, and resilient asynchronous C# communication models. 

### Key Research Objectives:
1. **Network Telemetry & Resilient Communication:** Implementing `Task`-based asynchronous programming with `CancellationToken` support to ensure non-blocking network streams and operational stability.
2. **Defensive Software Engineering:** Designing structured exception handling (`try-catch` blocks) for file system I/O and Socket operations to prevent system crashes (`UnauthorizedAccessException`, `IOException`).
3. **Endpoint Telemetry & Diagnostics:** Evaluating how operating systems capture system logs, process telemetry, and communicate state back to a centralized monitoring console.

---

## 🏗️ System Architecture

The project follows a standard decoupled Client-Server architecture:

```
+------------------------------------+           +----------------------------------+
|           C2 Server Console        |           |          Remote Telemetry        |
|            (Management UI)         | <=======> |              Agent               |
|  - Asynchronous Socket Listener    |   TCP/IP  |  - Telemetry Collection         |
|  - Session Thread Management       |  Stream   |  - Non-blocking I/O Operations  |
+------------------------------------+           +----------------------------------+
```

### Main Components:
* **Server Module (`Panel / C2Server`):** Manages inbound agent connections, maintains session health, and renders real-time telemetry to the user interface.
* **Agent Module (`Implant / Telemetry Agent`):** A lightweight client execution loop designed to communicate operational status and handle diagnostic commands.
* **Security & Diagnostic Modules:** Implements monitoring capabilities (screen buffer captures, file management diagnostics, and process tracking) to evaluate host performance and security state.

---

## 🔧 Technical Stack & Best Practices

* **Language & Framework:** C# / .NET Framework & .NET Core
* **Asynchronous Pattern:** `async / await` using `CancellationTokenSource` for clean thread disposal.
* **Robust Error Handling:** Multi-tiered exception handling to manage permissions, IO interrupts, and socket disconnects gracefully.
* **Clean Code Standards:** Decoupled modules with dedicated classes for network sockets, crypto operations, and system interactions.

---

## 🧪 Laboratory Setup & Verification

All experiments and test executions take place inside an isolated virtual lab environment:
1. **Hypervisor:** VMware Workstation / VirtualBox Isolated Host-Only Network.
2. **Lab Router:** Dedicated test router environment.
3. **Testing Boundaries:** No traffic is permitted to route outside the designated local lab subnet (`192.168.1.0/24`).

---

## 📄 License

This project is licensed under the **MIT License** for academic and research purposes. See the `LICENSE` file for full terms and conditions.