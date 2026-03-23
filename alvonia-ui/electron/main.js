const { app, BrowserWindow, ipcMain, desktopCapturer, screen, dialog, Menu, Tray, nativeTheme } = require('electron');
const path = require('path');
const isDev = require('electron-is-dev');
const fs = require('fs');
const { spawn } = require('child_process');

let mainWindow;
let tray;
let floatingToolbarWindow;

// ─── Streaming state ──────────────────────────────────────────────────────────
// Map of platformId → spawned ffmpeg child process
const ffmpegProcesses = new Map();

function getStreamSettingsPath() {
  return path.join(app.getPath('userData'), 'streaming-keys.json');
}

function readStreamSettings() {
  try {
    const p = getStreamSettingsPath();
    if (fs.existsSync(p)) return JSON.parse(fs.readFileSync(p, 'utf8'));
  } catch (_) {}
  return {};
}

function writeStreamSettings(data) {
  try {
    fs.writeFileSync(getStreamSettingsPath(), JSON.stringify(data, null, 2));
  } catch (_) {}
}

/**
 * Find ffmpeg on PATH or common install locations.
 */
function findFfmpeg() {
  const candidates = [
    'ffmpeg',
    '/usr/bin/ffmpeg',
    '/usr/local/bin/ffmpeg',
    'C:\\ffmpeg\\bin\\ffmpeg.exe',
    path.join(app.getPath('exe'), '..', 'ffmpeg.exe'),
  ];
  for (const c of candidates) {
    try {
      const { execSync } = require('child_process');
      execSync(`"${c}" -version`, { stdio: 'ignore' });
      return c;
    } catch (_) {}
  }
  return 'ffmpeg'; // fallback — let the OS resolve it
}

/**
 * Build FFmpeg args for a single platform output.
 * Landscape platforms share a tee output; vertical get their own invocation.
 */
function buildFfmpegArgs({ sourceId, rtmpUrl, isVertical, bitrate, fps }) {
  const videoBitrate = `${bitrate}k`;
  const audioBitrate = '160k';

  // Input: desktop capture via virtual device (Linux: x11grab, Win: gdigrab, Mac: avfoundation)
  let inputArgs;
  const platform = process.platform;

  if (platform === 'win32') {
    inputArgs = [
      '-f', 'gdigrab',
      '-framerate', String(fps),
      '-draw_mouse', '1',
      '-i', 'desktop',
    ];
  } else if (platform === 'darwin') {
    inputArgs = [
      '-f', 'avfoundation',
      '-framerate', String(fps),
      '-i', '1:0', // screen:audio
    ];
  } else {
    // Linux
    inputArgs = [
      '-f', 'x11grab',
      '-framerate', String(fps),
      '-i', process.env.DISPLAY || ':0.0',
    ];
  }

  // Video filter: vertical crops/scales to 9:16 (1080×1920)
  const videoFilter = isVertical
    ? ['-vf', 'crop=ih*9/16:ih,scale=1080:1920']
    : [];

  const encodeArgs = [
    '-c:v', 'libx264',
    '-preset', 'veryfast',
    '-tune', 'zerolatency',
    '-b:v', videoBitrate,
    '-maxrate', videoBitrate,
    '-bufsize', `${bitrate * 2}k`,
    '-g', String(fps * 2),
    '-keyint_min', String(fps),
    '-pix_fmt', 'yuv420p',
    '-c:a', 'aac',
    '-b:a', audioBitrate,
    '-ar', '44100',
  ];

  return [
    ...inputArgs,
    ...videoFilter,
    ...encodeArgs,
    '-f', 'flv',
    rtmpUrl,
  ];
}

function spawnFfmpegStream({ platformId, sourceId, rtmpUrl, isVertical, bitrate, fps }) {
  const ffmpeg = findFfmpeg();
  const args = buildFfmpegArgs({ sourceId, rtmpUrl, isVertical, bitrate, fps });

  const proc = spawn(ffmpeg, args, { stdio: ['pipe', 'pipe', 'pipe'] });

  proc.stderr.on('data', data => {
    const msg = data.toString();
    // Detect "frame=" in output which means encoding is running
    if (msg.includes('frame=') && mainWindow) {
      mainWindow.webContents.send('stream-status', { platformId, status: 'live' });
    }
  });

  proc.on('error', err => {
    if (mainWindow) {
      mainWindow.webContents.send('stream-status', { platformId, status: 'error', error: err.message });
    }
  });

  proc.on('exit', (code) => {
    ffmpegProcesses.delete(platformId);
    if (mainWindow) {
      mainWindow.webContents.send('stream-status', { platformId, status: 'idle' });
    }
  });

  return proc;
}

function createWindow() {
  const { width, height } = screen.getPrimaryDisplay().workAreaSize;

  mainWindow = new BrowserWindow({
    width: 1280,
    height: 800,
    minWidth: 900,
    minHeight: 600,
    frame: false,
    transparent: false,
    backgroundColor: '#0f0f0f',
    titleBarStyle: 'hidden',
    titleBarOverlay: {
      color: '#1a1a1a',
      symbolColor: '#ffffff',
      height: 40
    },
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      preload: path.join(__dirname, 'preload.js'),
      webSecurity: false
    },
    icon: path.join(__dirname, '../assets/icon.png'),
    show: false
  });

  mainWindow.loadURL(
    isDev
      ? 'http://localhost:3000'
      : `file://${path.join(__dirname, '../build/index.html')}`
  );

  mainWindow.once('ready-to-show', () => {
    mainWindow.show();
    if (isDev) mainWindow.webContents.openDevTools();
  });

  mainWindow.on('closed', () => {
    mainWindow = null;
  });

  // Custom title bar context menu
  mainWindow.webContents.on('context-menu', (_, params) => {
    Menu.buildFromTemplate([
      { role: 'undo' },
      { role: 'redo' },
      { type: 'separator' },
      { role: 'cut' },
      { role: 'copy' },
      { role: 'paste' }
    ]).popup();
  });
}

function createTray() {
  const iconPath = path.join(__dirname, '../assets/tray-icon.png');
  if (fs.existsSync(iconPath)) {
    tray = new Tray(iconPath);
    const contextMenu = Menu.buildFromTemplate([
      { label: 'Show RecordIt', click: () => mainWindow?.show() },
      { label: 'Start Recording', click: () => mainWindow?.webContents.send('start-recording') },
      { type: 'separator' },
      { label: 'Quit', click: () => app.quit() }
    ]);
    tray.setToolTip('RecordIt');
    tray.setContextMenu(contextMenu);
    tray.on('double-click', () => mainWindow?.show());
  }
}

app.whenReady().then(() => {
  createWindow();
  createTray();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

// IPC Handlers
ipcMain.handle('get-sources', async () => {
  const sources = await desktopCapturer.getSources({
    types: ['window', 'screen'],
    thumbnailSize: { width: 320, height: 180 }
  });
  return sources.map(s => ({
    id: s.id,
    name: s.name,
    thumbnail: s.thumbnail.toDataURL()
  }));
});

ipcMain.handle('save-recording', async (_, buffer) => {
  const { filePath } = await dialog.showSaveDialog(mainWindow, {
    title: 'Save Recording',
    defaultPath: `recordit-${Date.now()}.webm`,
    filters: [
      { name: 'WebM Video', extensions: ['webm'] },
      { name: 'MP4 Video', extensions: ['mp4'] },
      { name: 'All Files', extensions: ['*'] }
    ]
  });
  if (filePath) {
    fs.writeFileSync(filePath, Buffer.from(buffer));
    return { success: true, filePath };
  }
  return { success: false };
});

ipcMain.handle('save-whiteboard', async (_, dataUrl) => {
  const { filePath } = await dialog.showSaveDialog(mainWindow, {
    title: 'Save Whiteboard',
    defaultPath: `whiteboard-${Date.now()}.png`,
    filters: [
      { name: 'PNG Image', extensions: ['png'] },
      { name: 'All Files', extensions: ['*'] }
    ]
  });
  if (filePath) {
    const base64 = dataUrl.replace(/^data:image\/png;base64,/, '');
    fs.writeFileSync(filePath, Buffer.from(base64, 'base64'));
    return { success: true, filePath };
  }
  return { success: false };
});

ipcMain.handle('minimize-window', () => mainWindow?.minimize());
ipcMain.handle('maximize-window', () => {
  if (mainWindow?.isMaximized()) mainWindow.unmaximize();
  else mainWindow?.maximize();
});
ipcMain.handle('close-window', () => mainWindow?.close());
ipcMain.handle('is-maximized', () => mainWindow?.isMaximized() ?? false);

ipcMain.handle('get-theme', () => nativeTheme.shouldUseDarkColors ? 'dark' : 'light');

ipcMain.handle('show-notification', (_, { title, body }) => {
  const { Notification } = require('electron');
  new Notification({ title, body }).show();
});

// ─── Streaming IPC ────────────────────────────────────────────────────────────

ipcMain.handle('get-stream-settings', () => readStreamSettings());

ipcMain.handle('save-stream-settings', (_, data) => {
  writeStreamSettings(data);
  return { success: true };
});

ipcMain.handle('start-stream', async (_, { platforms, sourceId, bitrate, fps }) => {
  try {
    // Stop any existing streams first
    for (const [id, proc] of ffmpegProcesses) {
      try { proc.kill('SIGTERM'); } catch (_) {}
    }
    ffmpegProcesses.clear();

    const keys = readStreamSettings();

    // Validate that enabled platforms have keys
    const missing = platforms.filter(p => !p.rtmpUrl.trim().endsWith('/'));
    // We just spawn regardless; missing keys result in FFmpeg connect error

    for (const p of platforms) {
      const streamKey = keys[p.id] || '';
      if (!streamKey && !p.rtmpUrl.includes('?')) {
        // No key, skip with error status
        if (mainWindow) {
          mainWindow.webContents.send('stream-status', { platformId: p.id, status: 'error', error: 'No stream key configured' });
        }
        continue;
      }

      const fullRtmpUrl = p.rtmpUrl.endsWith('/') || p.rtmpUrl.endsWith('=')
        ? p.rtmpUrl + streamKey
        : p.rtmpUrl;

      if (mainWindow) {
        mainWindow.webContents.send('stream-status', { platformId: p.id, status: 'connecting' });
      }

      const proc = spawnFfmpegStream({
        platformId: p.id,
        sourceId,
        rtmpUrl: fullRtmpUrl,
        isVertical: !!p.isVertical,
        bitrate: bitrate || 6000,
        fps: fps || 30,
      });

      ffmpegProcesses.set(p.id, proc);
    }

    return { success: true };
  } catch (err) {
    return { success: false, error: err.message };
  }
});

ipcMain.handle('stop-stream', async () => {
  for (const [id, proc] of ffmpegProcesses) {
    try {
      // Send 'q' to FFmpeg stdin for graceful stop, then SIGTERM
      if (proc.stdin && !proc.stdin.destroyed) proc.stdin.write('q');
      setTimeout(() => { try { proc.kill('SIGTERM'); } catch (_) {} }, 500);
    } catch (_) {}
    if (mainWindow) {
      mainWindow.webContents.send('stream-status', { platformId: id, status: 'idle' });
    }
  }
  ffmpegProcesses.clear();
  return { success: true };
});

ipcMain.handle('get-stream-status', () => {
  const status = {};
  for (const [id] of ffmpegProcesses) {
    status[id] = 'live';
  }
  return status;
});

// ─── Floating Recording Toolbar (separate window, like Teams/Zoom) ───────────

function createFloatingToolbar() {
  if (floatingToolbarWindow && !floatingToolbarWindow.isDestroyed()) {
    floatingToolbarWindow.show();
    return;
  }

  const { width } = screen.getPrimaryDisplay().workAreaSize;

  floatingToolbarWindow = new BrowserWindow({
    width: 420,
    height: 52,
    x: Math.round(width / 2 - 210),
    y: 10,
    frame: false,
    transparent: true,
    alwaysOnTop: true,
    skipTaskbar: true,
    resizable: false,
    hasShadow: true,
    focusable: false,
    type: 'toolbar',
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      preload: path.join(__dirname, 'preload.js'),
    },
  });

  // Load an inline HTML page for the floating toolbar
  floatingToolbarWindow.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(`
<!DOCTYPE html>
<html>
<head>
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { background: transparent; overflow: hidden; height: 100%; }
  body { font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; }
  .toolbar {
    display: flex; align-items: center; gap: 4px;
    padding: 6px 10px;
    background: rgba(13,13,13,0.95);
    border: 1px solid rgba(99,102,241,0.3);
    border-radius: 14px;
    box-shadow: 0 8px 32px rgba(0,0,0,0.6), 0 0 0 1px rgba(255,255,255,0.05);
    backdrop-filter: blur(20px);
    -webkit-app-region: drag;
    height: 44px;
  }
  .btn {
    display: flex; align-items: center; justify-content: center;
    gap: 4px; min-width: 32px; height: 32px; padding: 0 8px;
    border: none; border-radius: 8px;
    background: transparent; color: #a3a3a3;
    cursor: pointer; font-size: 12px; font-weight: 500;
    transition: all 0.15s ease;
    -webkit-app-region: no-drag;
  }
  .btn:hover { background: #333; color: #f5f5f5; }
  .btn.stop { background: rgba(239,68,68,0.15); color: #ef4444; }
  .btn.stop:hover { background: rgba(239,68,68,0.25); }
  .btn.muted { color: #ef4444; }
  .btn.active { background: rgba(99,102,241,0.2); color: #6366f1; }
  .divider { width: 1px; height: 22px; background: #2a2a2a; margin: 0 4px; }
  .timer {
    display: flex; align-items: center; gap: 6px;
    padding: 0 8px; color: #ef4444;
    font-size: 13px; font-weight: 700;
    font-variant-numeric: tabular-nums;
    font-family: 'Cascadia Code', 'Fira Code', monospace;
  }
  .rec-dot {
    width: 7px; height: 7px; border-radius: 50%;
    background: #ef4444;
    animation: pulse 1.5s ease infinite;
  }
  @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:0.3} }
  .icon { font-size: 15px; line-height: 1; }
</style>
</head>
<body>
<div class="toolbar">
  <button class="btn stop" id="stopBtn" title="Stop Recording"><span class="icon">&#9632;</span></button>
  <div class="timer"><div class="rec-dot"></div><span id="timer">00:00</span></div>
  <div class="divider"></div>
  <button class="btn" id="pauseBtn" title="Pause"><span class="icon">&#9208;</span></button>
  <div class="divider"></div>
  <button class="btn" id="micBtn" title="Toggle Mic"><span class="icon">&#127908;</span></button>
  <button class="btn" id="camBtn" title="Toggle Camera"><span class="icon">&#127909;</span></button>
  <div class="divider"></div>
  <button class="btn" id="drawBtn" title="Screen Annotation"><span class="icon">&#9998;</span></button>
  <button class="btn" id="zoomBtn" title="Zoom"><span class="icon">&#128270;</span></button>
  <div class="divider"></div>
  <button class="btn" id="minimizeBtn" title="Minimize Toolbar"><span class="icon">&#9472;</span></button>
</div>
<script>
  let seconds = 0;
  let paused = false;
  let micMuted = false;
  const timerEl = document.getElementById('timer');
  const fmt = s => {
    const m = Math.floor(s/60);
    const sec = s%60;
    return String(m).padStart(2,'0')+':'+String(sec).padStart(2,'0');
  };
  setInterval(() => {
    if (!paused) { seconds++; timerEl.textContent = fmt(seconds); }
  }, 1000);

  document.getElementById('stopBtn').onclick = () => {
    window.electronAPI?.sendToolbarAction?.('stop');
  };
  document.getElementById('pauseBtn').onclick = () => {
    paused = !paused;
    document.getElementById('pauseBtn').classList.toggle('active', paused);
    window.electronAPI?.sendToolbarAction?.('pause');
  };
  document.getElementById('micBtn').onclick = () => {
    micMuted = !micMuted;
    document.getElementById('micBtn').classList.toggle('muted', micMuted);
    window.electronAPI?.sendToolbarAction?.('toggle-mic');
  };
  document.getElementById('camBtn').onclick = () => {
    document.getElementById('camBtn').classList.toggle('active');
    window.electronAPI?.sendToolbarAction?.('toggle-cam');
  };
  document.getElementById('drawBtn').onclick = () => {
    document.getElementById('drawBtn').classList.toggle('active');
    window.electronAPI?.sendToolbarAction?.('toggle-draw');
  };
  document.getElementById('zoomBtn').onclick = () => {
    window.electronAPI?.sendToolbarAction?.('zoom');
  };
  document.getElementById('minimizeBtn').onclick = () => {
    window.electronAPI?.sendToolbarAction?.('minimize');
  };
</script>
</body>
</html>
  `)}`);

  floatingToolbarWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });

  floatingToolbarWindow.on('closed', () => {
    floatingToolbarWindow = null;
  });
}

ipcMain.handle('show-floating-toolbar', () => {
  createFloatingToolbar();
  return { success: true };
});

ipcMain.handle('hide-floating-toolbar', () => {
  if (floatingToolbarWindow && !floatingToolbarWindow.isDestroyed()) {
    floatingToolbarWindow.close();
    floatingToolbarWindow = null;
  }
  return { success: true };
});

ipcMain.handle('toolbar-action', (_, action) => {
  // Forward toolbar actions to the main renderer
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send('toolbar-action', action);
  }
  if (action === 'stop' || action === 'minimize') {
    if (floatingToolbarWindow && !floatingToolbarWindow.isDestroyed()) {
      floatingToolbarWindow.close();
      floatingToolbarWindow = null;
    }
    if (action === 'stop' && mainWindow) {
      mainWindow.show();
      mainWindow.focus();
    }
  }
  return { success: true };
});
