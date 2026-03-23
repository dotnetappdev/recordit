import React, { useState, useRef, useEffect, useCallback } from 'react';
import {
  Monitor, Camera, Mic, MicOff, Video, VideoOff,
  Square, Circle, Plus, Trash2, RefreshCw, Settings2,
  Radio, Volume2, ChevronRight, X, Check, Layers,
  Twitch, Youtube, Cast, Bell, ThumbsUp, Heart, Star,
  Sparkles, Palette, Droplets, Focus, Wand2, SlidersHorizontal,
  Play, Pause, SkipBack, SkipForward, Scissors, Clock,
  Minimize2, Maximize2, ZoomIn, Pencil, Move, GripVertical,
  Search, MousePointer, Highlighter, Type, ChevronDown, ChevronUp
} from 'lucide-react';
import { Theme } from '../App';

// ─── Types ────────────────────────────────────────────────────────────────────

interface Source {
  id: string;
  name: string;
  thumbnail: string;
}

interface SceneSource {
  id: string;
  type: 'display' | 'window' | 'webcam' | 'image' | 'color';
  name: string;
  visible: boolean;
  locked: boolean;
}

type SceneAnimation = 'fade' | 'slide' | 'zoom' | 'cut';

interface Scene {
  id: string;
  name: string;
  sources: SceneSource[];
  animation: SceneAnimation;
}

interface SceneTemplate {
  name: string;
  icon: string;
  description: string;
  sources: Omit<SceneSource, 'id'>[];
  animation: SceneAnimation;
}

// ─── Effects Types ─────────────────────────────────────────────────────────

type EffectType = 'blur' | 'chroma_key' | 'color_correct' | 'vignette' | 'sharpen' | 'noise_reduction' | 'brightness' | 'saturation';

interface VideoEffect {
  id: string;
  type: EffectType;
  name: string;
  enabled: boolean;
  intensity: number; // 0-100
  params: Record<string, number>;
}

const EFFECT_PRESETS: { type: EffectType; name: string; icon: string; description: string; defaultIntensity: number }[] = [
  { type: 'blur',             name: 'Background Blur',     icon: '🔵', description: 'Gaussian blur on background',    defaultIntensity: 50 },
  { type: 'chroma_key',       name: 'Green Screen',        icon: '🟢', description: 'Chroma key / green screen removal', defaultIntensity: 75 },
  { type: 'color_correct',    name: 'Color Correction',    icon: '🎨', description: 'Adjust white balance & tones',   defaultIntensity: 50 },
  { type: 'vignette',         name: 'Vignette',            icon: '⬛', description: 'Dark edges cinematic effect',     defaultIntensity: 40 },
  { type: 'sharpen',          name: 'Sharpen',             icon: '🔺', description: 'Enhance edge detail',            defaultIntensity: 30 },
  { type: 'noise_reduction',  name: 'Noise Reduction',     icon: '🔇', description: 'Reduce video grain/noise',       defaultIntensity: 60 },
  { type: 'brightness',       name: 'Brightness/Contrast', icon: '☀️', description: 'Adjust brightness and contrast',  defaultIntensity: 50 },
  { type: 'saturation',       name: 'Saturation',          icon: '🌈', description: 'Boost or reduce color vibrance', defaultIntensity: 50 },
];

// ─── Timeline Types ────────────────────────────────────────────────────────

interface TimelineClip {
  id: string;
  name: string;
  type: 'video' | 'audio' | 'overlay';
  startTime: number; // seconds
  duration: number;  // seconds
  track: number;
  color: string;
}

interface TimelineMarker {
  id: string;
  time: number;
  label: string;
  color: string;
}

type YtEffect = 'like' | 'subscribe' | 'bell' | null;

type StreamStatus = 'idle' | 'connecting' | 'live' | 'error';

interface Platform {
  id: 'twitch' | 'youtube' | 'youtube_shorts' | 'kick' | 'tiktok';
  label: string;
  color: string;
  enabled: boolean;
  status: StreamStatus;
  isVertical?: boolean;
  rtmpBase: string;
}

interface RecordingPageProps {
  isRecording: boolean;
  onRecordingChange: (recording: boolean) => void;
  theme: Theme;
}

// ─── Defaults ─────────────────────────────────────────────────────────────────

const DEFAULT_PLATFORMS: Platform[] = [
  { id: 'twitch',         label: 'Twitch',           color: '#9146ff', enabled: false, status: 'idle', rtmpBase: 'rtmp://live.twitch.tv/app/' },
  { id: 'youtube',        label: 'YouTube',           color: '#ff0000', enabled: false, status: 'idle', rtmpBase: 'rtmp://a.rtmp.youtube.com/live2/' },
  { id: 'youtube_shorts', label: 'YouTube Shorts',    color: '#ff0000', enabled: false, status: 'idle', isVertical: true, rtmpBase: 'rtmp://a.rtmp.youtube.com/live2/' },
  { id: 'kick',           label: 'Kick',              color: '#53fc18', enabled: false, status: 'idle', rtmpBase: 'rtmps://fa723fc1b171.global-contribute.live-video.net/app/' },
  { id: 'tiktok',         label: 'TikTok',            color: '#fe2c55', enabled: false, status: 'idle', isVertical: true, rtmpBase: 'rtmp://push.tiktokv.com/live/' },
];

const SOURCE_TYPES = [
  { type: 'display' as const,  label: 'Display Capture', icon: '🖥️' },
  { type: 'window'  as const,  label: 'Window Capture',  icon: '🪟' },
  { type: 'webcam'  as const,  label: 'Video Capture',   icon: '📷' },
  { type: 'image'   as const,  label: 'Image/Media',     icon: '🖼️' },
  { type: 'color'   as const,  label: 'Colour Source',   icon: '🎨' },
];

// ─── Scene Templates ──────────────────────────────────────────────────────────

const SCENE_TEMPLATES: SceneTemplate[] = [
  {
    name: 'Gaming',
    icon: '🎮',
    description: 'Full screen game with webcam PiP overlay',
    sources: [
      { type: 'display', name: 'Game Capture', visible: true, locked: false },
      { type: 'webcam',  name: 'Face Cam',     visible: true, locked: false },
    ],
    animation: 'cut',
  },
  {
    name: 'Podcast',
    icon: '🎙️',
    description: 'Webcam on coloured background with lower third',
    sources: [
      { type: 'color',  name: 'Background',   visible: true, locked: true },
      { type: 'webcam', name: 'Camera',        visible: true, locked: false },
      { type: 'image',  name: 'Lower Third',   visible: true, locked: false },
    ],
    animation: 'fade',
  },
  {
    name: 'Tutorial',
    icon: '📚',
    description: 'Screen capture with presenter cam in corner',
    sources: [
      { type: 'display', name: 'Screen',        visible: true, locked: false },
      { type: 'webcam',  name: 'Presenter Cam', visible: true, locked: false },
    ],
    animation: 'slide',
  },
  {
    name: 'Just Chatting',
    icon: '💬',
    description: 'Full webcam with chat overlay panel',
    sources: [
      { type: 'webcam', name: 'Camera',       visible: true, locked: false },
      { type: 'image',  name: 'Chat Overlay', visible: true, locked: false },
    ],
    animation: 'zoom',
  },
  {
    name: 'Webcam Only',
    icon: '📷',
    description: 'Clean webcam view, no screen capture',
    sources: [
      { type: 'webcam', name: 'Camera', visible: true, locked: false },
    ],
    animation: 'fade',
  },
  {
    name: 'Screen Share',
    icon: '🖥️',
    description: 'Full screen capture only',
    sources: [
      { type: 'display', name: 'Screen', visible: true, locked: false },
    ],
    animation: 'cut',
  },
  {
    name: 'Starting Soon',
    icon: '⏳',
    description: 'Holding screen shown before going live',
    sources: [
      { type: 'image', name: 'Starting Soon Graphic', visible: true, locked: false },
      { type: 'color', name: 'Background',             visible: true, locked: true },
    ],
    animation: 'fade',
  },
  {
    name: 'Be Right Back',
    icon: '☕',
    description: 'BRB / intermission screen',
    sources: [
      { type: 'image', name: 'BRB Graphic', visible: true, locked: false },
      { type: 'color', name: 'Background',  visible: true, locked: true },
    ],
    animation: 'fade',
  },
];

const ANIMATION_LABELS: Record<SceneAnimation, string> = {
  cut:   'Cut',
  fade:  'Fade',
  slide: 'Slide',
  zoom:  'Zoom In',
};

const isElectron = !!(window as any).electronAPI;

// ─── Component ────────────────────────────────────────────────────────────────

const RecordingPage: React.FC<RecordingPageProps> = ({ isRecording, onRecordingChange, theme }) => {
  // Layout
  const [mode, setMode] = useState<'record' | 'stream'>('record');
  const [studioMode, setStudioMode] = useState(false);
  const [transition, setTransition] = useState<'cut' | 'fade'>('cut');

  // Scenes / Sources
  const [scenes, setScenes] = useState<Scene[]>([]);
  const [activeProgramId, setActiveProgramId] = useState<string | null>(null);
  const [activePreviewId, setActivePreviewId] = useState<string | null>(null);
  const [showNewScene, setShowNewScene] = useState(false);
  const [newSceneName, setNewSceneName] = useState('');
  const [selectedTemplate, setSelectedTemplate] = useState<SceneTemplate | null>(null);
  const [newSceneAnimation, setNewSceneAnimation] = useState<SceneAnimation>('cut');
  const [showAddSource, setShowAddSource] = useState(false);
  const [newSourceType, setNewSourceType] = useState<SceneSource['type']>('display');
  const [newSourceName, setNewSourceName] = useState('');

  // YouTube-style notification overlay
  const [ytEffect, setYtEffect] = useState<YtEffect>(null);
  const [ytEffectVisible, setYtEffectVisible] = useState(false);
  const ytEffectTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Capture sources
  const [captureSources, setCaptureSources] = useState<Source[]>([]);
  const [selectedCaptureId, setSelectedCaptureId] = useState<string | null>(null);
  const [loadingSources, setLoadingSources] = useState(false);

  // Recording
  const [quality, setQuality] = useState('1080');
  const [fps, setFps] = useState('30');
  const [micEnabled, setMicEnabled] = useState(true);
  const [cameraEnabled, setCameraEnabled] = useState(false);
  const [recDuration, setRecDuration] = useState(0);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const streamRef = useRef<MediaStream | null>(null);

  // Streaming
  const [platforms, setPlatforms] = useState<Platform[]>(DEFAULT_PLATFORMS);
  const [isStreaming, setIsStreaming] = useState(false);
  const [streamDuration, setStreamDuration] = useState(0);
  const [streamBitrate, setStreamBitrate] = useState('6000');
  const [streamError, setStreamError] = useState<string | null>(null);

  // Audio
  const [micVol, setMicVol] = useState(80);
  const [sysVol, setSysVol] = useState(70);
  const [micMuted, setMicMuted] = useState(false);
  const [sysMuted, setSysMuted] = useState(false);

  // Effects
  const [effects, setEffects] = useState<VideoEffect[]>([]);
  const [showEffectsPanel, setShowEffectsPanel] = useState(false);
  const [showAddEffect, setShowAddEffect] = useState(false);

  // Timeline
  const [showTimeline, setShowTimeline] = useState(true);
  const [timelineZoom, setTimelineZoom] = useState(1);
  const [timelinePosition, setTimelinePosition] = useState(0); // playhead in seconds
  const [timelinePlaying, setTimelinePlaying] = useState(false);
  const [timelineDuration] = useState(300); // 5 min default project length
  const [timelineClips, setTimelineClips] = useState<TimelineClip[]>([
    { id: '1', name: 'Screen Capture', type: 'video', startTime: 0, duration: 120, track: 0, color: '#6366f1' },
    { id: '2', name: 'Webcam', type: 'video', startTime: 10, duration: 100, track: 1, color: '#8b5cf6' },
    { id: '3', name: 'Desktop Audio', type: 'audio', startTime: 0, duration: 120, track: 2, color: '#22c55e' },
    { id: '4', name: 'Mic Audio', type: 'audio', startTime: 5, duration: 110, track: 3, color: '#06b6d4' },
  ]);
  const [timelineMarkers, setTimelineMarkers] = useState<TimelineMarker[]>([]);

  // Floating Toolbar
  const [showFloatingToolbar, setShowFloatingToolbar] = useState(false);
  const [floatingToolbarPos, setFloatingToolbarPos] = useState({ x: 20, y: 20 });
  const [isDraggingToolbar, setIsDraggingToolbar] = useState(false);
  const [floatingDrawMode, setFloatingDrawMode] = useState(false);
  const [floatingZoomLevel, setFloatingZoomLevel] = useState(100);
  const toolbarDragRef = useRef<{ startX: number; startY: number; origX: number; origY: number } | null>(null);

  // Timers
  useEffect(() => {
    let t: ReturnType<typeof setInterval>;
    if (isRecording) {
      t = setInterval(() => setRecDuration(d => d + 1), 1000);
    } else {
      setRecDuration(0);
    }
    return () => clearInterval(t);
  }, [isRecording]);

  useEffect(() => {
    let t: ReturnType<typeof setInterval>;
    if (isStreaming) {
      t = setInterval(() => setStreamDuration(d => d + 1), 1000);
    } else {
      setStreamDuration(0);
    }
    return () => clearInterval(t);
  }, [isStreaming]);

  // Streaming status listener
  useEffect(() => {
    if (!isElectron) return;
    const api = (window as any).electronAPI;
    if (api.onStreamStatus) {
      api.onStreamStatus((event: any, data: { platformId: string; status: StreamStatus }) => {
        setPlatforms(prev => prev.map(p =>
          p.id === data.platformId ? { ...p, status: data.status } : p
        ));
      });
    }
  }, []);

  // Load capture sources
  const loadSources = useCallback(async () => {
    setLoadingSources(true);
    try {
      if (isElectron) {
        const srcs = await (window as any).electronAPI.getSources();
        setCaptureSources(srcs);
        if (srcs.length > 0 && !selectedCaptureId) setSelectedCaptureId(srcs[0].id);
      } else {
        setCaptureSources([
          { id: 'screen', name: 'Entire Screen', thumbnail: '' },
          { id: 'window', name: 'Application Window', thumbnail: '' },
        ]);
        if (!selectedCaptureId) setSelectedCaptureId('screen');
      }
    } finally {
      setLoadingSources(false);
    }
  }, [selectedCaptureId]);

  useEffect(() => { loadSources(); }, []);

  const fmt = (s: number) => {
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = s % 60;
    return h > 0
      ? `${h}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`
      : `${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
  };

  // ── Scene management ────────────────────────────────────────────────────────

  const addScene = (template?: SceneTemplate) => {
    const tpl = template ?? selectedTemplate;
    const name = newSceneName.trim() || tpl?.name || `Scene ${scenes.length + 1}`;
    const animation: SceneAnimation = tpl ? tpl.animation : newSceneAnimation;
    const templateSources: SceneSource[] = tpl
      ? tpl.sources.map(s => ({ ...s, id: `${Date.now()}-${Math.random()}` }))
      : [];
    const scene: Scene = { id: Date.now().toString(), name, sources: templateSources, animation };
    setScenes(prev => {
      const next = [...prev, scene];
      if (!activeProgramId) setActiveProgramId(scene.id);
      if (!activePreviewId) setActivePreviewId(scene.id);
      return next;
    });
    setNewSceneName('');
    setSelectedTemplate(null);
    setNewSceneAnimation('cut');
    setShowNewScene(false);
  };

  const triggerYtEffect = (effect: Exclude<YtEffect, null>) => {
    if (ytEffectTimer.current) clearTimeout(ytEffectTimer.current);
    setYtEffect(effect);
    setYtEffectVisible(true);
    ytEffectTimer.current = setTimeout(() => {
      setYtEffectVisible(false);
      setTimeout(() => setYtEffect(null), 500);
    }, 3500);
  };

  const removeScene = (id: string) => {
    setScenes(prev => prev.filter(s => s.id !== id));
    if (activeProgramId === id) setActiveProgramId(null);
    if (activePreviewId === id) setActivePreviewId(null);
  };

  const addSource = () => {
    if (!activeProgramId) {
      // No scene selected — prompt user to create one first
      setShowAddSource(false);
      setShowNewScene(true);
      return;
    }
    const name = newSourceName.trim() || SOURCE_TYPES.find(t => t.type === newSourceType)?.label ?? 'Source';
    const source: SceneSource = {
      id: Date.now().toString(), type: newSourceType, name, visible: true, locked: false,
    };
    setScenes(prev => prev.map(s =>
      s.id === activeProgramId ? { ...s, sources: [...s.sources, source] } : s
    ));
    setNewSourceName('');
    setShowAddSource(false);
  };

  const removeSource = (sourceId: string) => {
    if (!activeProgramId) return;
    setScenes(prev => prev.map(s =>
      s.id === activeProgramId
        ? { ...s, sources: s.sources.filter(src => src.id !== sourceId) }
        : s
    ));
  };

  const toggleSourceVisibility = (sourceId: string) => {
    if (!activeProgramId) return;
    setScenes(prev => prev.map(s =>
      s.id === activeProgramId
        ? { ...s, sources: s.sources.map(src => src.id === sourceId ? { ...src, visible: !src.visible } : src) }
        : s
    ));
  };

  const transitionToProgram = () => {
    if (studioMode && activePreviewId) {
      setActiveProgramId(activePreviewId);
    }
  };

  // ── Effects ────────────────────────────────────────────────────────────────

  const addEffect = (type: EffectType) => {
    const preset = EFFECT_PRESETS.find(p => p.type === type);
    if (!preset) return;
    const effect: VideoEffect = {
      id: Date.now().toString(),
      type,
      name: preset.name,
      enabled: true,
      intensity: preset.defaultIntensity,
      params: {},
    };
    setEffects(prev => [...prev, effect]);
    setShowAddEffect(false);
  };

  const removeEffect = (id: string) => {
    setEffects(prev => prev.filter(e => e.id !== id));
  };

  const toggleEffect = (id: string) => {
    setEffects(prev => prev.map(e => e.id === id ? { ...e, enabled: !e.enabled } : e));
  };

  const setEffectIntensity = (id: string, intensity: number) => {
    setEffects(prev => prev.map(e => e.id === id ? { ...e, intensity } : e));
  };

  // ── Timeline ──────────────────────────────────────────────────────────────

  useEffect(() => {
    let t: ReturnType<typeof setInterval>;
    if (timelinePlaying) {
      t = setInterval(() => {
        setTimelinePosition(p => {
          if (p >= timelineDuration) { setTimelinePlaying(false); return 0; }
          return p + 0.1;
        });
      }, 100);
    }
    return () => clearInterval(t);
  }, [timelinePlaying, timelineDuration]);

  const addTimelineMarker = () => {
    const marker: TimelineMarker = {
      id: Date.now().toString(),
      time: timelinePosition,
      label: `Marker ${timelineMarkers.length + 1}`,
      color: '#f59e0b',
    };
    setTimelineMarkers(prev => [...prev, marker]);
  };

  const fmtTimeline = (s: number) => {
    const m = Math.floor(s / 60);
    const sec = Math.floor(s % 60);
    const ms = Math.floor((s % 1) * 10);
    return `${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}.${ms}`;
  };

  // ── Floating Toolbar ─────────────────────────────────────────────────────

  // Show floating toolbar in separate window (Electron) or in-page (browser)
  useEffect(() => {
    if (isRecording) {
      if (isElectron) {
        (window as any).electronAPI.showFloatingToolbar();
      } else {
        setShowFloatingToolbar(true);
      }
    } else {
      if (isElectron) {
        (window as any).electronAPI.hideFloatingToolbar();
      } else {
        setShowFloatingToolbar(false);
      }
    }
  }, [isRecording]);

  // Listen for toolbar actions from separate window
  useEffect(() => {
    if (!isElectron) return;
    const api = (window as any).electronAPI;
    if (api.onToolbarAction) {
      api.onToolbarAction((_event: any, action: string) => {
        switch (action) {
          case 'stop': stopRecording(); break;
          case 'toggle-mic': setMicMuted(m => !m); break;
          case 'toggle-cam': setCameraEnabled(c => !c); break;
          case 'toggle-draw': setFloatingDrawMode(d => !d); break;
        }
      });
    }
  }, []);

  const handleToolbarMouseDown = (e: React.MouseEvent) => {
    setIsDraggingToolbar(true);
    toolbarDragRef.current = {
      startX: e.clientX, startY: e.clientY,
      origX: floatingToolbarPos.x, origY: floatingToolbarPos.y,
    };
  };

  useEffect(() => {
    if (!isDraggingToolbar) return;
    const onMove = (e: MouseEvent) => {
      if (!toolbarDragRef.current) return;
      setFloatingToolbarPos({
        x: toolbarDragRef.current.origX + (e.clientX - toolbarDragRef.current.startX),
        y: toolbarDragRef.current.origY + (e.clientY - toolbarDragRef.current.startY),
      });
    };
    const onUp = () => { setIsDraggingToolbar(false); toolbarDragRef.current = null; };
    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
    return () => { window.removeEventListener('mousemove', onMove); window.removeEventListener('mouseup', onUp); };
  }, [isDraggingToolbar]);

  // ── Recording ───────────────────────────────────────────────────────────────

  const startRecording = async () => {
    if (!selectedCaptureId) return;
    try {
      let stream: MediaStream;
      if (isElectron) {
        stream = await (navigator.mediaDevices as any).getUserMedia({
          audio: false,
          video: {
            mandatory: {
              chromeMediaSource: 'desktop',
              chromeMediaSourceId: selectedCaptureId,
              maxWidth: quality === '1080' ? 1920 : quality === '720' ? 1280 : 854,
              maxHeight: quality === '1080' ? 1080 : quality === '720' ? 720 : 480,
              maxFrameRate: parseInt(fps),
            },
          },
        });
      } else {
        stream = await navigator.mediaDevices.getDisplayMedia({ video: { frameRate: parseInt(fps) }, audio: micEnabled });
      }
      if (micEnabled) {
        try {
          const audio = await navigator.mediaDevices.getUserMedia({ audio: true });
          audio.getAudioTracks().forEach(t => stream.addTrack(t));
        } catch (_) {}
      }
      streamRef.current = stream;
      chunksRef.current = [];
      const rec = new MediaRecorder(stream, { mimeType: 'video/webm;codecs=vp9' });
      rec.ondataavailable = e => { if (e.data.size > 0) chunksRef.current.push(e.data); };
      rec.onstop = async () => {
        const blob = new Blob(chunksRef.current, { type: 'video/webm' });
        if (isElectron) {
          const buf = await blob.arrayBuffer();
          await (window as any).electronAPI.saveRecording(buf);
        } else {
          const url = URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url; a.download = `recordit-${Date.now()}.webm`; a.click();
          URL.revokeObjectURL(url);
        }
        stream.getTracks().forEach(t => t.stop());
        streamRef.current = null;
      };
      rec.start(1000);
      mediaRecorderRef.current = rec;
      onRecordingChange(true);
    } catch (e: any) {
      setStreamError(e.message || 'Failed to start recording');
    }
  };

  const stopRecording = () => {
    mediaRecorderRef.current?.stop();
    onRecordingChange(false);
  };

  // ── Streaming ───────────────────────────────────────────────────────────────

  const enabledPlatforms = platforms.filter(p => p.enabled);

  const startStream = async () => {
    if (enabledPlatforms.length === 0) {
      setStreamError('Select at least one platform to stream to.');
      return;
    }
    setStreamError(null);

    if (isElectron) {
      try {
        const keys = await (window as any).electronAPI.getStreamSettings();
        const platformConfigs = enabledPlatforms.map(p => ({
          id: p.id,
          label: p.label,
          rtmpUrl: p.rtmpBase + (keys[p.id] || ''),
          isVertical: !!p.isVertical,
        }));
        const result = await (window as any).electronAPI.startStream({
          platforms: platformConfigs,
          sourceId: selectedCaptureId,
          bitrate: parseInt(streamBitrate),
          fps: parseInt(fps),
        });
        if (result.success) {
          setIsStreaming(true);
          setPlatforms(prev => prev.map(p =>
            p.enabled ? { ...p, status: 'connecting' } : p
          ));
        } else {
          setStreamError(result.error || 'Failed to start stream');
        }
      } catch (e: any) {
        setStreamError(e.message || 'Stream error');
      }
    } else {
      // Browser demo mode
      setIsStreaming(true);
      setPlatforms(prev => prev.map(p =>
        p.enabled ? { ...p, status: 'live' } : p
      ));
    }
  };

  const stopStream = async () => {
    if (isElectron) {
      await (window as any).electronAPI.stopStream();
    }
    setIsStreaming(false);
    setPlatforms(prev => prev.map(p => ({ ...p, status: 'idle' })));
  };

  const togglePlatform = (id: Platform['id']) => {
    setPlatforms(prev => prev.map(p => p.id === id ? { ...p, enabled: !p.enabled } : p));
  };

  const activeScene = scenes.find(s => s.id === activeProgramId);
  const previewScene = scenes.find(s => s.id === activePreviewId);

  // ─── Render ──────────────────────────────────────────────────────────────────

  return (
    <div className="studio-root">
      {/* ── Header ── */}
      <div className="studio-header">
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <h1 className="studio-title">Studio</h1>
          <div className="mode-tabs">
            <button
              className={`mode-tab ${mode === 'record' ? 'active' : ''}`}
              onClick={() => setMode('record')}
            >
              <Circle size={12} /> Record
            </button>
            <button
              className={`mode-tab ${mode === 'stream' ? 'active' : ''}`}
              onClick={() => setMode('stream')}
            >
              <Radio size={12} /> Stream
            </button>
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <button
            className={`btn btn-ghost studio-mode-btn ${studioMode ? 'active' : ''}`}
            onClick={() => setStudioMode(m => !m)}
            title="Toggle Studio Mode (split preview/program)"
          >
            <Layers size={14} />
            {studioMode ? 'Studio Mode ON' : 'Studio Mode'}
          </button>
          {mode === 'record' && (
            <>
              <select className="form-select" style={{ width: 100, fontSize: 12 }} value={quality} onChange={e => setQuality(e.target.value)}>
                <option value="1080">1080p</option>
                <option value="720">720p</option>
                <option value="480">480p</option>
              </select>
              <select className="form-select" style={{ width: 80, fontSize: 12 }} value={fps} onChange={e => setFps(e.target.value)}>
                <option value="60">60 fps</option>
                <option value="30">30 fps</option>
                <option value="24">24 fps</option>
              </select>
            </>
          )}
        </div>
      </div>

      {/* ── Main canvas area ── */}
      <div className={`studio-canvas ${studioMode ? 'studio-mode' : ''}`} style={{ position: 'relative' }}>
        {studioMode && (
          <div className="studio-pane studio-pane-preview">
            <div className="studio-pane-label">PREVIEW</div>
            <div className="studio-pane-video">
              {previewScene ? (
                <div className="studio-scene-placeholder">
                  <div style={{ fontSize: 13, color: 'var(--color-text-secondary)', fontWeight: 600 }}>
                    {previewScene.name}
                  </div>
                  <div style={{ fontSize: 11, color: 'var(--color-text-tertiary)', marginTop: 4 }}>
                    {previewScene.sources.length} source{previewScene.sources.length !== 1 ? 's' : ''}
                  </div>
                </div>
              ) : (
                <div className="studio-empty-pane">No preview scene</div>
              )}
            </div>
            <div className="studio-transition-controls">
              <span style={{ fontSize: 11, color: 'var(--color-text-tertiary)' }}>Transition:</span>
              {['cut', 'fade'].map(t => (
                <button
                  key={t}
                  className={`btn btn-ghost ${transition === t ? 'active' : ''}`}
                  style={{ padding: '3px 8px', fontSize: 11 }}
                  onClick={() => setTransition(t as any)}
                >{t.charAt(0).toUpperCase() + t.slice(1)}</button>
              ))}
              <button className="btn btn-primary" style={{ padding: '4px 12px', fontSize: 11 }} onClick={transitionToProgram}>
                <ChevronRight size={12} /> Cut
              </button>
            </div>
          </div>
        )}

        <div className="studio-pane studio-pane-program">
          <div className="studio-pane-label" style={{ color: isStreaming || isRecording ? '#ff4444' : undefined }}>
            {isStreaming || isRecording ? '● ' : ''}{studioMode ? 'PROGRAM' : 'OUTPUT'}
            {activeScene && (
              <span style={{ marginLeft: 8, fontSize: 10, color: 'var(--color-text-tertiary)', fontWeight: 400 }}>
                [{ANIMATION_LABELS[activeScene.animation]}]
              </span>
            )}
          </div>
          <div className="studio-pane-video" style={{ position: 'relative' }}>
            {activeScene ? (
              <div className="studio-scene-placeholder">
                <div style={{ fontSize: 13, color: 'var(--color-text-secondary)', fontWeight: 600 }}>
                  {activeScene.name}
                </div>
                <div style={{ fontSize: 11, color: 'var(--color-text-tertiary)', marginTop: 4 }}>
                  {activeScene.sources.length} source{activeScene.sources.length !== 1 ? 's' : ''}
                </div>
              </div>
            ) : (
              <div className="studio-empty-pane">
                <Monitor size={32} style={{ opacity: 0.3, marginBottom: 8 }} />
                <div style={{ fontSize: 12, color: 'var(--color-text-tertiary)', marginBottom: 12 }}>
                  Add a scene and sources to begin
                </div>
                <button
                  className="btn btn-primary"
                  style={{ fontSize: 12, padding: '6px 14px' }}
                  onClick={() => setShowNewScene(true)}
                >
                  <Plus size={13} /> Create First Scene
                </button>
              </div>
            )}

            {/* YouTube-style notification overlay */}
            {ytEffect && (
              <div className={`yt-overlay ${ytEffectVisible ? 'yt-overlay-in' : 'yt-overlay-out'}`}>
                {ytEffect === 'like' && (
                  <div className="yt-card yt-like">
                    <ThumbsUp size={20} className="yt-icon-anim" />
                    <span className="yt-label">Thanks for the like!</span>
                  </div>
                )}
                {ytEffect === 'subscribe' && (
                  <div className="yt-card yt-subscribe">
                    <div className="yt-sub-icon">▶</div>
                    <div className="yt-sub-text">
                      <div className="yt-sub-title">New Subscriber!</div>
                      <div className="yt-sub-body">Hit the Subscribe button</div>
                    </div>
                    <button className="yt-sub-btn" onClick={() => triggerYtEffect('bell')}>
                      <Bell size={14} />
                    </button>
                  </div>
                )}
                {ytEffect === 'bell' && (
                  <div className="yt-card yt-bell">
                    <Bell size={22} className="yt-bell-ring" />
                    <div className="yt-sub-text">
                      <div className="yt-sub-title">Ring the Bell!</div>
                      <div className="yt-sub-body">Never miss an upload 🔔</div>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* ── Stream platform panel (stream mode only) ── */}
      {mode === 'stream' && (
        <div className="stream-platform-bar">
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
            <span style={{ fontSize: 11, color: 'var(--color-text-tertiary)', marginRight: 4 }}>Stream to:</span>
            {platforms.map(p => (
              <button
                key={p.id}
                className={`platform-chip ${p.enabled ? 'enabled' : ''}`}
                style={{ '--chip-color': p.color } as React.CSSProperties}
                onClick={() => !isStreaming && togglePlatform(p.id)}
                disabled={isStreaming}
              >
                <span className={`platform-chip-dot ${p.status === 'live' ? 'live' : p.status === 'connecting' ? 'connecting' : p.status === 'error' ? 'err' : ''}`} />
                {p.label}
                {p.isVertical && <span style={{ fontSize: 9, opacity: 0.7, marginLeft: 2 }}>9:16</span>}
                {p.status === 'live' && <span style={{ fontSize: 9, color: '#4ade80', marginLeft: 4 }}>LIVE</span>}
              </button>
            ))}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginLeft: 'auto' }}>
            <span style={{ fontSize: 11, color: 'var(--color-text-tertiary)' }}>Bitrate:</span>
            <select className="form-select" style={{ width: 90, fontSize: 11 }} value={streamBitrate} onChange={e => setStreamBitrate(e.target.value)} disabled={isStreaming}>
              <option value="3000">3 Mbps</option>
              <option value="4500">4.5 Mbps</option>
              <option value="6000">6 Mbps</option>
              <option value="8000">8 Mbps</option>
            </select>
          </div>
        </div>
      )}

      {streamError && (
        <div style={{ padding: '6px 12px', background: 'rgba(239,68,68,0.1)', borderTop: '1px solid rgba(239,68,68,0.3)', fontSize: 12, color: '#f87171', display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{ flex: 1 }}>{streamError}</span>
          <button onClick={() => setStreamError(null)} style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'inherit' }}><X size={12} /></button>
        </div>
      )}

      {/* ── Timeline View (between preview and docks) ── */}
      {showTimeline && (
        <div className="timeline-container">
          <div className="timeline-toolbar">
            <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <button className="btn btn-ghost" style={{ padding: '3px 6px' }} onClick={() => { setTimelinePlaying(false); setTimelinePosition(0); }} title="Go to start">
                <SkipBack size={12} />
              </button>
              <button className="btn btn-ghost" style={{ padding: '3px 6px' }} onClick={() => setTimelinePlaying(p => !p)} title={timelinePlaying ? 'Pause' : 'Play'}>
                {timelinePlaying ? <Pause size={12} /> : <Play size={12} />}
              </button>
              <button className="btn btn-ghost" style={{ padding: '3px 6px' }} onClick={() => setTimelinePosition(timelineDuration)} title="Go to end">
                <SkipForward size={12} />
              </button>
              <div style={{ width: 1, height: 14, background: 'var(--color-border-default)', margin: '0 4px' }} />
              <span className="timeline-timecode">{fmtTimeline(timelinePosition)}</span>
              <span style={{ fontSize: 10, color: 'var(--color-text-tertiary)' }}>/</span>
              <span className="timeline-timecode" style={{ opacity: 0.5 }}>{fmtTimeline(timelineDuration)}</span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <button className="btn btn-ghost" style={{ padding: '3px 6px', fontSize: 10 }} onClick={addTimelineMarker} title="Add marker">
                <Plus size={10} /> Marker
              </button>
              <button className="btn btn-ghost" style={{ padding: '3px 6px', fontSize: 10 }} title="Split at playhead">
                <Scissors size={10} /> Split
              </button>
              <div style={{ width: 1, height: 14, background: 'var(--color-border-default)', margin: '0 2px' }} />
              <button className="btn btn-ghost" style={{ padding: '3px 6px' }} onClick={() => setTimelineZoom(z => Math.max(0.5, z - 0.25))} title="Zoom out">
                <Search size={10} />−
              </button>
              <span style={{ fontSize: 10, color: 'var(--color-text-secondary)', minWidth: 32, textAlign: 'center' }}>{Math.round(timelineZoom * 100)}%</span>
              <button className="btn btn-ghost" style={{ padding: '3px 6px' }} onClick={() => setTimelineZoom(z => Math.min(4, z + 0.25))} title="Zoom in">
                <Search size={10} />+
              </button>
              <div style={{ width: 1, height: 14, background: 'var(--color-border-default)', margin: '0 2px' }} />
              <button className="btn btn-ghost" style={{ padding: '3px 6px' }} onClick={() => setShowEffectsPanel(p => !p)} title="Toggle effects panel">
                <Sparkles size={11} style={{ color: showEffectsPanel ? 'var(--color-brand-primary)' : undefined }} />
              </button>
              <button className="btn btn-ghost" style={{ padding: '3px 6px' }} onClick={() => setShowTimeline(false)} title="Hide timeline">
                <ChevronDown size={11} />
              </button>
            </div>
          </div>

          <div className="timeline-body">
            {/* Track labels */}
            <div className="timeline-track-labels">
              {['Video 1', 'Video 2', 'Audio 1', 'Audio 2'].map((label, i) => (
                <div key={i} className="timeline-track-label">
                  <span style={{ fontSize: 10, color: 'var(--color-text-secondary)' }}>{label}</span>
                </div>
              ))}
            </div>

            {/* Timeline tracks */}
            <div className="timeline-tracks" onClick={(e) => {
              const rect = e.currentTarget.getBoundingClientRect();
              const x = e.clientX - rect.left;
              const time = (x / (rect.width * timelineZoom)) * timelineDuration;
              setTimelinePosition(Math.max(0, Math.min(timelineDuration, time)));
            }}>
              {/* Time ruler */}
              <div className="timeline-ruler">
                {Array.from({ length: Math.ceil(timelineDuration / 30) + 1 }, (_, i) => (
                  <div key={i} className="timeline-ruler-mark" style={{ left: `${(i * 30 / timelineDuration) * 100 * timelineZoom}%` }}>
                    <span>{Math.floor(i * 30 / 60)}:{String((i * 30) % 60).padStart(2, '0')}</span>
                  </div>
                ))}
              </div>

              {/* Tracks */}
              {[0, 1, 2, 3].map(track => (
                <div key={track} className="timeline-track">
                  {timelineClips.filter(c => c.track === track).map(clip => (
                    <div
                      key={clip.id}
                      className="timeline-clip"
                      style={{
                        left: `${(clip.startTime / timelineDuration) * 100 * timelineZoom}%`,
                        width: `${(clip.duration / timelineDuration) * 100 * timelineZoom}%`,
                        background: `${clip.color}33`,
                        borderColor: clip.color,
                      }}
                      title={`${clip.name} (${Math.floor(clip.duration)}s)`}
                    >
                      <span className="timeline-clip-label">{clip.name}</span>
                    </div>
                  ))}
                </div>
              ))}

              {/* Playhead */}
              <div className="timeline-playhead" style={{ left: `${(timelinePosition / timelineDuration) * 100 * timelineZoom}%` }}>
                <div className="timeline-playhead-head" />
                <div className="timeline-playhead-line" />
              </div>

              {/* Markers */}
              {timelineMarkers.map(m => (
                <div key={m.id} className="timeline-marker" style={{ left: `${(m.time / timelineDuration) * 100 * timelineZoom}%` }} title={m.label}>
                  <div style={{ width: 8, height: 8, background: m.color, borderRadius: 2, transform: 'rotate(45deg)' }} />
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {!showTimeline && (
        <div style={{ display: 'flex', justifyContent: 'center', padding: 2, background: 'var(--color-bg-surface)', borderTop: '1px solid var(--color-border-default)', cursor: 'pointer' }} onClick={() => setShowTimeline(true)}>
          <ChevronUp size={12} style={{ color: 'var(--color-text-tertiary)' }} />
        </div>
      )}

      {/* ── Effects Panel (slides in from right when active) ── */}
      {showEffectsPanel && (
        <div className="effects-panel">
          <div className="effects-panel-header">
            <span style={{ fontSize: 11, fontWeight: 700, color: 'var(--color-text-primary)', textTransform: 'uppercase', letterSpacing: '0.06em' }}>
              <Sparkles size={11} style={{ marginRight: 4, verticalAlign: 'middle' }} /> Effects
            </span>
            <div style={{ display: 'flex', gap: 4 }}>
              <button className="dock-btn" onClick={() => setShowAddEffect(true)} title="Add effect"><Plus size={12} /></button>
              <button className="dock-btn" onClick={() => setShowEffectsPanel(false)} title="Close"><X size={12} /></button>
            </div>
          </div>
          <div className="effects-panel-body">
            {effects.length === 0 ? (
              <div className="dock-empty">
                No effects applied.<br />Click + to add one.
              </div>
            ) : (
              effects.map(effect => (
                <div key={effect.id} className={`effect-item ${!effect.enabled ? 'disabled' : ''}`}>
                  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 4 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                      <span style={{ fontSize: 12 }}>{EFFECT_PRESETS.find(p => p.type === effect.type)?.icon}</span>
                      <span style={{ fontSize: 11, fontWeight: 600, color: effect.enabled ? 'var(--color-text-primary)' : 'var(--color-text-tertiary)' }}>
                        {effect.name}
                      </span>
                    </div>
                    <div style={{ display: 'flex', gap: 2 }}>
                      <button className="dock-item-action" onClick={() => toggleEffect(effect.id)} title={effect.enabled ? 'Disable' : 'Enable'}>
                        {effect.enabled ? <Video size={10} /> : <VideoOff size={10} />}
                      </button>
                      <button className="dock-item-action" onClick={() => removeEffect(effect.id)} title="Remove">
                        <Trash2 size={10} />
                      </button>
                    </div>
                  </div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                    <input
                      type="range" min={0} max={100}
                      value={effect.intensity}
                      onChange={e => setEffectIntensity(effect.id, parseInt(e.target.value))}
                      disabled={!effect.enabled}
                      className="volume-slider"
                    />
                    <span style={{ fontSize: 10, color: 'var(--color-text-tertiary)', width: 28, textAlign: 'right' }}>
                      {effect.intensity}%
                    </span>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>
      )}

      {/* ── Docks row ── */}
      <div className="studio-docks">
        {/* Scenes dock */}
        <div className="dock">
          <div className="dock-header">
            <span className="dock-title">Scenes</span>
            <button className="dock-btn" onClick={() => setShowNewScene(true)} title="Add scene"><Plus size={12} /></button>
          </div>
          <div className="dock-body">
            {scenes.length === 0 ? (
              <div className="dock-empty">No scenes yet.<br />Click + to add one.</div>
            ) : (
              scenes.map(scene => (
                <div
                  key={scene.id}
                  className={`dock-item ${activeProgramId === scene.id ? 'program' : ''} ${activePreviewId === scene.id && studioMode ? 'preview' : ''}`}
                  onClick={() => studioMode ? setActivePreviewId(scene.id) : setActiveProgramId(scene.id)}
                >
                  <span className="dock-item-name">{scene.name}</span>
                  <button
                    className="dock-item-action"
                    onClick={e => { e.stopPropagation(); removeScene(scene.id); }}
                    title="Remove scene"
                  ><Trash2 size={10} /></button>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Sources dock */}
        <div className="dock dock-sources">
          <div className="dock-header">
            <span className="dock-title">Sources</span>
            <button
              className="dock-btn"
              onClick={() => activeProgramId && setShowAddSource(true)}
              disabled={!activeProgramId}
              title="Add source"
            ><Plus size={12} /></button>
          </div>
          <div className="dock-body">
            {!activeProgramId ? (
              <div className="dock-empty" style={{ textAlign: 'center' }}>
                <Monitor size={20} style={{ opacity: 0.3, marginBottom: 6 }} />
                <div>Create a scene first,</div>
                <div>then add sources to it.</div>
                <button
                  className="btn btn-primary"
                  style={{ marginTop: 8, fontSize: 11, padding: '4px 10px' }}
                  onClick={() => setShowNewScene(true)}
                >
                  <Plus size={11} /> New Scene
                </button>
              </div>
            ) : !activeScene || activeScene.sources.length === 0 ? (
              <div className="dock-empty">No sources.<br />Click + to add one.</div>
            ) : (
              [...activeScene.sources].reverse().map(src => (
                <div key={src.id} className={`dock-item ${!src.visible ? 'hidden' : ''}`}>
                  <span style={{ fontSize: 11, marginRight: 6, opacity: 0.7 }}>
                    {SOURCE_TYPES.find(t => t.type === src.type)?.icon ?? '📁'}
                  </span>
                  <span className="dock-item-name">{src.name}</span>
                  <div style={{ display: 'flex', gap: 2, marginLeft: 'auto' }}>
                    <button
                      className="dock-item-action"
                      onClick={() => toggleSourceVisibility(src.id)}
                      title={src.visible ? 'Hide' : 'Show'}
                    >{src.visible ? <Video size={10} /> : <VideoOff size={10} />}</button>
                    <button
                      className="dock-item-action"
                      onClick={() => removeSource(src.id)}
                      title="Remove"
                    ><Trash2 size={10} /></button>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Audio mixer */}
        <div className="dock dock-audio">
          <div className="dock-header">
            <span className="dock-title">Audio Mixer</span>
          </div>
          <div className="dock-body" style={{ gap: 10 }}>
            {[
              { label: 'Mic / Aux', vol: micVol, setVol: setMicVol, muted: micMuted, setMuted: setMicMuted },
              { label: 'Desktop', vol: sysVol, setVol: setSysVol, muted: sysMuted, setMuted: setSysMuted },
            ].map(ch => (
              <div key={ch.label} style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 11, color: 'var(--color-text-secondary)' }}>{ch.label}</span>
                  <button
                    className="dock-item-action"
                    style={{ color: ch.muted ? '#f87171' : undefined }}
                    onClick={() => ch.setMuted((m: boolean) => !m)}
                  >
                    {ch.muted ? <MicOff size={10} /> : <Mic size={10} />}
                  </button>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  <input
                    type="range" min={0} max={100}
                    value={ch.muted ? 0 : ch.vol}
                    onChange={e => ch.setVol(parseInt(e.target.value))}
                    disabled={ch.muted}
                    className="volume-slider"
                  />
                  <span style={{ fontSize: 10, color: 'var(--color-text-tertiary)', width: 24, textAlign: 'right' }}>
                    {ch.muted ? '—' : `${ch.vol}%`}
                  </span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* ── Controls bar ── */}
      <div className="studio-controls">
        {mode === 'record' ? (
          <>
            {isRecording ? (
              <button className="record-btn record-btn-stop" onClick={stopRecording}>
                <Square size={14} fill="currentColor" />
                Stop Recording
              </button>
            ) : (
              <button className="record-btn record-btn-start" onClick={startRecording} disabled={!selectedCaptureId && scenes.length === 0}>
                <Circle size={14} fill="currentColor" />
                Start Recording
              </button>
            )}
            {isRecording && (
              <div className="recording-indicator">
                <div className="recording-dot" />
                <span className="recording-time">{fmt(recDuration)}</span>
              </div>
            )}
          </>
        ) : (
          <>
            {isStreaming ? (
              <button className="record-btn record-btn-stop" onClick={stopStream}>
                <Square size={14} fill="currentColor" />
                Stop Stream
              </button>
            ) : (
              <button
                className="record-btn stream-btn-start"
                onClick={startStream}
                disabled={enabledPlatforms.length === 0}
              >
                <Radio size={14} />
                Start Streaming
              </button>
            )}
            {isStreaming && (
              <div className="recording-indicator">
                <div className="recording-dot" style={{ background: '#a855f7' }} />
                <span className="recording-time">{fmt(streamDuration)}</span>
                <span style={{ marginLeft: 6, fontSize: 11, color: 'var(--color-text-tertiary)' }}>
                  {enabledPlatforms.filter(p => p.status === 'live').length}/{enabledPlatforms.length} live
                </span>
              </div>
            )}
          </>
        )}

        <div style={{ marginLeft: 'auto', display: 'flex', gap: 6, alignItems: 'center' }}>
          {/* YouTube-style notification triggers */}
          <div className="yt-trigger-group" title="YouTube notification effects">
            <button
              className="btn btn-ghost yt-trigger-btn"
              onClick={() => triggerYtEffect('like')}
              title="Show like notification"
              style={{ color: '#ff4444' }}
            ><ThumbsUp size={13} /></button>
            <button
              className="btn btn-ghost yt-trigger-btn"
              onClick={() => triggerYtEffect('subscribe')}
              title="Show subscribe notification"
              style={{ color: '#ff0000' }}
            ><Youtube size={13} /></button>
            <button
              className="btn btn-ghost yt-trigger-btn"
              onClick={() => triggerYtEffect('bell')}
              title="Show bell notification"
              style={{ color: '#fbbf24' }}
            ><Bell size={13} /></button>
          </div>

          <div style={{ width: 1, height: 18, background: 'var(--color-border-default)', margin: '0 2px' }} />

          <button
            className={`btn btn-ghost ${micEnabled ? '' : 'active'}`}
            onClick={() => setMicEnabled(m => !m)}
            title="Toggle mic"
          >{micEnabled ? <Mic size={13} /> : <MicOff size={13} />}</button>
          <button
            className={`btn btn-ghost`}
            onClick={() => setCameraEnabled(c => !c)}
            title="Toggle camera"
          >{cameraEnabled ? <Camera size={13} /> : <VideoOff size={13} />}</button>
          <button
            className="btn btn-ghost"
            onClick={loadSources}
            title="Refresh sources"
            disabled={loadingSources}
          ><RefreshCw size={13} className={loadingSources ? 'spin' : ''} /></button>
        </div>
      </div>

      {/* ── Dialogs ── */}
      {showNewScene && (
        <div className="dialog-overlay" onClick={() => { setShowNewScene(false); setSelectedTemplate(null); setNewSceneName(''); }}>
          <div className="dialog dialog-wide" onClick={e => e.stopPropagation()}>
            <div className="dialog-header">
              <span className="dialog-title">New Scene</span>
              <button className="dialog-close" onClick={() => { setShowNewScene(false); setSelectedTemplate(null); setNewSceneName(''); }}><X size={14} /></button>
            </div>
            <div className="dialog-body">
              {/* Template picker */}
              <div className="form-group">
                <label className="form-label">Start from a template (optional)</label>
                <div className="template-grid">
                  {SCENE_TEMPLATES.map(tpl => (
                    <button
                      key={tpl.name}
                      className={`template-card ${selectedTemplate?.name === tpl.name ? 'selected' : ''}`}
                      onClick={() => {
                        setSelectedTemplate(t => t?.name === tpl.name ? null : tpl);
                        if (!newSceneName) setNewSceneName(tpl.name);
                        setNewSceneAnimation(tpl.animation);
                      }}
                    >
                      <span className="template-icon">{tpl.icon}</span>
                      <span className="template-name">{tpl.name}</span>
                      <span className="template-desc">{tpl.description}</span>
                      {selectedTemplate?.name === tpl.name && <Check size={10} className="template-check" />}
                    </button>
                  ))}
                </div>
              </div>

              {/* Scene name */}
              <div className="form-group">
                <label className="form-label">Scene Name</label>
                <input
                  className="form-input"
                  placeholder={selectedTemplate?.name ?? 'e.g. Main Scene'}
                  value={newSceneName}
                  onChange={e => setNewSceneName(e.target.value)}
                  onKeyDown={e => e.key === 'Enter' && addScene()}
                  autoFocus
                />
              </div>

              {/* Transition animation */}
              <div className="form-group">
                <label className="form-label">Scene Transition</label>
                <div style={{ display: 'flex', gap: 6 }}>
                  {(Object.keys(ANIMATION_LABELS) as SceneAnimation[]).map(anim => (
                    <button
                      key={anim}
                      className={`btn btn-ghost ${newSceneAnimation === anim ? 'active' : ''}`}
                      style={{ fontSize: 11, padding: '4px 10px' }}
                      onClick={() => setNewSceneAnimation(anim)}
                    >
                      {ANIMATION_LABELS[anim]}
                    </button>
                  ))}
                </div>
              </div>

              {selectedTemplate && (
                <div className="template-preview">
                  <span style={{ fontSize: 11, color: 'var(--color-text-tertiary)' }}>
                    Template sources: {selectedTemplate.sources.map(s => s.name).join(', ')}
                  </span>
                </div>
              )}
            </div>
            <div className="dialog-footer">
              <button className="btn btn-ghost" onClick={() => { setShowNewScene(false); setSelectedTemplate(null); setNewSceneName(''); }}>Cancel</button>
              <button className="btn btn-primary" onClick={() => addScene()}>
                <Plus size={13} /> {selectedTemplate ? `Add "${selectedTemplate.name}" Scene` : 'Add Scene'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ── Floating Recording Toolbar (in-page fallback for browser mode) ── */}
      {showFloatingToolbar && isRecording && !isElectron && (
        <div
          className="floating-toolbar"
          style={{ left: floatingToolbarPos.x, top: floatingToolbarPos.y }}
        >
          <div className="floating-toolbar-drag" onMouseDown={handleToolbarMouseDown} title="Drag to move">
            <GripVertical size={12} />
          </div>

          <div className="floating-toolbar-group">
            <button className="floating-btn recording-active-btn" onClick={stopRecording} title="Stop Recording">
              <Square size={13} fill="currentColor" />
            </button>
            <div className="floating-timer">
              <div className="recording-dot" style={{ width: 6, height: 6 }} />
              <span>{fmt(recDuration)}</span>
            </div>
          </div>

          <div className="floating-toolbar-divider" />

          <div className="floating-toolbar-group">
            <button
              className={`floating-btn ${micMuted ? 'muted' : ''}`}
              onClick={() => setMicMuted(m => !m)}
              title={micMuted ? 'Unmute mic' : 'Mute mic'}
            >
              {micMuted ? <MicOff size={13} /> : <Mic size={13} />}
            </button>
            <button
              className={`floating-btn ${!cameraEnabled ? 'muted' : ''}`}
              onClick={() => setCameraEnabled(c => !c)}
              title={cameraEnabled ? 'Disable camera' : 'Enable camera'}
            >
              {cameraEnabled ? <Camera size={13} /> : <VideoOff size={13} />}
            </button>
          </div>

          <div className="floating-toolbar-divider" />

          <div className="floating-toolbar-group">
            <button
              className={`floating-btn ${floatingDrawMode ? 'active' : ''}`}
              onClick={() => setFloatingDrawMode(d => !d)}
              title="Screen annotation"
            >
              <Pencil size={13} />
            </button>
            <button
              className="floating-btn"
              onClick={() => setFloatingZoomLevel(z => z < 200 ? z + 25 : 100)}
              title={`Zoom: ${floatingZoomLevel}%`}
            >
              <ZoomIn size={13} />
              <span style={{ fontSize: 9 }}>{floatingZoomLevel}%</span>
            </button>
            <button
              className="floating-btn"
              onClick={() => setShowFloatingToolbar(false)}
              title="Minimize toolbar"
            >
              <Minimize2 size={13} />
            </button>
          </div>
        </div>
      )}

      {/* Minimized floating toolbar restore button (browser mode only) */}
      {!showFloatingToolbar && isRecording && !isElectron && (
        <button
          className="floating-restore-btn"
          onClick={() => setShowFloatingToolbar(true)}
          title="Show recording toolbar"
        >
          <Maximize2 size={12} />
          <div className="recording-dot" style={{ width: 6, height: 6 }} />
        </button>
      )}

      {/* ── Add Effect dialog ── */}
      {showAddEffect && (
        <div className="dialog-overlay" onClick={() => setShowAddEffect(false)}>
          <div className="dialog" onClick={e => e.stopPropagation()} style={{ maxWidth: 520 }}>
            <div className="dialog-header">
              <span className="dialog-title"><Sparkles size={14} style={{ marginRight: 6, verticalAlign: 'middle' }} />Add Video Effect</span>
              <button className="dialog-close" onClick={() => setShowAddEffect(false)}><X size={14} /></button>
            </div>
            <div className="dialog-body">
              <div className="effect-grid">
                {EFFECT_PRESETS.map(preset => (
                  <button
                    key={preset.type}
                    className="effect-preset-card"
                    onClick={() => addEffect(preset.type)}
                  >
                    <span style={{ fontSize: 24 }}>{preset.icon}</span>
                    <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--color-text-primary)' }}>{preset.name}</span>
                    <span style={{ fontSize: 10, color: 'var(--color-text-tertiary)', lineHeight: 1.3 }}>{preset.description}</span>
                  </button>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}

      {showAddSource && (
        <div className="dialog-overlay" onClick={() => setShowAddSource(false)}>
          <div className="dialog" onClick={e => e.stopPropagation()}>
            <div className="dialog-header">
              <span className="dialog-title">Add Source</span>
              <button className="dialog-close" onClick={() => setShowAddSource(false)}><X size={14} /></button>
            </div>
            <div className="dialog-body">
              <div className="form-group">
                <label className="form-label">Source Type</label>
                <div className="source-type-grid">
                  {SOURCE_TYPES.map(t => (
                    <button
                      key={t.type}
                      className={`source-type-btn ${newSourceType === t.type ? 'selected' : ''}`}
                      onClick={() => setNewSourceType(t.type)}
                    >
                      <span style={{ fontSize: 20 }}>{t.icon}</span>
                      <span style={{ fontSize: 11 }}>{t.label}</span>
                      {newSourceType === t.type && <Check size={10} className="source-type-check" />}
                    </button>
                  ))}
                </div>
              </div>
              <div className="form-group">
                <label className="form-label">Name (optional)</label>
                <input
                  className="form-input"
                  placeholder={SOURCE_TYPES.find(t => t.type === newSourceType)?.label ?? 'Source'}
                  value={newSourceName}
                  onChange={e => setNewSourceName(e.target.value)}
                  onKeyDown={e => e.key === 'Enter' && addSource()}
                />
              </div>
            </div>
            <div className="dialog-footer">
              <button className="btn btn-ghost" onClick={() => setShowAddSource(false)}>Cancel</button>
              <button className="btn btn-primary" onClick={addSource}>
                <Plus size={13} /> Add Source
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default RecordingPage;
