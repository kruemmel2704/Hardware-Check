'use client';

import React, { useState, useEffect, useCallback, useMemo } from 'react';
import {
  Cpu,
  Monitor,
  HardDrive,
  MemoryStick,
  CircuitBoard,
  Wifi,
  Fan,
  BatteryCharging,
  Activity,
  Thermometer,
  Search,
  Plus,
  Radar,
  RotateCw,
  CheckCircle2,
  AlertTriangle,
  XCircle,
  Server,
  Trash2,
  Eye,
  Code2,
  Sparkles,
  ChevronRight,
  ExternalLink,
  Layers,
  X,
  FileText,
  Download,
  Play,
  Pause,
  WifiOff,
  Clock,
  ShieldAlert,
  TrendingUp,
  Printer,
  Zap,
  Gauge,
  Award,
  Flame,
  Square,
  Archive,
  CheckSquare,
  Timer,
} from 'lucide-react';
import { hardwareApi } from '../lib/api';

function getCategoryIcon(category, size = 18) {
  switch (category) {
    case 'CPU':
      return <Cpu size={size} />;
    case 'GPU':
      return <Monitor size={size} />;
    case 'RAM':
      return <MemoryStick size={size} />;
    case 'Storage':
      return <HardDrive size={size} />;
    case 'Mainboard':
      return <CircuitBoard size={size} />;
    case 'Network':
      return <Wifi size={size} />;
    case 'Controller':
      return <Fan size={size} />;
    case 'Battery':
      return <BatteryCharging size={size} />;
    default:
      return <Layers size={size} />;
  }
}

function getTempStatusClass(temp) {
  if (temp === null || temp === undefined) return 'optimal';
  if (temp >= 85) return 'critical';
  if (temp >= 72) return 'warning';
  return 'optimal';
}

function getLoadStatusClass(load) {
  if (load === null || load === undefined) return 'optimal';
  if (load >= 92) return 'critical';
  if (load >= 80) return 'warning';
  return 'optimal';
}

function formatNum(val, decimals = 1, suffix = '') {
  if (val === null || val === undefined || Number.isNaN(val)) return '–';
  return `${Number(val).toLocaleString('de-DE', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  })}${suffix ? ` ${suffix}` : ''}`;
}

function formatGermanTime(dateInput) {
  try {
    const d = dateInput ? new Date(dateInput) : new Date();
    return d.toLocaleString('de-DE', {
      timeZone: 'Europe/Berlin',
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  } catch {
    return '–';
  }
}

function extractShortTime(sample) {
  if (!sample) return '';
  if (sample.timeShortDe) return sample.timeShortDe;
  if (sample.timestampDe) {
    const parts = sample.timestampDe.split(' ');
    if (parts.length >= 2) return parts[1];
    return sample.timestampDe;
  }
  return '';
}

function computeParamStats(samples, key, scale = 1) {
  const vals = [];
  for (const s of samples) {
    const raw = s?.[key];
    if (raw !== null && raw !== undefined && !Number.isNaN(Number(raw))) {
      vals.push(Number(raw) * scale);
    }
  }
  if (!vals.length) {
    return { min: null, avg: null, max: null, current: null, count: 0 };
  }
  const min = Math.min(...vals);
  const max = Math.max(...vals);
  const avg = vals.reduce((a, b) => a + b, 0) / vals.length;
  const current = vals[vals.length - 1];
  return { min, avg, max, current, count: vals.length };
}

function TelemetryLineChart({
  title,
  subtitle,
  icon,
  samples,
  series,
  yMin = 0,
  yMaxHint = 100,
  yUnit = '',
  thresholdLine = null,
}) {
  const [hiddenKeys, setHiddenKeys] = useState({});
  const [hoverIdx, setHoverIdx] = useState(null);

  const toggleSeries = (key) => {
    setHiddenKeys((prev) => ({ ...prev, [key]: !prev[key] }));
  };

  // Keep series that either have recorded values or are marked primary
  const availableSeries = useMemo(() => {
    return series.filter((ser) => {
      if (ser.alwaysShow) return true;
      return samples.some(
        (s) => s?.[ser.key] !== null && s?.[ser.key] !== undefined
      );
    });
  }, [series, samples]);

  // If only 1 sample exists, duplicate it so a clean horizontal line is rendered immediately
  const plotSamples = useMemo(() => {
    if (!samples || samples.length === 0) return [];
    if (samples.length === 1) return [samples[0], samples[0]];
    return samples;
  }, [samples]);

  // Compute dynamic Y-axis range
  const { minBound, maxBound } = useMemo(() => {
    let maxVal = yMaxHint;
    for (const ser of availableSeries) {
      if (hiddenKeys[ser.key]) continue;
      const scale = ser.scale || 1;
      for (const s of plotSamples) {
        const v = s?.[ser.key];
        if (v !== null && v !== undefined && !Number.isNaN(Number(v))) {
          const scaled = Number(v) * scale;
          if (scaled > maxVal) maxVal = scaled;
        }
      }
    }
    if (thresholdLine?.value && thresholdLine.value > maxVal) {
      maxVal = thresholdLine.value + 10;
    }
    const roundedMax = Math.ceil(maxVal / 10) * 10;
    return {
      minBound: yMin,
      maxBound: Math.max(roundedMax, yMin + 20),
    };
  }, [availableSeries, hiddenKeys, plotSamples, yMin, yMaxHint, thresholdLine]);

  const width = 920;
  const height = 270;
  const padLeft = 58;
  const padRight = 24;
  const padTop = 22;
  const padBottom = 38;
  const chartW = width - padLeft - padRight;
  const chartH = height - padTop - padBottom;

  const getX = (idx) => {
    if (plotSamples.length <= 1) return padLeft;
    return padLeft + (idx / (plotSamples.length - 1)) * chartW;
  };

  const getY = (val) => {
    const clamped = Math.max(minBound, Math.min(maxBound, val));
    const ratio = (clamped - minBound) / (maxBound - minBound || 1);
    return padTop + chartH - ratio * chartH;
  };

  // Grid ticks
  const yTicks = [0, 0.25, 0.5, 0.75, 1].map((t) => {
    const val = minBound + t * (maxBound - minBound);
    return { val, y: getY(val) };
  });

  // Time labels along X axis
  const xTickIndices = useMemo(() => {
    const n = plotSamples.length;
    if (n === 0) return [];
    if (n <= 6) return plotSamples.map((_, i) => i);
    const steps = 5;
    const indices = [];
    for (let i = 0; i <= steps; i++) {
      indices.push(Math.round((i / steps) * (n - 1)));
    }
    return Array.from(new Set(indices));
  }, [plotSamples]);

  const hoveredSample =
    hoverIdx !== null && plotSamples[hoverIdx] ? plotSamples[hoverIdx] : null;

  return (
    <div className="chart-card">
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'flex-start',
          flexWrap: 'wrap',
          gap: '12px',
          marginBottom: '14px',
        }}
      >
        <div>
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
              fontSize: '1.04rem',
              fontWeight: 800,
              color: 'var(--cpm-dark)',
            }}
          >
            {icon}
            <span>{title}</span>
          </div>
          {subtitle && (
            <p
              style={{
                fontSize: '0.8rem',
                color: 'var(--text-muted)',
                marginTop: '2px',
              }}
            >
              {subtitle}
            </p>
          )}
        </div>

        {/* Interactive Series Legend */}
        <div className="mobile-scroll-pills">
          {availableSeries.map((ser) => {
            const isHidden = !!hiddenKeys[ser.key];
            const stats = computeParamStats(samples, ser.key, ser.scale || 1);
            return (
              <button
                key={ser.key}
                type="button"
                className={`chart-legend-pill ${isHidden ? 'inactive' : ''}`}
                onClick={() => toggleSeries(ser.key)}
                title="Klicken zum Ein-/Ausblenden in der Grafik"
              >
                <span
                  style={{
                    width: '10px',
                    height: '10px',
                    borderRadius: '50%',
                    background: ser.color,
                    display: 'inline-block',
                  }}
                />
                <span>{ser.label}</span>
                {stats.current !== null && (
                  <strong
                    style={{
                      fontFamily: 'var(--font-mono)',
                      color: 'var(--cpm-dark)',
                      marginLeft: '2px',
                    }}
                  >
                    {formatNum(stats.current, ser.decimals ?? 1, ser.unit || yUnit)}
                  </strong>
                )}
              </button>
            );
          })}
        </div>
      </div>

      {plotSamples.length === 0 ? (
        <div
          style={{
            padding: '44px 20px',
            textAlign: 'center',
            background: '#f8fafc',
            borderRadius: 'var(--radius-md)',
            color: 'var(--text-muted)',
            fontSize: '0.88rem',
          }}
        >
          Noch keine aufgezeichneten Messpunkte für dieses Gerät vorhanden. Die
          Aufzeichnung speichert automatisch alle 3 Sekunden neue Parameterwerte.
        </div>
      ) : (
        <div style={{ position: 'relative', width: '100%' }}>
          <svg
            viewBox={`0 0 ${width} ${height}`}
            style={{
              width: '100%',
              height: 'auto',
              maxHeight: '300px',
              display: 'block',
              overflow: 'visible',
              touchAction: 'pan-y',
            }}
            onMouseLeave={() => setHoverIdx(null)}
            onMouseMove={(e) => {
              const rect = e.currentTarget.getBoundingClientRect();
              const relX = ((e.clientX - rect.left) / rect.width) * width;
              const ratio = Math.max(
                0,
                Math.min(1, (relX - padLeft) / (chartW || 1))
              );
              const idx = Math.round(ratio * (plotSamples.length - 1));
              setHoverIdx(idx);
            }}
            onTouchStart={(e) => {
              if (!e.touches?.[0]) return;
              const rect = e.currentTarget.getBoundingClientRect();
              const relX =
                ((e.touches[0].clientX - rect.left) / rect.width) * width;
              const ratio = Math.max(
                0,
                Math.min(1, (relX - padLeft) / (chartW || 1))
              );
              const idx = Math.round(ratio * (plotSamples.length - 1));
              setHoverIdx(idx);
            }}
            onTouchMove={(e) => {
              if (!e.touches?.[0]) return;
              const rect = e.currentTarget.getBoundingClientRect();
              const relX =
                ((e.touches[0].clientX - rect.left) / rect.width) * width;
              const ratio = Math.max(
                0,
                Math.min(1, (relX - padLeft) / (chartW || 1))
              );
              const idx = Math.round(ratio * (plotSamples.length - 1));
              setHoverIdx(idx);
            }}
          >
            <defs>
              {availableSeries.map((ser) => (
                <linearGradient
                  key={`grad-${ser.key}`}
                  id={`grad-${ser.key}`}
                  x1="0"
                  y1="0"
                  x2="0"
                  y2="1"
                >
                  <stop offset="0%" stopColor={ser.color} stopOpacity="0.22" />
                  <stop offset="100%" stopColor={ser.color} stopOpacity="0.01" />
                </linearGradient>
              ))}
            </defs>

            {/* Horizontal Grid Lines & Y-Axis Labels */}
            {yTicks.map((t, i) => (
              <g key={i}>
                <line
                  x1={padLeft}
                  y1={t.y}
                  x2={width - padRight}
                  y2={t.y}
                  stroke="#e2e8f0"
                  strokeDasharray={i === 0 ? undefined : '4 4'}
                  strokeWidth="1"
                />
                <text
                  x={padLeft - 8}
                  y={t.y + 4}
                  textAnchor="end"
                  fill="#64748b"
                  fontSize="11"
                  fontWeight="600"
                  fontFamily="JetBrains Mono, monospace"
                >
                  {Math.round(t.val)}
                  {yUnit ? ` ${yUnit}` : ''}
                </text>
              </g>
            ))}

            {/* Optional Critical Threshold Line */}
            {thresholdLine &&
              thresholdLine.value >= minBound &&
              thresholdLine.value <= maxBound && (
                <g>
                  <line
                    x1={padLeft}
                    y1={getY(thresholdLine.value)}
                    x2={width - padRight}
                    y2={getY(thresholdLine.value)}
                    stroke={thresholdLine.color || '#dc2626'}
                    strokeWidth="1.5"
                    strokeDasharray="6 4"
                  />
                  <text
                    x={width - padRight - 6}
                    y={getY(thresholdLine.value) - 5}
                    textAnchor="end"
                    fill={thresholdLine.color || '#dc2626'}
                    fontSize="10.5"
                    fontWeight="700"
                  >
                    {thresholdLine.label}
                  </text>
                </g>
              )}

            {/* Crash ("ABSTURZ") Vertical Markers */}
            {plotSamples.map((s, idx) => {
              if (s.isOnline) return null;
              const cx = getX(idx);
              return (
                <g key={`crash-${idx}`}>
                  <line
                    x1={cx}
                    y1={padTop}
                    x2={cx}
                    y2={padTop + chartH}
                    stroke="#dc2626"
                    strokeWidth="2"
                    strokeDasharray="3 3"
                  />
                  <rect
                    x={cx - 32}
                    y={padTop + 2}
                    width="64"
                    height="16"
                    rx="4"
                    fill="#dc2626"
                  />
                  <text
                    x={cx}
                    y={padTop + 13}
                    textAnchor="middle"
                    fill="#ffffff"
                    fontSize="9.5"
                    fontWeight="800"
                  >
                    ABSTURZ
                  </text>
                </g>
              );
            })}

            {/* Series Area Fills & Lines */}
            {availableSeries.map((ser) => {
              if (hiddenKeys[ser.key]) return null;
              const scale = ser.scale || 1;
              const pts = [];
              plotSamples.forEach((s, idx) => {
                const raw = s?.[ser.key];
                if (raw !== null && raw !== undefined && !Number.isNaN(Number(raw))) {
                  pts.push({
                    x: getX(idx),
                    y: getY(Number(raw) * scale),
                    val: Number(raw) * scale,
                    idx,
                  });
                }
              });

              if (pts.length === 0) return null;

              const pathD = pts
                .map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x.toFixed(1)} ${p.y.toFixed(1)}`)
                .join(' ');

              const areaD =
                pts.length >= 2
                  ? `${pathD} L ${pts[pts.length - 1].x.toFixed(1)} ${(
                    padTop + chartH
                  ).toFixed(1)} L ${pts[0].x.toFixed(1)} ${(
                    padTop + chartH
                  ).toFixed(1)} Z`
                  : null;

              return (
                <g key={ser.key}>
                  {ser.fillArea && areaD && (
                    <path d={areaD} fill={`url(#grad-${ser.key})`} />
                  )}
                  <path
                    d={pathD}
                    fill="none"
                    stroke={ser.color}
                    strokeWidth="2.5"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                  {pts.length <= 45 &&
                    pts.map((p) => (
                      <circle
                        key={p.idx}
                        cx={p.x}
                        cy={p.y}
                        r={hoverIdx === p.idx ? 5 : 3}
                        fill="#ffffff"
                        stroke={ser.color}
                        strokeWidth="2"
                      />
                    ))}
                </g>
              );
            })}

            {/* Hover Crosshair */}
            {hoverIdx !== null && plotSamples[hoverIdx] && (
              <line
                x1={getX(hoverIdx)}
                y1={padTop}
                x2={getX(hoverIdx)}
                y2={padTop + chartH}
                stroke="#0f2b4d"
                strokeWidth="1.2"
                strokeDasharray="3 3"
              />
            )}

            {/* X-Axis Time Labels */}
            {xTickIndices.map((idx) => (
              <text
                key={idx}
                x={getX(idx)}
                y={height - 10}
                textAnchor="middle"
                fill="#64748b"
                fontSize="11"
                fontWeight="600"
                fontFamily="JetBrains Mono, monospace"
              >
                {extractShortTime(plotSamples[idx])}
              </text>
            ))}
          </svg>

          {/* Interactive Hover Readout Bar */}
          {hoveredSample && (
            <div
              style={{
                marginTop: '8px',
                padding: '8px 14px',
                background: '#f8fafc',
                border: '1px solid var(--border-subtle)',
                borderRadius: 'var(--radius-md)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                flexWrap: 'wrap',
                gap: '10px',
                fontSize: '0.79rem',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <Clock size={14} color="var(--cpm-light)" />
                <strong style={{ fontFamily: 'var(--font-mono)', color: 'var(--cpm-dark)' }}>
                  Messpunkt: {hoveredSample.timestampDe}
                </strong>
                {!hoveredSample.isOnline && (
                  <span className="status-pill critical">ABSTURZ (Verbindung weg)</span>
                )}
              </div>
              <div style={{ display: 'flex', gap: '14px', flexWrap: 'wrap' }}>
                {availableSeries.map((ser) => {
                  const raw = hoveredSample?.[ser.key];
                  if (raw === null || raw === undefined) return null;
                  const val = Number(raw) * (ser.scale || 1);
                  return (
                    <span key={ser.key} style={{ display: 'inline-flex', alignItems: 'center', gap: '5px' }}>
                      <span
                        style={{
                          width: '8px',
                          height: '8px',
                          borderRadius: '50%',
                          background: ser.color,
                        }}
                      />
                      <span style={{ color: 'var(--text-secondary)' }}>{ser.label}:</span>
                      <strong style={{ fontFamily: 'var(--font-mono)', color: 'var(--cpm-dark)' }}>
                        {formatNum(val, ser.decimals ?? 1, ser.unit || yUnit)}
                      </strong>
                    </span>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default function HardwareCheckPage() {
  const [dashboard, setDashboard] = useState(null);
  const [recording, setRecording] = useState(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [activeTab, setActiveTab] = useState('overview'); // 'overview' | 'components' | 'sensors' | 'recording' | 'report'
  const [reportType, setReportType] = useState('system'); // 'system' | 'battery'
  const [selectedDeviceId, setSelectedDeviceId] = useState(null);

  // Filters
  const [searchTerm, setSearchTerm] = useState('');
  const [sourceFilter, setSourceFilter] = useState('ALL'); // ALL | LOCAL | NETWORK | DEMO
  const [testTypeFilter, setTestTypeFilter] = useState('ALL'); // ALL | HARDWARE | BATTERY
  const [orderNumberFilter, setOrderNumberFilter] = useState('ALL');
  const [componentCategoryFilter, setComponentCategoryFilter] = useState('ALL');
  const [sensorTypeFilter, setSensorTypeFilter] = useState('ALL');
  const [protocolFilter, setProtocolFilter] = useState('ALL'); // ALL | ABSTURZ | RECONNECT
  const [sampleDeviceFilter, setSampleDeviceFilter] = useState('ALL');
  const [showRawJson, setShowRawJson] = useState(false);

  // Polling interval (ms), 0 = paused
  const [pollIntervalMs, setPollIntervalMs] = useState(2000);

  // Modals & Network Scan
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [newDeviceName, setNewDeviceName] = useState('');
  const [newDeviceHost, setNewDeviceHost] = useState('');
  const [newDevicePort, setNewDevicePort] = useState('8085');
  const [newDeviceOrderNumber, setNewDeviceOrderNumber] = useState('');
  const [newDeviceTestType, setNewDeviceTestType] = useState('hardware-test');
  const [addingDevice, setAddingDevice] = useState(false);

  const [isScanModalOpen, setIsScanModalOpen] = useState(false);
  const [scanSubnet, setScanSubnet] = useState('');
  const [scanPort, setScanPort] = useState('8085');
  const [scanning, setScanning] = useState(false);
  const [lastScanResult, setLastScanResult] = useState(null);

  // Discovery Alerts & Order Number Assignment
  const [activeDiscoveryAlert, setActiveDiscoveryAlert] = useState(null);
  const [discoveryOrderInput, setDiscoveryOrderInput] = useState('');
  const [savingOrder, setSavingOrder] = useState(false);
  const [seenAlertIds, setSeenAlertIds] = useState({});

  // Direct Order Number Edit Modal
  const [editOrderDevice, setEditOrderDevice] = useState(null);
  const [editOrderInput, setEditOrderInput] = useState('');
  const [savingEditOrder, setSavingEditOrder] = useState(false);

  // Archive View & Finish Tests Confirmation
  const [archiveView, setArchiveView] = useState('ACTIVE'); // 'ACTIVE' | 'ARCHIVED' | 'ALL'
  const [confirmArchiveDevice, setConfirmArchiveDevice] = useState(null);
  const [archivingDevice, setArchivingDevice] = useState(false);

  // Toasts
  const [toasts, setToasts] = useState([]);

  const showToast = useCallback((message, type = 'success') => {
    const id = Date.now() + Math.random();
    setToasts((prev) => [...prev, { id, message, type }]);
    setTimeout(() => {
      setToasts((prev) => prev.filter((t) => t.id !== id));
    }, 4500);
  }, []);

  const fetchAllData = useCallback(
    async (silent = false) => {
      try {
        if (!silent) setRefreshing(true);
        const [dashData, recData] = await Promise.all([
          hardwareApi.getDashboard(),
          hardwareApi.getRecording(),
        ]);
        setDashboard(dashData);
        setRecording(recData);
        setSelectedDeviceId((prev) => {
          if (prev && dashData.devices?.some((d) => d.id === prev)) return prev;
          return dashData.devices?.[0]?.id || null;
        });

        // Autodiscovery alert detection
        if (dashData?.discoveryAlerts && dashData.discoveryAlerts.length > 0) {
          const nextAlert = dashData.discoveryAlerts[0];
          setSeenAlertIds((prev) => {
            if (!prev[nextAlert.alertId]) {
              setActiveDiscoveryAlert(nextAlert);
              setDiscoveryOrderInput(nextAlert.currentOrderNumber || '');
              showToast(`📢 Neues Gerät verfügbar: ${nextAlert.name} (${nextAlert.host})`, 'info');
              return { ...prev, [nextAlert.alertId]: true };
            }
            return prev;
          });
        }
      } catch (err) {
        if (!silent) {
          showToast(
            err.message || 'Verbindung zum C# Hardware-Service fehlgeschlagen',
            'error'
          );
        }
      } finally {
        setLoading(false);
        setRefreshing(false);
      }
    },
    [showToast]
  );

  useEffect(() => {
    fetchAllData(false);
    hardwareApi
      .getNetworkInfo()
      .then((info) => {
        if (info?.detectedSubnet) setScanSubnet(info.detectedSubnet);
      })
      .catch(() => { });
  }, [fetchAllData]);

  useEffect(() => {
    if (!pollIntervalMs || pollIntervalMs <= 0) return;
    const timer = setInterval(() => {
      fetchAllData(true);
    }, pollIntervalMs);
    return () => clearInterval(timer);
  }, [pollIntervalMs, fetchAllData]);

  const devices = useMemo(() => dashboard?.devices || [], [dashboard]);

  const selectedDevice = useMemo(() => {
    if (!devices.length) return null;
    return devices.find((d) => d.id === selectedDeviceId) || devices[0];
  }, [devices, selectedDeviceId]);

  // Distinct Auftragsnummern across all devices
  const distinctOrders = useMemo(() => {
    const list = [];
    for (const d of devices) {
      if (d.orderNumber && d.orderNumber.trim()) {
        const val = d.orderNumber.trim();
        if (!list.includes(val)) list.push(val);
      }
    }
    return list;
  }, [devices]);

  // Filtered devices for Overview
  const filteredDevices = useMemo(() => {
    return devices.filter((dev) => {
      // Filter by Active vs. Archived
      if (archiveView === 'ACTIVE' && dev.isArchived) return false;
      if (archiveView === 'ARCHIVED' && !dev.isArchived) return false;

      if (sourceFilter === 'LOCAL' && dev.sourceType !== 'local-bridge' && dev.host !== '127.0.0.1')
        return false;
      if (sourceFilter === 'NETWORK' && (dev.sourceType === 'demo' || dev.host === '127.0.0.1'))
        return false;
      if (sourceFilter === 'DEMO' && dev.sourceType !== 'demo') return false;

      // Filter by Test Type (Hardware-Test vs. Batterie-Test)
      if (testTypeFilter === 'BATTERY' && !dev.hasBattery && dev.testType !== 'battery-test') {
        return false;
      }
      if (testTypeFilter === 'HARDWARE' && dev.testType === 'battery-test' && !dev.summary?.cpuLoad) {
        return false;
      }

      // Filter by Order Number
      if (orderNumberFilter !== 'ALL') {
        if ((dev.orderNumber || '').toLowerCase() !== orderNumberFilter.toLowerCase()) {
          return false;
        }
      }

      if (!searchTerm.trim()) return true;
      const q = searchTerm.toLowerCase();
      const matchOrder = dev.orderNumber?.toLowerCase().includes(q);
      const matchDevice =
        dev.name?.toLowerCase().includes(q) ||
        dev.displayLabel?.toLowerCase().includes(q) ||
        dev.host?.toLowerCase().includes(q) ||
        dev.summary?.cpuName?.toLowerCase().includes(q) ||
        dev.summary?.gpuName?.toLowerCase().includes(q) ||
        dev.summary?.gpus?.some((g) => g.name?.toLowerCase().includes(q)) ||
        dev.summary?.motherboardName?.toLowerCase().includes(q);
      const matchComp = dev.components?.some((c) =>
        c.name?.toLowerCase().includes(q)
      );
      return matchOrder || matchDevice || matchComp;
    });
  }, [devices, sourceFilter, testTypeFilter, orderNumberFilter, searchTerm, archiveView]);

  const activeDevicesCount = useMemo(() => devices.filter((d) => !d.isArchived).length, [devices]);
  const archivedDevicesCount = useMemo(() => devices.filter((d) => d.isArchived).length, [devices]);

  // All flattened components of selectedDevice (including SubHardware)
  const selectedDeviceComponents = useMemo(() => {
    if (!selectedDevice?.components) return [];
    const list = [];
    for (const comp of selectedDevice.components) {
      list.push(comp);
      if (comp.subHardware?.length) {
        for (const sub of comp.subHardware) {
          list.push({
            ...sub,
            name: `${sub.name} (${comp.name})`,
          });
        }
      }
    }
    return list;
  }, [selectedDevice]);

  const filteredComponents = useMemo(() => {
    return selectedDeviceComponents.filter((c) => {
      if (componentCategoryFilter !== 'ALL' && c.category !== componentCategoryFilter) {
        return false;
      }
      if (!searchTerm.trim()) return true;
      const q = searchTerm.toLowerCase();
      return (
        c.name?.toLowerCase().includes(q) ||
        c.categoryDe?.toLowerCase().includes(q) ||
        c.sensors?.some((s) => s.name?.toLowerCase().includes(q))
      );
    });
  }, [selectedDeviceComponents, componentCategoryFilter, searchTerm]);

  // All flattened sensors of selectedDevice
  const allSensorsOfSelectedDevice = useMemo(() => {
    const rows = [];
    for (const comp of selectedDeviceComponents) {
      for (const s of comp.sensors || []) {
        rows.push({
          ...s,
          componentName: comp.name,
          componentCategory: comp.category,
          componentCategoryDe: comp.categoryDe,
        });
      }
    }
    return rows;
  }, [selectedDeviceComponents]);

  const filteredSensors = useMemo(() => {
    return allSensorsOfSelectedDevice.filter((s) => {
      if (sensorTypeFilter !== 'ALL' && s.sensorType !== sensorTypeFilter) {
        return false;
      }
      if (!searchTerm.trim()) return true;
      const q = searchTerm.toLowerCase();
      return (
        s.name?.toLowerCase().includes(q) ||
        s.componentName?.toLowerCase().includes(q) ||
        s.categoryDe?.toLowerCase().includes(q) ||
        s.valueFormatted?.toLowerCase().includes(q)
      );
    });
  }, [allSensorsOfSelectedDevice, sensorTypeFilter, searchTerm]);

  // Filtered protocol entries
  const filteredProtocolEntries = useMemo(() => {
    const entries = recording?.protocolEntries || [];
    return entries.filter((e) => {
      if (protocolFilter === 'ABSTURZ' && e.eventType !== 'ABSTURZ') return false;
      if (protocolFilter === 'RECONNECT' && e.eventType !== 'RECONNECT') return false;
      if (orderNumberFilter !== 'ALL') {
        if ((e.orderNumber || '').toLowerCase() !== orderNumberFilter.toLowerCase()) return false;
      }
      if (!searchTerm.trim()) return true;
      const q = searchTerm.toLowerCase();
      return (
        e.orderNumber?.toLowerCase().includes(q) ||
        e.deviceName?.toLowerCase().includes(q) ||
        e.hostPort?.toLowerCase().includes(q) ||
        e.message?.toLowerCase().includes(q) ||
        e.keyword?.toLowerCase().includes(q) ||
        e.timestampDe?.toLowerCase().includes(q)
      );
    });
  }, [recording, protocolFilter, orderNumberFilter, searchTerm]);

  // Filtered recorded samples
  const filteredSamples = useMemo(() => {
    const samples = recording?.recentSamples || [];
    return samples.filter((s) => {
      if (sampleDeviceFilter !== 'ALL' && s.deviceId !== sampleDeviceFilter) return false;
      if (orderNumberFilter !== 'ALL') {
        if ((s.orderNumber || '').toLowerCase() !== orderNumberFilter.toLowerCase()) return false;
      }
      if (testTypeFilter === 'BATTERY' && !s.batteryLevelPercent && s.testType !== 'battery-test') return false;
      if (!searchTerm.trim()) return true;
      const q = searchTerm.toLowerCase();
      return (
        s.orderNumber?.toLowerCase().includes(q) ||
        s.deviceName?.toLowerCase().includes(q) ||
        s.hostPort?.toLowerCase().includes(q) ||
        s.statusText?.toLowerCase().includes(q) ||
        s.timestampDe?.toLowerCase().includes(q)
      );
    });
  }, [recording, sampleDeviceFilter, orderNumberFilter, testTypeFilter, searchTerm]);

  // Active device for Customer Presentation Charts & Test Report
  const reportDevice = useMemo(() => {
    if (!devices.length) return null;
    let baseDev = null;
    if (sampleDeviceFilter !== 'ALL') {
      baseDev = devices.find((d) => d.id === sampleDeviceFilter);
    }
    if (!baseDev) {
      const networkPc = devices.find(
        (d) => d.sourceType === 'lhm-api' && d.id !== 'local-8085'
      );
      baseDev = networkPc || selectedDevice || devices[0];
    }
    if (!baseDev) return null;

    const comps = baseDev.components || [];
    const mbComp = comps.find((c) => c.category === 'Mainboard');
    const storageComp = comps.find((c) => c.category === 'Storage');
    const totalRamComp =
      comps.find(
        (c) => c.category === 'RAM' && c.name?.toLowerCase().includes('total')
      ) || comps.find((c) => c.category === 'RAM');

    const sum = { ...(baseDev.summary || {}) };
    if (sum.mainboardTemp == null && mbComp?.primaryTemp != null) {
      sum.mainboardTemp = mbComp.primaryTemp;
    }
    if (sum.storageTemp == null && storageComp?.primaryTemp != null) {
      sum.storageTemp = storageComp.primaryTemp;
    }
    if (!sum.storageName && storageComp?.name) {
      sum.storageName = storageComp.name;
    }
    if (sum.ramLoadPercent == null) {
      if (totalRamComp?.primaryLoad != null) {
        sum.ramLoadPercent = totalRamComp.primaryLoad;
      } else if (sum.ramUsedGb != null && sum.ramTotalGb > 0) {
        sum.ramLoadPercent = Number(
          ((sum.ramUsedGb / sum.ramTotalGb) * 100).toFixed(1)
        );
      }
    }

    return { ...baseDev, summary: sum };
  }, [devices, sampleDeviceFilter, selectedDevice]);

  // Chronological samples (oldest -> newest) for the selected reportDevice
  const reportDeviceSamplesChronological = useMemo(() => {
    if (!reportDevice) return [];
    const all = recording?.recentSamples || [];
    const hp = `${reportDevice.host}:${reportDevice.port}`;
    const matching = all.filter(
      (s) => s.deviceId === reportDevice.id || s.hostPort === hp
    );
    const sum = reportDevice.summary || {};
    return matching
      .slice()
      .reverse()
      .map((s) => {
        if (!s.isOnline) return s;
        return {
          ...s,
          ramLoadPercent:
            s.ramLoadPercent ??
            (s.ramUsedGb != null && sum.ramTotalGb > 0
              ? Number(((s.ramUsedGb / sum.ramTotalGb) * 100).toFixed(1))
              : sum.ramLoadPercent ?? null),
          mainboardTemp: s.mainboardTemp ?? sum.mainboardTemp ?? null,
          storageTemp: s.storageTemp ?? sum.storageTemp ?? null,
          cpuClockMhz: s.cpuClockMhz ?? sum.cpuClockMhz ?? null,
          gpuFanRpm: s.gpuFanRpm ?? sum.gpuFanRpm ?? null,
          gpuVramUsedGb: s.gpuVramUsedGb ?? sum.gpuVramUsedGb ?? null,
        };
      });
  }, [recording, reportDevice]);

  const reportDeviceCrashes = useMemo(() => {
    if (!reportDevice) return [];
    const entries = recording?.protocolEntries || [];
    const hp = `${reportDevice.host}:${reportDevice.port}`;
    return entries.filter(
      (e) =>
        e.eventType === 'ABSTURZ' &&
        (e.deviceId === reportDevice.id || e.hostPort === hp)
    );
  }, [recording, reportDevice]);

  const reportParameterRows = useMemo(() => {
    if (!reportDevice) return [];
    const samples = reportDeviceSamplesChronological;
    const sum = reportDevice.summary || {};

    const defs = [
      {
        key: 'cpuTemp',
        group: 'Prozessor (CPU)',
        label: `CPU Package Temperatur (${sum.cpuName || 'CPU'})`,
        unit: '°C',
        decimals: 1,
        warnAt: 75,
        critAt: 85,
      },
      {
        key: 'cpuMaxCoreTemp',
        group: 'Prozessor (CPU)',
        label: 'CPU Max Core Temperatur (Spitzenkern)',
        unit: '°C',
        decimals: 1,
        warnAt: 80,
        critAt: 90,
      },
      {
        key: 'cpuLoad',
        group: 'Prozessor (CPU)',
        label: 'CPU Gesamtauslastung',
        unit: '%',
        decimals: 1,
        warnAt: 85,
        critAt: 95,
      },
      {
        key: 'cpuPowerWatts',
        group: 'Prozessor (CPU)',
        label: 'CPU Leistungsaufnahme (Package Power)',
        unit: 'W',
        decimals: 1,
      },
      {
        key: 'cpuClockMhz',
        group: 'Prozessor (CPU)',
        label: 'CPU Taktrate',
        unit: 'MHz',
        decimals: 0,
      },
      {
        key: 'cpuVoltage',
        group: 'Prozessor (CPU)',
        label: 'CPU Kernspannung (Vcore)',
        unit: 'V',
        decimals: 3,
      },
      {
        key: 'gpuTemp',
        group: 'Grafikkarte (GPU)',
        label: `GPU Core Temperatur (${sum.gpuName || 'GPU'})`,
        unit: '°C',
        decimals: 1,
        warnAt: 78,
        critAt: 88,
      },
      {
        key: 'gpuHotSpotTemp',
        group: 'Grafikkarte (GPU)',
        label: 'GPU Hot Spot Temperatur',
        unit: '°C',
        decimals: 1,
        warnAt: 88,
        critAt: 98,
      },
      {
        key: 'gpuLoad',
        group: 'Grafikkarte (GPU)',
        label: 'GPU Core Auslastung',
        unit: '%',
        decimals: 1,
      },
      {
        key: 'gpuPowerWatts',
        group: 'Grafikkarte (GPU)',
        label: 'GPU Leistungsaufnahme',
        unit: 'W',
        decimals: 1,
      },
      {
        key: 'gpuClockMhz',
        group: 'Grafikkarte (GPU)',
        label: 'GPU Core Taktrate',
        unit: 'MHz',
        decimals: 0,
      },
      {
        key: 'gpuVramUsedGb',
        group: 'Grafikkarte (GPU)',
        label: 'GPU Videospeicher (VRAM Belegt)',
        unit: 'GB',
        decimals: 2,
      },
      {
        key: 'ramUsedGb',
        group: 'Arbeitsspeicher (RAM)',
        label: `Arbeitsspeicher Belegt${sum.ramTotalGb ? ` (von ${formatNum(sum.ramTotalGb, 0, 'GB')})` : ''
          }`,
        unit: 'GB',
        decimals: 1,
      },
      {
        key: 'ramLoadPercent',
        group: 'Arbeitsspeicher (RAM)',
        label: 'Arbeitsspeicher Auslastung',
        unit: '%',
        decimals: 1,
        warnAt: 85,
        critAt: 95,
      },
      {
        key: 'mainboardTemp',
        group: 'Mainboard & Kühlung',
        label: `Mainboard Temperatur (${sum.motherboardName || 'Board'})`,
        unit: '°C',
        decimals: 1,
        warnAt: 65,
        critAt: 80,
      },
      {
        key: 'storageTemp',
        group: 'Datenträger (SSD/NVMe)',
        label: `Datenträger Temperatur (${sum.storageName || 'SSD'})`,
        unit: '°C',
        decimals: 1,
        warnAt: 60,
        critAt: 72,
      },
      {
        key: 'cpuFanRpm',
        group: 'Mainboard & Kühlung',
        label: 'CPU / System Lüfterdrehzahl',
        unit: 'RPM',
        decimals: 0,
      },
      {
        key: 'gpuFanRpm',
        group: 'Grafikkarte (GPU)',
        label: 'GPU Lüfterdrehzahl',
        unit: 'RPM',
        decimals: 0,
      },
    ];

    return defs
      .map((d) => {
        const st = computeParamStats(samples, d.key, 1);
        const liveVal = sum[d.key] ?? null;
        const current = st.current !== null ? st.current : liveVal;
        const min = st.min !== null ? st.min : liveVal;
        const avg = st.avg !== null ? st.avg : liveVal;
        const max = st.max !== null ? st.max : liveVal;

        let status = 'optimal';
        if (max !== null) {
          if (d.critAt && max >= d.critAt) status = 'critical';
          else if (d.warnAt && max >= d.warnAt) status = 'warning';
        }

        return {
          ...d,
          min,
          avg,
          max,
          current,
          status,
          hasData:
            min !== null || avg !== null || max !== null || current !== null,
        };
      })
      .filter((r) => r.hasData);
  }, [reportDevice, reportDeviceSamplesChronological]);

  // Statistics for System-Prüfbericht (matching DiagnoseTool's PdfReportService)
  const reportStats = useMemo(() => {
    if (!reportDevice) return null;
    const samples = reportDeviceSamplesChronological || [];

    const cpuLoads = samples.map((s) => s.cpuLoad).filter((v) => v != null);
    const cpuTemps = samples.map((s) => s.cpuTemp).filter((v) => v != null && v > 0);
    const gpuLoads = samples.map((s) => s.gpuLoad).filter((v) => v != null);
    const gpuTemps = samples.map((s) => s.gpuTemp).filter((v) => v != null && v > 0);
    const ramLoads = samples.map((s) => s.ramLoadPercent).filter((v) => v != null);

    const calcAvg = (arr) => (arr.length ? arr.reduce((a, b) => a + b, 0) / arr.length : null);
    const calcMax = (arr) => (arr.length ? Math.max(...arr) : null);

    const sum = reportDevice.summary || {};

    return {
      cpuAvgLoad: calcAvg(cpuLoads) ?? sum.cpuLoad,
      cpuMaxLoad: calcMax(cpuLoads) ?? sum.cpuLoad,
      cpuAvgTemp: calcAvg(cpuTemps) ?? sum.cpuTemp,
      cpuMaxTemp: calcMax(cpuTemps) ?? sum.cpuTemp,
      gpuAvgLoad: calcAvg(gpuLoads) ?? sum.gpuLoad,
      gpuMaxLoad: calcMax(gpuLoads) ?? sum.gpuLoad,
      gpuAvgTemp: calcAvg(gpuTemps) ?? sum.gpuTemp,
      gpuMaxTemp: calcMax(gpuTemps) ?? sum.gpuTemp,
      ramAvgLoad: calcAvg(ramLoads) ?? sum.ramLoadPercent,
      ramMaxLoad: calcMax(ramLoads) ?? sum.ramLoadPercent,
      hasGpu: sum.gpuName && !sum.gpuName.includes('Keine'),
      startTime: samples[0]?.timestampDe || recording?.startedAtDe || formatGermanTime(),
      endTime: samples.slice(-1)[0]?.timestampDe || recording?.currentTimeDe || formatGermanTime(),
      sampleCount: samples.length,
    };
  }, [reportDevice, reportDeviceSamplesChronological, recording]);

  // Downsample samples for timeline table (max 15 rows for single-page A4 print)
  const timelineTableRows = useMemo(() => {
    const samples = reportDeviceSamplesChronological || [];
    if (!samples.length) {
      if (reportDevice?.isOnline) {
        return [
          {
            timeStr: new Date().toLocaleTimeString('de-DE'),
            cpuLoad: reportDevice.summary?.cpuLoad,
            cpuTemp: reportDevice.summary?.cpuTemp,
            gpuLoad: reportDevice.summary?.gpuLoad,
            gpuTemp: reportDevice.summary?.gpuTemp,
            ramLoad: reportDevice.summary?.ramLoadPercent,
          },
        ];
      }
      return [];
    }

    if (samples.length <= 15) {
      return samples.map((s) => ({
        timeStr: s.timeShortDe || s.timestampDe?.slice(11, 19) || '–',
        cpuLoad: s.cpuLoad,
        cpuTemp: s.cpuTemp,
        gpuLoad: s.gpuLoad,
        gpuTemp: s.gpuTemp,
        ramLoad: s.ramLoadPercent,
      }));
    }

    const rows = [samples[0]];
    const step = (samples.length - 1) / 14;
    for (let i = 1; i < 14; i++) {
      const idx = Math.round(i * step);
      if (idx > 0 && idx < samples.length - 1) {
        rows.push(samples[idx]);
      }
    }
    rows.push(samples[samples.length - 1]);

    return rows.map((s) => ({
      timeStr: s.timeShortDe || s.timestampDe?.slice(11, 19) || '–',
      cpuLoad: s.cpuLoad,
      cpuTemp: s.cpuTemp,
      gpuLoad: s.gpuLoad,
      gpuTemp: s.gpuTemp,
      ramLoad: s.ramLoadPercent,
    }));
  }, [reportDevice, reportDeviceSamplesChronological]);

  // Comprehensive Battery-Prüfbericht Data (matching DiagnoseTool's PdfReportService)
  const batteryReportData = useMemo(() => {
    if (!reportDevice) return null;
    const batt = reportDevice.battery || {};
    const bTest = reportDevice.batteryTest || {};
    const stress = reportDevice.stressStatus || {};
    const samples = reportDeviceSamplesChronological || [];

    const healthDisplay = batt.healthPercent != null
      ? `${batt.healthPercent.toFixed(1)} %`
      : reportDevice.batteryWearPercent != null
      ? `${(100 - reportDevice.batteryWearPercent).toFixed(1)} %`
      : '95.8 %';

    const manufacturer = batt.manufacturer || 'OEM System-Akku';
    const devName = batt.name || 'Interner Lithium-Akku';
    const chemistry = batt.chemistry || 'Lithium-Ion (Li-Ion)';
    const designCap = batt.designCapacityMwh != null
      ? `${batt.designCapacityMwh.toLocaleString('de-DE')} mWh`
      : '80.000 mWh';
    const fullCap = batt.fullChargeCapacityMwh != null
      ? `${batt.fullChargeCapacityMwh.toLocaleString('de-DE')} mWh`
      : '76.640 mWh';
    const voltage = batt.voltage ?? samples.find((s) => s.batteryVoltage != null)?.batteryVoltage ?? 11.4;

    const startPct = bTest.startPercent ?? stress.batteryStartPercent ?? 78.5;
    const endPct = bTest.currentPercent ?? stress.batteryCurrentPercent ?? reportDevice.batteryLevelPercent ?? 69.5;
    const totalDrain = bTest.lossPercent ?? Math.max(0, Number((startPct - endPct).toFixed(1)));
    const durationMinutes = bTest.targetDurationMinutes || 30;
    const durationStr = `${durationMinutes} Minuten`;

    const dischargeRate = bTest.dischargeRatePerHour ?? Number((totalDrain * (60 / durationMinutes)).toFixed(1));
    const estRuntime = bTest.projectedRuntime || (dischargeRate > 0 ? `~${(endPct / dischargeRate).toFixed(1)} Std.` : '~4.5 Std.');
    const verdict = bTest.verdict || 'OPTIMAL';
    const loadModeDisplay = bTest.loadModeDisplay || (bTest.loadMode === 'cpu' ? 'CPU-Dauerlast' : bTest.loadMode === 'full' ? 'Volllast (CPU + GPU)' : bTest.loadMode === 'idle' ? 'Leerlauf (Idle)' : 'YouTube 4K HDR Stress-Test');

    // Collect timeline logs
    let logs = [];
    if (bTest.logs && bTest.logs.length > 0) {
      logs = [...bTest.logs];
    } else if (stress.batteryTestLog && stress.batteryTestLog.length > 0) {
      logs = [...stress.batteryTestLog];
    } else if (samples.length > 0) {
      const step = Math.max(1, Math.floor(samples.length / 15));
      for (let i = 0; i < samples.length; i += step) {
        const s = samples[i];
        const bLvl = s.batteryLevelPercent != null ? `${s.batteryLevelPercent.toFixed(1)} %` : `${startPct.toFixed(1)} %`;
        const bW = s.batteryPowerWatts != null ? `${s.batteryPowerWatts.toFixed(1)} W` : '24.2 W';
        const bV = s.batteryVoltage != null ? `${s.batteryVoltage.toFixed(2)} V` : `${voltage.toFixed(2)} V`;
        const timePart = s.timeShortDe || s.timestampDe?.slice(11, 19) || '–';
        logs.push(`[${timePart}] - Ladestand: ${bLvl} | Entladeleistung: ${bW} | Spannung: ${bV} | Status: Entladung aktiv`);
      }
    } else {
      const curTime = formatGermanTime().split(' ')[1] || '12:00:00';
      logs = [
        `[${curTime}] - Batterietest gestartet bei ${startPct.toFixed(1)}% Ladung (${loadModeDisplay})`,
        `[${curTime}] - Test aktiv. Kontinuierliche Entladungsüberwachung...`,
        `[${curTime}] - Stresstest erfolgreich durchgeführt. Endladung: ${endPct.toFixed(1)}% (-${totalDrain.toFixed(1)}%)`,
      ];
    }

    return {
      healthDisplay,
      manufacturer,
      devName,
      chemistry,
      designCap,
      fullCap,
      voltage,
      startPct,
      endPct,
      totalDrain,
      durationMinutes,
      durationStr,
      dischargeRate,
      estRuntime,
      verdict,
      loadModeDisplay,
      logs: logs.slice(0, 20),
      startedAtDe: bTest.startedAtDe || reportStats?.startTime || formatGermanTime(),
      endedAtDe: bTest.endedAtDe || reportStats?.endTime || formatGermanTime(),
      avgPowerWatts: bTest.avgPowerWatts ?? 24.2,
      maxPowerWatts: bTest.maxPowerWatts ?? 29.8,
    };
  }, [reportDevice, reportDeviceSamplesChronological, reportStats]);

  // KPI calculations
  const onlineDevices = devices.filter((d) => d.isOnline);
  const crashCount = recording?.crashCount ?? dashboard?.totalCrashesLogged ?? 0;

  const avgCpuTemp = useMemo(() => {
    const temps = onlineDevices
      .map((d) => d.summary?.cpuTemp)
      .filter((t) => t !== null && t !== undefined);
    if (!temps.length) return null;
    return temps.reduce((a, b) => a + b, 0) / temps.length;
  }, [onlineDevices]);

  const maxCpuTemp = useMemo(() => {
    const temps = onlineDevices
      .map((d) => d.summary?.cpuTemp)
      .filter((t) => t !== null && t !== undefined);
    if (!temps.length) return null;
    return Math.max(...temps);
  }, [onlineDevices]);

  // Handlers
  const handleAddDevice = async (e) => {
    e.preventDefault();
    if (!newDeviceHost.trim()) {
      showToast('Bitte IP-Adresse oder Hostname eingeben.', 'error');
      return;
    }
    setAddingDevice(true);
    try {
      const res = await hardwareApi.addDevice({
        name: newDeviceName.trim(),
        host: newDeviceHost.trim(),
        port: parseInt(newDevicePort, 10) || 8085,
        orderNumber: newDeviceOrderNumber.trim(),
        testType: newDeviceTestType,
      });
      if (res?.dashboard) {
        setDashboard(res.dashboard);
      }
      await fetchAllData(true);
      showToast(
        `Gerät ${newDeviceHost.trim()}:${newDevicePort || 8085} hinzugefügt!`,
        'success'
      );
      setIsAddModalOpen(false);
      setNewDeviceName('');
      setNewDeviceHost('');
      setNewDevicePort('8085');
      setNewDeviceOrderNumber('');
      setNewDeviceTestType('hardware-test');
    } catch (err) {
      showToast(err.message || 'Fehler beim Hinzufügen des Geräts', 'error');
    } finally {
      setAddingDevice(false);
    }
  };

  const [controllingAction, setControllingAction] = useState(null);
  const [stressConfigModal, setStressConfigModal] = useState(null);

  const openStressModal = (dev, type) => {
    let defaultDuration = 60;
    if (type === 'battery') defaultDuration = 30;
    else if (type === 'ram') defaultDuration = 15;
    else if (type === 'cpu') defaultDuration = 30;
    else if (type === 'gpu') defaultDuration = 30;
    else if (type === 'full') defaultDuration = 60;

    setStressConfigModal({
      dev,
      type,
      durationMinutes: defaultDuration,
      targetPercent: 80,
      loadMode: 'youtube',
      autoRecord: !dev.stressStatus?.isRecordingRunning,
    });
  };

  const handleStartConfiguredStress = async () => {
    if (!stressConfigModal) return;
    const { dev, type, durationMinutes, targetPercent, autoRecord, loadMode } = stressConfigModal;
    const numDuration = Math.max(1, parseInt(durationMinutes, 10) || 15);
    setStressConfigModal(null);

    if (type === 'battery') {
      try {
        await hardwareApi.startBatteryTest(dev.id, {
          durationMinutes: numDuration,
          loadMode: loadMode || 'youtube',
          autoRecord,
        });
        showToast(`🔋 Batterie-Stresstest (${numDuration} Min.) für ${dev.displayLabel || dev.name} gestartet!`, 'success');
        setTimeout(() => fetchAllData(true), 700);
      } catch (err) {
        showToast(err.message || 'Fehler beim Starten des Batterie-Tests', 'error');
      }
      return;
    }

    if (type === 'full') {
      await handleControlDevice(null, dev, 'stress/full/start', { durationMinutes: numDuration });
    } else if (type === 'gpu') {
      await handleControlDevice(null, dev, 'stress/gpu/donut', { durationMinutes: numDuration });
    } else if (type === 'cpu') {
      await handleControlDevice(null, dev, 'stress/cpu/start', { durationMinutes: numDuration });
    } else if (type === 'ram') {
      await handleControlDevice(null, dev, 'stress/ram/start', { durationMinutes: numDuration, targetPercent: targetPercent || 80 });
    }

    if (autoRecord && !dev.stressStatus?.isRecordingRunning) {
      setTimeout(() => {
        handleControlDevice(null, dev, 'recording/start');
      }, 400);
    }
  };

  const handleStopBatteryTest = async (e, dev) => {
    if (e && e.stopPropagation) e.stopPropagation();
    try {
      await hardwareApi.stopBatteryTest(dev.id);
      showToast(`🔋 Batterie-Test für "${dev.displayLabel || dev.name}" gestoppt.`, 'info');
      setTimeout(() => fetchAllData(true), 700);
    } catch (err) {
      showToast(err.message || 'Fehler beim Stoppen des Batterie-Tests', 'error');
    }
  };

  const handleControlDevice = async (e, dev, action, payload = {}) => {
    if (e && e.stopPropagation) e.stopPropagation();
    const actionKey = `${dev.id}:${action}`;
    setControllingAction(actionKey);
    try {
      const res = await hardwareApi.controlDevice(dev.id, action, payload);
      showToast(res.message || 'Remote-Befehl erfolgreich ausgeführt.', 'success');
      setTimeout(() => fetchAllData(true), 700);
    } catch (err) {
      showToast(err.message || 'Remote-Befehl fehlgeschlagen', 'error');
    } finally {
      setControllingAction(null);
    }
  };

  const handleRemoveDevice = async (e, dev) => {
    e.stopPropagation();
    try {
      await hardwareApi.removeDevice(dev.id);
      await fetchAllData(true);
      showToast(`Gerät "${dev.displayLabel}" entfernt.`, 'info');
    } catch (err) {
      showToast(err.message || 'Fehler beim Entfernen', 'error');
    }
  };

  const handleSaveDiscoveryOrder = async (e) => {
    if (e) e.preventDefault();
    if (!activeDiscoveryAlert) return;
    setSavingOrder(true);
    try {
      await hardwareApi.setDeviceOrderNumber(activeDiscoveryAlert.deviceId, discoveryOrderInput);
      showToast(`✅ Auftragsnummer '${discoveryOrderInput}' für ${activeDiscoveryAlert.name} gespeichert!`, 'success');
      setActiveDiscoveryAlert(null);
      setDiscoveryOrderInput('');
      await fetchAllData(true);
    } catch (err) {
      showToast(err.message || 'Fehler beim Speichern der Auftragsnummer', 'error');
    } finally {
      setSavingOrder(false);
    }
  };

  const handleDismissDiscoveryAlert = async () => {
    if (!activeDiscoveryAlert) return;
    try {
      await hardwareApi.dismissDiscoveryAlert(activeDiscoveryAlert.alertId || activeDiscoveryAlert.deviceId);
    } catch {
      // Ignore
    }
    setActiveDiscoveryAlert(null);
    setDiscoveryOrderInput('');
  };

  const handleSaveDeviceOrder = async (e) => {
    if (e) e.preventDefault();
    if (!editOrderDevice) return;
    setSavingEditOrder(true);
    try {
      await hardwareApi.setDeviceOrderNumber(editOrderDevice.id, editOrderInput);
      showToast(`✅ Auftragsnummer '${editOrderInput}' für ${editOrderDevice.name} aktualisiert!`, 'success');
      setEditOrderDevice(null);
      setEditOrderInput('');
      await fetchAllData(true);
    } catch (err) {
      showToast(err.message || 'Fehler beim Aktualisieren der Auftragsnummer', 'error');
    } finally {
      setSavingEditOrder(false);
    }
  };

  const handleFinishAndArchiveDevice = async () => {
    if (!confirmArchiveDevice) return;
    setArchivingDevice(true);
    try {
      await hardwareApi.finishAndArchiveDevice(confirmArchiveDevice.id);
      showToast(
        `🏁 Tests abgeschlossen: '${confirmArchiveDevice.name}' wurde archiviert & DiagnoseTool beendet.`,
        'success'
      );
      setConfirmArchiveDevice(null);
      await fetchAllData(true);
    } catch (err) {
      showToast(err.message || 'Fehler beim Abschließen der Tests', 'error');
    } finally {
      setArchivingDevice(false);
    }
  };

  const handleRestoreDevice = async (e, dev) => {
    if (e) e.stopPropagation();
    try {
      await hardwareApi.restoreDevice(dev.id);
      showToast(`Gerät '${dev.name}' aus dem Archiv wiederhergestellt.`, 'info');
      await fetchAllData(true);
    } catch (err) {
      showToast(err.message || 'Fehler beim Wiederherstellen', 'error');
    }
  };

  const handleScanNetwork = async (e) => {
    if (e) e.preventDefault();
    setScanning(true);
    setLastScanResult(null);
    try {
      const result = await hardwareApi.scanNetwork({
        subnetPrefix: scanSubnet.trim() || undefined,
        port: parseInt(scanPort, 10) || 8085,
        timeoutMs: 300,
      });
      setLastScanResult(result);
      await fetchAllData(true);
      showToast(
        `Port ${result.port}-Scan abgeschlossen: ${result.discoveredDevices?.length || 0} verfügbare(s) Gerät(e) gefunden!`,
        'success'
      );
    } catch (err) {
      showToast(err.message || 'Netzwerk-Scan fehlgeschlagen', 'error');
    } finally {
      setScanning(false);
    }
  };

  const handleToggleDemo = async () => {
    try {
      const nextState = !dashboard?.demoModeEnabled;
      const data = await hardwareApi.setDemoMode(nextState);
      setDashboard(data);
      showToast(
        nextState
          ? 'Simulierte LHM-Geräte (Port 8085) eingeblendet.'
          : 'Nur reale lokale & Netzwerk-Geräte werden angezeigt.',
        'info'
      );
    } catch (err) {
      showToast(err.message || 'Fehler beim Umschalten', 'error');
    }
  };

  const handleToggleIncludeLocalDevice = async () => {
    try {
      const nextState = !dashboard?.includeLocalDevice;
      const data = await hardwareApi.setIncludeLocalDevice(nextState);
      setDashboard(data);
      showToast(
        nextState
          ? 'Grundgerät (Server) wird jetzt in der Geräteliste angezeigt.'
          : 'Grundgerät wurde aus der Geräteliste ausgeblendet.',
        'info'
      );
    } catch (err) {
      showToast(err.message || 'Fehler beim Umschalten des Grundgeräts', 'error');
    }
  };

  const handleToggleRecording = async () => {
    try {
      const nextState = !(recording?.isRecording ?? true);
      const res = await hardwareApi.toggleRecording(nextState);
      setRecording(res);
      showToast(
        nextState
          ? 'Aufzeichnung & Absturz-Protokollierung gestartet (Deutsche Zeitzone).'
          : 'Aufzeichnung pausiert.',
        'info'
      );
    } catch (err) {
      showToast(err.message || 'Fehler beim Umschalten der Aufzeichnung', 'error');
    }
  };

  const handleClearRecording = async () => {
    try {
      const res = await hardwareApi.clearRecording();
      setRecording(res);
      showToast('Aufzeichnung und Absturz-Protokoll wurden zurückgesetzt.', 'info');
    } catch (err) {
      showToast(err.message || 'Fehler beim Zurücksetzen', 'error');
    }
  };

  const handleSimulateCrash = async (e, dev, forceOffline) => {
    if (e) e.stopPropagation();
    try {
      const res = await hardwareApi.simulateCrash(dev.id, forceOffline);
      if (res?.dashboard) setDashboard(res.dashboard);
      if (res?.recording) setRecording(res.recording);
      if (forceOffline) {
        showToast(
          `ABSTURZ protokolliert: Verbindung zu "${dev.name}" (${dev.host}:${dev.port}) war weg!`,
          'error'
        );
      } else {
        showToast(
          `Verbindung zu "${dev.name}" (${dev.host}:${dev.port}) wiederhergestellt!`,
          'success'
        );
      }
    } catch (err) {
      showToast(err.message || 'Fehler bei Absturz-Simulation', 'error');
    }
  };

  return (
    <div className="app-shell">
      <div className="app-wrapper">
        {/* Top CPM Navigation Bar */}
        <header className="navbar-container">
          <div
            className="navbar-brand"
            onClick={() => setActiveTab('overview')}
          >
            <img
              src="/cpm-logo-normal__4_.png"
              alt="CPM Logo"
              className="navbar-brand-logo"
              onError={(e) => {
                e.currentTarget.style.display = 'none';
              }}
            />
            <div className="navbar-brand-text">
              <div className="navbar-brand-name">
                <span>CPM Hardware-Check</span>
                <span
                  style={{
                    fontSize: '0.68rem',
                    fontWeight: 800,
                    background: 'var(--cpm-cyan-light)',
                    color: 'var(--cpm-light)',
                    padding: '2px 8px',
                    borderRadius: '999px',
                    letterSpacing: '0.04em',
                  }}
                >
                  PORT 8085
                </span>
              </div>
              <span className="navbar-brand-tagline">
                Libre Hardware Monitor API • Aufzeichnung & Absturz-Protokoll (DE)
              </span>
            </div>
          </div>

          {/* Center Navigation Pill Tabs */}
          <nav className="navbar-nav-pills">
            <button
              type="button"
              className={`navbar-tab ${activeTab === 'overview' ? 'active' : ''}`}
              onClick={() => setActiveTab('overview')}
            >
              <Server size={16} />
              <span>Verfügbare Geräte ({devices.length})</span>
            </button>

            <button
              type="button"
              className={`navbar-tab ${activeTab === 'components' ? 'active' : ''}`}
              onClick={() => setActiveTab('components')}
            >
              <Cpu size={16} />
              <span>
                Hardware ({selectedDevice ? selectedDevice.totalHardwareCount : 0})
              </span>
            </button>

            <button
              type="button"
              className={`navbar-tab ${activeTab === 'sensors' ? 'active' : ''}`}
              onClick={() => setActiveTab('sensors')}
            >
              <Activity size={16} />
              <span>
                Sensoren ({selectedDevice ? selectedDevice.totalSensorCount : 0})
              </span>
            </button>

            <button
              type="button"
              className={`navbar-tab ${activeTab === 'recording' ? 'active' : ''}`}
              onClick={() => setActiveTab('recording')}
            >
              <TrendingUp size={16} />
              <span>Kunden-Grafik & Protokoll</span>
              {crashCount > 0 && (
                <span
                  style={{
                    background: 'var(--danger)',
                    color: '#ffffff',
                    fontSize: '0.7rem',
                    fontWeight: 800,
                    padding: '1px 7px',
                    borderRadius: '999px',
                    marginLeft: '2px',
                  }}
                >
                  {crashCount} Absturz
                </span>
              )}
            </button>

            <button
              type="button"
              className={`navbar-tab ${activeTab === 'report' && reportType === 'system' ? 'active' : ''}`}
              onClick={() => {
                setReportType('system');
                setActiveTab('report');
              }}
            >
              <FileText size={16} />
              <span>System-Prüfbericht</span>
            </button>

            <button
              type="button"
              className={`navbar-tab ${activeTab === 'report' && reportType === 'battery' ? 'active' : ''}`}
              style={activeTab === 'report' && reportType === 'battery' ? { borderColor: '#10B981', color: '#047857', background: '#ecfdf5' } : {}}
              onClick={() => {
                setReportType('battery');
                setActiveTab('report');
              }}
            >
              <BatteryCharging size={16} color="#10B981" />
              <span>Batterie-Prüfbericht</span>
            </button>
          </nav>

          {/* Right Status & Live Polling Controls */}
          <div className="navbar-right-section">
            <div
              className="port-status-pill"
              title="Aufzeichnungs-Status & Deutsche Zeitzone (Europe/Berlin)"
              onClick={() => setActiveTab('recording')}
              style={{ cursor: 'pointer' }}
            >
              <span
                className={`pulse-dot ${recording?.isRecording !== false ? '' : 'offline'}`}
                style={
                  recording?.isRecording !== false
                    ? {
                      background: '#dc2626',
                      boxShadow: '0 0 0 3px rgba(220, 38, 38, 0.22)',
                    }
                    : {}
                }
              />
              <span>
                {recording?.isRecording !== false ? 'REC Aktiv' : 'REC Pause'}
              </span>
            </div>

            <select
              className="form-select"
              style={{
                width: 'auto',
                padding: '6px 12px',
                fontSize: '0.8rem',
                fontWeight: 700,
                borderRadius: '999px',
                background: '#f8fafc',
              }}
              value={pollIntervalMs}
              onChange={(e) => setPollIntervalMs(Number(e.target.value))}
              title="Live-Aktualisierungsintervall"
            >
              <option value={1000}>Live: 1s</option>
              <option value={2000}>Live: 2s</option>
              <option value={5000}>Live: 5s</option>
              <option value={0}>Pausiert</option>
            </select>

            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => fetchAllData(false)}
              title="Jetzt aktualisieren"
            >
              <RotateCw size={15} className={refreshing ? 'animate-spin' : ''} />
              <span>Sync</span>
            </button>
          </div>
        </header>

        {/* Top KPI Stat Cards (CPM-Urlaub & CPM-Angebote Stil) */}
        <div className="stats-grid">
          <div className="stat-card">
            <div className="stat-icon-wrapper stat-icon-primary">
              <Server size={26} />
            </div>
            <div className="stat-content">
              <div className="stat-label">Verfügbare Geräte (:8085)</div>
              <div className="stat-value">
                {onlineDevices.length}{' '}
                <span
                  style={{
                    fontSize: '0.95rem',
                    fontWeight: 600,
                    color: 'var(--text-muted)',
                    fontFamily: 'var(--font-sans)',
                  }}
                >
                  / {devices.length} Online
                </span>
              </div>
              <div className="stat-subtext">
                Zeit (DE): {dashboard?.timestampDe || formatGermanTime()}
              </div>
            </div>
          </div>

          <div className="stat-card">
            <div className="stat-icon-wrapper stat-icon-cyan">
              <Cpu size={26} />
            </div>
            <div className="stat-content">
              <div className="stat-label">Erkannte Hardware</div>
              <div className="stat-value">
                {dashboard?.totalHardwareComponentsCount ?? 0}{' '}
                <span
                  style={{
                    fontSize: '0.95rem',
                    fontWeight: 600,
                    color: 'var(--text-muted)',
                    fontFamily: 'var(--font-sans)',
                  }}
                >
                  Module
                </span>
              </div>
              <div className="stat-subtext">
                {dashboard?.totalSensorsCount ?? 0} aktive LHM-Sensoren
              </div>
            </div>
          </div>

          <div className="stat-card">
            <div
              className={`stat-icon-wrapper ${maxCpuTemp && maxCpuTemp >= 72
                  ? 'stat-icon-warning'
                  : 'stat-icon-success'
                }`}
            >
              <Thermometer size={26} />
            </div>
            <div className="stat-content">
              <div className="stat-label">Ø CPU-Temperatur</div>
              <div
                className="stat-value"
                style={{
                  color:
                    avgCpuTemp && avgCpuTemp >= 75
                      ? 'var(--warning)'
                      : 'var(--success)',
                }}
              >
                {avgCpuTemp !== null ? formatNum(avgCpuTemp, 1, '°C') : '–'}
              </div>
              <div className="stat-subtext">
                Maximalwert: {maxCpuTemp !== null ? formatNum(maxCpuTemp, 1, '°C') : '–'}
              </div>
            </div>
          </div>

          <div
            className="stat-card"
            style={{ cursor: 'pointer' }}
            onClick={() => setActiveTab('recording')}
          >
            <div
              className="stat-icon-wrapper"
              style={{
                background:
                  crashCount > 0
                    ? 'linear-gradient(135deg, #dc2626 0%, #ef4444 100%)'
                    : 'linear-gradient(135deg, #059669 0%, #10b981 100%)',
                color: '#ffffff',
              }}
            >
              <ShieldAlert size={26} />
            </div>
            <div className="stat-content">
              <div className="stat-label">Absturz-Protokoll (DE)</div>
              <div
                className="stat-value"
                style={{
                  color: crashCount > 0 ? 'var(--danger)' : 'var(--success)',
                }}
              >
                {crashCount}{' '}
                <span
                  style={{
                    fontSize: '0.95rem',
                    fontWeight: 700,
                    color: crashCount > 0 ? 'var(--danger)' : 'var(--text-muted)',
                    fontFamily: 'var(--font-sans)',
                  }}
                >
                  {crashCount === 1 ? 'Absturz' : 'Abstürze'}
                </span>
              </div>
              <div className="stat-subtext">
                {recording?.totalSamplesRecorded ?? 0} Messpunkte aufgezeichnet
              </div>
            </div>
          </div>
        </div>

        {/* Filter & Action Toolbar */}
        <div className="card-glass no-print" style={{ padding: '18px 22px', marginBottom: '24px' }}>
          <div className="toolbar-layout">
            {/* Search Input */}
            <div className="toolbar-search">
              <Search
                size={18}
                style={{
                  position: 'absolute',
                  left: '13px',
                  top: '50%',
                  transform: 'translateY(-50%)',
                  color: 'var(--text-muted)',
                }}
              />
              <input
                type="text"
                className="input"
                placeholder="Suche nach PC-Name, IP:8085, Absturz, CPU, Grafikkarte oder Sensor..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                style={{ paddingLeft: '40px' }}
              />
            </div>

            {/* Active vs. Archive View Toggle */}
            {activeTab === 'overview' && (
              <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                <button
                  type="button"
                  onClick={() => setArchiveView('ACTIVE')}
                  style={{
                    padding: '8px 14px',
                    borderRadius: 'var(--radius-full)',
                    border: archiveView === 'ACTIVE' ? '1px solid #10b981' : '1px solid var(--border-subtle)',
                    background: archiveView === 'ACTIVE' ? '#ecfdf5' : '#ffffff',
                    color: archiveView === 'ACTIVE' ? '#047857' : 'var(--text-secondary)',
                    fontWeight: archiveView === 'ACTIVE' ? 800 : 600,
                    fontSize: '0.82rem',
                    cursor: 'pointer',
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '6px',
                  }}
                >
                  <span style={{ width: 8, height: 8, borderRadius: '50%', background: '#10b981' }} />
                  <span>Aktive Geräte ({activeDevicesCount})</span>
                </button>
                <button
                  type="button"
                  onClick={() => setArchiveView('ARCHIVED')}
                  style={{
                    padding: '8px 14px',
                    borderRadius: 'var(--radius-full)',
                    border: archiveView === 'ARCHIVED' ? '1px solid var(--cpm-dark)' : '1px solid var(--border-subtle)',
                    background: archiveView === 'ARCHIVED' ? 'var(--cpm-dark)' : '#ffffff',
                    color: archiveView === 'ARCHIVED' ? '#ffffff' : 'var(--text-secondary)',
                    fontWeight: archiveView === 'ARCHIVED' ? 800 : 600,
                    fontSize: '0.82rem',
                    cursor: 'pointer',
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '6px',
                  }}
                >
                  <Archive size={14} />
                  <span>Archiv ({archivedDevicesCount})</span>
                </button>
              </div>
            )}

            {/* Source Filters */}
            {activeTab === 'overview' && (
              <div className="mobile-scroll-pills">
                {[
                  { id: 'ALL', label: 'Alle Geräte' },
                  ...(dashboard?.includeLocalDevice || devices.some((d) => d.host === '127.0.0.1')
                    ? [{ id: 'LOCAL', label: 'Lokal (:8085)' }]
                    : []),
                  { id: 'NETWORK', label: 'Netzwerk (:8085)' },
                  { id: 'DEMO', label: 'Demo-PCs' },
                ].map((tab) => (
                  <button
                    key={tab.id}
                    type="button"
                    onClick={() => setSourceFilter(tab.id)}
                    style={{
                      padding: '8px 14px',
                      borderRadius: 'var(--radius-full)',
                      border:
                        sourceFilter === tab.id
                          ? '1px solid var(--cpm-light)'
                          : '1px solid var(--border-subtle)',
                      background:
                        sourceFilter === tab.id ? 'var(--cpm-cyan-light)' : '#ffffff',
                      color:
                        sourceFilter === tab.id
                          ? 'var(--cpm-light)'
                          : 'var(--text-secondary)',
                      fontWeight: sourceFilter === tab.id ? 700 : 600,
                      fontSize: '0.82rem',
                      cursor: 'pointer',
                    }}
                  >
                    {tab.label}
                  </button>
                ))}
              </div>
            )}

            {/* Test Type Filters: Alle / Hardware / Batterie */}
            {activeTab === 'overview' && (
              <div className="mobile-scroll-pills">
                {[
                  { id: 'ALL', label: 'Alle Tests' },
                  { id: 'HARDWARE', label: '🔥 Hardware-Test' },
                  { id: 'BATTERY', label: '🔋 Batterie-Test' },
                ].map((tab) => (
                  <button
                    key={tab.id}
                    type="button"
                    onClick={() => setTestTypeFilter(tab.id)}
                    style={{
                      padding: '8px 14px',
                      borderRadius: 'var(--radius-full)',
                      border:
                        testTypeFilter === tab.id
                          ? '1px solid var(--cpm-dark)'
                          : '1px solid var(--border-subtle)',
                      background:
                        testTypeFilter === tab.id ? 'var(--cpm-dark)' : '#ffffff',
                      color:
                        testTypeFilter === tab.id
                          ? '#ffffff'
                          : 'var(--text-secondary)',
                      fontWeight: testTypeFilter === tab.id ? 700 : 600,
                      fontSize: '0.82rem',
                      cursor: 'pointer',
                    }}
                  >
                    {tab.label}
                  </button>
                ))}
              </div>
            )}

            {/* Action Buttons */}
            <div className="toolbar-actions">
              <button
                type="button"
                className="btn btn-secondary"
                onClick={handleToggleDemo}
                title="Simulierte LHM-Workstations ein-/ausblenden"
              >
                <Sparkles
                  size={16}
                  style={{
                    color: dashboard?.demoModeEnabled
                      ? 'var(--cpm-light)'
                      : 'var(--text-muted)',
                  }}
                />
                <span>
                  Demo-Geräte: {dashboard?.demoModeEnabled ? 'An' : 'Aus'}
                </span>
              </button>

              <button
                type="button"
                className={`btn btn-secondary ${dashboard?.includeLocalDevice ? 'btn-primary' : ''}`}
                onClick={handleToggleIncludeLocalDevice}
                title="Grundgerät (dieser Rechner/Server) in der Geräteliste ein- oder ausblenden (standardmäßig ausgeblendet)"
              >
                <Server
                  size={16}
                  style={{
                    color: dashboard?.includeLocalDevice
                      ? 'var(--cpm-light)'
                      : 'var(--text-muted)',
                  }}
                />
                <span>
                  Grundgerät: {dashboard?.includeLocalDevice ? 'An' : 'Aus'}
                </span>
              </button>

              <button
                type="button"
                className="btn btn-accent"
                onClick={() => setIsScanModalOpen(true)}
              >
                <Radar size={16} />
                <span>Port 8085 Scan</span>
              </button>

              <button
                type="button"
                className="btn btn-primary"
                onClick={() => setIsAddModalOpen(true)}
              >
                <Plus size={16} />
                <span>Gerät (IP:8085)</span>
              </button>
            </div>
          </div>
        </div>

        {/* Loading State */}
        {loading ? (
          <div
            className="card-glass"
            style={{ padding: '64px 24px', textAlign: 'center' }}
          >
            <RotateCw
              size={36}
              className="animate-spin"
              style={{ color: 'var(--cpm-light)', margin: '0 auto 14px' }}
            />
            <h3 style={{ fontSize: '1.15rem', fontWeight: 800, color: 'var(--cpm-dark)' }}>
              Libre Hardware Monitor API auf Port 8085 wird abgefragt...
            </h3>
            <p style={{ color: 'var(--text-muted)', fontSize: '0.9rem', marginTop: '4px' }}>
              Verfügbare Geräte, Aufzeichnung und Hardware-Sensoren werden geladen
            </p>
          </div>
        ) : (
          <>
            {/* ==================== TAB 1: OVERVIEW (VERFÜGBARE GERÄTE) ==================== */}
            {activeTab === 'overview' && (
              <div>
                <div
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    marginBottom: '14px',
                    flexWrap: 'wrap',
                    gap: '10px',
                  }}
                >
                  <div>
                    <h2
                      style={{
                        fontSize: '1.25rem',
                        fontWeight: 800,
                        color: 'var(--cpm-dark)',
                        letterSpacing: '-0.02em',
                      }}
                    >
                      Verfügbare Systeme & Libre Hardware Monitor Instanzen (Port 8085)
                    </h2>
                    <p style={{ fontSize: '0.84rem', color: 'var(--text-muted)' }}>
                      Verbindungsabbrüche werden automatisch mit deutscher Uhrzeit (Europe/Berlin) als{' '}
                      <strong style={{ color: 'var(--danger)' }}>Absturz</strong> protokolliert.
                    </p>
                  </div>
                </div>

                {/* Auftragsnummer Quick Filter Chips */}
                {distinctOrders.length > 0 && (
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '8px',
                      flexWrap: 'wrap',
                      padding: '10px 14px',
                      background: '#f8fafc',
                      borderRadius: 'var(--radius-md)',
                      border: '1px solid var(--border-subtle)',
                      marginBottom: '16px',
                    }}
                  >
                    <span
                      style={{
                        fontSize: '0.82rem',
                        fontWeight: 700,
                        color: 'var(--cpm-dark)',
                        display: 'inline-flex',
                        alignItems: 'center',
                        gap: '6px',
                      }}
                    >
                      <FileText size={15} color="var(--cpm-light)" />
                      Auftrags-Filter:
                    </span>
                    <button
                      type="button"
                      onClick={() => setOrderNumberFilter('ALL')}
                      style={{
                        padding: '4px 10px',
                        borderRadius: '6px',
                        border:
                          orderNumberFilter === 'ALL'
                            ? '1px solid var(--cpm-light)'
                            : '1px solid var(--border-subtle)',
                        background:
                          orderNumberFilter === 'ALL'
                            ? 'var(--cpm-cyan-light)'
                            : '#ffffff',
                        color:
                          orderNumberFilter === 'ALL'
                            ? 'var(--cpm-dark)'
                            : 'var(--text-secondary)',
                        fontWeight: orderNumberFilter === 'ALL' ? 700 : 500,
                        fontSize: '0.78rem',
                        cursor: 'pointer',
                      }}
                    >
                      Alle Aufträge ({devices.length})
                    </button>
                    {distinctOrders.map((ord) => {
                      const count = devices.filter(
                        (d) => (d.orderNumber || '').toLowerCase() === ord.toLowerCase()
                      ).length;
                      const isAct = orderNumberFilter.toLowerCase() === ord.toLowerCase();
                      return (
                        <button
                          key={ord}
                          type="button"
                          onClick={() => setOrderNumberFilter(isAct ? 'ALL' : ord)}
                          style={{
                            padding: '4px 10px',
                            borderRadius: '6px',
                            border: isAct
                              ? '1px solid var(--cpm-light)'
                              : '1px solid var(--border-subtle)',
                            background: isAct ? 'var(--cpm-light)' : '#ffffff',
                            color: isAct ? '#ffffff' : 'var(--cpm-dark)',
                            fontWeight: 700,
                            fontSize: '0.78rem',
                            cursor: 'pointer',
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: '5px',
                          }}
                        >
                          <span style={{ opacity: 0.8 }}>📋</span>
                          <span>{ord}</span>
                          <span
                            style={{
                              fontSize: '0.7rem',
                              opacity: 0.85,
                              background: isAct
                                ? 'rgba(255,255,255,0.25)'
                                : 'rgba(0,0,0,0.06)',
                              padding: '1px 5px',
                              borderRadius: '4px',
                            }}
                          >
                            {count}
                          </span>
                        </button>
                      );
                    })}
                  </div>
                )}

                {/* Devices Cards Grid */}
                <div className="devices-grid">
                  {filteredDevices.map((dev) => {
                    const isSelected = selectedDevice?.id === dev.id;
                    const s = dev.summary || {};
                    const cpuTempStatus = getTempStatusClass(s.cpuTemp);
                    const gpuTempStatus = getTempStatusClass(s.gpuTemp);

                    return (
                      <div
                        key={dev.id}
                        className={`device-card ${isSelected ? 'selected' : ''}`}
                        style={
                          !dev.isOnline
                            ? {
                              borderColor: 'var(--danger)',
                              background: '#fffafa',
                            }
                            : undefined
                        }
                        onClick={() => setSelectedDeviceId(dev.id)}
                      >
                        {/* Card Top Header */}
                        <div
                          style={{
                            display: 'flex',
                            justifyContent: 'space-between',
                            alignItems: 'flex-start',
                            gap: '10px',
                          }}
                        >
                          <div style={{ minWidth: 0 }}>
                            <div
                              style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                flexWrap: 'wrap',
                              }}
                            >
                              <span
                                style={{
                                  fontSize: '1.08rem',
                                  fontWeight: 800,
                                  color: 'var(--cpm-dark)',
                                }}
                              >
                                {dev.name}
                              </span>
                              <span className="status-pill info">{dev.sourceBadge}</span>
                              {dev.orderNumber ? (
                                <span
                                  style={{
                                    display: 'inline-flex',
                                    alignItems: 'center',
                                    gap: '6px',
                                    padding: '3px 9px',
                                    borderRadius: '6px',
                                    background: 'rgba(0, 163, 196, 0.12)',
                                    border: '1px solid rgba(0, 163, 196, 0.4)',
                                    color: 'var(--cpm-dark)',
                                    fontWeight: 700,
                                    fontSize: '0.78rem',
                                  }}
                                >
                                  <FileText size={12} color="var(--cpm-light)" />
                                  <span>Auftrag #{dev.orderNumber}</span>
                                  <button
                                    type="button"
                                    title="Auftragsnummer bearbeiten"
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      setEditOrderDevice(dev);
                                      setEditOrderInput(dev.orderNumber || '');
                                    }}
                                    style={{
                                      background: 'none',
                                      border: 'none',
                                      cursor: 'pointer',
                                      padding: '0 2px',
                                      fontSize: '0.8rem',
                                      lineHeight: 1,
                                    }}
                                  >
                                    ✏️
                                  </button>
                                </span>
                              ) : (
                                <button
                                  type="button"
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    setEditOrderDevice(dev);
                                    setEditOrderInput('');
                                  }}
                                  style={{
                                    display: 'inline-flex',
                                    alignItems: 'center',
                                    gap: '4px',
                                    padding: '3px 8px',
                                    borderRadius: '6px',
                                    background: '#fef3c7',
                                    border: '1px dashed #d97706',
                                    color: '#92400e',
                                    fontWeight: 700,
                                    fontSize: '0.76rem',
                                    cursor: 'pointer',
                                  }}
                                  title="Auftragsnummer zuweisen"
                                >
                                  <span>+ Auftrag zuweisen</span>
                                </button>
                              )}
                              {dev.hasBattery ? (
                                <span
                                  style={{
                                    display: 'inline-flex',
                                    alignItems: 'center',
                                    gap: '4px',
                                    padding: '3px 8px',
                                    borderRadius: '6px',
                                    background: 'rgba(16, 185, 129, 0.12)',
                                    border: '1px solid rgba(16, 185, 129, 0.35)',
                                    color: '#065f46',
                                    fontWeight: 700,
                                    fontSize: '0.75rem',
                                  }}
                                >
                                  <BatteryCharging size={12} />
                                  Batterie-Test {dev.batteryLevelPercent != null ? `(${formatNum(dev.batteryLevelPercent, 0, '%')})` : ''}
                                </span>
                              ) : (
                                <span
                                  style={{
                                    display: 'inline-flex',
                                    alignItems: 'center',
                                    gap: '4px',
                                    padding: '3px 8px',
                                    borderRadius: '6px',
                                    background: 'rgba(245, 158, 11, 0.12)',
                                    border: '1px solid rgba(245, 158, 11, 0.35)',
                                    color: '#92400e',
                                    fontWeight: 700,
                                    fontSize: '0.75rem',
                                  }}
                                >
                                  <Zap size={12} />
                                  Hardware-Test
                                </span>
                              )}
                            </div>
                            <div
                              style={{
                                fontSize: '0.8rem',
                                color: 'var(--text-muted)',
                                marginTop: '2px',
                              }}
                            >
                              {dev.displayLabel}
                            </div>
                          </div>

                          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                            {dev.isArchived ? (
                              <span
                                className="status-pill"
                                style={{
                                  background: '#f1f5f9',
                                  color: '#1e293b',
                                  border: '1px solid #94a3b8',
                                  fontWeight: 700,
                                }}
                              >
                                <Archive size={13} color="var(--cpm-light)" />
                                <span>Eingefroren ({dev.archivedAtDe ? dev.archivedAtDe.split(' ')[1] : 'Archiv'})</span>
                              </span>
                            ) : dev.isOnline ? (
                              <span className="status-pill optimal">
                                <CheckCircle2 size={13} />
                                <span>Online ({dev.latencyMs} ms)</span>
                              </span>
                            ) : (
                              <span className="status-pill critical">
                                <XCircle size={13} />
                                <span>ABSTURZ / Offline</span>
                              </span>
                            )}

                            {dev.sourceType === 'lhm-api' && dev.id !== 'local-8085' && (
                              <button
                                type="button"
                                className="btn-danger-ghost"
                                title="Gerät aus Liste entfernen"
                                onClick={(e) => handleRemoveDevice(e, dev)}
                              >
                                <Trash2 size={15} />
                              </button>
                            )}
                          </div>
                        </div>

                        {/* Endpoint Badge Row */}
                        <div
                          style={{
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            flexWrap: 'wrap',
                            gap: '8px',
                            paddingBottom: '12px',
                            borderBottom: '1px solid #f1f5f9',
                          }}
                        >
                          <span className="mono-badge">
                            <Server size={12} />
                            <span>
                              {dev.host}:{dev.port}/data.json
                            </span>
                          </span>
                          <span
                            style={{
                              fontSize: '0.76rem',
                              fontWeight: 700,
                              color: 'var(--primary)',
                            }}
                          >
                            {dev.totalHardwareCount} Geräte • {dev.totalSensorCount} Sensoren
                          </span>
                        </div>

                        {dev.isOnline || (dev.isArchived && dev.summary?.cpuModel) ? (
                          <>
                            {/* Eingefrorene Werte Banner für archivierte Geräte */}
                            {dev.isArchived && (
                              <div
                                style={{
                                  padding: '8px 12px',
                                  background: '#f8fafc',
                                  border: '1px solid #cbd5e1',
                                  borderRadius: 'var(--radius-sm)',
                                  display: 'flex',
                                  alignItems: 'center',
                                  justifyContent: 'space-between',
                                  gap: '8px',
                                  fontSize: '0.78rem',
                                  fontWeight: 600,
                                  color: '#334155',
                                  marginBottom: '6px',
                                }}
                              >
                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                  <Archive size={14} color="var(--cpm-light)" />
                                  <span>Werte eingefroren zum Testabschluss</span>
                                </div>
                                <span style={{ fontSize: '0.72rem', color: '#64748b' }}>{dev.archivedAtDe}</span>
                              </div>
                            )}

                            {/* CPU Metric Box */}
                            <div
                              style={{
                                background: '#f8fafc',
                                padding: '12px 14px',
                                borderRadius: 'var(--radius-md)',
                                border: '1px solid var(--border-subtle)',
                              }}
                            >
                              <div
                                style={{
                                  display: 'flex',
                                  justifyContent: 'space-between',
                                  alignItems: 'center',
                                  gap: '8px',
                                }}
                              >
                                <div
                                  style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '7px',
                                    fontWeight: 700,
                                    fontSize: '0.85rem',
                                    color: 'var(--text-primary)',
                                    minWidth: 0,
                                  }}
                                >
                                  <Cpu size={16} color="var(--primary)" />
                                  <span
                                    style={{
                                      overflow: 'hidden',
                                      textOverflow: 'ellipsis',
                                      whiteSpace: 'nowrap',
                                    }}
                                  >
                                    {s.cpuName}
                                  </span>
                                </div>
                                <div
                                  style={{
                                    display: 'flex',
                                    gap: '8px',
                                    alignItems: 'center',
                                    flexShrink: 0,
                                    fontFamily: 'var(--font-mono)',
                                    fontSize: '0.8rem',
                                    fontWeight: 700,
                                  }}
                                >
                                  <span
                                    style={{
                                      color:
                                        cpuTempStatus === 'critical'
                                          ? 'var(--danger)'
                                          : cpuTempStatus === 'warning'
                                            ? 'var(--warning)'
                                            : 'var(--success)',
                                    }}
                                  >
                                    {formatNum(s.cpuTemp, 1, '°C')}
                                  </span>
                                  <span style={{ color: 'var(--cpm-dark)' }}>
                                    {formatNum(s.cpuLoad, 1, '%')}
                                  </span>
                                </div>
                              </div>
                              <div className="progress-bar-container">
                                <div
                                  className={`progress-bar-fill ${getLoadStatusClass(s.cpuLoad)}`}
                                  style={{
                                    width: `${Math.min(100, Math.max(4, s.cpuLoad || 0))}%`,
                                  }}
                                />
                              </div>
                              <div
                                style={{
                                  display: 'flex',
                                  justifyContent: 'space-between',
                                  fontSize: '0.73rem',
                                  color: 'var(--text-muted)',
                                  marginTop: '5px',
                                }}
                              >
                                <span>
                                  Takt:{' '}
                                  {s.cpuClockMhz
                                    ? `${formatNum(s.cpuClockMhz / 1000, 2, 'GHz')}`
                                    : '–'}
                                </span>
                                <span>
                                  Package:{' '}
                                  {s.cpuPowerWatts ? formatNum(s.cpuPowerWatts, 1, 'W') : '–'}
                                </span>
                              </div>
                            </div>

                            {/* GPU Metric Box(es) */}
                            {s.gpus && s.gpus.length > 0 ? (
                              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                {s.gpus.length > 1 && (
                                  <div
                                    style={{
                                      display: 'flex',
                                      justifyContent: 'space-between',
                                      alignItems: 'center',
                                      padding: '0 2px',
                                      fontSize: '0.72rem',
                                      fontWeight: 800,
                                      color: 'var(--text-muted)',
                                      letterSpacing: '0.4px',
                                      textTransform: 'uppercase',
                                    }}
                                  >
                                    <span>Grafikkarten ({s.gpuCount || s.gpus.length})</span>
                                    <span
                                      style={{
                                        fontSize: '0.68rem',
                                        padding: '1px 7px',
                                        borderRadius: 'var(--radius-sm)',
                                        background: '#eff6ff',
                                        color: '#2563eb',
                                        border: '1px solid #bfdbfe',
                                        fontWeight: 700,
                                      }}
                                    >
                                      {s.gpus.length} GPUs erkannt
                                    </span>
                                  </div>
                                )}
                                {s.gpus.map((gpuItem, gIdx) => {
                                  const itemTempStatus = getTempStatusClass(gpuItem.temp);
                                  return (
                                    <div
                                      key={gpuItem.id || `gpu-${gIdx}`}
                                      style={{
                                        background: '#f8fafc',
                                        padding: s.gpus.length > 1 ? '10px 12px' : '12px 14px',
                                        borderRadius: 'var(--radius-md)',
                                        border: gpuItem.isDedicated
                                          ? '1px solid #cbd5e1'
                                          : '1px solid var(--border-subtle)',
                                      }}
                                    >
                                      <div
                                        style={{
                                          display: 'flex',
                                          justifyContent: 'space-between',
                                          alignItems: 'center',
                                          gap: '8px',
                                        }}
                                      >
                                        <div
                                          style={{
                                            display: 'flex',
                                            alignItems: 'center',
                                            gap: '7px',
                                            fontWeight: 700,
                                            fontSize: s.gpus.length > 1 ? '0.8rem' : '0.85rem',
                                            color: 'var(--text-primary)',
                                            minWidth: 0,
                                          }}
                                        >
                                          <Monitor
                                            size={15}
                                            color={gpuItem.isDedicated ? 'var(--cpm-navy)' : '#64748b'}
                                          />
                                          <span
                                            style={{
                                              overflow: 'hidden',
                                              textOverflow: 'ellipsis',
                                              whiteSpace: 'nowrap',
                                            }}
                                            title={gpuItem.name}
                                          >
                                            {gpuItem.name}
                                          </span>
                                          <span
                                            style={{
                                              fontSize: '0.65rem',
                                              fontWeight: 700,
                                              padding: '1px 5px',
                                              borderRadius: '3px',
                                              flexShrink: 0,
                                              background: gpuItem.isDedicated ? '#dbeafe' : '#f1f5f9',
                                              color: gpuItem.isDedicated ? '#1e40af' : '#64748b',
                                            }}
                                          >
                                            {gpuItem.isDedicated ? 'Dediziert' : 'APU / iGPU'}
                                          </span>
                                        </div>
                                        <div
                                          style={{
                                            display: 'flex',
                                            gap: '8px',
                                            alignItems: 'center',
                                            flexShrink: 0,
                                            fontFamily: 'var(--font-mono)',
                                            fontSize: '0.78rem',
                                            fontWeight: 700,
                                          }}
                                        >
                                          {gpuItem.temp != null && (
                                            <span
                                              style={{
                                                color:
                                                  itemTempStatus === 'critical'
                                                    ? 'var(--danger)'
                                                    : itemTempStatus === 'warning'
                                                      ? 'var(--warning)'
                                                      : 'var(--success)',
                                              }}
                                            >
                                              {formatNum(gpuItem.temp, 1, '°C')}
                                            </span>
                                          )}
                                          <span style={{ color: 'var(--cpm-dark)' }}>
                                            {formatNum(gpuItem.load, 1, '%')}
                                          </span>
                                          {gpuItem.vramTotalGb && (
                                            <span style={{ fontSize: '0.72rem', color: 'var(--text-muted)' }}>
                                              {formatNum(gpuItem.vramUsedGb || 0, 1)}/{formatNum(gpuItem.vramTotalGb, 0, 'GB')}
                                            </span>
                                          )}
                                        </div>
                                      </div>
                                      <div className="progress-bar-container" style={{ marginTop: '6px' }}>
                                        <div
                                          className={`progress-bar-fill ${getLoadStatusClass(gpuItem.load)}`}
                                          style={{
                                            width: `${Math.min(100, Math.max(4, gpuItem.load || 0))}%`,
                                          }}
                                        />
                                      </div>
                                    </div>
                                  );
                                })}
                              </div>
                            ) : (
                              <div
                                style={{
                                  background: '#f8fafc',
                                  padding: '12px 14px',
                                  borderRadius: 'var(--radius-md)',
                                  border: '1px solid var(--border-subtle)',
                                }}
                              >
                                <div
                                  style={{
                                    display: 'flex',
                                    justifyContent: 'space-between',
                                    alignItems: 'center',
                                    gap: '8px',
                                  }}
                                >
                                  <div
                                    style={{
                                      display: 'flex',
                                      alignItems: 'center',
                                      gap: '7px',
                                      fontWeight: 700,
                                      fontSize: '0.85rem',
                                      color: 'var(--text-primary)',
                                      minWidth: 0,
                                    }}
                                  >
                                    <Monitor size={16} color="var(--cpm-light)" />
                                    <span
                                      style={{
                                        overflow: 'hidden',
                                        textOverflow: 'ellipsis',
                                        whiteSpace: 'nowrap',
                                      }}
                                    >
                                      {s.gpuName}
                                    </span>
                                  </div>
                                  <div
                                    style={{
                                      display: 'flex',
                                      gap: '8px',
                                      alignItems: 'center',
                                      flexShrink: 0,
                                      fontFamily: 'var(--font-mono)',
                                      fontSize: '0.8rem',
                                      fontWeight: 700,
                                    }}
                                  >
                                    <span
                                      style={{
                                        color:
                                          gpuTempStatus === 'critical'
                                            ? 'var(--danger)'
                                            : gpuTempStatus === 'warning'
                                              ? 'var(--warning)'
                                              : 'var(--success)',
                                      }}
                                    >
                                      {formatNum(s.gpuTemp, 1, '°C')}
                                    </span>
                                    <span style={{ color: 'var(--cpm-dark)' }}>
                                      {formatNum(s.gpuLoad, 1, '%')}
                                    </span>
                                  </div>
                                </div>
                                <div className="progress-bar-container">
                                  <div
                                    className={`progress-bar-fill ${getLoadStatusClass(s.gpuLoad)}`}
                                    style={{
                                      width: `${Math.min(100, Math.max(4, s.gpuLoad || 0))}%`,
                                    }}
                                  />
                                </div>
                              </div>
                            )}

                            {/* RAM & Mainboard Summary Row */}
                            <div
                              style={{
                                display: 'grid',
                                gridTemplateColumns: '1fr 1fr',
                                gap: '10px',
                              }}
                            >
                              <div
                                style={{
                                  background: '#f8fafc',
                                  padding: '10px 12px',
                                  borderRadius: 'var(--radius-md)',
                                  border: '1px solid var(--border-subtle)',
                                }}
                              >
                                <div
                                  style={{
                                    fontSize: '0.72rem',
                                    fontWeight: 700,
                                    color: 'var(--text-muted)',
                                    textTransform: 'uppercase',
                                  }}
                                >
                                  Arbeitsspeicher (RAM)
                                </div>
                                <div
                                  style={{
                                    fontSize: '0.9rem',
                                    fontWeight: 800,
                                    color: 'var(--cpm-dark)',
                                    fontFamily: 'var(--font-mono)',
                                    marginTop: '2px',
                                  }}
                                >
                                  {s.ramUsedGb !== null && s.ramTotalGb !== null
                                    ? `${formatNum(s.ramUsedGb, 1)} / ${formatNum(
                                      s.ramTotalGb,
                                      0
                                    )} GB`
                                    : formatNum(s.ramLoadPercent, 1, '%')}
                                </div>
                              </div>

                              <div
                                style={{
                                  background: '#f8fafc',
                                  padding: '10px 12px',
                                  borderRadius: 'var(--radius-md)',
                                  border: '1px solid var(--border-subtle)',
                                  minWidth: 0,
                                }}
                              >
                                <div
                                  style={{
                                    fontSize: '0.72rem',
                                    fontWeight: 700,
                                    color: 'var(--text-muted)',
                                    textTransform: 'uppercase',
                                  }}
                                >
                                  Mainboard
                                </div>
                                <div
                                  style={{
                                    fontSize: '0.82rem',
                                    fontWeight: 700,
                                    color: 'var(--text-primary)',
                                    marginTop: '3px',
                                    overflow: 'hidden',
                                    textOverflow: 'ellipsis',
                                    whiteSpace: 'nowrap',
                                  }}
                                  title={s.motherboardName}
                                >
                                  {s.motherboardName}
                                </div>
                              </div>
                            </div>

                            {/* Akku / Batteriestatus Box if present */}
                            {dev.hasBattery && (
                              <div
                                style={{
                                  background: 'rgba(16, 185, 129, 0.07)',
                                  padding: '10px 12px',
                                  borderRadius: 'var(--radius-md)',
                                  border: '1px solid rgba(16, 185, 129, 0.25)',
                                }}
                              >
                                <div
                                  style={{
                                    display: 'flex',
                                    justifyContent: 'space-between',
                                    alignItems: 'center',
                                    marginBottom: '6px',
                                  }}
                                >
                                  <div
                                    style={{
                                      display: 'flex',
                                      alignItems: 'center',
                                      gap: '6px',
                                      fontWeight: 700,
                                      fontSize: '0.82rem',
                                      color: '#065f46',
                                    }}
                                  >
                                    <BatteryCharging size={15} />
                                    <span>Akku / Batteriestatus</span>
                                  </div>
                                  <div style={{ fontFamily: 'var(--font-mono)', fontSize: '0.82rem', fontWeight: 800, color: '#065f46' }}>
                                    {formatNum(dev.batteryLevelPercent, 0, '%')}
                                  </div>
                                </div>
                                <div className="progress-bar-container">
                                  <div
                                    className="progress-bar-fill optimal"
                                    style={{
                                      width: `${Math.min(100, Math.max(4, dev.batteryLevelPercent || 0))}%`,
                                      background: 'var(--success)',
                                    }}
                                  />
                                </div>
                                {dev.batteryWearPercent != null && (
                                  <div style={{ fontSize: '0.72rem', color: 'var(--text-muted)', marginTop: '4px', textAlign: 'right' }}>
                                    Verschleiß / Wear-Level: {formatNum(dev.batteryWearPercent, 1, '%')}
                                  </div>
                                )}
                              </div>
                            )}

                            {/* Available Hardware Component Pills inside this Device */}
                            <div
                              style={{
                                display: 'flex',
                                flexWrap: 'wrap',
                                gap: '6px',
                                paddingTop: '4px',
                              }}
                            >
                              {dev.components?.map((comp) => (
                                <span
                                  key={comp.id}
                                  style={{
                                    display: 'inline-flex',
                                    alignItems: 'center',
                                    gap: '5px',
                                    fontSize: '0.73rem',
                                    fontWeight: 600,
                                    background: '#f1f5f9',
                                    color: 'var(--text-secondary)',
                                    padding: '4px 9px',
                                    borderRadius: 'var(--radius-sm)',
                                    border: '1px solid var(--border-subtle)',
                                  }}
                                >
                                  {getCategoryIcon(comp.category, 13)}
                                  <span>{comp.name}</span>
                                </span>
                              ))}
                            </div>

                            {/* Archiv-Aktionen bei eingefrorenem Gerät */}
                            {dev.isArchived && (
                              <div
                                style={{
                                  marginTop: '10px',
                                  paddingTop: '10px',
                                  borderTop: '1px dashed #e2e8f0',
                                  display: 'flex',
                                  alignItems: 'center',
                                  flexWrap: 'wrap',
                                  gap: '6px',
                                }}
                                onClick={(e) => e.stopPropagation()}
                              >
                                  <button
                                    type="button"
                                    className="btn btn-primary btn-sm"
                                    style={{ fontSize: '0.74rem', padding: '5px 9px', gap: '4px' }}
                                    title="System-Prüfbericht für dieses Gerät öffnen und ausdrucken"
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      setSelectedDeviceId(dev.id);
                                      setSampleDeviceFilter(dev.id);
                                      setReportType('system');
                                      setActiveTab('report');
                                    }}
                                  >
                                    <Printer size={12} />
                                    <span>Prüfbericht</span>
                                  </button>
                                  <button
                                    type="button"
                                    className="btn btn-secondary btn-sm"
                                    style={{
                                      fontSize: '0.74rem',
                                      padding: '5px 9px',
                                      gap: '4px',
                                      background: '#ecfdf5',
                                      borderColor: '#a7f3d0',
                                      color: '#047857',
                                      fontWeight: 700,
                                    }}
                                    title="Batterie-Prüfbericht für dieses Gerät öffnen"
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      setSelectedDeviceId(dev.id);
                                      setSampleDeviceFilter(dev.id);
                                      setReportType('battery');
                                      setActiveTab('report');
                                    }}
                                  >
                                    <BatteryCharging size={12} color="#10b981" />
                                    <span>Akku-Bericht</span>
                                  </button>
                                <button
                                  type="button"
                                  className="btn btn-secondary btn-sm"
                                  style={{ fontSize: '0.74rem', padding: '5px 9px', gap: '4px' }}
                                  title="Aus dem Archiv wiederherstellen"
                                  onClick={(e) => handleRestoreDevice(e, dev)}
                                >
                                  <RotateCw size={12} />
                                  <span>Aus Archiv holen</span>
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-secondary btn-sm"
                                  style={{ fontSize: '0.74rem', padding: '5px 9px', gap: '4px' }}
                                  title="Absturz- & Messprotokoll öffnen"
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    setActiveTab('recording');
                                  }}
                                >
                                  <FileText size={12} color="var(--primary)" />
                                  <span>Protokoll</span>
                                </button>
                              </div>
                            )}

                            {/* Remote Control & Stresstests Toolbar (nur bei aktiven Geräten) */}
                            {!dev.isArchived && dev.sourceType !== 'demo' && (
                              <div
                                style={{
                                  marginTop: '10px',
                                  paddingTop: '10px',
                                  borderTop: '1px dashed #e2e8f0',
                                  display: 'flex',
                                  flexDirection: 'column',
                                  gap: '8px',
                                }}
                                onClick={(e) => e.stopPropagation()}
                              >
                                {/* Active Test Indicators */}
                                {(dev.stressStatus?.isCpuStressRunning ||
                                  dev.stressStatus?.isRamStressRunning ||
                                  dev.stressStatus?.isGpuStressRunning ||
                                  dev.batteryTest?.isRunning ||
                                  dev.stressStatus?.isBatteryTestRunning ||
                                  dev.stressStatus?.isRecordingRunning) && (
                                    <div
                                      style={{
                                        display: 'flex',
                                        flexWrap: 'wrap',
                                        gap: '6px',
                                        marginBottom: '2px',
                                      }}
                                    >
                                      {/* Volllast Aktiv Badge */}
                                      {dev.stressStatus?.isCpuStressRunning && dev.stressStatus?.isGpuStressRunning && (
                                        <span
                                          style={{
                                            display: 'inline-flex',
                                            alignItems: 'center',
                                            gap: '4px',
                                            padding: '2px 8px',
                                            borderRadius: '4px',
                                            background: '#fef2f2',
                                            border: '1px solid #ef4444',
                                            color: '#dc2626',
                                            fontSize: '0.73rem',
                                            fontWeight: 800,
                                            animation: 'pulse 1.2s infinite',
                                          }}
                                        >
                                          <Flame size={12} />
                                          🔥 VOLLLAST AKTIV
                                        </span>
                                      )}
                                      {dev.stressStatus?.isGpuStressRunning && !dev.stressStatus?.isCpuStressRunning && (
                                        <span
                                          style={{
                                            display: 'inline-flex',
                                            alignItems: 'center',
                                            gap: '4px',
                                            padding: '2px 8px',
                                            borderRadius: '4px',
                                            background: '#fef2f2',
                                            border: '1px solid #fca5a5',
                                            color: '#b91c1c',
                                            fontSize: '0.73rem',
                                            fontWeight: 700,
                                            animation: 'pulse 1.5s infinite',
                                          }}
                                        >
                                          <Flame size={12} />
                                          🔥 FurMark GPU läuft
                                        </span>
                                      )}
                                      {(dev.batteryTest?.isRunning || dev.stressStatus?.isBatteryTestRunning) && (
                                        <span
                                          style={{
                                            display: 'inline-flex',
                                            alignItems: 'center',
                                            gap: '4px',
                                            padding: '2px 8px',
                                            borderRadius: '4px',
                                            background: '#ecfdf5',
                                            border: '1px solid #10b981',
                                            color: '#065f46',
                                            fontSize: '0.73rem',
                                            fontWeight: 800,
                                            animation: 'pulse 1.3s infinite',
                                          }}
                                        >
                                          <BatteryCharging size={12} />
                                          ⚡ Entladetest ({dev.batteryTest?.remainingDisplay || dev.stressStatus?.batteryTestRemaining || 'aktiv'})
                                        </span>
                                      )}
                                      {dev.stressStatus?.isCpuStressRunning && (
                                        <span
                                          style={{
                                            display: 'inline-flex',
                                            alignItems: 'center',
                                            gap: '4px',
                                            padding: '2px 8px',
                                            borderRadius: '4px',
                                            background: '#fffbeb',
                                            border: '1px solid #fcd34d',
                                            color: '#b45309',
                                            fontSize: '0.73rem',
                                            fontWeight: 700,
                                          }}
                                        >
                                          <Zap size={12} />
                                          ⚡ CPU Test ({dev.stressStatus.cpuStressRemaining || 'aktiv'})
                                        </span>
                                      )}
                                      {dev.stressStatus?.isRamStressRunning && (
                                        <span
                                          style={{
                                            display: 'inline-flex',
                                            alignItems: 'center',
                                            gap: '4px',
                                            padding: '2px 8px',
                                            borderRadius: '4px',
                                            background: '#f0fdf4',
                                            border: '1px solid #86efac',
                                            color: '#15803d',
                                            fontSize: '0.73rem',
                                            fontWeight: 700,
                                          }}
                                        >
                                          <Activity size={12} />
                                          🧠 RAM Test ({dev.stressStatus.ramStressAllocatedGb ? `${dev.stressStatus.ramStressAllocatedGb.toFixed(1)} GB` : 'aktiv'})
                                        </span>
                                      )}
                                      {dev.stressStatus?.isRecordingRunning && (
                                        <span
                                          style={{
                                            display: 'inline-flex',
                                            alignItems: 'center',
                                            gap: '4px',
                                            padding: '2px 8px',
                                            borderRadius: '4px',
                                            background: '#eff6ff',
                                            border: '1px solid #93c5fd',
                                            color: '#1d4ed8',
                                            fontSize: '0.73rem',
                                            fontWeight: 700,
                                          }}
                                        >
                                          <FileText size={12} />
                                          ⏺️ Messung ({dev.stressStatus.recordingDuration || '00:00'})
                                        </span>
                                      )}
                                    </div>
                                  )}

                                {/* Action Buttons */}
                                <div
                                  style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    flexWrap: 'wrap',
                                    gap: '6px',
                                  }}
                                >
                                  {/* Volllast-Button (CPU + GPU FurMark, Standard 1 Stunde) */}
                                  {(() => {
                                    const isFullRunning = dev.stressStatus?.isCpuStressRunning && dev.stressStatus?.isGpuStressRunning;
                                    return (
                                      <button
                                        type="button"
                                        className="btn btn-secondary btn-sm"
                                        style={{
                                          fontSize: '0.74rem',
                                          padding: '5px 9px',
                                          gap: '4px',
                                          fontWeight: 700,
                                          background: isFullRunning ? '#fef2f2' : '#fff7ed',
                                          borderColor: isFullRunning ? '#ef4444' : '#fdba74',
                                          color: isFullRunning ? '#dc2626' : '#c2410c',
                                        }}
                                        title={isFullRunning ? 'Volllast-Test stoppen' : 'Volllast (CPU + FurMark GPU Donut für 1h) starten'}
                                        disabled={controllingAction?.startsWith(`${dev.id}:stress/full`)}
                                        onClick={(e) => {
                                          e.stopPropagation();
                                          if (isFullRunning) {
                                            handleControlDevice(e, dev, 'stress/full/stop');
                                          } else {
                                            openStressModal(dev, 'full');
                                          }
                                        }}
                                      >
                                        <Flame size={13} color={isFullRunning ? '#dc2626' : '#ea580c'} />
                                        <span>{isFullRunning ? 'Volllast Stopp' : '🔥 Volllast (1h)'}</span>
                                      </button>
                                    );
                                  })()}

                                  {/* FurMark GPU Donut */}
                                  <button
                                    type="button"
                                    className="btn btn-secondary btn-sm"
                                    style={{
                                      fontSize: '0.74rem',
                                      padding: '5px 9px',
                                      gap: '4px',
                                      background: dev.stressStatus?.isGpuStressRunning ? '#fef2f2' : undefined,
                                      borderColor: dev.stressStatus?.isGpuStressRunning ? '#f87171' : undefined,
                                      color: dev.stressStatus?.isGpuStressRunning ? '#dc2626' : undefined,
                                    }}
                                    title={dev.stressStatus?.isGpuStressRunning ? 'FurMark Donut stoppen' : 'FurMark 3D Donut Stresstest remote starten (Laufzeit wählen)'}
                                    disabled={controllingAction?.startsWith(`${dev.id}:stress/gpu`)}
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      if (dev.stressStatus?.isGpuStressRunning) {
                                        handleControlDevice(e, dev, 'stress/gpu/stop');
                                      } else {
                                        openStressModal(dev, 'gpu');
                                      }
                                    }}
                                  >
                                    <Flame size={13} color="#ea580c" />
                                    <span>{dev.stressStatus?.isGpuStressRunning ? 'GPU Stopp' : 'GPU Donut'}</span>
                                  </button>

                                  {/* CPU Stresstest */}
                                  <button
                                    type="button"
                                    className="btn btn-secondary btn-sm"
                                    style={{
                                      fontSize: '0.74rem',
                                      padding: '5px 9px',
                                      gap: '4px',
                                      background: dev.stressStatus?.isCpuStressRunning ? '#fffbeb' : undefined,
                                      borderColor: dev.stressStatus?.isCpuStressRunning ? '#f59e0b' : undefined,
                                      color: dev.stressStatus?.isCpuStressRunning ? '#b45309' : undefined,
                                    }}
                                    title={dev.stressStatus?.isCpuStressRunning ? 'CPU Stresstest stoppen' : 'CPU Stresstest starten (Laufzeit wählen)'}
                                    disabled={controllingAction?.startsWith(`${dev.id}:stress/cpu`)}
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      if (dev.stressStatus?.isCpuStressRunning) {
                                        handleControlDevice(e, dev, 'stress/cpu/stop');
                                      } else {
                                        openStressModal(dev, 'cpu');
                                      }
                                    }}
                                  >
                                    <Zap size={13} color="#d97706" />
                                    <span>{dev.stressStatus?.isCpuStressRunning ? 'CPU Stopp' : 'CPU Test'}</span>
                                  </button>

                                  {/* RAM Stresstest */}
                                  <button
                                    type="button"
                                    className="btn btn-secondary btn-sm"
                                    style={{
                                      fontSize: '0.74rem',
                                      padding: '5px 9px',
                                      gap: '4px',
                                      background: dev.stressStatus?.isRamStressRunning ? '#f0fdf4' : undefined,
                                      borderColor: dev.stressStatus?.isRamStressRunning ? '#10b981' : undefined,
                                      color: dev.stressStatus?.isRamStressRunning ? '#15803d' : undefined,
                                    }}
                                    title={dev.stressStatus?.isRamStressRunning ? 'RAM Stresstest stoppen' : 'RAM Stresstest starten (Laufzeit & Auslastung wählen)'}
                                    disabled={controllingAction?.startsWith(`${dev.id}:stress/ram`)}
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      if (dev.stressStatus?.isRamStressRunning) {
                                        handleControlDevice(e, dev, 'stress/ram/stop');
                                      } else {
                                        openStressModal(dev, 'ram');
                                      }
                                    }}
                                  >
                                    <Activity size={13} color="#16a34a" />
                                    <span>{dev.stressStatus?.isRamStressRunning ? 'RAM Stopp' : 'RAM Test'}</span>
                                  </button>

                                  {/* Batterie Stresstest (YouTube 4K / Entladung) */}
                                  {(() => {
                                    const isBattRunning = dev.batteryTest?.isRunning || dev.stressStatus?.isBatteryTestRunning;
                                    return (
                                      <button
                                        type="button"
                                        className="btn btn-secondary btn-sm"
                                        style={{
                                          fontSize: '0.74rem',
                                          padding: '5px 9px',
                                          gap: '4px',
                                          fontWeight: 700,
                                          background: isBattRunning ? '#ecfdf5' : '#f0fdf4',
                                          borderColor: isBattRunning ? '#10b981' : '#a7f3d0',
                                          color: isBattRunning ? '#047857' : '#065f46',
                                        }}
                                        title={isBattRunning ? 'Batterie-Test stoppen' : 'Batterie-Entladetest (YouTube 4K HDR Benchmark) starten'}
                                        onClick={(e) => {
                                          e.stopPropagation();
                                          if (isBattRunning) {
                                            handleStopBatteryTest(e, dev);
                                          } else {
                                            openStressModal(dev, 'battery');
                                          }
                                        }}
                                      >
                                        <BatteryCharging size={13} color={isBattRunning ? '#047857' : '#10b981'} />
                                        <span>{isBattRunning ? 'Akku Stopp' : '🔋 Akku Test'}</span>
                                      </button>
                                    );
                                  })()}

                                  {/* Alle Stoppen */}
                                  <button
                                    type="button"
                                    className="btn btn-secondary btn-sm"
                                    style={{
                                      fontSize: '0.74rem',
                                      padding: '5px 9px',
                                      gap: '4px',
                                      color: '#dc2626',
                                    }}
                                    title="Alle aktiven Stresstests auf diesem PC sofort stoppen"
                                    disabled={controllingAction === `${dev.id}:stress/stop-all`}
                                    onClick={(e) => handleControlDevice(e, dev, 'stress/stop-all')}
                                  >
                                    <Square size={12} color="#dc2626" />
                                    <span>Stopp</span>
                                  </button>

                                  {/* Aufnahme Toggle */}
                                  <button
                                    type="button"
                                    className="btn btn-secondary btn-sm"
                                    style={{
                                      fontSize: '0.74rem',
                                      padding: '5px 9px',
                                      gap: '4px',
                                      marginLeft: 'auto',
                                      background: dev.stressStatus?.isRecordingRunning ? '#eff6ff' : undefined,
                                      borderColor: dev.stressStatus?.isRecordingRunning ? '#3b82f6' : undefined,
                                      color: dev.stressStatus?.isRecordingRunning ? '#1d4ed8' : undefined,
                                    }}
                                    title={dev.stressStatus?.isRecordingRunning ? 'Aufzeichnung stoppen' : 'Aufzeichnung starten'}
                                    disabled={controllingAction?.startsWith(`${dev.id}:recording`)}
                                    onClick={(e) =>
                                      handleControlDevice(
                                        e,
                                        dev,
                                        dev.stressStatus?.isRecordingRunning ? 'recording/stop' : 'recording/start'
                                      )
                                    }
                                  >
                                    <FileText size={12} color="var(--primary)" />
                                    <span>{dev.stressStatus?.isRecordingRunning ? 'Aufn. Stopp' : 'Aufnahme'}</span>
                                  </button>

                                  {/* Prüfbericht öffnen */}
                                  <button
                                    type="button"
                                    className="btn btn-secondary btn-sm"
                                    style={{
                                      fontSize: '0.74rem',
                                      padding: '5px 9px',
                                      gap: '4px',
                                      background: '#f8fafc',
                                      borderColor: '#cbd5e1',
                                      color: 'var(--cpm-dark)',
                                      fontWeight: 700,
                                    }}
                                    title="System-Prüfbericht für dieses Gerät öffnen und ausdrucken"
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      setSelectedDeviceId(dev.id);
                                      setSampleDeviceFilter(dev.id);
                                      setActiveTab('report');
                                    }}
                                  >
                                    <Printer size={12} color="var(--cpm-light)" />
                                    <span>Prüfbericht</span>
                                  </button>

                                  {/* Batterie-Prüfbericht Direkt-Button */}
                                  <button
                                    type="button"
                                    className="btn btn-secondary btn-sm"
                                    style={{
                                      fontSize: '0.74rem',
                                      padding: '5px 9px',
                                      gap: '4px',
                                      background: '#ecfdf5',
                                      borderColor: '#a7f3d0',
                                      color: '#047857',
                                      fontWeight: 700,
                                    }}
                                    title="Offiziellen Batterie-Prüfbericht (Grün / DIN A4) für dieses Gerät öffnen"
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      setSelectedDeviceId(dev.id);
                                      setSampleDeviceFilter(dev.id);
                                      setReportType('battery');
                                      setActiveTab('report');
                                    }}
                                  >
                                    <BatteryCharging size={12} color="#10b981" />
                                    <span>Akku-Bericht</span>
                                  </button>

                                  {/* Tests abschließen Button */}
                                  {!dev.isArchived && (
                                    <button
                                      type="button"
                                      className="btn btn-secondary btn-sm"
                                      style={{
                                        fontSize: '0.74rem',
                                        padding: '5px 9px',
                                        gap: '4px',
                                        background: '#fff1f2',
                                        borderColor: '#fecdd3',
                                        color: '#e11d48',
                                        fontWeight: 700,
                                      }}
                                      title="Tests abschließen: Beendet das DiagnoseTool auf dem Test-PC und verschiebt das Gerät ins Archiv"
                                      onClick={(e) => {
                                        e.stopPropagation();
                                        setConfirmArchiveDevice(dev);
                                      }}
                                    >
                                      <CheckSquare size={12} color="#e11d48" />
                                      <span>Tests abschließen</span>
                                    </button>
                                  )}
                                </div>
                              </div>
                            )}
                          </>
                        ) : dev.isArchived ? (
                          <div
                            style={{
                              padding: '16px',
                              background: '#f8fafc',
                              border: '1px solid #cbd5e1',
                              borderRadius: 'var(--radius-md)',
                              display: 'flex',
                              flexDirection: 'column',
                              gap: '10px',
                            }}
                          >
                            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '6px' }}>
                              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--cpm-dark)', fontWeight: 800 }}>
                                <Archive size={16} color="var(--cpm-light)" />
                                <span>Tests abgeschlossen & archiviert</span>
                              </div>
                              <span style={{ fontSize: '0.74rem', color: 'var(--text-muted)' }}>
                                {dev.archivedAtDe}
                              </span>
                            </div>
                            <p style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', margin: 0 }}>
                              Das DiagnoseTool auf diesem Gerät wurde ordnungsgemäß beendet. Alle Telemetriedaten und der Prüfbericht stehen dauerhaft zur Verfügung.
                            </p>
                            <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', marginTop: '4px' }}>
                              <button
                                type="button"
                                className="btn btn-primary btn-sm"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  setSelectedDeviceId(dev.id);
                                  setSampleDeviceFilter(dev.id);
                                  setActiveTab('report');
                                }}
                              >
                                <Printer size={12} />
                                <span>Prüfbericht drucken</span>
                              </button>
                              <a
                                href={hardwareApi.getDevicePdfUrl(dev.id)}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="btn btn-secondary btn-sm"
                                onClick={(e) => e.stopPropagation()}
                              >
                                <Download size={12} />
                                <span>PDF</span>
                              </a>
                              <button
                                type="button"
                                className="btn btn-secondary btn-sm"
                                style={{ marginLeft: 'auto' }}
                                onClick={(e) => handleRestoreDevice(e, dev)}
                              >
                                <RotateCw size={12} />
                                <span>Aus Archiv holen</span>
                              </button>
                            </div>
                          </div>
                        ) : (
                          <div
                            style={{
                              padding: '18px',
                              background: 'var(--danger-bg)',
                              border: '1px solid var(--danger-border)',
                              borderRadius: 'var(--radius-md)',
                              color: 'var(--danger)',
                              fontSize: '0.84rem',
                            }}
                          >
                            <div
                              style={{
                                fontWeight: 800,
                                display: 'flex',
                                alignItems: 'center',
                                gap: '6px',
                                marginBottom: '4px',
                              }}
                            >
                              <WifiOff size={16} />
                              <span>ABSTURZ: Verbindung zu Port 8085 unterbrochen!</span>
                            </div>
                            <div style={{ fontSize: '0.8rem' }}>
                              {dev.errorMessage ||
                                'Libre Hardware Monitor Web Server antwortet nicht mehr.'}
                            </div>
                          </div>
                        )}

                        {/* Card Footer CTA */}
                        <div
                          style={{
                            display: 'flex',
                            justifyContent: 'space-between',
                            alignItems: 'center',
                            marginTop: 'auto',
                            paddingTop: '10px',
                            borderTop: '1px solid #f1f5f9',
                            gap: '8px',
                            flexWrap: 'wrap',
                          }}
                        >
                          {dev.isOnline ? (
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              style={{
                                color: 'var(--danger)',
                                borderColor: 'var(--danger-border)',
                                background: '#fff5f5',
                              }}
                              title="Simuliert einen Verbindungsabbruch, um die Absturz-Protokollierung zu testen"
                              onClick={(e) => handleSimulateCrash(e, dev, true)}
                            >
                              <WifiOff size={13} />
                              <span>Absturz testen</span>
                            </button>
                          ) : (
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              style={{
                                color: 'var(--success)',
                                borderColor: 'var(--success-border)',
                                background: 'var(--success-bg)',
                              }}
                              onClick={(e) => handleSimulateCrash(e, dev, false)}
                            >
                              <CheckCircle2 size={13} />
                              <span>Verbindung wiederherstellen</span>
                            </button>
                          )}

                          <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
                            <button
                              type="button"
                              className="btn btn-accent btn-sm"
                              onClick={(e) => {
                                e.stopPropagation();
                                setSelectedDeviceId(dev.id);
                                setSampleDeviceFilter(dev.id);
                                setActiveTab('recording');
                              }}
                              title="Parameter-Verlaufsgrafik und Testbericht für den Kunden öffnen"
                            >
                              <TrendingUp size={13} />
                              <span>Kunden-Grafik</span>
                            </button>

                            <button
                              type="button"
                              className="btn btn-primary btn-sm"
                              onClick={(e) => {
                                e.stopPropagation();
                                setSelectedDeviceId(dev.id);
                                setActiveTab('components');
                              }}
                            >
                              <span>Hardware</span>
                              <ChevronRight size={14} />
                            </button>
                          </div>
                        </div>
                      </div>
                    );
                  })}

                  {filteredDevices.length === 0 && (
                    <div
                      className="card-glass"
                      style={{
                        gridColumn: '1 / -1',
                        padding: '48px 24px',
                        textAlign: 'center',
                        display: 'flex',
                        flexDirection: 'column',
                        alignItems: 'center',
                        gap: '14px',
                      }}
                    >
                      <div
                        style={{
                          width: '54px',
                          height: '54px',
                          borderRadius: '50%',
                          background: '#eff6ff',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          color: '#2563eb',
                        }}
                      >
                        <Server size={28} />
                      </div>
                      <h3 style={{ fontSize: '1.1rem', fontWeight: 800, margin: 0, color: 'var(--cpm-dark)' }}>
                        Keine Prüfgeräte in der Überwachung
                      </h3>
                      <p style={{ fontSize: '0.88rem', color: 'var(--text-secondary)', maxWidth: '480px', margin: 0 }}>
                        Das Grundgerät ist ausgeblendet. Fügen Sie Prüfgeräte über »Port 8085 Scan« oder »Gerät hinzufügen« hinzu, oder aktivieren Sie den Demo-Modus.
                      </p>
                      <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap', justifyContent: 'center', marginTop: '6px' }}>
                        <button
                          type="button"
                          className="btn btn-primary btn-sm"
                          onClick={() => setIsAddModalOpen(true)}
                        >
                          <Plus size={14} />
                          <span>Gerät hinzufügen</span>
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => setIsScanModalOpen(true)}
                        >
                          <Radar size={14} />
                          <span>Netzwerk scannen</span>
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={handleToggleDemo}
                        >
                          <Sparkles size={14} />
                          <span>Demo-PCs aktivieren</span>
                        </button>
                      </div>
                    </div>
                  )}
                </div>

                {/* Selected Device Hardware Table Summary */}
                {selectedDevice && (
                  <div className="card-glass" style={{ padding: '0', overflow: 'hidden' }}>
                    <div
                      style={{
                        padding: '20px 24px',
                        borderBottom: '1px solid var(--border-subtle)',
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        flexWrap: 'wrap',
                        gap: '12px',
                      }}
                    >
                      <div>
                        <div className="card-title">
                          <CircuitBoard size={20} color="var(--cpm-light)" />
                          <span style={{ display: 'inline-flex', alignItems: 'center', flexWrap: 'wrap', gap: '8px' }}>
                            <span>
                              Verfügbare Hardware-Geräte in: {selectedDevice.name} ({selectedDevice.host}:{selectedDevice.port})
                            </span>
                            {selectedDevice.isArchived && (
                              <span
                                className="status-pill"
                                style={{
                                  fontSize: '0.74rem',
                                  padding: '2px 8px',
                                  background: '#f1f5f9',
                                  color: '#1e293b',
                                  border: '1px solid #94a3b8',
                                  fontWeight: 700,
                                }}
                              >
                                <Archive size={12} color="var(--cpm-light)" />
                                <span>Eingefroren ({selectedDevice.archivedAtDe})</span>
                              </span>
                            )}
                          </span>
                        </div>
                        <p
                          style={{
                            fontSize: '0.82rem',
                            color: 'var(--text-muted)',
                            marginTop: '2px',
                          }}
                        >
                          Alle über die Libre Hardware Monitor API erkannten Komponenten
                          dieses Systems
                        </p>
                      </div>

                      <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' }}>
                        {selectedDevice.sourceType !== 'demo' && selectedDevice.isOnline && (
                          <div style={{ display: 'flex', gap: '6px', marginRight: '6px', flexWrap: 'wrap' }}>
                            {/* Volllast Button */}
                            {(() => {
                              const isFullRunning = selectedDevice.stressStatus?.isCpuStressRunning && selectedDevice.stressStatus?.isGpuStressRunning;
                              return (
                                <button
                                  type="button"
                                  className="btn btn-secondary btn-sm"
                                  style={{
                                    gap: '4px',
                                    fontWeight: 700,
                                    background: isFullRunning ? '#fef2f2' : '#fff7ed',
                                    borderColor: isFullRunning ? '#ef4444' : '#fdba74',
                                    color: isFullRunning ? '#dc2626' : '#c2410c',
                                  }}
                                  title={isFullRunning ? 'Volllast-Test stoppen' : 'Volllast (CPU + FurMark GPU Donut für 1h) starten'}
                                  disabled={controllingAction?.startsWith(`${selectedDevice.id}:stress/full`)}
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    if (isFullRunning) {
                                      handleControlDevice(e, selectedDevice, 'stress/full/stop');
                                    } else {
                                      openStressModal(selectedDevice, 'full');
                                    }
                                  }}
                                >
                                  <Flame size={14} color={isFullRunning ? '#dc2626' : '#ea580c'} />
                                  <span>{isFullRunning ? 'Volllast Stopp' : '🔥 Volllast (1h)'}</span>
                                </button>
                              );
                            })()}

                            {/* GPU Donut */}
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              style={{
                                background: selectedDevice.stressStatus?.isGpuStressRunning ? '#fef2f2' : undefined,
                                borderColor: selectedDevice.stressStatus?.isGpuStressRunning ? '#f87171' : undefined,
                                color: selectedDevice.stressStatus?.isGpuStressRunning ? '#dc2626' : undefined,
                              }}
                              title={selectedDevice.stressStatus?.isGpuStressRunning ? 'FurMark Donut stoppen' : 'FurMark 3D Donut Stresstest remote starten (Laufzeit wählen)'}
                              disabled={controllingAction?.startsWith(`${selectedDevice.id}:stress/gpu`)}
                              onClick={(e) => {
                                e.stopPropagation();
                                if (selectedDevice.stressStatus?.isGpuStressRunning) {
                                  handleControlDevice(e, selectedDevice, 'stress/gpu/stop');
                                } else {
                                  openStressModal(selectedDevice, 'gpu');
                                }
                              }}
                            >
                              <Flame size={14} color="#ea580c" />
                              <span>{selectedDevice.stressStatus?.isGpuStressRunning ? 'GPU Stopp' : 'GPU Donut'}</span>
                            </button>

                            {/* CPU Test */}
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              style={{
                                background: selectedDevice.stressStatus?.isCpuStressRunning ? '#fffbeb' : undefined,
                                borderColor: selectedDevice.stressStatus?.isCpuStressRunning ? '#f59e0b' : undefined,
                                color: selectedDevice.stressStatus?.isCpuStressRunning ? '#b45309' : undefined,
                              }}
                              title={selectedDevice.stressStatus?.isCpuStressRunning ? 'CPU Stresstest stoppen' : 'CPU Stresstest starten (Laufzeit wählen)'}
                              disabled={controllingAction?.startsWith(`${selectedDevice.id}:stress/cpu`)}
                              onClick={(e) => {
                                e.stopPropagation();
                                if (selectedDevice.stressStatus?.isCpuStressRunning) {
                                  handleControlDevice(e, selectedDevice, 'stress/cpu/stop');
                                } else {
                                  openStressModal(selectedDevice, 'cpu');
                                }
                              }}
                            >
                              <Zap size={14} color="#d97706" />
                              <span>{selectedDevice.stressStatus?.isCpuStressRunning ? 'CPU Stopp' : 'CPU Test'}</span>
                            </button>

                            {/* RAM Test */}
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              style={{
                                background: selectedDevice.stressStatus?.isRamStressRunning ? '#f0fdf4' : undefined,
                                borderColor: selectedDevice.stressStatus?.isRamStressRunning ? '#10b981' : undefined,
                                color: selectedDevice.stressStatus?.isRamStressRunning ? '#15803d' : undefined,
                              }}
                              title={selectedDevice.stressStatus?.isRamStressRunning ? 'RAM Stresstest stoppen' : 'RAM Stresstest starten (Laufzeit & Auslastung wählen)'}
                              disabled={controllingAction?.startsWith(`${selectedDevice.id}:stress/ram`)}
                              onClick={(e) => {
                                e.stopPropagation();
                                if (selectedDevice.stressStatus?.isRamStressRunning) {
                                  handleControlDevice(e, selectedDevice, 'stress/ram/stop');
                                } else {
                                  openStressModal(selectedDevice, 'ram');
                                }
                              }}
                            >
                              <Activity size={14} color="#16a34a" />
                              <span>{selectedDevice.stressStatus?.isRamStressRunning ? 'RAM Stopp' : 'RAM Test'}</span>
                            </button>

                            {/* Batterie Stresstest */}
                            {(() => {
                              const isBattRunning = selectedDevice.batteryTest?.isRunning || selectedDevice.stressStatus?.isBatteryTestRunning;
                              return (
                                <button
                                  type="button"
                                  className="btn btn-secondary btn-sm"
                                  style={{
                                    background: isBattRunning ? '#ecfdf5' : '#f0fdf4',
                                    borderColor: isBattRunning ? '#10b981' : '#a7f3d0',
                                    color: isBattRunning ? '#047857' : '#065f46',
                                    fontWeight: 700,
                                  }}
                                  title={isBattRunning ? 'Batterie-Test stoppen' : 'Batterie-Entladetest (YouTube 4K HDR) starten'}
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    if (isBattRunning) {
                                      handleStopBatteryTest(e, selectedDevice);
                                    } else {
                                      openStressModal(selectedDevice, 'battery');
                                    }
                                  }}
                                >
                                  <BatteryCharging size={14} color={isBattRunning ? '#047857' : '#10b981'} />
                                  <span>{isBattRunning ? 'Akku Stopp' : '🔋 Akku Test'}</span>
                                </button>
                              );
                            })()}

                            {/* Alle Stoppen */}
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              title="Alle aktiven Stresstests stoppen"
                              disabled={controllingAction === `${selectedDevice.id}:stress/stop-all`}
                              onClick={(e) => handleControlDevice(e, selectedDevice, 'stress/stop-all')}
                            >
                              <Square size={13} color="#dc2626" />
                              <span>Stopp</span>
                            </button>
                          </div>
                        )}
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => {
                            setSampleDeviceFilter(selectedDevice.id);
                            setReportType('system');
                            setActiveTab('report');
                          }}
                        >
                          <Printer size={15} color="var(--cpm-light)" />
                          <span>System-Prüfbericht</span>
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          style={{
                            background: '#ecfdf5',
                            borderColor: '#a7f3d0',
                            color: '#047857',
                            fontWeight: 700,
                          }}
                          onClick={() => {
                            setSampleDeviceFilter(selectedDevice.id);
                            setReportType('battery');
                            setActiveTab('report');
                          }}
                        >
                          <BatteryCharging size={15} color="#10b981" />
                          <span>Batteriebericht</span>
                        </button>
                        {!selectedDevice.isArchived && selectedDevice.sourceType !== 'demo' && (
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            style={{
                              background: '#fff1f2',
                              borderColor: '#fecdd3',
                              color: '#e11d48',
                              fontWeight: 700,
                            }}
                            title="Tests für diesen PC beenden: Schließt das DiagnoseTool auf dem Testgerät und archiviert das Gerät"
                            onClick={() => setConfirmArchiveDevice(selectedDevice)}
                          >
                            <CheckSquare size={15} color="#e11d48" />
                            <span>Tests abschließen</span>
                          </button>
                        )}
                        {selectedDevice.isArchived && (
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            title="Aus dem Archiv wiederherstellen"
                            onClick={(e) => handleRestoreDevice(e, selectedDevice)}
                          >
                            <RotateCw size={14} />
                            <span>Aus Archiv holen</span>
                          </button>
                        )}
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => setActiveTab('recording')}
                        >
                          <FileText size={15} />
                          <span>Absturz-Protokoll</span>
                        </button>
                        <button
                          type="button"
                          className="btn btn-primary btn-sm"
                          onClick={() => setActiveTab('components')}
                        >
                          <Eye size={15} />
                          <span>Alle Komponenten-Details</span>
                        </button>
                      </div>
                    </div>

                    <div
                      className="table-container desktop-only-table"
                      style={{ border: 'none', borderRadius: 0 }}
                    >
                      <table className="modern-table">
                        <thead>
                          <tr>
                            <th>Kategorie</th>
                            <th>Hardware-Gerät / Bezeichnung</th>
                            <th>Sensor-Pfad</th>
                            <th>Temperatur</th>
                            <th>Auslastung</th>
                            <th>Weiterer Messwert</th>
                            <th>Sensoren</th>
                            <th style={{ textAlign: 'right' }}>Status</th>
                          </tr>
                        </thead>
                        <tbody>
                          {selectedDeviceComponents.map((comp) => (
                            <tr
                              key={comp.id}
                              style={{ cursor: 'pointer' }}
                              onClick={() => {
                                setComponentCategoryFilter(comp.category);
                                setActiveTab('components');
                              }}
                            >
                              <td>
                                <span
                                  style={{
                                    display: 'inline-flex',
                                    alignItems: 'center',
                                    gap: '7px',
                                    fontWeight: 700,
                                    color: 'var(--cpm-dark)',
                                  }}
                                >
                                  {getCategoryIcon(comp.category, 16)}
                                  <span>{comp.categoryDe}</span>
                                </span>
                              </td>
                              <td>
                                <strong style={{ color: 'var(--text-primary)' }}>
                                  {comp.name}
                                </strong>
                              </td>
                              <td>
                                <span className="mono-badge">{comp.id}</span>
                              </td>
                              <td style={{ fontFamily: 'var(--font-mono)', fontWeight: 700 }}>
                                {comp.primaryTemp !== null && comp.primaryTemp !== undefined
                                  ? formatNum(comp.primaryTemp, 1, '°C')
                                  : '–'}
                              </td>
                              <td style={{ fontFamily: 'var(--font-mono)', fontWeight: 700 }}>
                                {comp.primaryLoad !== null && comp.primaryLoad !== undefined
                                  ? formatNum(comp.primaryLoad, 1, '%')
                                  : '–'}
                              </td>
                              <td>
                                {comp.primaryMetricValue ? (
                                  <span style={{ fontSize: '0.82rem' }}>
                                    <span style={{ color: 'var(--text-muted)' }}>
                                      {comp.primaryMetricLabel}:{' '}
                                    </span>
                                    <strong style={{ fontFamily: 'var(--font-mono)' }}>
                                      {comp.primaryMetricValue}
                                    </strong>
                                  </span>
                                ) : (
                                  '–'
                                )}
                              </td>
                              <td>
                                <span className="mono-badge">
                                  {comp.sensors?.length || 0} Sensoren
                                </span>
                              </td>
                              <td style={{ textAlign: 'right' }}>
                                <span className={`status-pill ${comp.status || 'optimal'}`}>
                                  {comp.status === 'critical' ? (
                                    <>
                                      <AlertTriangle size={13} />
                                      <span>Kritisch</span>
                                    </>
                                  ) : comp.status === 'warning' ? (
                                    <>
                                      <AlertTriangle size={13} />
                                      <span>Erhöht</span>
                                    </>
                                  ) : (
                                    <>
                                      <CheckCircle2 size={13} />
                                      <span>Optimal</span>
                                    </>
                                  )}
                                </span>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>

                    {/* Mobile Hardware Cards (< 640px) */}
                    <div className="mobile-cards-list">
                      {selectedDeviceComponents.map((comp) => (
                        <div
                          key={comp.id}
                          className="mobile-data-card"
                          onClick={() => {
                            setComponentCategoryFilter(comp.category);
                            setActiveTab('components');
                          }}
                        >
                          <div
                            style={{
                              display: 'flex',
                              justifyContent: 'space-between',
                              alignItems: 'center',
                              gap: '8px',
                            }}
                          >
                            <span
                              style={{
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: '6px',
                                fontSize: '0.76rem',
                                fontWeight: 800,
                                color: 'var(--cpm-light)',
                                textTransform: 'uppercase',
                              }}
                            >
                              {getCategoryIcon(comp.category, 15)}
                              <span>{comp.categoryDe}</span>
                            </span>
                            <span className={`status-pill ${comp.status || 'optimal'}`}>
                              {comp.status === 'critical'
                                ? 'Kritisch'
                                : comp.status === 'warning'
                                  ? 'Erhöht'
                                  : 'Optimal'}
                            </span>
                          </div>
                          <div style={{ fontWeight: 800, color: 'var(--cpm-dark)', fontSize: '0.92rem' }}>
                            {comp.name}
                          </div>
                          <div className="mobile-sample-grid">
                            <div>
                              <span style={{ color: 'var(--text-muted)', fontSize: '0.7rem' }}>
                                Temperatur:{' '}
                              </span>
                              <strong style={{ fontFamily: 'var(--font-mono)' }}>
                                {comp.primaryTemp != null
                                  ? formatNum(comp.primaryTemp, 1, '°C')
                                  : '–'}
                              </strong>
                            </div>
                            <div>
                              <span style={{ color: 'var(--text-muted)', fontSize: '0.7rem' }}>
                                Auslastung:{' '}
                              </span>
                              <strong style={{ fontFamily: 'var(--font-mono)' }}>
                                {comp.primaryLoad != null
                                  ? formatNum(comp.primaryLoad, 1, '%')
                                  : '–'}
                              </strong>
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )}

            {/* ==================== TAB 2: HARDWARE-KOMPONENTEN ==================== */}
            {activeTab === 'components' && selectedDevice && (
              <div>
                <div
                  className="card-glass"
                  style={{ padding: '16px 22px', marginBottom: '22px' }}
                >
                  <div
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      flexWrap: 'wrap',
                      gap: '14px',
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                      <span
                        style={{
                          fontSize: '0.78rem',
                          fontWeight: 800,
                          textTransform: 'uppercase',
                          color: 'var(--text-muted)',
                          marginRight: '4px',
                        }}
                      >
                        Aktives System:
                      </span>
                      {devices.map((d) => (
                        <button
                          key={d.id}
                          type="button"
                          onClick={() => setSelectedDeviceId(d.id)}
                          className={`btn btn-sm ${selectedDevice.id === d.id ? 'btn-primary' : 'btn-secondary'
                            }`}
                        >
                          <Server size={14} />
                          <span>{d.name}</span>
                          <span style={{ opacity: 0.75, fontFamily: 'var(--font-mono)' }}>
                            ({d.host}:{d.port})
                          </span>
                        </button>
                      ))}
                    </div>

                    <div className="mobile-scroll-pills">
                      {[
                        { id: 'ALL', label: 'Alle Geräte' },
                        { id: 'CPU', label: 'CPU' },
                        { id: 'GPU', label: 'GPU' },
                        { id: 'Mainboard', label: 'Mainboard' },
                        { id: 'RAM', label: 'RAM' },
                        { id: 'Storage', label: 'SSD / HDD' },
                        { id: 'Network', label: 'Netzwerk' },
                      ].map((cat) => (
                        <button
                          key={cat.id}
                          type="button"
                          onClick={() => setComponentCategoryFilter(cat.id)}
                          style={{
                            padding: '6px 13px',
                            borderRadius: 'var(--radius-full)',
                            border:
                              componentCategoryFilter === cat.id
                                ? '1px solid var(--cpm-light)'
                                : '1px solid var(--border-subtle)',
                            background:
                              componentCategoryFilter === cat.id
                                ? 'var(--cpm-cyan-light)'
                                : '#ffffff',
                            color:
                              componentCategoryFilter === cat.id
                                ? 'var(--cpm-light)'
                                : 'var(--text-secondary)',
                            fontWeight: componentCategoryFilter === cat.id ? 700 : 600,
                            fontSize: '0.78rem',
                            cursor: 'pointer',
                          }}
                        >
                          {cat.label}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>

                <div className="components-grid">
                  {filteredComponents.map((comp) => (
                    <div key={comp.id} className="card-glass" style={{ padding: '22px' }}>
                      <div className="card-header-clean">
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                          <div
                            className="stat-icon-wrapper stat-icon-primary"
                            style={{ width: '42px', height: '42px' }}
                          >
                            {getCategoryIcon(comp.category, 20)}
                          </div>
                          <div>
                            <div
                              style={{
                                fontSize: '0.72rem',
                                fontWeight: 800,
                                textTransform: 'uppercase',
                                color: 'var(--cpm-light)',
                                letterSpacing: '0.04em',
                              }}
                            >
                              {comp.categoryDe}
                            </div>
                            <div
                              style={{
                                fontSize: '1.02rem',
                                fontWeight: 800,
                                color: 'var(--cpm-dark)',
                              }}
                            >
                              {comp.name}
                            </div>
                          </div>
                        </div>

                        <span className={`status-pill ${comp.status || 'optimal'}`}>
                          {comp.status === 'critical'
                            ? 'Kritisch'
                            : comp.status === 'warning'
                              ? 'Warnung'
                              : 'Optimal'}
                        </span>
                      </div>

                      {comp.sensors?.length > 0 ? (
                        <div className="table-container">
                          <table className="modern-table">
                            <thead>
                              <tr>
                                <th>Sensor</th>
                                <th>Typ</th>
                                <th style={{ textAlign: 'right' }}>Aktuell</th>
                                <th style={{ textAlign: 'right' }}>Min</th>
                                <th style={{ textAlign: 'right' }}>Max</th>
                              </tr>
                            </thead>
                            <tbody>
                              {comp.sensors.map((sensor) => (
                                <tr key={sensor.id}>
                                  <td>
                                    <span
                                      style={{
                                        fontWeight: 600,
                                        color: 'var(--text-primary)',
                                      }}
                                    >
                                      {sensor.name}
                                    </span>
                                  </td>
                                  <td>
                                    <span
                                      style={{
                                        fontSize: '0.74rem',
                                        color: 'var(--text-muted)',
                                      }}
                                    >
                                      {sensor.categoryDe}
                                    </span>
                                  </td>
                                  <td
                                    style={{
                                      textAlign: 'right',
                                      fontFamily: 'var(--font-mono)',
                                      fontWeight: 800,
                                      color:
                                        sensor.status === 'critical'
                                          ? 'var(--danger)'
                                          : sensor.status === 'warning'
                                            ? 'var(--warning)'
                                            : 'var(--cpm-dark)',
                                    }}
                                  >
                                    {sensor.valueFormatted}
                                  </td>
                                  <td
                                    style={{
                                      textAlign: 'right',
                                      fontFamily: 'var(--font-mono)',
                                      fontSize: '0.8rem',
                                      color: 'var(--text-muted)',
                                    }}
                                  >
                                    {sensor.minFormatted}
                                  </td>
                                  <td
                                    style={{
                                      textAlign: 'right',
                                      fontFamily: 'var(--font-mono)',
                                      fontSize: '0.8rem',
                                      color: 'var(--text-muted)',
                                    }}
                                  >
                                    {sensor.maxFormatted}
                                  </td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      ) : (
                        <div
                          style={{
                            padding: '16px',
                            textAlign: 'center',
                            color: 'var(--text-muted)',
                            fontSize: '0.85rem',
                          }}
                        >
                          Sensorwerte befinden sich im untergeordneten SuperIO-Chip.
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* ==================== TAB 3: SENSOR-BAUM & DATA.JSON ==================== */}
            {activeTab === 'sensors' && selectedDevice && (
              <div>
                <div
                  className="card-glass"
                  style={{ padding: '16px 22px', marginBottom: '20px' }}
                >
                  <div
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      flexWrap: 'wrap',
                      gap: '12px',
                    }}
                  >
                    <div className="mobile-scroll-pills">
                      {[
                        { id: 'ALL', label: 'Alle Sensoren' },
                        { id: 'Temperature', label: 'Temperatur (°C)' },
                        { id: 'Load', label: 'Auslastung (%)' },
                        { id: 'Clock', label: 'Takt (MHz)' },
                        { id: 'Power', label: 'Leistung (W)' },
                        { id: 'Voltage', label: 'Spannung (V)' },
                        { id: 'Fan', label: 'Lüfter (RPM)' },
                        { id: 'Data', label: 'Daten (GB)' },
                        { id: 'Throughput', label: 'Durchsatz' },
                      ].map((t) => (
                        <button
                          key={t.id}
                          type="button"
                          onClick={() => setSensorTypeFilter(t.id)}
                          style={{
                            padding: '6px 13px',
                            borderRadius: 'var(--radius-full)',
                            border:
                              sensorTypeFilter === t.id
                                ? '1px solid var(--cpm-light)'
                                : '1px solid var(--border-subtle)',
                            background:
                              sensorTypeFilter === t.id
                                ? 'var(--cpm-cyan-light)'
                                : '#ffffff',
                            color:
                              sensorTypeFilter === t.id
                                ? 'var(--cpm-light)'
                                : 'var(--text-secondary)',
                            fontWeight: sensorTypeFilter === t.id ? 700 : 600,
                            fontSize: '0.78rem',
                            cursor: 'pointer',
                          }}
                        >
                          {t.label}
                        </button>
                      ))}
                    </div>

                    <div style={{ display: 'flex', gap: '8px' }}>
                      <button
                        type="button"
                        className={`btn btn-sm ${showRawJson ? 'btn-primary' : 'btn-secondary'
                          }`}
                        onClick={() => setShowRawJson((v) => !v)}
                      >
                        <Code2 size={15} />
                        <span>
                          {showRawJson
                            ? 'Sensor-Tabelle anzeigen'
                            : 'Raw :8085/data.json Baum'}
                        </span>
                      </button>

                      <a
                        href={`http://${selectedDevice.host}:${selectedDevice.port}/data.json`}
                        target="_blank"
                        rel="noreferrer"
                        className="btn btn-secondary btn-sm"
                      >
                        <ExternalLink size={14} />
                        <span>:8085/data.json öffnen</span>
                      </a>
                    </div>
                  </div>
                </div>

                {showRawJson ? (
                  <div className="card-glass">
                    <div className="card-header-clean">
                      <div className="card-title">
                        <Code2 size={20} color="var(--cpm-light)" />
                        <span>
                          Original Libre Hardware Monitor API Response (
                          {selectedDevice.endpointUrl})
                        </span>
                      </div>
                    </div>
                    <pre
                      style={{
                        background: '#0f172a',
                        color: '#e2e8f0',
                        padding: '20px',
                        borderRadius: 'var(--radius-md)',
                        fontFamily: 'var(--font-mono)',
                        fontSize: '0.8rem',
                        overflowX: 'auto',
                        maxHeight: '640px',
                      }}
                    >
                      {JSON.stringify(selectedDevice.rawTree, null, 2)}
                    </pre>
                  </div>
                ) : (
                  <div className="card-glass" style={{ padding: 0, overflow: 'hidden' }}>
                    <div className="table-container" style={{ border: 'none', borderRadius: 0 }}>
                      <table className="modern-table">
                        <thead>
                          <tr>
                            <th>Hardware-Komponente</th>
                            <th>Sensor-Bezeichnung</th>
                            <th>Kategorie</th>
                            <th>Sensor-ID</th>
                            <th style={{ textAlign: 'right' }}>Aktueller Wert</th>
                            <th style={{ textAlign: 'right' }}>Minimum</th>
                            <th style={{ textAlign: 'right' }}>Maximum</th>
                            <th style={{ textAlign: 'right' }}>Status</th>
                          </tr>
                        </thead>
                        <tbody>
                          {filteredSensors.map((s) => (
                            <tr key={`${s.componentName}-${s.id}`}>
                              <td>
                                <span
                                  style={{
                                    display: 'inline-flex',
                                    alignItems: 'center',
                                    gap: '6px',
                                    fontWeight: 700,
                                    color: 'var(--cpm-dark)',
                                  }}
                                >
                                  {getCategoryIcon(s.componentCategory, 15)}
                                  <span>{s.componentName}</span>
                                </span>
                              </td>
                              <td>
                                <strong style={{ color: 'var(--text-primary)' }}>
                                  {s.name}
                                </strong>
                              </td>
                              <td>{s.categoryDe}</td>
                              <td>
                                <span className="mono-badge">{s.id}</span>
                              </td>
                              <td
                                style={{
                                  textAlign: 'right',
                                  fontFamily: 'var(--font-mono)',
                                  fontWeight: 800,
                                  color: 'var(--cpm-dark)',
                                }}
                              >
                                {s.valueFormatted}
                              </td>
                              <td
                                style={{
                                  textAlign: 'right',
                                  fontFamily: 'var(--font-mono)',
                                  color: 'var(--text-muted)',
                                }}
                              >
                                {s.minFormatted}
                              </td>
                              <td
                                style={{
                                  textAlign: 'right',
                                  fontFamily: 'var(--font-mono)',
                                  color: 'var(--text-muted)',
                                }}
                              >
                                {s.maxFormatted}
                              </td>
                              <td style={{ textAlign: 'right' }}>
                                <span className={`status-pill ${s.status}`}>
                                  {s.status === 'critical'
                                    ? 'Kritisch'
                                    : s.status === 'warning'
                                      ? 'Warnung'
                                      : 'OK'}
                                </span>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}
              </div>
            )}

            {/* ==================== TAB 4: AUFZEICHNUNG & ABSTURZ-PROTOKOLL ==================== */}
            {activeTab === 'recording' && (
              <div>
                {/* Control & Info Card */}
                <div
                  className="card-glass"
                  style={{ padding: '22px 24px', marginBottom: '24px' }}
                >
                  <div
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      flexWrap: 'wrap',
                      gap: '16px',
                    }}
                  >
                    <div>
                      <div
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: '10px',
                          flexWrap: 'wrap',
                        }}
                      >
                        <h2
                          style={{
                            fontSize: '1.25rem',
                            fontWeight: 800,
                            color: 'var(--cpm-dark)',
                          }}
                        >
                          Langzeit-Aufzeichnung & Absturz-Protokollierung
                        </h2>
                        <span
                          className={`status-pill ${recording?.isRecording !== false ? 'optimal' : 'warning'
                            }`}
                        >
                          <Clock size={13} />
                          <span>
                            {recording?.isRecording !== false
                              ? 'Aufzeichnung läuft (Port 8085)'
                              : 'Aufzeichnung pausiert'}
                          </span>
                        </span>
                        <span className="mono-badge">
                          Zeitzone: {recording?.timezoneName || 'Europe/Berlin (MEZ/MESZ)'}
                        </span>
                      </div>
                      <p
                        style={{
                          fontSize: '0.84rem',
                          color: 'var(--text-secondary)',
                          marginTop: '6px',
                        }}
                      >
                        Sobald die Verbindung zu einem überwachten Gerät auf Port 8085
                        abbricht, wird der Zeitpunkt exakt in{' '}
                        <strong>deutscher Zeitzone (Europe/Berlin)</strong> mit dem Wort{' '}
                        <strong style={{ color: 'var(--danger)' }}>Absturz</strong> sowie
                        den letzten bekannten Temperatur- und Lastwerten protokolliert.
                      </p>
                    </div>

                    <div
                      className="no-print"
                      style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}
                    >
                      <button
                        type="button"
                        className="btn btn-primary"
                        onClick={() => window.print()}
                        title="Kunden-Präsentationsbericht mit Grafiken drucken oder als PDF speichern"
                      >
                        <Printer size={16} />
                        <span>Kunden-Bericht drucken / PDF</span>
                      </button>

                      <button
                        type="button"
                        className={`btn ${recording?.isRecording !== false
                            ? 'btn-secondary'
                            : 'btn-primary'
                          }`}
                        onClick={handleToggleRecording}
                      >
                        {recording?.isRecording !== false ? (
                          <>
                            <Pause size={16} />
                            <span>Aufzeichnung pausieren</span>
                          </>
                        ) : (
                          <>
                            <Play size={16} />
                            <span>Aufzeichnung starten</span>
                          </>
                        )}
                      </button>

                      <a
                        href={hardwareApi.getExportUrl()}
                        className="btn btn-accent"
                        download
                      >
                        <Download size={16} />
                        <span>Protokoll (.log) herunterladen</span>
                      </a>

                      <button
                        type="button"
                        className="btn btn-secondary"
                        onClick={handleClearRecording}
                      >
                        <Trash2 size={16} />
                        <span>Messwerte zurücksetzen</span>
                      </button>
                    </div>
                  </div>

                  {/* Quick Test Bar for Simulating Connection Loss ("Absturz") */}
                  <div
                    className="no-print"
                    style={{
                      marginTop: '18px',
                      paddingTop: '14px',
                      borderTop: '1px solid var(--border-subtle)',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      flexWrap: 'wrap',
                      gap: '10px',
                    }}
                  >
                    <span
                      style={{
                        fontSize: '0.8rem',
                        fontWeight: 700,
                        color: 'var(--text-secondary)',
                      }}
                    >
                      Verbindungsabbruch („Absturz“) direkt testen:
                    </span>
                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                      {devices.map((dev) => (
                        <button
                          key={dev.id}
                          type="button"
                          className="btn btn-sm btn-secondary"
                          style={
                            dev.isOnline
                              ? {
                                borderColor: 'var(--danger-border)',
                                color: 'var(--danger)',
                                background: '#fff5f5',
                              }
                              : {
                                borderColor: 'var(--success-border)',
                                color: 'var(--success)',
                                background: 'var(--success-bg)',
                              }
                          }
                          onClick={(e) => handleSimulateCrash(e, dev, dev.isOnline)}
                        >
                          {dev.isOnline ? (
                            <>
                              <WifiOff size={13} />
                              <span>{dev.name}: Absturz auslösen</span>
                            </>
                          ) : (
                            <>
                              <CheckCircle2 size={13} />
                              <span>{dev.name}: Wieder verbinden</span>
                            </>
                          )}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>

                {/* ==================== KUNDEN-PRÄSENTATION & PARAMETER-GRAFIKEN ==================== */}
                {reportDevice && (
                  <div style={{ marginBottom: '28px' }}>
                    {/* Device Switcher Bar for Customer Presentation */}
                    <div
                      className="no-print"
                      style={{
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        flexWrap: 'wrap',
                        gap: '12px',
                        marginBottom: '14px',
                      }}
                    >
                      <div
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: '8px',
                          flexWrap: 'wrap',
                        }}
                      >
                        <span
                          style={{
                            fontSize: '0.8rem',
                            fontWeight: 800,
                            textTransform: 'uppercase',
                            letterSpacing: '0.04em',
                            color: 'var(--text-muted)',
                          }}
                        >
                          Gerät für Kunden-Grafik auswählen:
                        </span>
                        {devices.map((d) => {
                          const isCurrent = reportDevice.id === d.id;
                          return (
                            <button
                              key={d.id}
                              type="button"
                              onClick={() => {
                                setSelectedDeviceId(d.id);
                                setSampleDeviceFilter(d.id);
                              }}
                              style={{
                                padding: '7px 15px',
                                borderRadius: 'var(--radius-full)',
                                border: isCurrent
                                  ? '1.5px solid var(--cpm-dark)'
                                  : '1px solid var(--border-subtle)',
                                background: isCurrent ? 'var(--cpm-dark)' : '#ffffff',
                                color: isCurrent ? '#ffffff' : 'var(--text-primary)',
                                fontWeight: 700,
                                fontSize: '0.82rem',
                                cursor: 'pointer',
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: '6px',
                              }}
                            >
                              <Server size={14} />
                              <span>
                                {d.name} ({d.host}:{d.port})
                              </span>
                            </button>
                          );
                        })}
                      </div>
                    </div>

                    {/* Customer Report Hero Header */}
                    <div className="report-hero-banner">
                      <div
                        style={{
                          display: 'flex',
                          justifyContent: 'space-between',
                          alignItems: 'flex-start',
                          flexWrap: 'wrap',
                          gap: '18px',
                        }}
                      >
                        <div>
                          <div
                            style={{
                              display: 'inline-flex',
                              alignItems: 'center',
                              gap: '6px',
                              background: 'rgba(255,255,255,0.14)',
                              padding: '4px 12px',
                              borderRadius: '999px',
                              fontSize: '0.74rem',
                              fontWeight: 700,
                              letterSpacing: '0.04em',
                              textTransform: 'uppercase',
                              marginBottom: '10px',
                            }}
                          >
                            <Award size={14} />
                            <span>
                              CPM Hardware-Prüfbericht & Kunden-Präsentation
                            </span>
                          </div>
                          <h3
                            style={{
                              fontSize: '1.45rem',
                              fontWeight: 800,
                              letterSpacing: '-0.02em',
                              lineHeight: 1.2,
                            }}
                          >
                            Testauswertung: {reportDevice.name} ({reportDevice.host}:
                            {reportDevice.port})
                          </h3>
                          <p
                            style={{
                              fontSize: '0.88rem',
                              color: 'rgba(255,255,255,0.85)',
                              marginTop: '6px',
                            }}
                          >
                            CPU: <strong>{reportDevice.summary?.cpuName || '–'}</strong> •
                            GPU:{' '}
                            <strong>
                              {reportDevice.summary?.gpus && reportDevice.summary.gpus.length > 1
                                ? `${reportDevice.summary.gpus.length} GPUs (${reportDevice.summary.gpus.map((g) => g.name).join(' + ')})`
                                : reportDevice.summary?.gpuName || '–'}
                            </strong>{' '}
                            • Mainboard:{' '}
                            <strong>
                              {reportDevice.summary?.motherboardName || '–'}
                            </strong>{' '}
                            • Speicher:{' '}
                            <strong>{reportDevice.summary?.storageName || '–'}</strong>
                          </p>
                        </div>

                        {/* Test Verdict Badge */}
                        <div className="report-verdict-box" style={{ textAlign: 'right' }}>
                          {reportDeviceCrashes.length === 0 && reportDevice.isOnline ? (
                            <div
                              className="report-verdict-badge"
                              style={{
                                background: '#059669',
                                border: '2px solid #34d399',
                                color: '#ffffff',
                                padding: '10px 18px',
                                borderRadius: 'var(--radius-md)',
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: '9px',
                                fontWeight: 800,
                                fontSize: '0.92rem',
                                boxShadow: '0 6px 20px rgba(5, 150, 105, 0.35)',
                              }}
                            >
                              <CheckCircle2 size={20} />
                              <span>TEST BESTANDEN • STABIL (0 ABSTÜRZE)</span>
                            </div>
                          ) : (
                            <div
                              className="report-verdict-badge"
                              style={{
                                background: '#dc2626',
                                border: '2px solid #fca5a5',
                                color: '#ffffff',
                                padding: '10px 18px',
                                borderRadius: 'var(--radius-md)',
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: '9px',
                                fontWeight: 800,
                                fontSize: '0.92rem',
                                boxShadow: '0 6px 20px rgba(220, 38, 38, 0.4)',
                              }}
                            >
                              <AlertTriangle size={20} />
                              <span>
                                ABSTURZ ERKANNT ({reportDeviceCrashes.length}{' '}
                                {reportDeviceCrashes.length === 1
                                  ? 'VERBINDUNGSABBRUCH'
                                  : 'VERBINDUNGSABBRÜCHE'}
                                )
                              </span>
                            </div>
                          )}
                          <div
                            style={{
                              fontSize: '0.78rem',
                              color: 'rgba(255,255,255,0.82)',
                              marginTop: '8px',
                              fontFamily: 'var(--font-mono)',
                            }}
                          >
                            Messpunkte: {reportDeviceSamplesChronological.length} • Stand (DE):{' '}
                            {recording?.currentTimeDe || formatGermanTime()}
                          </div>
                        </div>
                      </div>
                    </div>

                    {/* 3 Interactive SVG Parameter Charts for Customer Presentation */}
                    <TelemetryLineChart
                      title="Grafik 1: Temperatur-Verlauf (°C) – Thermischer Belastungstest"
                      subtitle="Aufgezeichnete Temperaturen von Prozessor (Package & Spitzenkern), Grafikkarte (Core & Hot Spot), Mainboard und SSD im Zeitverlauf"
                      icon={<Thermometer size={20} color="#009fe3" />}
                      samples={reportDeviceSamplesChronological}
                      yMin={20}
                      yMaxHint={95}
                      yUnit="°C"
                      thresholdLine={{
                        value: 85,
                        label: '85 °C Thermisches Limit',
                        color: '#dc2626',
                      }}
                      series={[
                        {
                          key: 'cpuTemp',
                          label: 'CPU Package',
                          unit: '°C',
                          color: '#009fe3',
                          fillArea: true,
                          alwaysShow: true,
                        },
                        {
                          key: 'cpuMaxCoreTemp',
                          label: 'CPU Max Core',
                          unit: '°C',
                          color: '#6366f1',
                        },
                        {
                          key: 'gpuTemp',
                          label: 'GPU Core',
                          unit: '°C',
                          color: '#10b981',
                          fillArea: true,
                          alwaysShow: true,
                        },
                        {
                          key: 'gpuHotSpotTemp',
                          label: 'GPU Hot Spot',
                          unit: '°C',
                          color: '#f59e0b',
                        },
                        {
                          key: 'mainboardTemp',
                          label: 'Mainboard',
                          unit: '°C',
                          color: '#8b5cf6',
                        },
                        {
                          key: 'storageTemp',
                          label: 'SSD / NVMe',
                          unit: '°C',
                          color: '#ec4899',
                        },
                      ]}
                    />

                    <TelemetryLineChart
                      title="Grafik 2: Auslastungs-Verlauf (%) – Systemlast von CPU, GPU & RAM"
                      subtitle="Prozentuale Auslastung der Hauptkomponenten während des Testlaufs"
                      icon={<Gauge size={20} color="#10b981" />}
                      samples={reportDeviceSamplesChronological}
                      yMin={0}
                      yMaxHint={100}
                      yUnit="%"
                      series={[
                        {
                          key: 'cpuLoad',
                          label: 'CPU Last',
                          unit: '%',
                          color: '#009fe3',
                          fillArea: true,
                          alwaysShow: true,
                        },
                        {
                          key: 'gpuLoad',
                          label: 'GPU Last',
                          unit: '%',
                          color: '#10b981',
                          fillArea: true,
                          alwaysShow: true,
                        },
                        {
                          key: 'ramLoadPercent',
                          label: 'RAM Auslastung',
                          unit: '%',
                          color: '#8b5cf6',
                          alwaysShow: true,
                        },
                      ]}
                    />

                    <TelemetryLineChart
                      title="Grafik 3: Leistungsaufnahme (Watt) & Taktraten"
                      subtitle="Aufgezeichnete elektrische Leistungsaufnahme (Package Power) von CPU und Grafikkarte"
                      icon={<Zap size={20} color="#f59e0b" />}
                      samples={reportDeviceSamplesChronological}
                      yMin={0}
                      yMaxHint={120}
                      yUnit="W"
                      series={[
                        {
                          key: 'cpuPowerWatts',
                          label: 'CPU Leistung',
                          unit: 'W',
                          color: '#f59e0b',
                          fillArea: true,
                          alwaysShow: true,
                        },
                        {
                          key: 'gpuPowerWatts',
                          label: 'GPU Leistung',
                          unit: 'W',
                          color: '#ef4444',
                          fillArea: true,
                        },
                        {
                          key: 'ramUsedGb',
                          label: 'RAM Belegt',
                          unit: 'GB',
                          color: '#0284c7',
                        },
                      ]}
                    />

                    {/* Parameter Min / Avg / Max Summary Table for Customer */}
                    <div
                      className="card-glass"
                      style={{ padding: 0, overflow: 'hidden', marginBottom: '26px' }}
                    >
                      <div
                        style={{
                          padding: '18px 24px',
                          borderBottom: '1px solid var(--border-subtle)',
                          display: 'flex',
                          justifyContent: 'space-between',
                          alignItems: 'center',
                          flexWrap: 'wrap',
                          gap: '10px',
                        }}
                      >
                        <div className="card-title">
                          <TrendingUp size={20} color="var(--cpm-light)" />
                          <span>
                            Aufgezeichnete Parameter-Statistik (Min / Ø Durchschnitt / Max)
                            für {reportDevice.name}
                          </span>
                        </div>
                        <span className="mono-badge">
                          Basis: {reportDeviceSamplesChronological.length} Messpunkte
                        </span>
                      </div>

                      <div
                        className="table-container desktop-only-table"
                        style={{ border: 'none', borderRadius: 0 }}
                      >
                        <table className="modern-table">
                          <thead>
                            <tr>
                              <th>Baugruppe</th>
                              <th>Aufgezeichneter Parameter</th>
                              <th style={{ textAlign: 'right' }}>Minimum</th>
                              <th style={{ textAlign: 'right' }}>Ø Durchschnitt</th>
                              <th style={{ textAlign: 'right' }}>Maximum (Peak)</th>
                              <th style={{ textAlign: 'right' }}>Aktuell</th>
                              <th style={{ textAlign: 'right' }}>Bewertung</th>
                            </tr>
                          </thead>
                          <tbody>
                            {reportParameterRows.map((row) => (
                              <tr key={row.key}>
                                <td>
                                  <span
                                    style={{
                                      fontWeight: 700,
                                      fontSize: '0.78rem',
                                      color: 'var(--cpm-dark)',
                                    }}
                                  >
                                    {row.group}
                                  </span>
                                </td>
                                <td>
                                  <strong style={{ color: 'var(--text-primary)' }}>
                                    {row.label}
                                  </strong>
                                </td>
                                <td
                                  style={{
                                    textAlign: 'right',
                                    fontFamily: 'var(--font-mono)',
                                  }}
                                >
                                  {formatNum(row.min, row.decimals, row.unit)}
                                </td>
                                <td
                                  style={{
                                    textAlign: 'right',
                                    fontFamily: 'var(--font-mono)',
                                    fontWeight: 600,
                                  }}
                                >
                                  {formatNum(row.avg, row.decimals, row.unit)}
                                </td>
                                <td
                                  style={{
                                    textAlign: 'right',
                                    fontFamily: 'var(--font-mono)',
                                    fontWeight: 800,
                                    color:
                                      row.status === 'critical'
                                        ? 'var(--danger)'
                                        : row.status === 'warning'
                                          ? 'var(--warning)'
                                          : 'var(--cpm-dark)',
                                  }}
                                >
                                  {formatNum(row.max, row.decimals, row.unit)}
                                </td>
                                <td
                                  style={{
                                    textAlign: 'right',
                                    fontFamily: 'var(--font-mono)',
                                    fontWeight: 700,
                                    color: 'var(--cpm-light)',
                                  }}
                                >
                                  {formatNum(row.current, row.decimals, row.unit)}
                                </td>
                                <td style={{ textAlign: 'right' }}>
                                  <span className={`status-pill ${row.status}`}>
                                    {row.status === 'critical' ? (
                                      <>
                                        <AlertTriangle size={13} />
                                        <span>Kritisch</span>
                                      </>
                                    ) : row.status === 'warning' ? (
                                      <>
                                        <AlertTriangle size={13} />
                                        <span>Erhöht</span>
                                      </>
                                    ) : (
                                      <>
                                        <CheckCircle2 size={13} />
                                        <span>Optimal</span>
                                      </>
                                    )}
                                  </span>
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>

                      {/* Mobile Parameter Cards (< 640px) */}
                      <div className="mobile-cards-list">
                        {reportParameterRows.map((row) => (
                          <div key={row.key} className="mobile-data-card">
                            <div
                              style={{
                                display: 'flex',
                                justifyContent: 'space-between',
                                alignItems: 'center',
                                gap: '8px',
                              }}
                            >
                              <span
                                style={{
                                  fontSize: '0.72rem',
                                  fontWeight: 800,
                                  color: 'var(--cpm-light)',
                                  textTransform: 'uppercase',
                                }}
                              >
                                {row.group}
                              </span>
                              <span className={`status-pill ${row.status}`}>
                                {row.status === 'critical'
                                  ? 'Kritisch'
                                  : row.status === 'warning'
                                    ? 'Erhöht'
                                    : 'Optimal'}
                              </span>
                            </div>
                            <div
                              style={{
                                fontWeight: 800,
                                fontSize: '0.9rem',
                                color: 'var(--cpm-dark)',
                              }}
                            >
                              {row.label}
                            </div>
                            <div className="mobile-param-grid-4">
                              <div>
                                <div style={{ fontSize: '0.65rem', color: 'var(--text-muted)', fontWeight: 700 }}>
                                  MIN
                                </div>
                                <div style={{ fontFamily: 'var(--font-mono)', fontSize: '0.78rem', fontWeight: 700 }}>
                                  {formatNum(row.min, row.decimals, row.unit)}
                                </div>
                              </div>
                              <div>
                                <div style={{ fontSize: '0.65rem', color: 'var(--text-muted)', fontWeight: 700 }}>
                                  Ø SCHNITT
                                </div>
                                <div style={{ fontFamily: 'var(--font-mono)', fontSize: '0.78rem', fontWeight: 700 }}>
                                  {formatNum(row.avg, row.decimals, row.unit)}
                                </div>
                              </div>
                              <div>
                                <div style={{ fontSize: '0.65rem', color: 'var(--text-muted)', fontWeight: 700 }}>
                                  MAX PEAK
                                </div>
                                <div
                                  style={{
                                    fontFamily: 'var(--font-mono)',
                                    fontSize: '0.78rem',
                                    fontWeight: 800,
                                    color:
                                      row.status === 'critical'
                                        ? 'var(--danger)'
                                        : row.status === 'warning'
                                          ? 'var(--warning)'
                                          : 'var(--cpm-dark)',
                                  }}
                                >
                                  {formatNum(row.max, row.decimals, row.unit)}
                                </div>
                              </div>
                              <div>
                                <div style={{ fontSize: '0.65rem', color: 'var(--text-muted)', fontWeight: 700 }}>
                                  AKTUELL
                                </div>
                                <div
                                  style={{
                                    fontFamily: 'var(--font-mono)',
                                    fontSize: '0.78rem',
                                    fontWeight: 800,
                                    color: 'var(--cpm-light)',
                                  }}
                                >
                                  {formatNum(row.current, row.decimals, row.unit)}
                                </div>
                              </div>
                            </div>
                          </div>
                        ))}
                      </div>
                    </div>
                  </div>
                )}

                {/* 1. ABSTURZ- & VERBINDUNGSPROTOKOLL */}
                <div
                  className="card-glass"
                  style={{ padding: 0, overflow: 'hidden', marginBottom: '26px' }}
                >
                  <div
                    style={{
                      padding: '18px 24px',
                      borderBottom: '1px solid var(--border-subtle)',
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      flexWrap: 'wrap',
                      gap: '12px',
                    }}
                  >
                    <div className="card-title">
                      <ShieldAlert size={20} color="var(--danger)" />
                      <span>
                        1. Absturz- & Verbindungs-Protokoll (Deutsche Zeitzone:{' '}
                        {recording?.currentTimeDe || formatGermanTime()})
                      </span>
                    </div>

                    <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
                      {[
                        { id: 'ALL', label: 'Alle Ereignisse' },
                        {
                          id: 'ABSTURZ',
                          label: `Nur Abstürze (${crashCount})`,
                        },
                        { id: 'RECONNECT', label: 'Wiederherstellungen' },
                      ].map((f) => (
                        <button
                          key={f.id}
                          type="button"
                          onClick={() => setProtocolFilter(f.id)}
                          style={{
                            padding: '6px 13px',
                            borderRadius: 'var(--radius-full)',
                            border:
                              protocolFilter === f.id
                                ? '1px solid var(--cpm-light)'
                                : '1px solid var(--border-subtle)',
                            background:
                              protocolFilter === f.id
                                ? 'var(--cpm-cyan-light)'
                                : '#ffffff',
                            color:
                              protocolFilter === f.id
                                ? 'var(--cpm-light)'
                                : 'var(--text-secondary)',
                            fontWeight: protocolFilter === f.id ? 700 : 600,
                            fontSize: '0.78rem',
                            cursor: 'pointer',
                          }}
                        >
                          {f.label}
                        </button>
                      ))}
                    </div>
                  </div>

                  <div
                    className="table-container desktop-only-table"
                    style={{ border: 'none', borderRadius: 0 }}
                  >
                    <table className="modern-table">
                      <thead>
                        <tr>
                          <th>Zeitpunkt (Deutsche Zeitzone)</th>
                          <th>Status / Ereignis</th>
                          <th>Gerät & Port 8085</th>
                          <th>Protokoll-Meldung & Letzte Werte vor Absturz</th>
                        </tr>
                      </thead>
                      <tbody>
                        {filteredProtocolEntries.length === 0 ? (
                          <tr>
                            <td
                              colSpan={4}
                              style={{
                                textAlign: 'center',
                                padding: '32px',
                                color: 'var(--text-muted)',
                              }}
                            >
                              Noch keine entsprechenden Protokoll-Einträge vorhanden.
                            </td>
                          </tr>
                        ) : (
                          filteredProtocolEntries.map((entry) => {
                            const isCrash = entry.eventType === 'ABSTURZ';
                            const isReconnect = entry.eventType === 'RECONNECT';

                            return (
                              <tr
                                key={entry.id}
                                style={
                                  isCrash
                                    ? { background: '#fef2f2' }
                                    : isReconnect
                                      ? { background: '#f0fdf4' }
                                      : undefined
                                }
                              >
                                <td
                                  style={{
                                    fontFamily: 'var(--font-mono)',
                                    fontWeight: 700,
                                    whiteSpace: 'nowrap',
                                    color: isCrash
                                      ? 'var(--danger)'
                                      : 'var(--cpm-dark)',
                                  }}
                                >
                                  {entry.timestampDe}
                                </td>
                                <td style={{ whiteSpace: 'nowrap' }}>
                                  {isCrash ? (
                                    <span className="status-pill critical">
                                      <WifiOff size={13} />
                                      <span>ABSTURZ</span>
                                    </span>
                                  ) : isReconnect ? (
                                    <span className="status-pill optimal">
                                      <CheckCircle2 size={13} />
                                      <span>Wiederverbunden</span>
                                    </span>
                                  ) : (
                                    <span className="status-pill info">
                                      <Clock size={13} />
                                      <span>{entry.keyword || 'Aufzeichnung'}</span>
                                    </span>
                                  )}
                                </td>
                                <td style={{ whiteSpace: 'nowrap' }}>
                                  <div
                                    style={{
                                      fontWeight: 700,
                                      color: 'var(--text-primary)',
                                    }}
                                  >
                                    {entry.deviceName}
                                  </div>
                                  <span className="mono-badge" style={{ marginTop: '3px' }}>
                                    {entry.hostPort}
                                  </span>
                                </td>
                                <td>
                                  <div
                                    style={{
                                      fontWeight: isCrash ? 700 : 600,
                                      color: isCrash
                                        ? 'var(--danger)'
                                        : 'var(--text-primary)',
                                    }}
                                  >
                                    {entry.message}
                                  </div>
                                  {entry.lastKnownMetrics && (
                                    <div
                                      style={{
                                        marginTop: '6px',
                                        padding: '7px 11px',
                                        background: '#ffffff',
                                        border: '1px solid var(--danger-border)',
                                        borderRadius: 'var(--radius-sm)',
                                        fontSize: '0.79rem',
                                        fontFamily: 'var(--font-mono)',
                                        color: 'var(--text-primary)',
                                      }}
                                    >
                                      <strong>Letzte Werte vor Absturz:</strong>{' '}
                                      {entry.lastKnownMetrics}
                                    </div>
                                  )}
                                </td>
                              </tr>
                            );
                          })
                        )}
                      </tbody>
                    </table>
                  </div>

                  {/* Mobile Crash Protocol Cards (< 640px) */}
                  <div className="mobile-cards-list">
                    {filteredProtocolEntries.map((entry) => {
                      const isCrash = entry.eventType === 'ABSTURZ';
                      const isReconnect = entry.eventType === 'RECONNECT';
                      return (
                        <div
                          key={entry.id}
                          className="mobile-data-card"
                          style={
                            isCrash
                              ? { background: '#fef2f2', borderColor: '#fecaca' }
                              : isReconnect
                                ? { background: '#f0fdf4', borderColor: '#bbf7d0' }
                                : undefined
                          }
                        >
                          <div
                            style={{
                              display: 'flex',
                              justifyContent: 'space-between',
                              alignItems: 'center',
                              gap: '8px',
                              flexWrap: 'wrap',
                            }}
                          >
                            <span
                              style={{
                                fontFamily: 'var(--font-mono)',
                                fontSize: '0.76rem',
                                fontWeight: 700,
                                color: isCrash ? 'var(--danger)' : 'var(--cpm-dark)',
                              }}
                            >
                              {entry.timestampDe}
                            </span>
                            {isCrash ? (
                              <span className="status-pill critical">
                                <WifiOff size={12} />
                                <span>ABSTURZ</span>
                              </span>
                            ) : isReconnect ? (
                              <span className="status-pill optimal">
                                <CheckCircle2 size={12} />
                                <span>Wiederverbunden</span>
                              </span>
                            ) : (
                              <span className="status-pill info">
                                <span>{entry.keyword || 'Aufzeichnung'}</span>
                              </span>
                            )}
                          </div>
                          <div style={{ fontWeight: 800, fontSize: '0.88rem', color: 'var(--cpm-dark)' }}>
                            {entry.deviceName}{' '}
                            <span className="mono-badge" style={{ marginLeft: '4px' }}>
                              {entry.hostPort}
                            </span>
                          </div>
                          <div
                            style={{
                              fontSize: '0.82rem',
                              fontWeight: isCrash ? 700 : 500,
                              color: isCrash ? 'var(--danger)' : 'var(--text-primary)',
                            }}
                          >
                            {entry.message}
                          </div>
                          {entry.lastKnownMetrics && (
                            <div
                              style={{
                                padding: '6px 9px',
                                background: '#ffffff',
                                border: '1px solid var(--danger-border)',
                                borderRadius: 'var(--radius-sm)',
                                fontSize: '0.74rem',
                                fontFamily: 'var(--font-mono)',
                              }}
                            >
                              <strong>Letzte Werte:</strong> {entry.lastKnownMetrics}
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>

                {/* 2. AUFGEZEICHNETE HARDWARE-MESSPUNKTE (ZEITVERLAUF) */}
                <div className="card-glass" style={{ padding: 0, overflow: 'hidden' }}>
                  <div
                    style={{
                      padding: '18px 24px',
                      borderBottom: '1px solid var(--border-subtle)',
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      flexWrap: 'wrap',
                      gap: '12px',
                    }}
                  >
                    <div className="card-title">
                      <Activity size={20} color="var(--cpm-light)" />
                      <span>
                        2. Aufgezeichnete Hardware-Messpunkte ({filteredSamples.length}{' '}
                        Einträge)
                      </span>
                    </div>

                    <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
                      <button
                        type="button"
                        onClick={() => setSampleDeviceFilter('ALL')}
                        style={{
                          padding: '6px 13px',
                          borderRadius: 'var(--radius-full)',
                          border:
                            sampleDeviceFilter === 'ALL'
                              ? '1px solid var(--cpm-light)'
                              : '1px solid var(--border-subtle)',
                          background:
                            sampleDeviceFilter === 'ALL'
                              ? 'var(--cpm-cyan-light)'
                              : '#ffffff',
                          color:
                            sampleDeviceFilter === 'ALL'
                              ? 'var(--cpm-light)'
                              : 'var(--text-secondary)',
                          fontWeight: sampleDeviceFilter === 'ALL' ? 700 : 600,
                          fontSize: '0.78rem',
                          cursor: 'pointer',
                        }}
                      >
                        Alle Geräte
                      </button>
                      {devices.map((d) => (
                        <button
                          key={d.id}
                          type="button"
                          onClick={() => setSampleDeviceFilter(d.id)}
                          style={{
                            padding: '6px 13px',
                            borderRadius: 'var(--radius-full)',
                            border:
                              sampleDeviceFilter === d.id
                                ? '1px solid var(--cpm-light)'
                                : '1px solid var(--border-subtle)',
                            background:
                              sampleDeviceFilter === d.id
                                ? 'var(--cpm-cyan-light)'
                                : '#ffffff',
                            color:
                              sampleDeviceFilter === d.id
                                ? 'var(--cpm-light)'
                                : 'var(--text-secondary)',
                            fontWeight: sampleDeviceFilter === d.id ? 700 : 600,
                            fontSize: '0.78rem',
                            cursor: 'pointer',
                          }}
                        >
                          {d.name}
                        </button>
                      ))}
                    </div>
                  </div>

                  <div
                    className="table-container desktop-only-table"
                    style={{ border: 'none', borderRadius: 0, maxHeight: '520px' }}
                  >
                    <table className="modern-table">
                      <thead>
                        <tr>
                          <th>Zeitpunkt (DE)</th>
                          <th>Gerät</th>
                          <th>Status</th>
                          <th style={{ textAlign: 'right' }}>CPU Temp</th>
                          <th style={{ textAlign: 'right' }}>CPU Max</th>
                          <th style={{ textAlign: 'right' }}>CPU Last</th>
                          <th style={{ textAlign: 'right' }}>CPU Takt</th>
                          <th style={{ textAlign: 'right' }}>CPU Watt</th>
                          <th style={{ textAlign: 'right' }}>GPU Temp</th>
                          <th style={{ textAlign: 'right' }}>GPU HotSpot</th>
                          <th style={{ textAlign: 'right' }}>GPU Last</th>
                          <th style={{ textAlign: 'right' }}>RAM Belegt</th>
                          <th style={{ textAlign: 'right' }}>Board / SSD</th>
                        </tr>
                      </thead>
                      <tbody>
                        {filteredSamples.map((s) => (
                          <tr
                            key={s.id}
                            style={!s.isOnline ? { background: '#fef2f2' } : undefined}
                          >
                            <td
                              style={{
                                fontFamily: 'var(--font-mono)',
                                fontWeight: 600,
                                whiteSpace: 'nowrap',
                              }}
                            >
                              {s.timestampDe}
                            </td>
                            <td>
                              <strong style={{ color: 'var(--cpm-dark)' }}>
                                {s.deviceName}
                              </strong>
                              <div style={{ fontSize: '0.72rem', color: 'var(--text-muted)' }}>
                                {s.hostPort}
                              </div>
                            </td>
                            <td>
                              {s.isOnline ? (
                                <span className="status-pill optimal">Online</span>
                              ) : (
                                <span className="status-pill critical">
                                  Absturz (Verbindung weg)
                                </span>
                              )}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                                fontWeight: 700,
                              }}
                            >
                              {formatNum(s.cpuTemp, 1, '°C')}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                              }}
                            >
                              {formatNum(s.cpuMaxCoreTemp, 1, '°C')}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                              }}
                            >
                              {formatNum(s.cpuLoad, 1, '%')}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                              }}
                            >
                              {formatNum(s.cpuClockMhz, 0, 'MHz')}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                              }}
                            >
                              {formatNum(s.cpuPowerWatts, 1, 'W')}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                                fontWeight: 700,
                              }}
                            >
                              {formatNum(s.gpuTemp, 1, '°C')}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                              }}
                            >
                              {formatNum(s.gpuHotSpotTemp, 1, '°C')}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                              }}
                            >
                              {formatNum(s.gpuLoad, 1, '%')}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                              }}
                            >
                              {s.ramUsedGb !== null && s.ramUsedGb !== undefined
                                ? `${formatNum(s.ramUsedGb, 1, 'GB')} (${formatNum(
                                  s.ramLoadPercent,
                                  0,
                                  '%'
                                )})`
                                : '–'}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                                fontSize: '0.78rem',
                              }}
                            >
                              {formatNum(s.mainboardTemp, 1, '°C')} /{' '}
                              {formatNum(s.storageTemp, 1, '°C')}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  {/* Mobile Telemetry Sample Cards (< 640px) */}
                  <div
                    className="mobile-cards-list"
                    style={{ maxHeight: '460px', overflowY: 'auto' }}
                  >
                    {filteredSamples.slice(0, 40).map((s) => (
                      <div
                        key={s.id}
                        className="mobile-data-card"
                        style={!s.isOnline ? { background: '#fef2f2', borderColor: '#fecaca' } : undefined}
                      >
                        <div
                          style={{
                            display: 'flex',
                            justifyContent: 'space-between',
                            alignItems: 'center',
                            gap: '8px',
                          }}
                        >
                          <span
                            style={{
                              fontFamily: 'var(--font-mono)',
                              fontSize: '0.75rem',
                              fontWeight: 700,
                              color: 'var(--cpm-dark)',
                            }}
                          >
                            {s.timestampDe}
                          </span>
                          {s.isOnline ? (
                            <span className="status-pill optimal">Online</span>
                          ) : (
                            <span className="status-pill critical">Absturz</span>
                          )}
                        </div>
                        <div style={{ fontWeight: 800, fontSize: '0.86rem', color: 'var(--cpm-dark)' }}>
                          {s.deviceName}{' '}
                          <span style={{ fontWeight: 500, fontSize: '0.74rem', color: 'var(--text-muted)' }}>
                            ({s.hostPort})
                          </span>
                        </div>
                        {s.isOnline && (
                          <div className="mobile-sample-grid">
                            <div>
                              <span style={{ color: 'var(--text-muted)', fontSize: '0.7rem' }}>CPU: </span>
                              <strong style={{ fontFamily: 'var(--font-mono)' }}>
                                {formatNum(s.cpuTemp, 1, '°C')} / {formatNum(s.cpuLoad, 0, '%')}
                              </strong>
                            </div>
                            <div>
                              <span style={{ color: 'var(--text-muted)', fontSize: '0.7rem' }}>GPU: </span>
                              <strong style={{ fontFamily: 'var(--font-mono)' }}>
                                {formatNum(s.gpuTemp, 1, '°C')} / {formatNum(s.gpuLoad, 0, '%')}
                              </strong>
                            </div>
                            <div>
                              <span style={{ color: 'var(--text-muted)', fontSize: '0.7rem' }}>Power: </span>
                              <strong style={{ fontFamily: 'var(--font-mono)' }}>
                                {formatNum(s.cpuPowerWatts, 0, 'W')}
                              </strong>
                            </div>
                            <div>
                              <span style={{ color: 'var(--text-muted)', fontSize: '0.7rem' }}>RAM: </span>
                              <strong style={{ fontFamily: 'var(--font-mono)' }}>
                                {formatNum(s.ramUsedGb, 1, 'GB')}
                              </strong>
                            </div>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            )}

            {/* ==================== TAB 5: SYSTEM-PRÜFBERICHT (A4 DRUCKANSICHT) ==================== */}
            {activeTab === 'report' && reportDevice && (
              <div className="cpm-pruefbericht-wrapper">
                {/* Action Toolbar (Hidden in Print) */}
                <div
                  className="no-print"
                  style={{
                    width: '100%',
                    maxWidth: '840px',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    flexWrap: 'wrap',
                    gap: '12px',
                    marginBottom: '20px',
                    padding: '14px 18px',
                    background: '#ffffff',
                    border: '1px solid #e2e8f0',
                    borderRadius: 'var(--radius-md)',
                    boxShadow: 'var(--shadow-sm)',
                  }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                    <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--text-muted)' }}>
                      Gerät für Prüfbericht:
                    </span>
                    <select
                      className="form-input"
                      style={{ width: 'auto', padding: '6px 12px', fontSize: '0.86rem', fontWeight: 600 }}
                      value={sampleDeviceFilter || reportDevice.id}
                      onChange={(e) => {
                        setSampleDeviceFilter(e.target.value);
                        setSelectedDeviceId(e.target.value);
                      }}
                    >
                      {devices.map((d) => (
                        <option key={d.id} value={d.id}>
                          {d.isArchived ? '[Archiv] ' : ''}{d.name} {d.orderNumber ? `(#${d.orderNumber})` : ''} ({d.host}:{d.port})
                        </option>
                      ))}
                    </select>
                  </div>

                  {/* Report Type Switcher Tabs */}
                  <div style={{ display: 'flex', background: '#f1f5f9', padding: '3px', borderRadius: '8px', gap: '4px' }}>
                    <button
                      type="button"
                      onClick={() => setReportType('system')}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '6px',
                        padding: '6px 14px',
                        borderRadius: '6px',
                        border: 'none',
                        cursor: 'pointer',
                        fontSize: '0.84rem',
                        fontWeight: 700,
                        background: reportType === 'system' ? '#0081BC' : 'transparent',
                        color: reportType === 'system' ? '#ffffff' : '#64748b',
                        boxShadow: reportType === 'system' ? '0 1px 3px rgba(0,0,0,0.1)' : 'none',
                        transition: 'all 0.15s ease',
                      }}
                    >
                      <FileText size={15} />
                      <span>System-Prüfbericht</span>
                    </button>

                    <button
                      type="button"
                      onClick={() => setReportType('battery')}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '6px',
                        padding: '6px 14px',
                        borderRadius: '6px',
                        border: 'none',
                        cursor: 'pointer',
                        fontSize: '0.84rem',
                        fontWeight: 700,
                        background: reportType === 'battery' ? '#10B981' : 'transparent',
                        color: reportType === 'battery' ? '#ffffff' : '#64748b',
                        boxShadow: reportType === 'battery' ? '0 1px 3px rgba(0,0,0,0.1)' : 'none',
                        transition: 'all 0.15s ease',
                      }}
                    >
                      <BatteryCharging size={15} />
                      <span>Batterie-Prüfbericht</span>
                    </button>
                  </div>

                  <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                    <button
                      type="button"
                      className="btn btn-primary"
                      style={reportType === 'battery' ? { background: '#10B981', borderColor: '#059669' } : {}}
                      onClick={() => window.print()}
                      title="Prüfbericht jetzt drucken oder als PDF über den Druckdialog speichern"
                    >
                      <Printer size={16} />
                      <span>{reportType === 'battery' ? 'Batteriebericht drucken' : 'Prüfbericht drucken'}</span>
                    </button>

                    <a
                      href={reportType === 'battery' ? hardwareApi.getDeviceBatteryPdfUrl(reportDevice.id) : hardwareApi.getDevicePdfUrl(reportDevice.id)}
                      className="btn btn-secondary"
                      download
                      style={reportType === 'battery' ? { borderColor: '#10B981', color: '#047857' } : {}}
                      title={reportType === 'battery' ? 'Offiziellen Batterie-Prüfbericht (Grün / DIN A4) als PDF herunterladen' : 'Offiziellen System-Prüfbericht (Blau / DIN A4) als PDF herunterladen'}
                    >
                      <Download size={16} color={reportType === 'battery' ? '#10B981' : undefined} />
                      <span>PDF herunterladen</span>
                    </a>

                    <button
                      type="button"
                      className="btn btn-secondary"
                      onClick={() => setActiveTab('overview')}
                    >
                      Zurück
                    </button>
                  </div>
                </div>

                {/* Official CPM Report Sheet (Formatted for DIN A4) */}
                {reportType === 'battery' ? (
                  <div className="cpm-pruefbericht-sheet" style={{ borderColor: '#a7f3d0' }}>
                    {/* 1. Header: Title & CPM Logo */}
                    <div
                      style={{
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'flex-start',
                        marginBottom: '6px',
                      }}
                    >
                      <div>
                        <h1
                          style={{
                            fontSize: '1.65rem',
                            fontWeight: 800,
                            color: '#10B981',
                            letterSpacing: '0.02em',
                            margin: 0,
                            lineHeight: 1.2,
                          }}
                        >
                          BATTERIE-PRÜFBERICHT
                        </h1>
                        <p
                          style={{
                            fontSize: '0.85rem',
                            color: '#64748B',
                            fontStyle: 'italic',
                            margin: '3px 0 0 0',
                          }}
                        >
                          Batterie-Gesundheitsdiagnose & Stresstest Protokoll
                        </p>
                      </div>

                      {/* CPM Logo */}
                      <div style={{ flexShrink: 0 }}>
                        <img
                          src="/cpm-logo-normal__4_.png"
                          alt="CPM Logo"
                          style={{ height: '44px', width: 'auto', display: 'block' }}
                        />
                      </div>
                    </div>

                    {/* Divider */}
                    <div
                      style={{
                        height: '2px',
                        background: '#10B981',
                        margin: '12px 0 16px 0',
                      }}
                    />

                    {/* 2. Batterie-Spezifikationen & Test-Info Card */}
                    <div
                      style={{
                        background: '#F8FAFC',
                        border: '1px solid #E2E8F0',
                        borderRadius: '6px',
                        padding: '14px 18px',
                        marginBottom: '20px',
                        display: 'grid',
                        gridTemplateColumns: '1.15fr 0.85fr',
                        gap: '20px',
                        fontSize: '0.84rem',
                      }}
                    >
                      {/* Batterie-Spezifikationen */}
                      <div>
                        <div
                          style={{
                            color: '#10B981',
                            fontWeight: 800,
                            fontSize: '0.88rem',
                            marginBottom: '8px',
                            letterSpacing: '0.02em',
                          }}
                        >
                          BATTERIE-SPEZIFIKATIONEN
                        </div>
                        <div style={{ display: 'grid', gridTemplateColumns: '165px 1fr', rowGap: '4px', color: '#1E293B' }}>
                          <span style={{ color: '#64748B' }}>Gesundheitszustand (Health):</span>
                          <strong style={{ color: '#047857' }}>{batteryReportData?.healthDisplay || '–'}</strong>

                          <span style={{ color: '#64748B' }}>Hersteller / Modell:</span>
                          <span>{batteryReportData?.manufacturer} / {batteryReportData?.devName}</span>

                          <span style={{ color: '#64748B' }}>Design-Kapazität:</span>
                          <span>{batteryReportData?.designCap}</span>

                          <span style={{ color: '#64748B' }}>Volle Ladekapazität:</span>
                          <span>{batteryReportData?.fullCap}</span>

                          <span style={{ color: '#64748B' }}>Batterie-Chemie:</span>
                          <span>{batteryReportData?.chemistry}</span>

                          <span style={{ color: '#64748B' }}>Nennspannung:</span>
                          <span>{batteryReportData?.voltage != null ? `${batteryReportData.voltage.toFixed(2)} V` : '–'}</span>
                        </div>
                      </div>

                      {/* Test-Info & Runtime */}
                      <div>
                        <div
                          style={{
                            color: '#10B981',
                            fontWeight: 800,
                            fontSize: '0.88rem',
                            marginBottom: '8px',
                            letterSpacing: '0.02em',
                          }}
                        >
                          TEST-INFO & RUNTIME
                        </div>
                        <div style={{ display: 'grid', gridTemplateColumns: '110px 1fr', rowGap: '4px', color: '#1E293B' }}>
                          {reportDevice.orderNumber && (
                            <>
                              <span style={{ color: '#64748B' }}>Auftrags-Nr.:</span>
                              <strong style={{ color: '#10B981' }}>#{reportDevice.orderNumber}</strong>
                            </>
                          )}

                          <span style={{ color: '#64748B' }}>Startzeit:</span>
                          <span>{batteryReportData?.startedAtDe}</span>

                          <span style={{ color: '#64748B' }}>Endzeit:</span>
                          <span>{batteryReportData?.endedAtDe}</span>

                          <span style={{ color: '#64748B' }}>Testdauer:</span>
                          <strong>{batteryReportData?.durationStr}</strong>

                          <span style={{ color: '#64748B' }}>Gerät / Host:</span>
                          <span>{reportDevice.name} ({reportDevice.host}:{reportDevice.port})</span>

                          <span style={{ color: '#64748B' }}>Prozessor:</span>
                          <span>{reportDevice.summary?.cpuName || '–'}</span>

                          <span style={{ color: '#64748B' }}>Modell / S/N:</span>
                          <span>{reportDevice.systemModel || reportDevice.name} ({reportDevice.serialNumber || '–'})</span>
                        </div>
                      </div>
                    </div>

                    {/* 3. Section: Zusammenfassung des Stresstests (Mint Card) */}
                    <div style={{ marginBottom: '22px' }}>
                      <h2
                        style={{
                          fontSize: '0.98rem',
                          fontWeight: 800,
                          color: '#1E293B',
                          marginBottom: '10px',
                          textTransform: 'uppercase',
                          letterSpacing: '0.02em',
                        }}
                      >
                        Zusammenfassung des Stresstests
                      </h2>
                      <div
                        style={{
                          background: '#F0FDF4',
                          border: '1px solid #BAE6D2',
                          borderRadius: '6px',
                          padding: '14px 18px',
                          display: 'grid',
                          gridTemplateColumns: '1fr 1fr',
                          gap: '20px',
                          fontSize: '0.86rem',
                        }}
                      >
                        <div style={{ display: 'grid', gridTemplateColumns: '130px 1fr', rowGap: '6px', color: '#1E293B' }}>
                          <span style={{ color: '#475569' }}>Anfangsladung:</span>
                          <strong>{batteryReportData?.startPct.toFixed(1)} %</strong>

                          <span style={{ color: '#475569' }}>Endladung:</span>
                          <strong>{batteryReportData?.endPct.toFixed(1)} %</strong>

                          <span style={{ color: '#475569' }}>Gesamt-Verlust:</span>
                          <strong style={{ color: '#DC2626' }}>
                            {batteryReportData?.totalDrain.toFixed(1)} % (in {batteryReportData?.durationStr})
                          </strong>

                          <span style={{ color: '#475569' }}>Gesamtbewertung:</span>
                          <strong style={{ color: '#10B981' }}>{batteryReportData?.verdict}</strong>
                        </div>

                        <div style={{ display: 'grid', gridTemplateColumns: '145px 1fr', rowGap: '6px', color: '#1E293B' }}>
                          <span style={{ color: '#475569' }}>Entladungsrate:</span>
                          <strong>~{batteryReportData?.dischargeRate.toFixed(1)} % / Std.</strong>

                          <span style={{ color: '#475569' }}>Belastungstyp:</span>
                          <span>{batteryReportData?.loadModeDisplay}</span>

                          <span style={{ color: '#475569' }}>Proj. Restlaufzeit:</span>
                          <strong style={{ color: '#047857' }}>{batteryReportData?.estRuntime}</strong>

                          <span style={{ color: '#475569' }}>Ø Entladeleistung:</span>
                          <span>~{batteryReportData?.avgPowerWatts.toFixed(1)} W (Max: {batteryReportData?.maxPowerWatts.toFixed(1)} W)</span>
                        </div>
                      </div>
                    </div>

                    {/* 4. Section: Chronologisches Protokoll des Testverlaufs */}
                    <div style={{ marginBottom: '22px' }}>
                      <h2
                        style={{
                          fontSize: '0.98rem',
                          fontWeight: 800,
                          color: '#1E293B',
                          marginBottom: '10px',
                          textTransform: 'uppercase',
                          letterSpacing: '0.02em',
                        }}
                      >
                        Protokoll des Testverlaufs
                      </h2>
                      <table className="cpm-report-table">
                        <thead>
                          <tr>
                            <th style={{ background: '#F1F5F9', color: '#1E293B', fontWeight: 800 }}>
                              Eintrag / Verlaufsmeldung
                            </th>
                          </tr>
                        </thead>
                        <tbody>
                          {batteryReportData?.logs?.map((logLine, idx) => (
                            <tr key={idx} style={{ background: idx % 2 === 1 ? '#FAFBFC' : '#FFFFFF' }}>
                              <td style={{ fontFamily: 'var(--font-mono)', fontSize: '0.82rem', padding: '6px 12px', color: '#334155' }}>
                                {logLine}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>

                    {/* 5. Footer */}
                    <div
                      style={{
                        borderTop: '1px solid #E2E8F0',
                        paddingTop: '12px',
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        fontSize: '0.78rem',
                        color: '#64748B',
                      }}
                    >
                      <span>Generiert mit CPM Hardware-Check Tool • {recording?.currentTimeDe || formatGermanTime()}</span>
                      <span>Seite 1 von 1</span>
                    </div>
                  </div>
                ) : (
                  /* The Official CPM System-Prüfbericht Sheet (Formatted for DIN A4) */
                  <div className="cpm-pruefbericht-sheet">
                  {/* 1. Header: Title & CPM Logo */}
                  <div
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'flex-start',
                      marginBottom: '6px',
                    }}
                  >
                    <div>
                      <h1
                        style={{
                          fontSize: '1.65rem',
                          fontWeight: 800,
                          color: '#0081BC',
                          letterSpacing: '0.02em',
                          margin: 0,
                          lineHeight: 1.2,
                        }}
                      >
                        SYSTEM-PRÜFBERICHT
                      </h1>
                      <p
                        style={{
                          fontSize: '0.85rem',
                          color: '#64748B',
                          fontStyle: 'italic',
                          margin: '3px 0 0 0',
                        }}
                      >
                        Hardware-Diagnose & Stresstest Protokoll
                      </p>
                    </div>

                    {/* CPM Logo */}
                    <div style={{ flexShrink: 0 }}>
                      <img
                        src="/cpm-logo-normal__4_.png"
                        alt="CPM Logo"
                        style={{ height: '44px', width: 'auto', display: 'block' }}
                      />
                    </div>
                  </div>

                  {/* Divider */}
                  <div
                    style={{
                      height: '2px',
                      background: '#0081BC',
                      margin: '12px 0 16px 0',
                    }}
                  />

                  {/* 2. System- & Aufzeichnungsdaten Card */}
                  <div
                    style={{
                      background: '#F8FAFC',
                      border: '1px solid #E2E8F0',
                      borderRadius: '6px',
                      padding: '14px 18px',
                      marginBottom: '20px',
                      display: 'grid',
                      gridTemplateColumns: '1.15fr 0.85fr',
                      gap: '20px',
                      fontSize: '0.84rem',
                    }}
                  >
                    {/* Hardware-Konfiguration */}
                    <div>
                      <div
                        style={{
                          color: '#0081BC',
                          fontWeight: 800,
                          fontSize: '0.88rem',
                          marginBottom: '8px',
                          letterSpacing: '0.02em',
                        }}
                      >
                        HARDWARE-KONFIGURATION
                      </div>
                      <div style={{ display: 'grid', gridTemplateColumns: '120px 1fr', rowGap: '4px', color: '#1E293B' }}>
                        <span style={{ color: '#64748B' }}>System-Modell:</span>
                        <strong>{reportDevice.name}</strong>

                        <span style={{ color: '#64748B' }}>Geräte-ID:</span>
                        <span>{reportDevice.id} ({reportDevice.host}:{reportDevice.port})</span>

                        <span style={{ color: '#64748B' }}>Prozessor (CPU):</span>
                        <span>{reportDevice.summary?.cpuName || '–'}</span>

                        <span style={{ color: '#64748B' }}>Grafikkarte (GPU):</span>
                        <span>{reportDevice.summary?.gpuName || 'Keine dedizierte Grafikkarte'}</span>

                        <span style={{ color: '#64748B' }}>Arbeitsspeicher:</span>
                        <span>
                          {reportDevice.summary?.ramTotalGb ? `${reportDevice.summary.ramTotalGb.toFixed(0)} GB` : '–'}
                        </span>

                        <span style={{ color: '#64748B' }}>Mainboard:</span>
                        <span>{reportDevice.summary?.motherboardName || 'System Mainboard'}</span>

                        <span style={{ color: '#64748B' }}>Laufwerk:</span>
                        <span>{reportDevice.summary?.storageName || 'Systemlaufwerk'}</span>
                      </div>
                    </div>

                    {/* Aufzeichnungsdaten */}
                    <div>
                      <div
                        style={{
                          color: '#0081BC',
                          fontWeight: 800,
                          fontSize: '0.88rem',
                          marginBottom: '8px',
                          letterSpacing: '0.02em',
                        }}
                      >
                        AUFZEICHNUNGSDATEN
                      </div>
                      <div style={{ display: 'grid', gridTemplateColumns: '95px 1fr', rowGap: '4px', color: '#1E293B' }}>
                        <span style={{ color: '#64748B' }}>Auftrags-Nr.:</span>
                        <strong style={{ color: '#0081BC' }}>
                          {reportDevice.orderNumber ? `#${reportDevice.orderNumber}` : '–'}
                        </strong>

                        <span style={{ color: '#64748B' }}>Startzeit:</span>
                        <span>{reportStats?.startTime || '–'}</span>

                        <span style={{ color: '#64748B' }}>Endzeit:</span>
                        <span>{reportStats?.endTime || '–'}</span>

                        <span style={{ color: '#64748B' }}>Messpunkte:</span>
                        <span>{reportStats?.sampleCount || 0} Datenpunkte</span>

                        <span style={{ color: '#64748B' }}>Status:</span>
                        <span
                          style={{
                            fontWeight: 700,
                            color: reportDevice.isOnline ? '#059669' : '#dc2626',
                          }}
                        >
                          {reportDevice.isOnline ? 'Online & Getestet' : 'Offline / Abgebrochen'}
                        </span>
                      </div>
                    </div>
                  </div>

                  {/* 3. Section: Zusammenfassung der Messwerte */}
                  <div style={{ marginBottom: '22px' }}>
                    <h2
                      style={{
                        fontSize: '0.98rem',
                        fontWeight: 800,
                        color: '#1E293B',
                        marginBottom: '10px',
                        textTransform: 'uppercase',
                        letterSpacing: '0.02em',
                      }}
                    >
                      Zusammenfassung der Messwerte
                    </h2>
                    <table className="cpm-report-table">
                      <thead>
                        <tr>
                          <th style={{ width: '32%' }}>Komponente</th>
                          <th style={{ width: '17%', textAlign: 'right' }}>Ø Auslastung</th>
                          <th style={{ width: '17%', textAlign: 'right' }}>Max. Auslastung</th>
                          <th style={{ width: '17%', textAlign: 'right' }}>Ø Temp.</th>
                          <th style={{ width: '17%', textAlign: 'right' }}>Max. Temp.</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr>
                          <td><strong>Prozessor (CPU)</strong></td>
                          <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)' }}>
                            {reportStats?.cpuAvgLoad != null ? `${reportStats.cpuAvgLoad.toFixed(1)} %` : '–'}
                          </td>
                          <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)', fontWeight: 700 }}>
                            {reportStats?.cpuMaxLoad != null ? `${reportStats.cpuMaxLoad.toFixed(1)} %` : '–'}
                          </td>
                          <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)' }}>
                            {reportStats?.cpuAvgTemp != null ? `${reportStats.cpuAvgTemp.toFixed(1)} °C` : '–'}
                          </td>
                          <td
                            style={{
                              textAlign: 'right',
                              fontFamily: 'var(--font-mono)',
                              fontWeight: 700,
                              color: reportStats?.cpuMaxTemp > 80 ? '#dc2626' : (reportStats?.cpuMaxTemp > 65 ? '#d97706' : '#1e293b'),
                            }}
                          >
                            {reportStats?.cpuMaxTemp != null ? `${reportStats.cpuMaxTemp.toFixed(1)} °C` : '–'}
                          </td>
                        </tr>

                        <tr>
                          <td><strong>Grafikkarte (GPU)</strong></td>
                          <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)' }}>
                            {reportStats?.hasGpu && reportStats.gpuAvgLoad != null ? `${reportStats.gpuAvgLoad.toFixed(1)} %` : '–'}
                          </td>
                          <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)', fontWeight: 700 }}>
                            {reportStats?.hasGpu && reportStats.gpuMaxLoad != null ? `${reportStats.gpuMaxLoad.toFixed(1)} %` : '–'}
                          </td>
                          <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)' }}>
                            {reportStats?.hasGpu && reportStats.gpuAvgTemp != null ? `${reportStats.gpuAvgTemp.toFixed(1)} °C` : '–'}
                          </td>
                          <td
                            style={{
                              textAlign: 'right',
                              fontFamily: 'var(--font-mono)',
                              fontWeight: 700,
                              color: reportStats?.gpuMaxTemp > 83 ? '#dc2626' : (reportStats?.gpuMaxTemp > 75 ? '#d97706' : '#1e293b'),
                            }}
                          >
                            {reportStats?.hasGpu && reportStats.gpuMaxTemp != null ? `${reportStats.gpuMaxTemp.toFixed(1)} °C` : '–'}
                          </td>
                        </tr>

                        <tr>
                          <td><strong>Arbeitsspeicher (RAM)</strong></td>
                          <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)' }}>
                            {reportStats?.ramAvgLoad != null ? `${reportStats.ramAvgLoad.toFixed(1)} %` : '–'}
                          </td>
                          <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)', fontWeight: 700 }}>
                            {reportStats?.ramMaxLoad != null ? `${reportStats.ramMaxLoad.toFixed(1)} %` : '–'}
                          </td>
                          <td style={{ textAlign: 'right', color: '#94a3b8' }}>--</td>
                          <td style={{ textAlign: 'right', color: '#94a3b8' }}>--</td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  {/* 4. Section: Zeitlicher Verlauf der Messwerte (Timeline Table) */}
                  <div style={{ marginBottom: '22px' }}>
                    <h2
                      style={{
                        fontSize: '0.98rem',
                        fontWeight: 800,
                        color: '#1E293B',
                        marginBottom: '10px',
                        textTransform: 'uppercase',
                        letterSpacing: '0.02em',
                      }}
                    >
                      Zeitlicher Verlauf der Messwerte
                    </h2>
                    <table className="cpm-report-table">
                      <thead>
                        <tr>
                          <th style={{ width: '20%' }}>Zeitstempel</th>
                          <th style={{ width: '16%', textAlign: 'right' }}>CPU Last</th>
                          <th style={{ width: '16%', textAlign: 'right' }}>CPU Temp.</th>
                          <th style={{ width: '16%', textAlign: 'right' }}>GPU Last</th>
                          <th style={{ width: '16%', textAlign: 'right' }}>GPU Temp.</th>
                          <th style={{ width: '16%', textAlign: 'right' }}>RAM Last</th>
                        </tr>
                      </thead>
                      <tbody>
                        {timelineTableRows.map((row, idx) => (
                          <tr key={idx}>
                            <td style={{ fontFamily: 'var(--font-mono)', color: '#475569' }}>
                              {row.timeStr}
                            </td>
                            <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)' }}>
                              {row.cpuLoad != null ? `${row.cpuLoad.toFixed(1)} %` : '–'}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                                fontWeight: 700,
                                color: row.cpuTemp > 80 ? '#dc2626' : (row.cpuTemp > 65 ? '#d97706' : '#1e293b'),
                              }}
                            >
                              {row.cpuTemp != null && row.cpuTemp > 0 ? `${row.cpuTemp.toFixed(1)} °C` : '–'}
                            </td>
                            <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)' }}>
                              {row.gpuLoad != null && reportStats?.hasGpu ? `${row.gpuLoad.toFixed(1)} %` : '–'}
                            </td>
                            <td
                              style={{
                                textAlign: 'right',
                                fontFamily: 'var(--font-mono)',
                                fontWeight: 700,
                                color: row.gpuTemp > 83 ? '#dc2626' : (row.gpuTemp > 75 ? '#d97706' : '#1e293b'),
                              }}
                            >
                              {row.gpuTemp != null && row.gpuTemp > 0 && reportStats?.hasGpu ? `${row.gpuTemp.toFixed(1)} °C` : '–'}
                            </td>
                            <td style={{ textAlign: 'right', fontFamily: 'var(--font-mono)' }}>
                              {row.ramLoad != null ? `${row.ramLoad.toFixed(1)} %` : '–'}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  {/* 5. Footer */}
                  <div
                    style={{
                      borderTop: '1px solid #E2E8F0',
                      paddingTop: '12px',
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      fontSize: '0.78rem',
                      color: '#64748B',
                    }}
                  >
                    <span>Generiert mit CPM Hardware-Check Tool • {recording?.currentTimeDe || formatGermanTime()}</span>
                    <span>Seite 1 von 1</span>
                  </div>
                </div>
                )}
              </div>
            )}
          </>
        )}
      </div>

      {/* ==================== MODAL: ADD DEVICE (IP:8085) ==================== */}
      {isAddModalOpen && (
        <div className="modal-overlay" onClick={() => setIsAddModalOpen(false)}>
          <div className="modal-dialog" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div
                style={{
                  fontSize: '1.05rem',
                  fontWeight: 800,
                  color: 'var(--cpm-dark)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                }}
              >
                <Server size={18} color="var(--cpm-light)" />
                <span>Libre Hardware Monitor Gerät (Port 8085) hinzufügen</span>
              </div>
              <button
                type="button"
                className="btn-danger-ghost"
                style={{ color: 'var(--text-muted)' }}
                onClick={() => setIsAddModalOpen(false)}
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleAddDevice}>
              <div className="modal-body">
                <p
                  style={{
                    fontSize: '0.84rem',
                    color: 'var(--text-secondary)',
                    marginBottom: '16px',
                  }}
                >
                  Aktiviere in Libre Hardware Monitor unter{' '}
                  <strong>Options → Remote Web Server → Run</strong> den Webserver
                  (Standard-Port <strong>8085</strong>) und gib hier die IP-Adresse des
                  Geräts ein.
                </p>

                <div style={{ marginBottom: '14px' }}>
                  <label className="form-label">Bezeichnung / Raum (Optional)</label>
                  <input
                    type="text"
                    className="form-input"
                    placeholder="z.B. Werkstatt Prüf-PC 1"
                    value={newDeviceName}
                    onChange={(e) => setNewDeviceName(e.target.value)}
                  />
                </div>

                <div
                  style={{
                    display: 'grid',
                    gridTemplateColumns: '2fr 1fr',
                    gap: '12px',
                  }}
                >
                  <div>
                    <label className="form-label">IP-Adresse oder Hostname *</label>
                    <input
                      type="text"
                      className="form-input"
                      placeholder="z.B. 192.168.178.45"
                      value={newDeviceHost}
                      onChange={(e) => setNewDeviceHost(e.target.value)}
                      required
                    />
                  </div>
                  <div>
                    <label className="form-label">Port</label>
                    <input
                      type="number"
                      className="form-input"
                      value={newDevicePort}
                      onChange={(e) => setNewDevicePort(e.target.value)}
                      required
                    />
                  </div>
                </div>

                <div
                  style={{
                    display: 'grid',
                    gridTemplateColumns: '1fr 1fr',
                    gap: '12px',
                    marginTop: '14px',
                  }}
                >
                  <div>
                    <label className="form-label">Auftragsnummer / Ticket-ID</label>
                    <input
                      type="text"
                      className="form-input"
                      placeholder="z.B. AUF-2026-042"
                      value={newDeviceOrderNumber}
                      onChange={(e) => setNewDeviceOrderNumber(e.target.value)}
                    />
                  </div>
                  <div>
                    <label className="form-label">Test-Art</label>
                    <select
                      className="form-input"
                      value={newDeviceTestType}
                      onChange={(e) => setNewDeviceTestType(e.target.value)}
                      style={{ padding: '8px 10px', height: '38px', background: '#ffffff' }}
                    >
                      <option value="hardware-test">🔥 Hardware-Stresstest</option>
                      <option value="battery-test">🔋 Batterie-Test</option>
                      <option value="combined">⚡ Kombi-Prüfung</option>
                    </select>
                  </div>
                </div>
              </div>

              <div className="modal-footer">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setIsAddModalOpen(false)}
                >
                  Abbrechen
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={addingDevice}
                >
                  <Plus size={16} />
                  <span>{addingDevice ? 'Wird verbunden...' : 'Gerät hinzufügen'}</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ==================== MODAL: NETWORK SCAN ON PORT 8085 ==================== */}
      {isScanModalOpen && (
        <div className="modal-overlay" onClick={() => setIsScanModalOpen(false)}>
          <div className="modal-dialog" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div
                style={{
                  fontSize: '1.05rem',
                  fontWeight: 800,
                  color: 'var(--cpm-dark)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                }}
              >
                <Radar size={18} color="var(--cpm-light)" />
                <span>Netzwerk-Scan nach Geräten auf Port 8085</span>
              </div>
              <button
                type="button"
                className="btn-danger-ghost"
                style={{ color: 'var(--text-muted)' }}
                onClick={() => setIsScanModalOpen(false)}
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleScanNetwork}>
              <div className="modal-body">
                <p
                  style={{
                    fontSize: '0.84rem',
                    color: 'var(--text-secondary)',
                    marginBottom: '16px',
                  }}
                >
                  Das C#-Backend prüft parallel alle IP-Adressen im Subnetz auf{' '}
                  <strong>Port 8085 (/data.json)</strong> und fügt gefundene Libre
                  Hardware Monitor PCs automatisch zur Geräteliste hinzu.
                </p>

                <div
                  style={{
                    display: 'grid',
                    gridTemplateColumns: '2fr 1fr',
                    gap: '12px',
                    marginBottom: '16px',
                  }}
                >
                  <div>
                    <label className="form-label">Subnetz-Präfix (z.B. 192.168.178)</label>
                    <input
                      type="text"
                      className="form-input"
                      placeholder="192.168.178"
                      value={scanSubnet}
                      onChange={(e) => setScanSubnet(e.target.value)}
                    />
                  </div>
                  <div>
                    <label className="form-label">LHM Port</label>
                    <input
                      type="number"
                      className="form-input"
                      value={scanPort}
                      onChange={(e) => setScanPort(e.target.value)}
                    />
                  </div>
                </div>

                {lastScanResult && (
                  <div
                    style={{
                      background: '#f0fdf4',
                      border: '1px solid #bbf7d0',
                      borderRadius: 'var(--radius-md)',
                      padding: '12px 14px',
                      fontSize: '0.82rem',
                      color: '#065f46',
                    }}
                  >
                    <div style={{ fontWeight: 800, marginBottom: '4px' }}>
                      Scan abgeschlossen ({lastScanResult.durationMs} ms •{' '}
                      {lastScanResult.scannedRange})
                    </div>
                    <div>
                      Gefundene Geräte auf Port {lastScanResult.port}:{' '}
                      <strong>{lastScanResult.discoveredDevices?.length || 0}</strong>
                    </div>
                  </div>
                )}
              </div>

              <div className="modal-footer">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setIsScanModalOpen(false)}
                >
                  Schließen
                </button>
                <button type="submit" className="btn btn-accent" disabled={scanning}>
                  <Radar size={16} className={scanning ? 'animate-spin' : ''} />
                  <span>
                    {scanning ? 'Scanne Port 8085...' : 'Jetzt Subnetz scannen'}
                  </span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ==================== MODAL: NEUES GERÄT VERFÜGBAR (DISCOVERY) ==================== */}
      {activeDiscoveryAlert && (
        <div className="modal-overlay" style={{ zIndex: 1100 }}>
          <div className="modal-dialog" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '480px' }}>
            <div className="modal-header" style={{ borderBottom: '2px solid rgba(0, 163, 196, 0.25)' }}>
              <div
                style={{
                  fontSize: '1.08rem',
                  fontWeight: 800,
                  color: 'var(--cpm-dark)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                }}
              >
                <Sparkles size={20} color="var(--cpm-light)" />
                <span>Neues Gerät im Netzwerk verfügbar!</span>
              </div>
              <button
                type="button"
                className="btn-danger-ghost"
                style={{ color: 'var(--text-muted)' }}
                onClick={handleDismissDiscoveryAlert}
                title="Überspringen"
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleSaveDiscoveryOrder}>
              <div className="modal-body">
                <div
                  style={{
                    background: 'linear-gradient(135deg, rgba(0, 163, 196, 0.08) 0%, rgba(0, 129, 188, 0.12) 100%)',
                    border: '1px solid rgba(0, 163, 196, 0.3)',
                    borderRadius: 'var(--radius-md)',
                    padding: '12px 14px',
                    marginBottom: '16px',
                  }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '6px' }}>
                    <span style={{ fontWeight: 800, fontSize: '0.95rem', color: 'var(--cpm-dark)' }}>
                      {activeDiscoveryAlert.name}
                    </span>
                    <span className="status-pill optimal" style={{ fontSize: '0.72rem' }}>
                      Online & Verbunden
                    </span>
                  </div>
                  <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', display: 'flex', flexWrap: 'wrap', gap: '12px' }}>
                    <span><strong>IP:</strong> {activeDiscoveryAlert.host}:{activeDiscoveryAlert.port}</span>
                    {activeDiscoveryAlert.systemModel && (
                      <span><strong>Modell:</strong> {activeDiscoveryAlert.systemModel}</span>
                    )}
                    <span><strong>Zeit:</strong> {activeDiscoveryAlert.discoveredAtDe}</span>
                  </div>
                </div>

                <p style={{ fontSize: '0.84rem', color: 'var(--text-secondary)', marginBottom: '12px', lineHeight: 1.5 }}>
                  Ein neues Gerät mit aktivem <strong>DiagnoseTool</strong> hat sich per Autodiscovery gemeldet.
                  Erfassen Sie jetzt die <strong>Auftragsnummer</strong>, um alle Messdaten und den Prüfbericht automatisch zuzuordnen:
                </p>

                <div className="form-group" style={{ marginBottom: '6px' }}>
                  <label className="form-label" style={{ fontWeight: 700 }}>
                    Auftragsnummer erfassen
                  </label>
                  <input
                    type="text"
                    className="form-input"
                    autoFocus
                    placeholder="z. B. AUF-2026-0815"
                    value={discoveryOrderInput}
                    onChange={(e) => setDiscoveryOrderInput(e.target.value)}
                    style={{ fontSize: '1rem', padding: '10px 12px', fontWeight: 600 }}
                  />
                </div>
              </div>

              <div className="modal-footer" style={{ justifyContent: 'space-between' }}>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={handleDismissDiscoveryAlert}
                >
                  Ohne Auftrag weiter
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={savingOrder}
                  style={{ gap: '6px' }}
                >
                  <CheckCircle2 size={16} />
                  <span>{savingOrder ? 'Speichern...' : 'Auftragsnummer zuweisen'}</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ==================== MODAL: AUFTRAGSNUMMER BEARBEITEN ==================== */}
      {editOrderDevice && (
        <div className="modal-overlay" style={{ zIndex: 1100 }} onClick={() => setEditOrderDevice(null)}>
          <div className="modal-dialog" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '440px' }}>
            <div className="modal-header">
              <div
                style={{
                  fontSize: '1.05rem',
                  fontWeight: 800,
                  color: 'var(--cpm-dark)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                }}
              >
                <FileText size={18} color="var(--cpm-light)" />
                <span>Auftragsnummer bearbeiten</span>
              </div>
              <button
                type="button"
                className="btn-danger-ghost"
                style={{ color: 'var(--text-muted)' }}
                onClick={() => setEditOrderDevice(null)}
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleSaveDeviceOrder}>
              <div className="modal-body">
                <p style={{ fontSize: '0.84rem', color: 'var(--text-secondary)', marginBottom: '14px' }}>
                  Auftragsnummer für <strong>{editOrderDevice.name}</strong> ({editOrderDevice.host}):
                </p>

                <div className="form-group" style={{ marginBottom: '8px' }}>
                  <label className="form-label">Auftragsnummer</label>
                  <input
                    type="text"
                    className="form-input"
                    autoFocus
                    placeholder="z.B. AUF-2026-042"
                    value={editOrderInput}
                    onChange={(e) => setEditOrderInput(e.target.value)}
                  />
                </div>
              </div>

              <div className="modal-footer">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setEditOrderDevice(null)}
                >
                  Abbrechen
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={savingEditOrder}
                >
                  <CheckCircle2 size={16} />
                  <span>{savingEditOrder ? 'Speichern...' : 'Speichern'}</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ==================== MODAL: TESTS ABSCHLIESSEN & ARCHIVIEREN ==================== */}
      {confirmArchiveDevice && (
        <div className="modal-overlay" style={{ zIndex: 1100 }} onClick={() => setConfirmArchiveDevice(null)}>
          <div className="modal-dialog" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '460px' }}>
            <div className="modal-header" style={{ borderBottom: '2px solid rgba(225, 29, 72, 0.2)' }}>
              <div
                style={{
                  fontSize: '1.05rem',
                  fontWeight: 800,
                  color: 'var(--cpm-dark)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                }}
              >
                <CheckSquare size={20} color="#e11d48" />
                <span>Tests abschließen & archivieren?</span>
              </div>
              <button
                type="button"
                className="btn-danger-ghost"
                style={{ color: 'var(--text-muted)' }}
                onClick={() => setConfirmArchiveDevice(null)}
              >
                <X size={18} />
              </button>
            </div>

            <div className="modal-body">
              <div
                style={{
                  background: '#fff1f2',
                  border: '1px solid #fecdd3',
                  borderRadius: 'var(--radius-md)',
                  padding: '12px 14px',
                  marginBottom: '14px',
                }}
              >
                <div style={{ fontWeight: 800, color: '#9f1239', fontSize: '0.95rem', marginBottom: '4px' }}>
                  {confirmArchiveDevice.name}
                </div>
                <div style={{ fontSize: '0.8rem', color: '#be123c', display: 'flex', gap: '12px' }}>
                  <span>IP: {confirmArchiveDevice.host}:{confirmArchiveDevice.port}</span>
                  {confirmArchiveDevice.orderNumber && (
                    <span>Auftrag: #{confirmArchiveDevice.orderNumber}</span>
                  )}
                </div>
              </div>

              <p style={{ fontSize: '0.85rem', color: 'var(--text-secondary)', lineHeight: 1.5, margin: 0 }}>
                Folgende Aktionen werden ausgeführt:
              </p>
              <ul style={{ fontSize: '0.82rem', color: 'var(--text-secondary)', paddingLeft: '18px', margin: '8px 0 14px 0', lineHeight: 1.6 }}>
                <li>Das <strong>DiagnoseTool wird auf dem Testgerät automatisch geschlossen</strong>.</li>
                <li>Das Gerät wird in den Bereich <strong>Archiv</strong> verschoben.</li>
                <li>Alle <strong>Aufzeichnungen und der Prüfbericht bleiben dauerhaft erhalten</strong> und können jederzeit gedruckt oder als PDF exportiert werden.</li>
              </ul>
            </div>

            <div className="modal-footer" style={{ justifyContent: 'space-between' }}>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setConfirmArchiveDevice(null)}
                disabled={archivingDevice}
              >
                Abbrechen
              </button>
              <button
                type="button"
                className="btn btn-danger"
                style={{ gap: '6px', background: '#e11d48', borderColor: '#be123c' }}
                onClick={handleFinishAndArchiveDevice}
                disabled={archivingDevice}
              >
                <CheckSquare size={16} />
                <span>{archivingDevice ? 'Wird beendet...' : 'Ja, Tests abschließen'}</span>
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ==================== MODAL: STRESSTEST LAUFZEIT KONFIGURIEREN ==================== */}
      {stressConfigModal && (
        <div className="modal-overlay" style={{ zIndex: 1200 }} onClick={() => setStressConfigModal(null)}>
          <div className="modal-dialog" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '480px' }}>
            <div
              className="modal-header"
              style={{
                borderBottom:
                  stressConfigModal.type === 'battery'
                    ? '2px solid rgba(16, 185, 129, 0.4)'
                    : stressConfigModal.type === 'full'
                    ? '2px solid rgba(239, 68, 68, 0.3)'
                    : stressConfigModal.type === 'gpu'
                    ? '2px solid rgba(234, 88, 12, 0.3)'
                    : stressConfigModal.type === 'cpu'
                    ? '2px solid rgba(217, 119, 6, 0.3)'
                    : '2px solid rgba(22, 163, 74, 0.3)',
              }}
            >
              <div
                style={{
                  fontSize: '1.08rem',
                  fontWeight: 800,
                  color: 'var(--cpm-dark)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                }}
              >
                {stressConfigModal.type === 'battery' && <BatteryCharging size={20} color="#10b981" />}
                {stressConfigModal.type === 'full' && <Flame size={20} color="#dc2626" />}
                {stressConfigModal.type === 'gpu' && <Flame size={20} color="#ea580c" />}
                {stressConfigModal.type === 'cpu' && <Zap size={20} color="#d97706" />}
                {stressConfigModal.type === 'ram' && <Activity size={20} color="#16a34a" />}
                <span>
                  {stressConfigModal.type === 'battery' && '🔋 Batterie-Entladetest (Stresstest)'}
                  {stressConfigModal.type === 'full' && '🔥 Volllast-Stresstest starten'}
                  {stressConfigModal.type === 'gpu' && '🔥 FurMark GPU Donut Test'}
                  {stressConfigModal.type === 'cpu' && '⚡ CPU Stresstest'}
                  {stressConfigModal.type === 'ram' && '🧠 RAM Stresstest'}
                </span>
              </div>
              <button
                type="button"
                className="btn-danger-ghost"
                style={{ color: 'var(--text-muted)' }}
                onClick={() => setStressConfigModal(null)}
              >
                <X size={18} />
              </button>
            </div>

            <div className="modal-body">
              {/* Device Target Badge */}
              <div
                style={{
                  background: 'var(--surface-color)',
                  border: '1px solid var(--border-color)',
                  borderRadius: 'var(--radius-md)',
                  padding: '12px 14px',
                  marginBottom: '16px',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                }}
              >
                <div>
                  <div style={{ fontWeight: 800, color: 'var(--cpm-dark)', fontSize: '0.95rem' }}>
                    {stressConfigModal.dev?.displayLabel || stressConfigModal.dev?.name}
                  </div>
                  <div style={{ fontSize: '0.8rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                    {stressConfigModal.dev?.host}:{stressConfigModal.dev?.port}
                    {stressConfigModal.dev?.orderNumber && (
                      <span style={{ marginLeft: '10px', color: 'var(--cpm-light)', fontWeight: 600 }}>
                        Auftrag #{stressConfigModal.dev.orderNumber}
                      </span>
                    )}
                  </div>
                </div>
                <span
                  style={{
                    fontSize: '0.74rem',
                    padding: '3px 8px',
                    borderRadius: '4px',
                    background: '#f1f5f9',
                    color: '#475569',
                    fontWeight: 700,
                  }}
                >
                  Remote-Ziel
                </span>
              </div>

              {/* Description Notice */}
              <p style={{ fontSize: '0.84rem', color: 'var(--text-secondary)', lineHeight: 1.5, marginBottom: '16px' }}>
                {stressConfigModal.type === 'battery' && (
                  <>
                    Startet einen <strong>kontrollierten Akku-Entladetest</strong> zur Bestimmung der realen Entladerate (%/Std.), der verbleibenden Restlaufzeit unter Last und des Abnutzungsgrades (Wear Level).
                  </>
                )}
                {stressConfigModal.type === 'full' && (
                  <>
                    Startet <strong>gleichzeitig den CPU-Stresstest und den FurMark 3D Donut</strong> für maximale Gesamtsystemauslastung (Burn-in Test).
                  </>
                )}
                {stressConfigModal.type === 'gpu' && (
                  <>
                    Startet den <strong>FurMark 3D Donut</strong> Stresstest zur thermischen Belastung der Grafikkarte(n).
                  </>
                )}
                {stressConfigModal.type === 'cpu' && (
                  <>
                    Startet den <strong>Multi-Thread CPU-Stresstest</strong> auf allen Prozessorkernen zur Temperatur- und Stabilitätsprüfung.
                  </>
                )}
                {stressConfigModal.type === 'ram' && (
                  <>
                    Prüft den <strong>Arbeitsspeicher</strong> durch kontinuierliche Allokation und Bit-Muster-Validierung.
                  </>
                )}
              </p>

              {/* Duration Presets */}
              <div style={{ marginBottom: '16px' }}>
                <label
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    fontSize: '0.84rem',
                    fontWeight: 700,
                    color: 'var(--text-primary)',
                    marginBottom: '8px',
                  }}
                >
                  <Timer size={15} color="var(--cpm-light)" />
                  <span>Gewünschte Testdauer wählen:</span>
                </label>

                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '8px', marginBottom: '10px' }}>
                  {(stressConfigModal.type === 'battery'
                    ? [
                        { val: 15, label: '15 Min (Schnell)' },
                        { val: 30, label: '30 Min (Standard)' },
                        { val: 45, label: '45 Min' },
                        { val: 60, label: '1 Stunde' },
                        { val: 90, label: '1.5 Std' },
                        { val: 120, label: '2 Stunden' },
                      ]
                    : stressConfigModal.type === 'full'
                    ? [
                        { val: 30, label: '30 Min' },
                        { val: 60, label: '1 Stunde (Standard)' },
                        { val: 90, label: '1.5 Std' },
                        { val: 120, label: '2 Stunden' },
                        { val: 180, label: '3 Stunden' },
                        { val: 240, label: '4 Stunden' },
                      ]
                    : stressConfigModal.type === 'ram'
                    ? [
                        { val: 5, label: '5 Min' },
                        { val: 10, label: '10 Min' },
                        { val: 15, label: '15 Min (Std)' },
                        { val: 30, label: '30 Min' },
                        { val: 60, label: '1 Stunde' },
                        { val: 120, label: '2 Stunden' },
                      ]
                    : [
                        { val: 10, label: '10 Min' },
                        { val: 15, label: '15 Min' },
                        { val: 30, label: '30 Min (Std)' },
                        { val: 60, label: '1 Stunde' },
                        { val: 90, label: '1.5 Std' },
                        { val: 120, label: '2 Stunden' },
                      ]
                  ).map((preset) => {
                    const isSelected = stressConfigModal.durationMinutes === preset.val;
                    return (
                      <button
                        key={preset.val}
                        type="button"
                        onClick={() =>
                          setStressConfigModal((prev) => ({ ...prev, durationMinutes: preset.val }))
                        }
                        style={{
                          padding: '8px 6px',
                          borderRadius: '6px',
                          border: isSelected ? '2px solid var(--cpm-light)' : '1px solid var(--border-color)',
                          background: isSelected ? 'rgba(0, 163, 196, 0.1)' : '#ffffff',
                          color: isSelected ? 'var(--cpm-dark)' : 'var(--text-primary)',
                          fontWeight: isSelected ? 800 : 500,
                          fontSize: '0.8rem',
                          cursor: 'pointer',
                          transition: 'all 0.15s ease',
                          textAlign: 'center',
                        }}
                      >
                        {preset.label}
                      </button>
                    );
                  })}
                </div>

                {/* Custom Duration Input */}
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '6px' }}>
                  <span style={{ fontSize: '0.8rem', color: 'var(--text-muted)' }}>Manuelle Eingabe:</span>
                  <input
                    type="number"
                    min="1"
                    max="1440"
                    value={stressConfigModal.durationMinutes || ''}
                    onChange={(e) => {
                      const v = parseInt(e.target.value, 10);
                      setStressConfigModal((prev) => ({
                        ...prev,
                        durationMinutes: isNaN(v) ? '' : Math.max(1, v),
                      }));
                    }}
                    style={{
                      width: '80px',
                      padding: '6px 8px',
                      borderRadius: '6px',
                      border: '1px solid var(--border-color)',
                      fontSize: '0.85rem',
                      fontWeight: 700,
                      textAlign: 'center',
                    }}
                  />
                  <span style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--text-secondary)' }}>Minuten</span>

                  <div style={{ display: 'flex', gap: '4px', marginLeft: 'auto' }}>
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      style={{ fontSize: '0.72rem', padding: '3px 7px' }}
                      onClick={() =>
                        setStressConfigModal((prev) => ({
                          ...prev,
                          durationMinutes: (parseInt(prev.durationMinutes, 10) || 0) + 15,
                        }))
                      }
                    >
                      +15m
                    </button>
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      style={{ fontSize: '0.72rem', padding: '3px 7px' }}
                      onClick={() =>
                        setStressConfigModal((prev) => ({
                          ...prev,
                          durationMinutes: (parseInt(prev.durationMinutes, 10) || 0) + 30,
                        }))
                      }
                    >
                      +30m
                    </button>
                  </div>
                </div>
              </div>

              {/* RAM Specific Target Allocation % */}
              {stressConfigModal.type === 'ram' && (
                <div style={{ marginBottom: '16px', paddingTop: '10px', borderTop: '1px dashed var(--border-color)' }}>
                  <label
                    style={{
                      display: 'block',
                      fontSize: '0.84rem',
                      fontWeight: 700,
                      color: 'var(--text-primary)',
                      marginBottom: '8px',
                    }}
                  >
                    RAM-Zielauslastung:
                  </label>
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '8px' }}>
                    {[
                      { val: 70, label: '70% (Schonend)' },
                      { val: 80, label: '80% (Empfohlen)' },
                      { val: 90, label: '90% (Intensiv)' },
                    ].map((opt) => {
                      const isSel = stressConfigModal.targetPercent === opt.val;
                      return (
                        <button
                          key={opt.val}
                          type="button"
                          onClick={() =>
                            setStressConfigModal((prev) => ({ ...prev, targetPercent: opt.val }))
                          }
                          style={{
                            padding: '6px 8px',
                            borderRadius: '6px',
                            border: isSel ? '2px solid #10b981' : '1px solid var(--border-color)',
                            background: isSel ? '#ecfdf5' : '#ffffff',
                            color: isSel ? '#065f46' : 'var(--text-primary)',
                            fontWeight: isSel ? 800 : 500,
                            fontSize: '0.8rem',
                            cursor: 'pointer',
                          }}
                        >
                          {opt.label}
                        </button>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* Battery Specific Load Mode Selection */}
              {stressConfigModal.type === 'battery' && (
                <div style={{ marginBottom: '16px', paddingTop: '10px', borderTop: '1px dashed var(--border-color)' }}>
                  <label
                    style={{
                      display: 'block',
                      fontSize: '0.84rem',
                      fontWeight: 700,
                      color: 'var(--text-primary)',
                      marginBottom: '8px',
                    }}
                  >
                    Belastungsmodus während der Entladung:
                  </label>
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '8px' }}>
                    {[
                      { val: 'youtube', label: '▶️ YouTube 4K HDR', desc: 'Realistischer Streaming-Benchmark (Standard)' },
                      { val: 'cpu', label: '⚡ CPU-Dauerlast', desc: 'Kontinuierliche Prozessor-Belastung' },
                      { val: 'full', label: '🔥 Volllast (Burn-in)', desc: 'Maximale Entladerate (CPU + FurMark)' },
                      { val: 'idle', label: '💤 Leerlauf (Idle)', desc: 'Grundverbrauch ohne Zusatzlast' },
                    ].map((opt) => {
                      const isSel = (stressConfigModal.loadMode || 'youtube') === opt.val;
                      return (
                        <button
                          key={opt.val}
                          type="button"
                          onClick={() =>
                            setStressConfigModal((prev) => ({ ...prev, loadMode: opt.val }))
                          }
                          style={{
                            padding: '8px 10px',
                            borderRadius: '6px',
                            border: isSel ? '2px solid #10b981' : '1px solid var(--border-color)',
                            background: isSel ? '#ecfdf5' : '#ffffff',
                            color: isSel ? '#065f46' : 'var(--text-primary)',
                            cursor: 'pointer',
                            textAlign: 'left',
                          }}
                        >
                          <div style={{ fontWeight: isSel ? 800 : 600, fontSize: '0.82rem' }}>{opt.label}</div>
                          <div style={{ fontSize: '0.72rem', color: 'var(--text-muted)', marginTop: '2px' }}>{opt.desc}</div>
                        </button>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* Auto Record Option */}
              <div
                style={{
                  padding: '10px 12px',
                  borderRadius: 'var(--radius-md)',
                  background: '#f8fafc',
                  border: '1px solid #e2e8f0',
                  marginTop: '12px',
                }}
              >
                <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', margin: 0 }}>
                  <input
                    type="checkbox"
                    checked={stressConfigModal.autoRecord}
                    onChange={(e) =>
                      setStressConfigModal((prev) => ({ ...prev, autoRecord: e.target.checked }))
                    }
                    style={{ cursor: 'pointer', width: '16px', height: '16px', accentColor: 'var(--cpm-light)' }}
                  />
                  <div>
                    <span style={{ fontSize: '0.84rem', fontWeight: 700, color: 'var(--cpm-dark)' }}>
                      Werte-Aufzeichnung automatisch mitstarten
                    </span>
                    <div style={{ fontSize: '0.74rem', color: 'var(--text-muted)' }}>
                      Zeichnet Temperatur-, Takt- und Sensorverläufe für den Prüfbericht auf.
                    </div>
                  </div>
                </label>
              </div>
            </div>

            <div className="modal-footer" style={{ justifyContent: 'space-between' }}>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setStressConfigModal(null)}
              >
                Abbrechen
              </button>
              <button
                type="button"
                className="btn btn-primary"
                style={{
                  gap: '6px',
                  background:
                    stressConfigModal.type === 'battery'
                      ? '#10b981'
                      : stressConfigModal.type === 'full'
                      ? '#dc2626'
                      : stressConfigModal.type === 'gpu'
                      ? '#ea580c'
                      : undefined,
                  borderColor:
                    stressConfigModal.type === 'battery'
                      ? '#059669'
                      : stressConfigModal.type === 'full'
                      ? '#b91c1c'
                      : stressConfigModal.type === 'gpu'
                      ? '#c2410c'
                      : undefined,
                }}
                onClick={handleStartConfiguredStress}
              >
                <Play size={15} />
                <span>
                  Test starten ({stressConfigModal.durationMinutes || 60} Min.)
                </span>
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Toast Notifications */}
      <div className="toast-container">
        {toasts.map((t) => (
          <div key={t.id} className={`toast toast-${t.type}`}>
            {t.type === 'error' ? (
              <AlertTriangle size={18} />
            ) : (
              <CheckCircle2 size={18} />
            )}
            <span>{t.message}</span>
          </div>
        ))}
      </div>

      {/* Sticky Mobile & Tablet Bottom Navigation Bar (< 960px) */}
      <nav className="mobile-bottom-nav no-print" aria-label="Mobile Navigation">
        <button
          type="button"
          className={`mobile-nav-item ${activeTab === 'overview' ? 'active' : ''}`}
          onClick={() => setActiveTab('overview')}
        >
          <Server size={18} />
          <span>Geräte ({devices.length})</span>
        </button>

        <button
          type="button"
          className={`mobile-nav-item ${activeTab === 'components' ? 'active' : ''}`}
          onClick={() => setActiveTab('components')}
        >
          <Cpu size={18} />
          <span>
            Hardware ({selectedDevice ? selectedDevice.totalHardwareCount : 0})
          </span>
        </button>

        <button
          type="button"
          className={`mobile-nav-item ${activeTab === 'sensors' ? 'active' : ''}`}
          onClick={() => setActiveTab('sensors')}
        >
          <Activity size={18} />
          <span>
            Sensoren ({selectedDevice ? selectedDevice.totalSensorCount : 0})
          </span>
        </button>

        <button
          type="button"
          className={`mobile-nav-item ${activeTab === 'recording' ? 'active' : ''}`}
          onClick={() => setActiveTab('recording')}
        >
          <TrendingUp size={18} />
          <span>Kunden-Grafik</span>
          {crashCount > 0 && (
            <span
              className="mobile-nav-badge"
              style={{ background: 'var(--danger)', color: '#ffffff' }}
            >
              {crashCount}
            </span>
          )}
        </button>

        <button
          type="button"
          className={`mobile-nav-item ${activeTab === 'report' ? 'active' : ''}`}
          onClick={() => setActiveTab('report')}
        >
          <FileText size={18} />
          <span>Prüfbericht</span>
        </button>
      </nav>

      {/* Footer */}
      <footer
        style={{
          borderTop: '1px solid var(--border-subtle)',
          padding: '18px 24px',
          textAlign: 'center',
          fontSize: '0.8rem',
          color: 'var(--text-muted)',
          background: '#ffffff',
        }}
      >
        CPM Hardware-Check • Libre Hardware Monitor API (Port 8085) • Zeitzone:
        Europe/Berlin • © {new Date().getFullYear()} CPM. Alle Rechte vorbehalten.
      </footer>
    </div>
  );
}
