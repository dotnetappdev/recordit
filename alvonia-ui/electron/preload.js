const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
  getSources: () => ipcRenderer.invoke('get-sources'),
  saveRecording: (buffer) => ipcRenderer.invoke('save-recording', buffer),
  saveWhiteboard: (dataUrl) => ipcRenderer.invoke('save-whiteboard', dataUrl),
  minimizeWindow: () => ipcRenderer.invoke('minimize-window'),
  maximizeWindow: () => ipcRenderer.invoke('maximize-window'),
  closeWindow: () => ipcRenderer.invoke('close-window'),
  isMaximized: () => ipcRenderer.invoke('is-maximized'),
  getTheme: () => ipcRenderer.invoke('get-theme'),
  showNotification: (opts) => ipcRenderer.invoke('show-notification', opts),
  onStartRecording: (cb) => ipcRenderer.on('start-recording', cb),
  onStopRecording: (cb) => ipcRenderer.on('stop-recording', cb),
  isElectron: true
});
