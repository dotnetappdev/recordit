import React, { useState, useEffect } from 'react';
import { Sun, Moon, Monitor, Folder, Bell, Keyboard, Info, Globe, Shield, Radio, Eye, EyeOff, Save, Check } from 'lucide-react';
import { Theme } from '../App';

const isElectron = !!(window as any).electronAPI;

// Platform stream key config
interface StreamKeys {
  twitch: string;
  youtube: string;
  youtube_shorts: string;
  kick: string;
  tiktok: string;
  custom_rtmp_url: string;
  custom_rtmp_key: string;
}

interface SettingsPageProps {
  theme: Theme;
  onToggleTheme: () => void;
}

const SettingsPage: React.FC<SettingsPageProps> = ({ theme, onToggleTheme }) => {
  const [notifications, setNotifications] = useState(true);

  // Stream keys state
  const [streamKeys, setStreamKeys] = useState<StreamKeys>({
    twitch: '', youtube: '', youtube_shorts: '', kick: '', tiktok: '',
    custom_rtmp_url: '', custom_rtmp_key: '',
  });
  const [keyVisibility, setKeyVisibility] = useState<Record<string, boolean>>({});
  const [streamKeysSaved, setStreamKeysSaved] = useState(false);

  // Load keys on mount
  useEffect(() => {
    if (isElectron) {
      (window as any).electronAPI.getStreamSettings().then((keys: StreamKeys) => {
        if (keys) setStreamKeys(keys);
      });
    }
  }, []);

  const saveStreamKeys = async () => {
    if (isElectron) {
      await (window as any).electronAPI.saveStreamSettings(streamKeys);
    }
    setStreamKeysSaved(true);
    setTimeout(() => setStreamKeysSaved(false), 2000);
  };

  const setKey = (field: keyof StreamKeys, value: string) =>
    setStreamKeys(prev => ({ ...prev, [field]: value }));

  const toggleVisibility = (field: string) =>
    setKeyVisibility(prev => ({ ...prev, [field]: !prev[field] }));
  const [autoSave, setAutoSave] = useState(true);
  const [hardwareAccel, setHardwareAccel] = useState(true);
  const [startMinimized, setStartMinimized] = useState(false);
  const [showCountdown, setShowCountdown] = useState(true);
  const [quality, setQuality] = useState('1080');
  const [format, setFormat] = useState('webm');
  const [frameRate, setFrameRate] = useState('30');
  const [saveDir, setSaveDir] = useState('~/Videos/RecordIt');
  const [lang, setLang] = useState('en');

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <h1 className="page-title">Settings</h1>
          <p className="page-subtitle">Configure RecordIt preferences</p>
        </div>
      </div>

      <div className="page-body" style={{ maxWidth: 640 }}>
        {/* Appearance */}
        <div className="settings-section">
          <div className="settings-section-title">Appearance</div>
          <div className="card">
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Theme</div>
                <div className="settings-row-description">Choose your preferred color scheme</div>
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                {[
                  { value: 'dark', icon: <Moon size={14} />, label: 'Dark' },
                  { value: 'light', icon: <Sun size={14} />, label: 'Light' },
                  { value: 'system', icon: <Monitor size={14} />, label: 'System' },
                ].map(t => (
                  <button
                    key={t.value}
                    className={`btn btn-ghost`}
                    style={{
                      background: theme === t.value ? 'var(--color-brand-primary)' : undefined,
                      color: theme === t.value ? 'white' : undefined,
                      padding: '6px 10px',
                      fontSize: 'var(--font-size-sm)'
                    }}
                    onClick={t.value !== 'system' ? onToggleTheme : undefined}
                  >
                    {t.icon}
                    {t.label}
                  </button>
                ))}
              </div>
            </div>
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Language</div>
                <div className="settings-row-description">Interface language</div>
              </div>
              <select className="form-select" style={{ width: 140 }} value={lang} onChange={e => setLang(e.target.value)}>
                <option value="en">English</option>
                <option value="de">Deutsch</option>
                <option value="fr">Français</option>
                <option value="es">Español</option>
                <option value="ja">日本語</option>
                <option value="zh">中文</option>
              </select>
            </div>
          </div>
        </div>

        {/* Recording */}
        <div className="settings-section">
          <div className="settings-section-title">Recording</div>
          <div className="card">
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Default Quality</div>
                <div className="settings-row-description">Output video resolution</div>
              </div>
              <select className="form-select" style={{ width: 140 }} value={quality} onChange={e => setQuality(e.target.value)}>
                <option value="1080">1080p HD</option>
                <option value="1440">1440p QHD</option>
                <option value="4k">4K Ultra HD</option>
                <option value="720">720p</option>
                <option value="480">480p</option>
              </select>
            </div>
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Output Format</div>
                <div className="settings-row-description">Container format for recordings</div>
              </div>
              <select className="form-select" style={{ width: 140 }} value={format} onChange={e => setFormat(e.target.value)}>
                <option value="webm">WebM (VP9)</option>
                <option value="mp4">MP4 (H.264)</option>
                <option value="mkv">MKV</option>
                <option value="avi">AVI</option>
              </select>
            </div>
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Frame Rate</div>
                <div className="settings-row-description">Default frames per second</div>
              </div>
              <select className="form-select" style={{ width: 140 }} value={frameRate} onChange={e => setFrameRate(e.target.value)}>
                <option value="60">60 fps</option>
                <option value="30">30 fps</option>
                <option value="24">24 fps</option>
                <option value="15">15 fps</option>
              </select>
            </div>
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Show Countdown</div>
                <div className="settings-row-description">3-second countdown before recording starts</div>
              </div>
              <label className="toggle">
                <input type="checkbox" checked={showCountdown} onChange={e => setShowCountdown(e.target.checked)} />
                <span className="toggle-slider" />
              </label>
            </div>
          </div>
        </div>

        {/* Storage */}
        <div className="settings-section">
          <div className="settings-section-title">Storage</div>
          <div className="card">
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Save Location</div>
                <div className="settings-row-description" style={{ fontFamily: 'monospace', fontSize: 11 }}>{saveDir}</div>
              </div>
              <button className="btn btn-ghost">
                <Folder size={14} />
                Browse
              </button>
            </div>
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Auto-Save Recordings</div>
                <div className="settings-row-description">Automatically save when recording stops</div>
              </div>
              <label className="toggle">
                <input type="checkbox" checked={autoSave} onChange={e => setAutoSave(e.target.checked)} />
                <span className="toggle-slider" />
              </label>
            </div>
          </div>
        </div>

        {/* System */}
        <div className="settings-section">
          <div className="settings-section-title">System</div>
          <div className="card">
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Notifications</div>
                <div className="settings-row-description">Show system notifications when recording completes</div>
              </div>
              <label className="toggle">
                <input type="checkbox" checked={notifications} onChange={e => setNotifications(e.target.checked)} />
                <span className="toggle-slider" />
              </label>
            </div>
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Hardware Acceleration</div>
                <div className="settings-row-description">Use GPU for encoding when available</div>
              </div>
              <label className="toggle">
                <input type="checkbox" checked={hardwareAccel} onChange={e => setHardwareAccel(e.target.checked)} />
                <span className="toggle-slider" />
              </label>
            </div>
            <div className="settings-row">
              <div>
                <div className="settings-row-label">Start Minimized</div>
                <div className="settings-row-description">Launch RecordIt minimized to system tray</div>
              </div>
              <label className="toggle">
                <input type="checkbox" checked={startMinimized} onChange={e => setStartMinimized(e.target.checked)} />
                <span className="toggle-slider" />
              </label>
            </div>
          </div>
        </div>

        {/* Keyboard shortcuts */}
        <div className="settings-section">
          <div className="settings-section-title">
            <Keyboard size={12} style={{ display: 'inline', marginRight: 4 }} />
            Keyboard Shortcuts
          </div>
          <div className="card">
            {[
              { action: 'Start/Stop Recording', shortcut: 'Ctrl+Shift+R' },
              { action: 'Pause Recording', shortcut: 'Ctrl+Shift+P' },
              { action: 'Open Whiteboard', shortcut: 'Ctrl+Shift+W' },
              { action: 'Take Screenshot', shortcut: 'Ctrl+Shift+S' },
              { action: 'Minimize to Tray', shortcut: 'Ctrl+M' },
            ].map((item, i) => (
              <div key={i} className="settings-row" style={{ padding: '10px 0' }}>
                <span className="settings-row-label">{item.action}</span>
                <kbd style={{
                  background: 'var(--color-bg-elevated)',
                  border: '1px solid var(--color-border-strong)',
                  borderRadius: 'var(--radius-sm)',
                  padding: '2px 8px',
                  fontSize: 'var(--font-size-xs)',
                  fontFamily: 'monospace',
                  color: 'var(--color-text-secondary)'
                }}>{item.shortcut}</kbd>
              </div>
            ))}
          </div>
        </div>

        {/* Streaming */}
        <div className="settings-section">
          <div className="settings-section-title">
            <Radio size={12} style={{ display: 'inline', marginRight: 4 }} />
            Streaming — API Keys &amp; Stream Keys
          </div>
          <div className="card">
            <p style={{ fontSize: 'var(--font-size-sm)', color: 'var(--color-text-tertiary)', marginBottom: 16, lineHeight: 1.5 }}>
              Keys are stored locally in your app data. Never share them.
            </p>

            {([
              { id: 'twitch',         label: 'Twitch',         hint: 'Dashboard → Creator Dashboard → Stream → Stream key' },
              { id: 'youtube',        label: 'YouTube (Full)',  hint: 'YouTube Studio → Go Live → Stream key' },
              { id: 'youtube_shorts', label: 'YouTube Shorts', hint: 'Same stream key as YouTube — vertical 9:16 output' },
              { id: 'kick',           label: 'Kick',           hint: 'Kick dashboard → Channel → Stream key' },
              { id: 'tiktok',         label: 'TikTok LIVE',    hint: 'TikTok LIVE Studio → Stream key' },
            ] as { id: keyof StreamKeys; label: string; hint: string }[]).map(p => (
              <div key={p.id} style={{ marginBottom: 16 }}>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 4 }}>
                  <div>
                    <div className="settings-row-label">{p.label}</div>
                    <div className="settings-row-description">{p.hint}</div>
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                  <input
                    type={keyVisibility[p.id] ? 'text' : 'password'}
                    className="form-input"
                    placeholder="Paste stream key…"
                    value={streamKeys[p.id]}
                    onChange={e => setKey(p.id, e.target.value)}
                    style={{ flex: 1, fontFamily: streamKeys[p.id] ? 'monospace' : undefined, fontSize: 13 }}
                  />
                  <button
                    className="btn btn-ghost"
                    style={{ padding: '6px 8px' }}
                    onClick={() => toggleVisibility(p.id)}
                    title={keyVisibility[p.id] ? 'Hide' : 'Show'}
                  >
                    {keyVisibility[p.id] ? <EyeOff size={14} /> : <Eye size={14} />}
                  </button>
                </div>
              </div>
            ))}

            {/* Custom RTMP */}
            <div style={{ borderTop: '1px solid var(--color-border-default)', paddingTop: 12, marginTop: 4 }}>
              <div className="settings-row-label" style={{ marginBottom: 8 }}>Custom RTMP</div>
              <div className="form-group" style={{ marginBottom: 8 }}>
                <label className="form-label">Server URL</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="rtmp://your-server/live"
                  value={streamKeys.custom_rtmp_url}
                  onChange={e => setKey('custom_rtmp_url', e.target.value)}
                  style={{ fontFamily: 'monospace', fontSize: 13 }}
                />
              </div>
              <div className="form-group">
                <label className="form-label">Stream Key</label>
                <div style={{ display: 'flex', gap: 6 }}>
                  <input
                    type={keyVisibility['custom_rtmp_key'] ? 'text' : 'password'}
                    className="form-input"
                    placeholder="Stream key…"
                    value={streamKeys.custom_rtmp_key}
                    onChange={e => setKey('custom_rtmp_key', e.target.value)}
                    style={{ flex: 1, fontFamily: 'monospace', fontSize: 13 }}
                  />
                  <button className="btn btn-ghost" style={{ padding: '6px 8px' }} onClick={() => toggleVisibility('custom_rtmp_key')}>
                    {keyVisibility['custom_rtmp_key'] ? <EyeOff size={14} /> : <Eye size={14} />}
                  </button>
                </div>
              </div>
            </div>

            <div style={{ marginTop: 16, display: 'flex', justifyContent: 'flex-end' }}>
              <button className="btn btn-primary" onClick={saveStreamKeys}>
                {streamKeysSaved ? <><Check size={13} /> Saved</> : <><Save size={13} /> Save Keys</>}
              </button>
            </div>
          </div>
        </div>

        {/* About */}
        <div className="settings-section">
          <div className="settings-section-title">About</div>
          <div className="card">
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 16 }}>
              <div style={{ width: 48, height: 48, background: 'var(--color-brand-primary)', borderRadius: 12, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <span style={{ color: 'white', fontWeight: 700, fontSize: 20 }}>R</span>
              </div>
              <div>
                <div style={{ fontWeight: 700, fontSize: 'var(--font-size-lg)', color: 'var(--color-text-primary)' }}>RecordIt</div>
                <div style={{ fontSize: 'var(--font-size-sm)', color: 'var(--color-text-secondary)' }}>Version 1.0.0 · Alvonia UI</div>
              </div>
            </div>
            <div style={{ fontSize: 'var(--font-size-sm)', color: 'var(--color-text-tertiary)', lineHeight: 1.6, borderTop: '1px solid var(--color-border-default)', paddingTop: 12 }}>
              RecordIt is a screen recording and whiteboard collaboration tool built with Alvonia UI.
              Perfect for tutorials, demos, and team collaboration.
            </div>
            <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
              <button className="btn btn-ghost" style={{ fontSize: 'var(--font-size-sm)' }}>
                <Globe size={13} /> Website
              </button>
              <button className="btn btn-ghost" style={{ fontSize: 'var(--font-size-sm)' }}>
                <Shield size={13} /> Privacy Policy
              </button>
              <button className="btn btn-ghost" style={{ fontSize: 'var(--font-size-sm)' }}>
                <Info size={13} /> Changelog
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default SettingsPage;
