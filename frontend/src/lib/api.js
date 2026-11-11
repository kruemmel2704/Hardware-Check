export function getApiBase() {
  if (typeof window === 'undefined') {
    return 'http://127.0.0.1:5085';
  }
  const { protocol, hostname, port } = window.location;
  // When served directly by the C# backend on port 8085 or 5085, use same-origin relative URLs
  if (port === '8085' || port === '5085') {
    return '';
  }
  // When running Next.js dev server (port 3000), talk to the C# listener on 5085
  return `${protocol}//${hostname}:5085`;
}

async function request(path, options = {}) {
  const base = getApiBase();
  const res = await fetch(`${base}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(options.headers || {}),
    },
  });

  if (!res.ok) {
    let errMsg = `HTTP-Fehler ${res.status}`;
    try {
      const body = await res.json();
      if (body?.error) errMsg = body.error;
    } catch {
      // Ignore parse error
    }
    throw new Error(errMsg);
  }

  return res.json();
}

export const hardwareApi = {
  getDashboard: () => request('/api/hardware/dashboard'),
  getDevice: (id) => request(`/api/hardware/devices/${encodeURIComponent(id)}`),
  addDevice: (payload) =>
    request('/api/hardware/devices', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  removeDevice: (id) =>
    request(`/api/hardware/devices/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    }),
  scanNetwork: (payload) =>
    request('/api/hardware/scan', {
      method: 'POST',
      body: JSON.stringify(payload || {}),
    }),
  setDemoMode: (enabled) =>
    request('/api/hardware/demo', {
      method: 'POST',
      body: JSON.stringify({ enabled }),
    }),
  setIncludeLocalDevice: (enabled) =>
    request('/api/hardware/local-device/toggle', {
      method: 'POST',
      body: JSON.stringify({ enabled }),
    }),
  getNetworkInfo: () => request('/api/hardware/network-info'),
  getRecording: () => request('/api/hardware/recording'),
  toggleRecording: (enabled) =>
    request('/api/hardware/recording/toggle', {
      method: 'POST',
      body: JSON.stringify({ enabled }),
    }),
  clearRecording: () =>
    request('/api/hardware/recording/clear', {
      method: 'POST',
    }),
  simulateCrash: (deviceId, offline) =>
    request('/api/hardware/recording/simulate-crash', {
      method: 'POST',
      body: JSON.stringify({ deviceId, offline }),
    }),
  controlDevice: (id, action, payload = {}) =>
    request(`/api/hardware/devices/${encodeURIComponent(id)}/control/${action.replace(/^\//, '')}`, {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  setDeviceOrderNumber: (id, orderNumber) =>
    request(`/api/hardware/devices/${encodeURIComponent(id)}/order-number`, {
      method: 'POST',
      body: JSON.stringify({ orderNumber }),
    }),
  dismissDiscoveryAlert: (deviceIdOrAlertId) =>
    request('/api/hardware/discovery/dismiss', {
      method: 'POST',
      body: JSON.stringify({ deviceId: deviceIdOrAlertId }),
    }),
  finishAndArchiveDevice: (id) =>
    request(`/api/hardware/devices/${encodeURIComponent(id)}/finish-and-archive`, {
      method: 'POST',
    }),
  restoreDevice: (id) =>
    request(`/api/hardware/devices/${encodeURIComponent(id)}/restore`, {
      method: 'POST',
    }),
  startBatteryTest: (id, payload = {}) =>
    request(`/api/hardware/devices/${encodeURIComponent(id)}/battery-test/start`, {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  stopBatteryTest: (id) =>
    request(`/api/hardware/devices/${encodeURIComponent(id)}/battery-test/stop`, {
      method: 'POST',
    }),
  getDevicePdfUrl: (deviceId) => `${getApiBase()}/api/hardware/devices/${encodeURIComponent(deviceId)}/report/pdf`,
  getDeviceBatteryPdfUrl: (deviceId) => `${getApiBase()}/api/hardware/devices/${encodeURIComponent(deviceId)}/report/battery-pdf`,
  getExportUrl: () => `${getApiBase()}/api/hardware/recording/export`,

  // --- DiagnoseTool Fernsteuerung (voller Funktionsumfang via REST, Proxy auf Port 8085 des Clients) ---
  // Katalog aller Endpunkte des DiagnoseTools
  getRemoteCapabilities: (id) => request(`/api/hardware/devices/${encodeURIComponent(id)}/remote`),
  // Gesamtzustand (App, FurMark, Reparatur, Stresstests, Akkutest, Aufzeichnung)
  getRemoteOverview: (id) => request(`/api/hardware/devices/${encodeURIComponent(id)}/remote-overview`),
  // Beliebiger GET, z.B. remoteGet(id, 'stress/cpu') oder remoteGet(id, 'recording?samples=true&last=100')
  remoteGet: (id, path) =>
    request(`/api/hardware/devices/${encodeURIComponent(id)}/remote/${String(path).replace(/^\//, '')}`),
  // Beliebige Aktion, z.B. remoteAction(id, 'tools/FurMark/launch') oder remoteAction(id, 'ui/tab', { tab: 'battery' })
  remoteAction: (id, action, payload = {}) =>
    request(`/api/hardware/devices/${encodeURIComponent(id)}/remote/control/${String(action).replace(/^\//, '')}`, {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  getRemoteAppState: (id) => hardwareApi.remoteGet(id, 'app/state'),
  getRemoteDiagnostics: (id) => hardwareApi.remoteGet(id, 'diagnostics'),
  getRemoteTools: (id) => hardwareApi.remoteGet(id, 'tools'),
  launchRemoteTool: (id, key) => hardwareApi.remoteAction(id, `tools/${encodeURIComponent(key)}/launch`),
  setRemoteToolPath: (id, key, path) => hardwareApi.remoteAction(id, `tools/${encodeURIComponent(key)}/path`, { path }),
  clearRemoteToolPath: (id, key) => hardwareApi.remoteAction(id, `tools/${encodeURIComponent(key)}/clear-path`),
  refreshRemoteTools: (id) => hardwareApi.remoteAction(id, 'tools/refresh'),
  runRemoteRepair: (id, key) => hardwareApi.remoteAction(id, `repair/${encodeURIComponent(key)}`),
  configureRemoteCpuStress: (id, payload) => hardwareApi.remoteAction(id, 'stress/cpu/config', payload),
  configureRemoteRamStress: (id, payload) => hardwareApi.remoteAction(id, 'stress/ram/config', payload),
  startRemoteBatteryTest: (id, durationMinutes = 30, openVideo = true) =>
    hardwareApi.remoteAction(id, 'battery-test/start', { durationMinutes, openVideo }),
  stopRemoteBatteryTest: (id) => hardwareApi.remoteAction(id, 'battery-test/stop'),
  clearRemoteRecording: (id) => hardwareApi.remoteAction(id, 'recording/clear'),
  selectRemoteTab: (id, tab) => hardwareApi.remoteAction(id, 'ui/tab', { tab }),
  showRemoteStatus: (id, message, isError = false) => hardwareApi.remoteAction(id, 'ui/status', { message, isError }),
  remoteWindowAction: (id, action) => hardwareApi.remoteAction(id, 'ui/window', { action }),
  setRemoteSleepPrevention: (id, preventSleep) => hardwareApi.remoteAction(id, 'power/sleep', { preventSleep }),
  announceRemoteClient: (id) => hardwareApi.remoteAction(id, 'hardware-check/announce'),
  getRemoteSystemReportUrl: (id) => `${getApiBase()}/api/hardware/devices/${encodeURIComponent(id)}/remote/report/system`,
  getRemoteBatteryReportUrl: (id) => `${getApiBase()}/api/hardware/devices/${encodeURIComponent(id)}/remote/report/battery`,
  // --- Absturzanalyse (Windows-Ereignisanzeige der Clients) ---
  getCrashReports: (deviceId) =>
    request(`/api/hardware/crash-reports${deviceId ? `?deviceId=${encodeURIComponent(deviceId)}` : ''}`),
  getCrashReport: (reportId) => request(`/api/hardware/crash-reports/${encodeURIComponent(reportId)}`),
  getCrashReportExportUrl: (reportId) =>
    `${getApiBase()}/api/hardware/crash-reports/${encodeURIComponent(reportId)}/export`,
  pullCrashAnalysis: (deviceId, { hours = 24, lastCrash = false } = {}) =>
    request(
      `/api/hardware/devices/${encodeURIComponent(deviceId)}/crash-analysis/pull?hours=${hours}&lastCrash=${lastCrash}`,
      { method: 'POST' }
    ),
  getRemoteRecordingCsvUrl: (id) => `${getApiBase()}/api/hardware/devices/${encodeURIComponent(id)}/remote/recording/export`,
};
