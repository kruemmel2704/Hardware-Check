'use client';

import React, { useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  Clock,
  Download,
  FileSearch,
  Power,
  PowerOff,
  RotateCw,
  ShieldAlert,
  WifiOff,
  X,
  Zap,
} from 'lucide-react';
import { hardwareApi } from '../lib/api';

// ---------------------------------------------------------------------------
// Kritikalität
// ---------------------------------------------------------------------------

export const SEVERITY_STYLES = {
  KRITISCH: { bg: '#fef2f2', color: '#b91c1c', border: '#fca5a5', label: 'Kritisch' },
  HOCH: { bg: '#fff7ed', color: '#c2410c', border: '#fdba74', label: 'Hoch' },
  MITTEL: { bg: '#fffbeb', color: '#b45309', border: '#fde68a', label: 'Mittel' },
  NIEDRIG: { bg: '#eff6ff', color: '#1d4ed8', border: '#bfdbfe', label: 'Niedrig' },
  INFO: { bg: '#f1f5f9', color: '#334155', border: '#cbd5e1', label: 'Info' },
};

export function SeverityBadge({ severity, score, size = 'md' }) {
  const s = SEVERITY_STYLES[severity] || SEVERITY_STYLES.INFO;
  return (
    <span
      className="status-pill"
      style={{
        background: s.bg,
        color: s.color,
        border: `1px solid ${s.border}`,
        fontSize: size === 'lg' ? '0.85rem' : undefined,
        padding: size === 'lg' ? '6px 14px' : undefined,
      }}
      title="Kritikalität laut Absturzanalyse"
    >
      <ShieldAlert size={size === 'lg' ? 15 : 12} />
      <span>
        {s.label}
        {typeof score === 'number' && score > 0 ? ` (${score}/100)` : ''}
      </span>
    </span>
  );
}

// ---------------------------------------------------------------------------
// Ereignistyp-Badges für das Protokoll
// ---------------------------------------------------------------------------

const EVENT_TYPES = {
  ABSTURZ: { cls: 'critical', Icon: WifiOff, label: 'ABSTURZ' },
  RECONNECT: { cls: 'optimal', Icon: CheckCircle2, label: 'Wiederverbunden' },
  PROGRAMM_GESCHLOSSEN: { cls: 'closed', Icon: PowerOff, label: 'Programm geschlossen' },
  PROGRAMM_GESTARTET: { cls: 'optimal', Icon: Power, label: 'Programm gestartet' },
  PROGRAMM_BEENDET: { cls: 'closed', Icon: PowerOff, label: 'Programm beendet' },
  UNTERBRECHUNG: { cls: 'warning', Icon: WifiOff, label: 'Netzwerkunterbrechung' },
  NEUSTART: { cls: 'info', Icon: RotateCw, label: 'Geplanter Neustart' },
  ABSTURZ_ANALYSE: { cls: 'critical', Icon: FileSearch, label: 'Absturzanalyse' },
  ANALYSE: { cls: 'info', Icon: FileSearch, label: 'Ereignisanzeige' },
};

export function getEventRowStyle(entry, mobile = false) {
  switch (entry?.eventType) {
    case 'ABSTURZ':
      return mobile ? { background: '#fef2f2', borderColor: '#fecaca' } : { background: '#fef2f2' };
    case 'RECONNECT':
    case 'PROGRAMM_GESTARTET':
      return mobile ? { background: '#f0fdf4', borderColor: '#bbf7d0' } : { background: '#f0fdf4' };
    case 'PROGRAMM_GESCHLOSSEN':
    case 'PROGRAMM_BEENDET':
      return mobile ? { background: '#f8fafc', borderColor: '#cbd5e1' } : { background: '#f8fafc' };
    case 'UNTERBRECHUNG':
      return mobile ? { background: '#fffbeb', borderColor: '#fde68a' } : { background: '#fffbeb' };
    case 'ABSTURZ_ANALYSE': {
      const s = SEVERITY_STYLES[entry.severity] || SEVERITY_STYLES.INFO;
      return mobile ? { background: s.bg, borderColor: s.border } : { background: s.bg };
    }
    default:
      return undefined;
  }
}

export function EventTypeBadge({ entry, size = 13 }) {
  const t = EVENT_TYPES[entry?.eventType];
  if (!t) {
    return (
      <span className="status-pill info">
        <Clock size={size} />
        <span>{entry?.keyword || 'Aufzeichnung'}</span>
      </span>
    );
  }
  const { Icon } = t;
  let label = t.label;
  if (entry.eventType === 'ABSTURZ' && entry.keyword && entry.keyword !== 'Absturz') {
    label = entry.keyword.toUpperCase();
  }
  return (
    <span style={{ display: 'inline-flex', gap: '5px', flexWrap: 'wrap', alignItems: 'center' }}>
      <span className={`status-pill ${t.cls}`}>
        <Icon size={size} />
        <span>{label}</span>
      </span>
      {entry.severity && entry.severity !== 'INFO' && entry.eventType !== 'UNTERBRECHUNG' && (
        <SeverityBadge severity={entry.severity} />
      )}
    </span>
  );
}

// ---------------------------------------------------------------------------
// Geräte-Status (online / Programm geschlossen / Absturz)
// ---------------------------------------------------------------------------

export function DeviceClosedBox({ dev }) {
  return (
    <div
      style={{
        padding: '18px',
        background: '#f8fafc',
        border: '1px dashed #94a3b8',
        borderRadius: 'var(--radius-md)',
        color: '#334155',
        fontSize: '0.84rem',
      }}
    >
      <div style={{ fontWeight: 800, display: 'flex', alignItems: 'center', gap: '6px', marginBottom: '4px' }}>
        <PowerOff size={16} />
        <span>Programm geschlossen – kein Absturz</span>
      </div>
      <div style={{ fontSize: '0.8rem' }}>
        Das DiagnoseTool wurde ordnungsgemäß beendet
        {dev.closedReason ? ` (${dev.closedReason})` : ''}
        {dev.closedAtDe ? ` um ${dev.closedAtDe}` : ''}. Die Aufzeichnung endet an dieser Stelle.
      </div>
    </div>
  );
}

export function LastCrashAnalysisBanner({ analysis, onOpen }) {
  if (!analysis) return null;
  const s = SEVERITY_STYLES[analysis.severity] || SEVERITY_STYLES.INFO;
  return (
    <div
      style={{
        padding: '10px 12px',
        background: s.bg,
        border: `1px solid ${s.border}`,
        borderRadius: 'var(--radius-md)',
        fontSize: '0.8rem',
        color: s.color,
        display: 'flex',
        flexDirection: 'column',
        gap: '6px',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontWeight: 800, flexWrap: 'wrap' }}>
        <AlertTriangle size={14} />
        <span>Letzte Absturzanalyse</span>
        <SeverityBadge severity={analysis.severity} score={analysis.severityScore} />
      </div>
      <div style={{ color: 'var(--text-primary)', fontWeight: 600 }}>{analysis.headline}</div>
      <div style={{ color: 'var(--text-muted)', fontSize: '0.74rem' }}>
        {analysis.crashTypeDisplay} • {analysis.category} • empfangen {analysis.receivedAtDe}
      </div>
      <button
        type="button"
        className="btn btn-secondary btn-sm"
        style={{ alignSelf: 'flex-start' }}
        onClick={(e) => {
          e.stopPropagation();
          onOpen?.(analysis.reportId);
        }}
      >
        <FileSearch size={13} />
        <span>Analyse & Ereignisanzeige öffnen</span>
      </button>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Vollständige Absturzanalyse (Modal)
// ---------------------------------------------------------------------------

const LEVEL_COLORS = {
  Kritisch: '#b91c1c',
  Fehler: '#dc2626',
  Warnung: '#b45309',
  Information: '#334155',
};

function Section({ title, children }) {
  return (
    <div style={{ marginTop: '18px' }}>
      <div
        style={{
          fontSize: '0.78rem',
          fontWeight: 800,
          textTransform: 'uppercase',
          letterSpacing: '0.04em',
          color: 'var(--cpm-dark)',
          marginBottom: '8px',
        }}
      >
        {title}
      </div>
      {children}
    </div>
  );
}

function BulletList({ items }) {
  if (!items || items.length === 0) return <div style={{ color: 'var(--text-muted)' }}>–</div>;
  return (
    <ul style={{ margin: 0, paddingLeft: '18px', display: 'flex', flexDirection: 'column', gap: '4px' }}>
      {items.map((i, idx) => (
        <li key={idx} style={{ fontSize: '0.84rem' }}>
          {i}
        </li>
      ))}
    </ul>
  );
}

function fmt(v, unit) {
  return typeof v === 'number' ? `${v.toFixed(1)} ${unit}` : '–';
}

export function CrashAnalysisModal({ reportId, onClose }) {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const [levelFilter, setLevelFilter] = useState('ALLE');
  const [logFilter, setLogFilter] = useState('ALLE');
  const [search, setSearch] = useState('');
  const [limit, setLimit] = useState(300);

  useEffect(() => {
    if (!reportId) return;
    let cancelled = false;
    setData(null);
    setError(null);
    hardwareApi
      .getCrashReport(reportId)
      .then((d) => !cancelled && setData(d))
      .catch((e) => !cancelled && setError(e.message || 'Bericht konnte nicht geladen werden'));
    return () => {
      cancelled = true;
    };
  }, [reportId]);

  const report = data?.report;
  const summary = data?.summary;

  const filteredEvents = useMemo(() => {
    const events = report?.events || [];
    const q = search.trim().toLowerCase();
    return events.filter((e) => {
      if (levelFilter !== 'ALLE' && e.levelName !== levelFilter) return false;
      if (logFilter !== 'ALLE' && e.log !== logFilter) return false;
      if (!q) return true;
      return (
        String(e.eventId).includes(q) ||
        (e.provider || '').toLowerCase().includes(q) ||
        (e.message || '').toLowerCase().includes(q)
      );
    });
  }, [report, levelFilter, logFilter, search]);

  if (!reportId) return null;

  const act = report?.previousSession?.lastActivity;

  return (
    <div className="modal-overlay" onClick={onClose} style={{ zIndex: 1050 }}>
      <div
        className="modal-dialog"
        onClick={(e) => e.stopPropagation()}
        style={{ maxWidth: '1100px', maxHeight: '92vh', display: 'flex', flexDirection: 'column' }}
      >
        <div className="modal-header">
          <div style={{ fontSize: '1.05rem', fontWeight: 800, color: 'var(--cpm-dark)', display: 'flex', alignItems: 'center', gap: '8px' }}>
            <FileSearch size={18} color="var(--cpm-light)" />
            <span>Absturzanalyse (Windows-Ereignisanzeige)</span>
          </div>
          <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
            <a className="btn btn-secondary btn-sm" href={hardwareApi.getCrashReportExportUrl(reportId)} target="_blank" rel="noreferrer">
              <Download size={13} />
              <span>Als Textdatei</span>
            </a>
            <button type="button" className="btn btn-secondary btn-sm" onClick={onClose} aria-label="Schließen">
              <X size={14} />
            </button>
          </div>
        </div>

        <div className="modal-body" style={{ overflowY: 'auto' }}>
          {error && <div style={{ color: 'var(--danger)', fontWeight: 700 }}>{error}</div>}
          {!error && !report && <div style={{ color: 'var(--text-muted)' }}>Lade Bericht …</div>}

          {report && (
            <>
              <div style={{ display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' }}>
                <SeverityBadge severity={report.severity} score={report.severityScore} size="lg" />
                <span className="mono-badge">{report.crashTypeDisplay}</span>
                <span className="mono-badge">{report.category}</span>
              </div>
              <div style={{ fontSize: '1.15rem', fontWeight: 800, color: 'var(--cpm-dark)', marginTop: '10px' }}>{report.headline}</div>
              <div style={{ marginTop: '6px', fontSize: '0.88rem', color: 'var(--text-primary)' }}>{report.summary}</div>
              <div style={{ marginTop: '6px', fontSize: '0.8rem', color: 'var(--text-muted)', fontStyle: 'italic' }}>{report.severityExplanation}</div>

              <div className="modal-grid-2col" style={{ marginTop: '14px', display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '8px', fontSize: '0.8rem' }}>
                <div><strong>Gerät:</strong> {summary?.deviceName} ({report.machineName})</div>
                <div><strong>Auftrags-Nr.:</strong> {summary?.orderNumber || report.previousSession?.orderNumber || '–'}</div>
                <div><strong>Absturzzeitpunkt:</strong> {report.crashTimeLocal || '–'}</div>
                <div><strong>Systemstart:</strong> {report.systemBootTimeLocal || '–'}</div>
                <div><strong>Betriebssystem:</strong> {report.osDescription}</div>
                <div><strong>DiagnoseTool:</strong> v{report.appVersion} • Bericht {report.reportId}</div>
                <div><strong>WHEA (30 Tage):</strong> {report.wheaFatalLast30Days} schwerwiegend / {report.wheaCorrectedLast30Days} korrigiert</div>
                <div><strong>Unerwartete Abschaltungen (30 Tage):</strong> {report.unexpectedShutdownsLast30Days}</div>
              </div>

              {report.bugCheck && (
                <Section title="Bluescreen (BugCheck)">
                  <div style={{ padding: '10px 12px', background: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: 'var(--radius-sm)', fontSize: '0.84rem' }}>
                    <div style={{ fontFamily: 'var(--font-mono)', fontWeight: 800 }}>
                      {report.bugCheck.codeHex} {report.bugCheck.name}
                    </div>
                    <div style={{ marginTop: '4px' }}>{report.bugCheck.description}</div>
                    {report.bugCheck.parameters?.length > 0 && (
                      <div style={{ marginTop: '4px', fontFamily: 'var(--font-mono)', fontSize: '0.76rem' }}>
                        Parameter: {report.bugCheck.parameters.join(', ')}
                      </div>
                    )}
                    {report.bugCheck.dumpPath && (
                      <div style={{ marginTop: '4px', fontFamily: 'var(--font-mono)', fontSize: '0.76rem' }}>Speicherabbild: {report.bugCheck.dumpPath}</div>
                    )}
                  </div>
                </Section>
              )}

              {act && (
                <Section title="Zustand kurz vor dem Ausfall">
                  <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', fontSize: '0.8rem' }}>
                    {act.isCpuStressRunning && <span className="status-pill warning"><Zap size={12} /> CPU-Stresstest</span>}
                    {act.isGpuStressRunning && <span className="status-pill warning"><Zap size={12} /> GPU-Stresstest</span>}
                    {act.isRamStressRunning && <span className="status-pill warning"><Zap size={12} /> RAM-Stresstest</span>}
                    {act.isBatteryTestRunning && <span className="status-pill warning"><Zap size={12} /> Akkutest</span>}
                    <span className="mono-badge">CPU {fmt(act.cpuTemp, '°C')} / {fmt(act.cpuLoad, '%')} / {fmt(act.cpuPowerWatts, 'W')}</span>
                    <span className="mono-badge">GPU {fmt(act.gpuTemp, '°C')} (HS {fmt(act.gpuHotSpotTemp, '°C')}) / {fmt(act.gpuLoad, '%')} / {fmt(act.gpuPowerWatts, 'W')}</span>
                    <span className="mono-badge">RAM {fmt(act.ramPercent, '%')}</span>
                  </div>
                </Section>
              )}

              {report.previousSession?.unhandledException && (
                <Section title="Unbehandelte Ausnahme im DiagnoseTool">
                  <pre style={{ whiteSpace: 'pre-wrap', fontSize: '0.74rem', background: '#f8fafc', padding: '8px', borderRadius: '6px', maxHeight: '180px', overflow: 'auto' }}>
                    {report.previousSession.unhandledException}
                  </pre>
                </Section>
              )}

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: '16px' }}>
                <Section title="Wahrscheinliche Ursachen">
                  <BulletList items={report.likelyCauses} />
                </Section>
                <Section title="Empfohlene Maßnahmen">
                  <BulletList items={report.recommendations} />
                </Section>
              </div>

              <Section title="Technische Details">
                <BulletList items={report.technicalDetails} />
              </Section>

              <Section title={`Befunde (${report.findings?.length || 0})`}>
                <div style={{ overflowX: 'auto' }}>
                  <table className="modern-table" style={{ width: '100%', fontSize: '0.78rem' }}>
                    <thead>
                      <tr>
                        <th>Zeit</th>
                        <th>Kritikalität</th>
                        <th>Kategorie</th>
                        <th>Befund & Bewertung</th>
                        <th>Quelle</th>
                      </tr>
                    </thead>
                    <tbody>
                      {(report.findings || []).map((f, idx) => (
                        <tr key={idx}>
                          <td style={{ fontFamily: 'var(--font-mono)', whiteSpace: 'nowrap' }}>{f.timeLocal || '–'}</td>
                          <td><SeverityBadge severity={f.severity} /></td>
                          <td style={{ whiteSpace: 'nowrap' }}>{f.category}</td>
                          <td>
                            <div style={{ fontWeight: 700 }}>{f.title}</div>
                            <div>{f.interpretation}</div>
                            {f.evidence && (
                              <div style={{ color: 'var(--text-muted)', fontFamily: 'var(--font-mono)', fontSize: '0.72rem', marginTop: '3px' }}>{f.evidence}</div>
                            )}
                          </td>
                          <td style={{ whiteSpace: 'nowrap', fontFamily: 'var(--font-mono)' }}>
                            {f.provider ? `${f.provider} #${f.eventId}` : '–'}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </Section>

              <Section title={`Vollständiger Auszug Ereignisanzeige (${report.events?.length || 0} Ereignisse${report.eventsTruncated ? ', gekürzt' : ''})`}>
                <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', marginBottom: '8px' }}>
                  <select value={logFilter} onChange={(e) => setLogFilter(e.target.value)} className="form-input" style={{ width: 'auto' }}>
                    <option value="ALLE">Alle Protokolle</option>
                    <option value="System">System</option>
                    <option value="Application">Anwendung</option>
                  </select>
                  <select value={levelFilter} onChange={(e) => setLevelFilter(e.target.value)} className="form-input" style={{ width: 'auto' }}>
                    <option value="ALLE">Alle Ebenen</option>
                    <option value="Kritisch">Kritisch</option>
                    <option value="Fehler">Fehler</option>
                    <option value="Warnung">Warnung</option>
                    <option value="Information">Information</option>
                  </select>
                  <input
                    className="form-input"
                    style={{ flex: 1, minWidth: '180px' }}
                    placeholder="Suche nach Quelle, Ereignis-ID oder Text …"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                  />
                  <span style={{ alignSelf: 'center', fontSize: '0.78rem', color: 'var(--text-muted)' }}>{filteredEvents.length} Treffer</span>
                </div>
                <div style={{ overflowX: 'auto', maxHeight: '420px', overflowY: 'auto', border: '1px solid var(--border-subtle)', borderRadius: '6px' }}>
                  <table className="modern-table" style={{ width: '100%', fontSize: '0.74rem' }}>
                    <thead>
                      <tr>
                        <th>Zeit</th>
                        <th>Protokoll</th>
                        <th>Ebene</th>
                        <th>Quelle</th>
                        <th>ID</th>
                        <th>Meldung</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredEvents.slice(0, limit).map((e, idx) => (
                        <tr key={`${e.recordId || idx}-${e.log}`}>
                          <td style={{ fontFamily: 'var(--font-mono)', whiteSpace: 'nowrap' }}>{e.timeLocal}</td>
                          <td>{e.log}</td>
                          <td style={{ color: LEVEL_COLORS[e.levelName] || '#334155', fontWeight: 700 }}>{e.levelName}</td>
                          <td style={{ whiteSpace: 'nowrap' }}>{e.provider}</td>
                          <td style={{ fontFamily: 'var(--font-mono)' }}>{e.eventId}</td>
                          <td style={{ whiteSpace: 'pre-wrap', minWidth: '320px' }}>{e.message}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                {filteredEvents.length > limit && (
                  <button type="button" className="btn btn-secondary btn-sm" style={{ marginTop: '8px' }} onClick={() => setLimit((l) => l + 500)}>
                    Weitere {Math.min(500, filteredEvents.length - limit)} anzeigen
                  </button>
                )}
              </Section>

              {report.error && (
                <div style={{ marginTop: '14px', fontSize: '0.78rem', color: 'var(--warning)' }}>Hinweis: {report.error}</div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
