import React, { useState, useRef, useEffect, useCallback } from 'react';
import {
  Monitor, Camera, Mic, MicOff, VideoOff, Video,
  Square, Circle, Settings2, RefreshCw, Check
} from 'lucide-react';
import { Theme } from '../App';

interface Source {
  id: string;
  name: string;
  thumbnail: string;
}

interface RecordingPageProps {
  isRecording: boolean;
  onRecordingChange: (recording: boolean) => void;
  theme: Theme;
}

const QUALITY_OPTIONS = [
  { label: '1080p HD', value: '1080' },
  { label: '720p', value: '720' },
  { label: '480p', value: '480' },
];

const FPS_OPTIONS = [
  { label: '60 fps', value: '60' },
  { label: '30 fps', value: '30' },
  { label: '24 fps', value: '24' },
];

const isElectron = !!(window as any).electronAPI;

const RecordingPage: React.FC<RecordingPageProps> = ({ isRecording, onRecordingChange, theme }) => {
  const [sources, setSources] = useState<Source[]>([]);
  const [selectedSource, setSelectedSource] = useState<string | null>(null);
  const [micEnabled, setMicEnabled] = useState(true);
  const [cameraEnabled, setCameraEnabled] = useState(false);
  const [quality, setQuality] = useState('1080');
  const [fps, setFps] = useState('30');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const streamRef = useRef<MediaStream | null>(null);

  const loadSources = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      if (isElectron) {
        const srcs = await (window as any).electronAPI.getSources();
        setSources(srcs);
        if (srcs.length > 0 && !selectedSource) setSelectedSource(srcs[0].id);
      } else {
        // Browser fallback - just show a demo source
        setSources([
          { id: 'screen', name: 'Entire Screen', thumbnail: '' },
          { id: 'window', name: 'Application Window', thumbnail: '' },
        ]);
        setSelectedSource('screen');
      }
    } catch (e) {
      setError('Failed to load capture sources');
    } finally {
      setLoading(false);
    }
  }, [selectedSource]);

  useEffect(() => {
    loadSources();
  }, []);

  const startRecording = async () => {
    if (!selectedSource) return;
    try {
      let stream: MediaStream;

      if (isElectron) {
        const constraints: any = {
          audio: false,
          video: {
            mandatory: {
              chromeMediaSource: 'desktop',
              chromeMediaSourceId: selectedSource,
              maxWidth: quality === '1080' ? 1920 : quality === '720' ? 1280 : 854,
              maxHeight: quality === '1080' ? 1080 : quality === '720' ? 720 : 480,
              maxFrameRate: parseInt(fps)
            }
          }
        };
        stream = await (navigator.mediaDevices as any).getUserMedia(constraints);
      } else {
        stream = await navigator.mediaDevices.getDisplayMedia({
          video: { frameRate: parseInt(fps) },
          audio: micEnabled
        });
      }

      if (micEnabled) {
        try {
          const audioStream = await navigator.mediaDevices.getUserMedia({ audio: true });
          audioStream.getAudioTracks().forEach(t => stream.addTrack(t));
        } catch (_) {}
      }

      streamRef.current = stream;
      chunksRef.current = [];

      const recorder = new MediaRecorder(stream, {
        mimeType: 'video/webm;codecs=vp9'
      });

      recorder.ondataavailable = (e) => {
        if (e.data.size > 0) chunksRef.current.push(e.data);
      };

      recorder.onstop = async () => {
        const blob = new Blob(chunksRef.current, { type: 'video/webm' });
        if (isElectron) {
          const buffer = await blob.arrayBuffer();
          await (window as any).electronAPI.saveRecording(buffer);
        } else {
          const url = URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `recordit-${Date.now()}.webm`;
          a.click();
          URL.revokeObjectURL(url);
        }
        stream.getTracks().forEach(t => t.stop());
        streamRef.current = null;
      };

      recorder.start(1000);
      mediaRecorderRef.current = recorder;
      onRecordingChange(true);
    } catch (e: any) {
      setError(e.message || 'Failed to start recording');
    }
  };

  const stopRecording = () => {
    mediaRecorderRef.current?.stop();
    onRecordingChange(false);
  };

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <h1 className="page-title">Screen Recorder</h1>
          <p className="page-subtitle">Capture your screen, window, or application</p>
        </div>
        <button className="btn btn-ghost" onClick={loadSources} disabled={loading}>
          <RefreshCw size={14} className={loading ? 'spin' : ''} />
          Refresh Sources
        </button>
      </div>

      <div className="page-body">
        {error && (
          <div className="toast toast-error" style={{ position: 'relative', bottom: 'auto', right: 'auto', marginBottom: '16px' }}>
            <span>{error}</span>
          </div>
        )}

        {/* Source Selection */}
        <div className="card" style={{ marginBottom: '16px' }}>
          <div className="card-header">
            <h2 className="card-title">
              <Monitor size={16} style={{ display: 'inline', marginRight: 8, verticalAlign: 'middle' }} />
              Capture Source
            </h2>
          </div>
          {loading ? (
            <div className="empty-state">
              <RefreshCw size={32} className="empty-state-icon" />
              <p>Loading sources...</p>
            </div>
          ) : sources.length === 0 ? (
            <div className="empty-state">
              <Monitor size={32} className="empty-state-icon" />
              <p className="empty-state-title">No sources found</p>
              <p className="empty-state-description">Click Refresh Sources to scan for available capture sources.</p>
            </div>
          ) : (
            <div className="source-grid">
              {sources.map(source => (
                <div
                  key={source.id}
                  className={`source-card ${selectedSource === source.id ? 'selected' : ''}`}
                  onClick={() => setSelectedSource(source.id)}
                >
                  <div className="source-thumbnail" style={{ position: 'relative', overflow: 'hidden' }}>
                    {source.thumbnail ? (
                      <img src={source.thumbnail} alt={source.name} style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                    ) : (
                      <div style={{ width: '100%', height: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                        <Monitor size={28} color="var(--color-text-tertiary)" />
                      </div>
                    )}
                    {selectedSource === source.id && (
                      <div style={{
                        position: 'absolute', top: 8, right: 8,
                        background: 'var(--color-brand-primary)',
                        borderRadius: '50%', width: 20, height: 20,
                        display: 'flex', alignItems: 'center', justifyContent: 'center'
                      }}>
                        <Check size={12} color="white" />
                      </div>
                    )}
                  </div>
                  <div className="source-name">{source.name}</div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Recording Options */}
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
          <div className="card">
            <div className="card-header">
              <h2 className="card-title">
                <Settings2 size={16} style={{ display: 'inline', marginRight: 8, verticalAlign: 'middle' }} />
                Quality Settings
              </h2>
            </div>
            <div className="form-group">
              <label className="form-label">Resolution</label>
              <select className="form-select" value={quality} onChange={e => setQuality(e.target.value)}>
                {QUALITY_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Frame Rate</label>
              <select className="form-select" value={fps} onChange={e => setFps(e.target.value)}>
                {FPS_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </div>
          </div>

          <div className="card">
            <div className="card-header">
              <h2 className="card-title">Audio & Camera</h2>
            </div>
            <div className="settings-row" style={{ padding: '12px 0' }}>
              <div>
                <div className="settings-row-label">Microphone</div>
                <div className="settings-row-description">Record system audio & mic</div>
              </div>
              <label className="toggle">
                <input type="checkbox" checked={micEnabled} onChange={e => setMicEnabled(e.target.checked)} />
                <span className="toggle-slider" />
              </label>
            </div>
            <div className="settings-row" style={{ padding: '12px 0' }}>
              <div>
                <div className="settings-row-label">Camera Overlay</div>
                <div className="settings-row-description">Show webcam in corner</div>
              </div>
              <label className="toggle">
                <input type="checkbox" checked={cameraEnabled} onChange={e => setCameraEnabled(e.target.checked)} />
                <span className="toggle-slider" />
              </label>
            </div>
          </div>
        </div>
      </div>

      {/* Controls Bar */}
      <div className="record-controls">
        {isRecording ? (
          <>
            <button className="record-btn record-btn-stop" onClick={stopRecording}>
              <Square size={16} fill="currentColor" />
              Stop Recording
            </button>
            <div className="recording-indicator">
              <div className="recording-dot" />
              <span className="recording-time">Recording in progress</span>
            </div>
          </>
        ) : (
          <>
            <button
              className="record-btn record-btn-start"
              onClick={startRecording}
              disabled={!selectedSource}
            >
              <Circle size={16} fill="currentColor" />
              Start Recording
            </button>
            <span style={{ color: 'var(--color-text-tertiary)', fontSize: 'var(--font-size-sm)' }}>
              {selectedSource ? `Ready to record` : 'Select a capture source first'}
            </span>
          </>
        )}

        <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: '8px' }}>
          <button
            className={`btn btn-ghost ${micEnabled ? '' : 'active'}`}
            onClick={() => setMicEnabled(m => !m)}
            title={micEnabled ? 'Mute microphone' : 'Unmute microphone'}
          >
            {micEnabled ? <Mic size={14} /> : <MicOff size={14} />}
          </button>
          <button
            className={`btn btn-ghost ${cameraEnabled ? '' : ''}`}
            onClick={() => setCameraEnabled(c => !c)}
            title={cameraEnabled ? 'Disable camera' : 'Enable camera'}
          >
            {cameraEnabled ? <Video size={14} /> : <VideoOff size={14} />}
          </button>
        </div>
      </div>
    </div>
  );
};

export default RecordingPage;
