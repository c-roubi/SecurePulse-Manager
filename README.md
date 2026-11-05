# 🛡️ Secure Remote Administration Framework
## إطار عمل الإدارة البعيدة الآمن والتعليمي

**Educational Cybersecurity Project | Red Team & Blue Team Framework**

---

## 📋 نظرة عامة

هذا المشروع عبارة عن إطار عمل تعليمي شامل لفهم آليات الإدارة البعيدة وكشفها. يتضمن:

- 🔴 **Red Team Module**: عميل وخادم تحكم مركزي آمن
- 🔵 **Blue Team Module**: نظام كشف وتتبع السلوك
- 📊 **Control Dashboard**: لوحة تحكم فعّالة للإدارة
- 🔐 **Security-First**: تشفير شامل وممارسات أمان عالية

---

## 🏗️ العمارة المعمارية

```
┌─────────────────┐
│  Target Agents  │
│   (Rust-based)  │
└────────┬────────┘
         │ TLS 1.3 + WebSocket
         ▼
┌─────────────────┐         ┌──────────────┐
│  C2 Server      │◄────────┤  Dashboard   │
│  (FastAPI)      │         │  (React.js)  │
└────────┬────────┘         └──────────────┘
         │
         ▼
┌─────────────────────────────┐
│  Detection Module (Python)  │
│  - Network Analysis         │
│  - Behavioral Monitoring    │
│  - YARA/Sigma Detection     │
└─────────────────────────────┘
```

---

## 🛠️ المكودس التقني

| المكون | اللغة | الإطار | الإصدار |
|--------|-------|--------|---------|
| **Agent** | Rust | tokio | 2024.11 |
| **Server** | Python | FastAPI | 3.11+ |
| **Dashboard** | TypeScript | React 18 | 18+ |
| **Detection** | Python | Custom | 3.11+ |
| **Database** | PostgreSQL | - | 15+ |

---

## 📁 هيكل المشروع

```
secure-remote-admin/
├── agent/                      # Rust Agent
│   ├── src/
│   │   ├── main.rs
│   │   ├── communicator.rs
│   │   ├── executor.rs
│   │   └── crypto.rs
│   ├── Cargo.toml
│   └── Dockerfile
│
├── server/                     # FastAPI Server
│   ├── app/
│   │   ├── main.py
│   │   ├── api/
│   │   ├── models/
│   │   └── services/
│   ├── requirements.txt
│   └── Dockerfile
│
├── dashboard/                  # React Dashboard
│   ├── src/
│   │   ├── components/
│   │   ├── services/
│   │   ├── store/
│   │   └── App.tsx
│   ├── package.json
│   └── Dockerfile
│
├── detection/                  # Detection Module
│   ├── detection/
│   │   ├── network_analyzer.py
│   │   ├── behavior_monitor.py
│   │   └── detection_engine.py
│   ├── rules/
│   │   ├── yara_rules/
│   │   └── sigma_rules/
│   ├── requirements.txt
│   └── Dockerfile
│
├── database/                   # Database Schemas
│   └── schema.sql
│
├── docs/                       # Documentation
│   ├── ARCHITECTURE.md
│   ├── API.md
│   └── INSTALLATION.md
│
├── docker-compose.yml          # Local Development
├── .env.example                # Environment Template
├── .gitignore
└── README.md
```

---

## 🚀 البدء السريع

### المتطلبات المسبقة

- Docker & Docker Compose
- Git
- (Optional) Rust 1.70+, Python 3.11+, Node.js 18+

### التثبيت والتشغيل

1. **استنساخ المستودع:**
```bash
cd /home/user/secure-remote-admin
```

2. **إعداد متغيرات البيئة:**
```bash
cp .env.example .env
```

3. **تشغيل جميع الخدمات:**
```bash
docker-compose up -d
```

4. **الوصول إلى الخدمات:**
- 📊 Dashboard: http://localhost:5173
- 🔌 API: http://localhost:8000/api
- 🗄️ Database: localhost:5432
- 🔍 Detection: http://localhost:8001

---

## 🔐 الأمان والتشفير

### بروتوكول الاتصالات
- **Transport Layer**: TLS 1.3
- **Application Layer**: AES-256-GCM
- **Authentication**: HMAC-SHA256 + JWT

### أفضل الممارسات
- ✅ تشفير end-to-end
- ✅ Pre-shared Keys للعملاء
- ✅ جلسات آمنة مع expiration
- ✅ Rate limiting و CORS
- ✅ Input validation شاملة

---

## 📚 التوثيق

- [المعمارية التفصيلية](docs/ARCHITECTURE.md)
- [توثيق API](docs/API.md)
- [دليل التثبيت](docs/INSTALLATION.md)
- [دليل الأمان](docs/SECURITY.md)

---

## 📊 خطة التطوير (12 أسبوع)

| # | المرحلة | المدة | الحالة |
|---|--------|------|--------|
| 1 | الإعداد والبيئة | أسبوع | ⏳ قادمة |
| 2 | تطوير العميل | أسبوعان | ⏳ قادمة |
| 3 | تطوير الخادم | أسبوعان | ⏳ قادمة |
| 4 | لوحة التحكم | أسبوعان | ⏳ قادمة |
| 5 | وحدة الكشف | أسبوعان | ⏳ قادمة |
| 6 | الاختبار والتوثيق | أسبوعان | ⏳ قادمة |

---

## ✅ المتطلبات الأخلاقية والقانونية

### نطاق الاستخدام
- ✅ بيئة مختبر معزولة محلية فقط
- ✅ تحت إشراف أكاديمي/مؤسسي
- ✅ أغراض تعليمية وبحثية فقط
- ✅ اختبارات الدفاع السيبراني المصرح بها

### عدم الاستخدام في
- ❌ الهجمات على أنظمة حقيقية
- ❌ الأنشطة الجرمية
- ❌ الأغراض غير التعليمية

---

## 🤝 المساهمة

هذا مشروع تعليمي. للمساهمة:

1. Fork المستودع
2. أنشئ branch للميزة الجديدة
3. اكتب اختبارات
4. أرسل Pull Request

---

## 📝 الترخيص

Educational License - للاستخدام التعليمي والبحثي فقط

---

## 📞 التواصل

- 📧 البريد: ropyk801@gmail.com
- 💬 قسم Issues للأسئلة والمقترحات

---

## ⚠️ إخلاء مسؤولية

هذا المشروع يُقدّم لأغراض تعليمية حصراً. المستخدمون مسؤولون كاملة عن استخدامهم له والالتزام بالقوانين المعمول بها.

---

**آخر تحديث:** 2024-11-05  
**الإصدار:** 0.1.0 (في التطوير)
