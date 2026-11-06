import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { AlertCircle, TrendingUp, Activity, Shield, Brain } from 'lucide-react';
import { AIAnalysisService, SecurityAlert, AIInsight } from '../services/aiAnalysis';

export default function AdvancedDashboard() {
  const { t, i18n } = useTranslation();
  const [alerts, setAlerts] = useState<SecurityAlert[]>([]);
  const [insights, setInsights] = useState<AIInsight[]>([]);
  const [predictions, setPredictions] = useState<string[]>([]);
  const [selectedAgent, setSelectedAgent] = useState<string>('all');
  const [reportData, setReportData] = useState<string>('');

  useEffect(() => {
    loadAIAnalysis();
  }, [selectedAgent]);

  const loadAIAnalysis = async () => {
    // Mock data - in production this would fetch real agent data
    const mockAgentLogs = [
      { type: 'command', agentId: selectedAgent, count: 150 },
      { type: 'keylogger', status: 'active', agentId: selectedAgent },
      { type: 'file_operation', agentId: selectedAgent, count: 600 },
    ];

    const mockMetrics = {
      avgResponseTime: 1200,
      encryptionEnabled: true,
      mutationEnabled: true,
      keyLoggerActive: true,
      uptime: 99.8,
    };

    const mockAgentData = {
      memoryUsage: 75,
      commandQueueSize: 250,
      lastBeaconAge: 5000,
      errorRate: 2.5,
    };

    // Generate AI analysis
    const detectedAlerts = AIAnalysisService.detectAnomalies(mockAgentLogs);
    const generatedInsights = AIAnalysisService.generateInsights(mockMetrics);
    const predictedIssues = AIAnalysisService.predictIssues(mockAgentData);

    setAlerts(detectedAlerts);
    setInsights(generatedInsights);
    setPredictions(predictedIssues);

    // Generate report
    const report = AIAnalysisService.generateSecurityReport(selectedAgent, {
      status: 'Online',
      uptime: 99.8,
      totalCommands: 1250,
      successRate: 98.5,
      encryptionEnabled: true,
      mutationEnabled: true,
      keyRatcheting: true,
      lastBeacon: new Date().toLocaleString(),
      activeSessions: 3,
      pendingCommands: 5,
      recommendations: [
        'Continue monitoring encryption parameters',
        'Review file access patterns for optimization',
        'Schedule periodic security audits',
      ],
    });

    setReportData(report);
  };

  const toggleLanguage = () => {
    const newLang = i18n.language === 'en' ? 'ar' : 'en';
    i18n.changeLanguage(newLang);
    localStorage.setItem('language', newLang);
  };

  return (
    <div className={`space-y-6 ${i18n.language === 'ar' ? 'rtl' : 'ltr'}`}>
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-4xl font-bold text-white flex items-center gap-2">
            <Brain className="w-10 h-10 text-purple-400" />
            {t('dashboard')} - {t('app_name')}
          </h1>
          <p className="text-slate-400 mt-2">
            🤖 {i18n.language === 'en' ? 'AI-Powered Security Analysis' : 'تحليل أمني مدعوم بالذكاء الاصطناعي'}
          </p>
        </div>
        <button
          onClick={toggleLanguage}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg font-semibold transition"
        >
          {i18n.language === 'en' ? 'العربية' : 'English'}
        </button>
      </div>

      {/* Security Alerts */}
      {alerts.length > 0 && (
        <div className="bg-slate-800 rounded-lg border border-slate-700 p-6">
          <h2 className="text-xl font-bold text-white mb-4 flex items-center gap-2">
            <AlertCircle className="w-6 h-6 text-red-500" />
            {i18n.language === 'en' ? 'Security Alerts' : 'تنبيهات الأمان'}
          </h2>
          <div className="space-y-3">
            {alerts.map((alert) => (
              <div
                key={alert.id}
                className={`p-4 rounded-lg border-l-4 ${
                  alert.level === 'critical'
                    ? 'bg-red-900 border-red-600 text-red-200'
                    : alert.level === 'high'
                    ? 'bg-orange-900 border-orange-600 text-orange-200'
                    : 'bg-yellow-900 border-yellow-600 text-yellow-200'
                }`}
              >
                <div className="flex items-start justify-between">
                  <div>
                    <p className="font-semibold">{alert.title}</p>
                    <p className="text-sm mt-1 opacity-90">{alert.description}</p>
                    <p className="text-xs mt-2 opacity-75">{alert.recommendation}</p>
                  </div>
                  <span className="text-xs font-bold uppercase px-2 py-1 bg-black bg-opacity-30 rounded">
                    {alert.level}
                  </span>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* AI Insights */}
      <div className="bg-slate-800 rounded-lg border border-slate-700 p-6">
        <h2 className="text-xl font-bold text-white mb-4 flex items-center gap-2">
          <TrendingUp className="w-6 h-6 text-green-400" />
          {i18n.language === 'en' ? 'AI Insights' : 'رؤى الذكاء الاصطناعي'}
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {insights.map((insight, idx) => (
            <div
              key={idx}
              className="bg-slate-700 rounded-lg p-4 border border-slate-600 hover:border-purple-500 transition"
            >
              <div className="flex items-start justify-between mb-2">
                <h3 className="font-semibold text-white">{insight.title}</h3>
                <div className="text-xs bg-purple-600 text-white px-2 py-1 rounded">
                  {insight.confidence}% {i18n.language === 'en' ? 'confidence' : 'ثقة'}
                </div>
              </div>
              <p className="text-slate-300 text-sm">{insight.content}</p>
              <div className="mt-3 text-xs text-slate-400">
                {i18n.language === 'en' ? 'Type' : 'النوع'}: <span className="text-purple-300">{insight.type}</span>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Predictions */}
      {predictions.length > 0 && (
        <div className="bg-slate-800 rounded-lg border border-slate-700 p-6">
          <h2 className="text-xl font-bold text-white mb-4 flex items-center gap-2">
            <Activity className="w-6 h-6 text-blue-400" />
            {i18n.language === 'en' ? 'Predicted Issues' : 'المشاكل المتنبأ بها'}
          </h2>
          <ul className="space-y-2">
            {predictions.map((pred, idx) => (
              <li key={idx} className="text-slate-300 flex items-start gap-2">
                <span className="text-blue-400 mt-1">→</span>
                <span>{pred}</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* Security Report */}
      <div className="bg-slate-900 rounded-lg border border-slate-700 p-6">
        <h2 className="text-xl font-bold text-white mb-4 flex items-center gap-2">
          <Shield className="w-6 h-6 text-green-400" />
          {i18n.language === 'en' ? 'Security Report' : 'تقرير الأمان'}
        </h2>
        <textarea
          value={reportData}
          readOnly
          className="w-full h-64 px-4 py-3 bg-slate-800 text-slate-300 font-mono text-sm rounded border border-slate-600 focus:outline-none"
        />
        <button
          onClick={() => {
            navigator.clipboard.writeText(reportData);
            alert(i18n.language === 'en' ? 'Copied!' : 'تم النسخ!');
          }}
          className="mt-4 px-4 py-2 bg-green-600 hover:bg-green-700 text-white rounded-lg font-semibold transition"
        >
          {i18n.language === 'en' ? 'Copy Report' : 'نسخ التقرير'}
        </button>
      </div>

      {/* Features Info */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <div className="bg-slate-800 rounded-lg p-4 border border-purple-500 border-opacity-50">
          <p className="text-purple-300 font-semibold mb-2">🤖 AI-Powered</p>
          <p className="text-slate-400 text-sm">
            {i18n.language === 'en'
              ? 'Intelligent anomaly detection and threat prediction'
              : 'كشف الشذوذ والتنبؤ بالتهديدات الذكية'}
          </p>
        </div>
        <div className="bg-slate-800 rounded-lg p-4 border border-green-500 border-opacity-50">
          <p className="text-green-300 font-semibold mb-2">🌍 Multi-Language</p>
          <p className="text-slate-400 text-sm">
            {i18n.language === 'en'
              ? 'Full support for Arabic and English interfaces'
              : 'دعم كامل لواجهات عربية وإنجليزية'}
          </p>
        </div>
        <div className="bg-slate-800 rounded-lg p-4 border border-blue-500 border-opacity-50">
          <p className="text-blue-300 font-semibold mb-2">📊 Real-Time</p>
          <p className="text-slate-400 text-sm">
            {i18n.language === 'en'
              ? 'Real-time monitoring and instant alerts'
              : 'المراقبة الفورية والتنبيهات الفورية'}
          </p>
        </div>
      </div>
    </div>
  );
}
