const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
  // Capture
  getSources: () => ipcRenderer.invoke('get-sources'),

  // Recording
  saveRecording: (buffer) => ipcRenderer.invoke('save-recording', buffer),
  saveWhiteboard: (dataUrl) => ipcRenderer.invoke('save-whiteboard', dataUrl),

  // Window management
  minimizeWindow: () => ipcRenderer.invoke('minimize-window'),
  maximizeWindow: () => ipcRenderer.invoke('maximize-window'),
  closeWindow: () => ipcRenderer.invoke('close-window'),
  isMaximized: () => ipcRenderer.invoke('is-maximized'),
  getTheme: () => ipcRenderer.invoke('get-theme'),
  showNotification: (opts) => ipcRenderer.invoke('show-notification', opts),

  // Streaming
  getStreamSettings: () => ipcRenderer.invoke('get-stream-settings'),
  saveStreamSettings: (data) => ipcRenderer.invoke('save-stream-settings', data),
  startStream: (config) => ipcRenderer.invoke('start-stream', config),
  stopStream: () => ipcRenderer.invoke('stop-stream'),
  getStreamStatus: () => ipcRenderer.invoke('get-stream-status'),

  // Streaming status events pushed from main process
  onStreamStatus: (cb) => ipcRenderer.on('stream-status', cb),

  // Recording events
  onStartRecording: (cb) => ipcRenderer.on('start-recording', cb),
  onStopRecording: (cb) => ipcRenderer.on('stop-recording', cb),

  // Floating toolbar (separate window like Teams/Zoom)
  showFloatingToolbar: () => ipcRenderer.invoke('show-floating-toolbar'),
  hideFloatingToolbar: () => ipcRenderer.invoke('hide-floating-toolbar'),
  sendToolbarAction: (action) => ipcRenderer.invoke('toolbar-action', action),
  onToolbarAction: (cb) => ipcRenderer.on('toolbar-action', cb),

  isElectron: true,
});
