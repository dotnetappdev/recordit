import React from 'react';
import { Minus, Maximize2, X, Sun, Moon, Video } from 'lucide-react';
import { Theme } from '../App';

interface TitleBarProps {
  theme: Theme;
  isRecording: boolean;
  recordingDuration: number;
  onToggleTheme: () => void;
}

function formatDuration(seconds: number): string {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

const isElectron = !!(window as any).electronAPI;

const TitleBar: React.FC<TitleBarProps> = ({ theme, isRecording, recordingDuration, onToggleTheme }) => {
  const handleMinimize = () => isElectron && (window as any).electronAPI.minimizeWindow();
  const handleMaximize = () => isElectron && (window as any).electronAPI.maximizeWindow();
  const handleClose = () => isElectron && (window as any).electronAPI.closeWindow();

  return (
    <div className="titlebar">
      <div className="titlebar-left">
        <div className="titlebar-logo">
          <div className="titlebar-logo-icon">
            <Video size={14} color="white" />
          </div>
          <span className="titlebar-logo-text">RecordIt</span>
        </div>
      </div>

      <div className="titlebar-center">
        {isRecording && (
          <div className="recording-indicator">
            <div className="recording-dot" />
            <span className="recording-time">{formatDuration(recordingDuration)}</span>
            <span className="badge badge-recording">REC</span>
          </div>
        )}
      </div>

      <div className="titlebar-right">
        <button
          className="titlebar-btn"
          onClick={onToggleTheme}
          title={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
        >
          {theme === 'dark' ? <Sun size={14} /> : <Moon size={14} />}
        </button>
        {isElectron && (
          <>
            <button className="titlebar-btn" onClick={handleMinimize} title="Minimize">
              <Minus size={14} />
            </button>
            <button className="titlebar-btn" onClick={handleMaximize} title="Maximize">
              <Maximize2 size={12} />
            </button>
            <button className="titlebar-btn close" onClick={handleClose} title="Close">
              <X size={14} />
            </button>
          </>
        )}
      </div>
    </div>
  );
};

export default TitleBar;
