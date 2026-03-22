const { app, BrowserWindow, ipcMain, desktopCapturer, screen, dialog, Menu, Tray, nativeTheme } = require('electron');
const path = require('path');
const isDev = require('electron-is-dev');
const fs = require('fs');
const { spawn } = require('child_process');

let mainWindow;
let tray;

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
