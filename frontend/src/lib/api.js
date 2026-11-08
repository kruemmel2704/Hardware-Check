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
  getExportUrl: () => `${getApiBase()}/api/hardware/recording/export`,
};
