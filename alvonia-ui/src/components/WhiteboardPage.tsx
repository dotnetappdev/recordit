import React, { useRef, useEffect, useState, useCallback } from 'react';
import {
  Pen, Eraser, Square, Circle, Minus, Type, Trash2,
  Download, Undo2, Redo2, ZoomIn, ZoomOut, Hand,
  Users, ChevronDown, MousePointer, Triangle,
  Highlighter, StickyNote, Image as ImageIcon
} from 'lucide-react';
import { Theme } from '../App';

type Tool = 'pen' | 'highlighter' | 'eraser' | 'line' | 'rect' | 'circle' | 'triangle'
  | 'text' | 'select' | 'pan' | 'sticky';

interface Point { x: number; y: number; }

interface DrawAction {
  type: 'path' | 'shape' | 'text' | 'sticky';
  points?: Point[];
  color: string;
  size: number;
  tool: Tool;
  x?: number; y?: number; w?: number; h?: number;
  text?: string;
  opacity?: number;
}

const COLORS = [
  '#f5f5f5', '#ef4444', '#f59e0b', '#22c55e',
  '#06b6d4', '#6366f1', '#8b5cf6', '#ec4899',
  '#000000', '#374151'
];

const PARTICIPANTS = [
  { name: 'You', color: '#6366f1', active: true },
  { name: 'Alex M.', color: '#ef4444', active: true },
  { name: 'Sam K.', color: '#22c55e', active: false },
];

interface WhiteboardPageProps {
  theme: Theme;
}

const WhiteboardPage: React.FC<WhiteboardPageProps> = ({ theme }) => {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const overlayRef = useRef<HTMLCanvasElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  const [tool, setTool] = useState<Tool>('pen');
  const [color, setColor] = useState('#f5f5f5');
  const [size, setSize] = useState(3);
  const [zoom, setZoom] = useState(100);
  const [showParticipants, setShowParticipants] = useState(false);

  const isDrawing = useRef(false);
  const startPoint = useRef<Point>({ x: 0, y: 0 });
  const history = useRef<ImageData[]>([]);
  const historyIndex = useRef(-1);
  const panOffset = useRef<Point>({ x: 0, y: 0 });
  const lastPan = useRef<Point>({ x: 0, y: 0 });

  const getCanvas = () => canvasRef.current;
  const getCtx = () => canvasRef.current?.getContext('2d') ?? null;
  const getOverlay = () => overlayRef.current?.getContext('2d') ?? null;

  const resizeCanvas = useCallback(() => {
    const canvas = getCanvas();
    const container = containerRef.current;
    if (!canvas || !container) return;
    const { width, height } = container.getBoundingClientRect();
    const overlay = overlayRef.current;

    [canvas, overlay].forEach(c => {
      if (c) { c.width = width; c.height = height; }
    });

    // Restore history
    const ctx = getCtx();
    if (ctx && history.current[historyIndex.current]) {
      ctx.putImageData(history.current[historyIndex.current], 0, 0);
    }
  }, []);

  useEffect(() => {
    resizeCanvas();
    const ro = new ResizeObserver(resizeCanvas);
    if (containerRef.current) ro.observe(containerRef.current);
    return () => ro.disconnect();
  }, [resizeCanvas]);

  const saveHistory = useCallback(() => {
    const ctx = getCtx();
    const canvas = getCanvas();
    if (!ctx || !canvas) return;
    const data = ctx.getImageData(0, 0, canvas.width, canvas.height);
    history.current = history.current.slice(0, historyIndex.current + 1);
    history.current.push(data);
    historyIndex.current = history.current.length - 1;
  }, []);

  const undo = () => {
    if (historyIndex.current <= 0) return;
    historyIndex.current--;
    const ctx = getCtx();
    const canvas = getCanvas();
    if (ctx && canvas && history.current[historyIndex.current]) {
      ctx.putImageData(history.current[historyIndex.current], 0, 0);
    }
  };

  const redo = () => {
    if (historyIndex.current >= history.current.length - 1) return;
    historyIndex.current++;
    const ctx = getCtx();
    const canvas = getCanvas();
    if (ctx && canvas && history.current[historyIndex.current]) {
      ctx.putImageData(history.current[historyIndex.current], 0, 0);
    }
  };

  const clearCanvas = () => {
    const ctx = getCtx();
    const canvas = getCanvas();
    if (!ctx || !canvas) return;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    saveHistory();
  };

  const getPos = (e: React.MouseEvent<HTMLCanvasElement>): Point => {
    const canvas = overlayRef.current;
    if (!canvas) return { x: 0, y: 0 };
    const rect = canvas.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  };

  const drawPath = (ctx: CanvasRenderingContext2D, from: Point, to: Point) => {
    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.lineTo(to.x, to.y);
    ctx.strokeStyle = tool === 'eraser' ? 'rgba(0,0,0,1)' : color;
    ctx.globalCompositeOperation = tool === 'eraser' ? 'destination-out' : 'source-over';
    ctx.globalAlpha = tool === 'highlighter' ? 0.4 : 1;
    ctx.lineWidth = tool === 'eraser' ? size * 4 : tool === 'highlighter' ? size * 5 : size;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.stroke();
    ctx.globalCompositeOperation = 'source-over';
    ctx.globalAlpha = 1;
  };

  const drawShape = (ctx: CanvasRenderingContext2D, start: Point, end: Point, preview = false) => {
    ctx.strokeStyle = color;
    ctx.lineWidth = size;
    ctx.globalAlpha = 1;
    ctx.globalCompositeOperation = 'source-over';
    const x = Math.min(start.x, end.x);
    const y = Math.min(start.y, end.y);
    const w = Math.abs(end.x - start.x);
    const h = Math.abs(end.y - start.y);

    ctx.beginPath();
    if (tool === 'rect') {
      ctx.strokeRect(x, y, w, h);
    } else if (tool === 'circle') {
      ctx.ellipse(x + w / 2, y + h / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
      ctx.stroke();
    } else if (tool === 'line') {
      ctx.moveTo(start.x, start.y);
      ctx.lineTo(end.x, end.y);
      ctx.stroke();
    } else if (tool === 'triangle') {
      ctx.moveTo(start.x + (end.x - start.x) / 2, start.y);
      ctx.lineTo(end.x, end.y);
      ctx.lineTo(start.x, end.y);
      ctx.closePath();
      ctx.stroke();
    }
  };

  const onMouseDown = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const pos = getPos(e);
    isDrawing.current = true;
    startPoint.current = pos;
    if (tool === 'pan') {
      lastPan.current = pos;
      return;
    }
    const ctx = getCtx();
    if (!ctx) return;
    if (tool === 'pen' || tool === 'highlighter' || tool === 'eraser') {
      ctx.beginPath();
      ctx.moveTo(pos.x, pos.y);
    }
  };

  const onMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!isDrawing.current) return;
    const pos = getPos(e);
    const ctx = getCtx();
    const overlay = getOverlay();

    if (tool === 'pen' || tool === 'highlighter' || tool === 'eraser') {
      if (ctx) drawPath(ctx, startPoint.current, pos);
      startPoint.current = pos;
    } else if (['rect', 'circle', 'line', 'triangle'].includes(tool)) {
      if (overlay && overlayRef.current) {
        overlay.clearRect(0, 0, overlayRef.current.width, overlayRef.current.height);
        drawShape(overlay, startPoint.current, pos, true);
      }
    }
  };

  const onMouseUp = (e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!isDrawing.current) return;
    const pos = getPos(e);
    isDrawing.current = false;

    const ctx = getCtx();
    const overlay = getOverlay();

    if (['rect', 'circle', 'line', 'triangle'].includes(tool)) {
      if (ctx) drawShape(ctx, startPoint.current, pos);
      if (overlay && overlayRef.current) {
        overlay.clearRect(0, 0, overlayRef.current.width, overlayRef.current.height);
      }
    }
    saveHistory();
  };

  const handleSave = async () => {
    const canvas = getCanvas();
    if (!canvas) return;
    const dataUrl = canvas.toDataURL('image/png');
    if ((window as any).electronAPI) {
      await (window as any).electronAPI.saveWhiteboard(dataUrl);
    } else {
      const a = document.createElement('a');
      a.href = dataUrl;
      a.download = `whiteboard-${Date.now()}.png`;
      a.click();
    }
  };

  const zoomIn = () => setZoom(z => Math.min(z + 25, 400));
  const zoomOut = () => setZoom(z => Math.max(z - 25, 25));

  const tools: { id: Tool; icon: React.ReactNode; label: string }[] = [
    { id: 'select', icon: <MousePointer size={16} />, label: 'Select' },
    { id: 'pen', icon: <Pen size={16} />, label: 'Pen' },
    { id: 'highlighter', icon: <Highlighter size={16} />, label: 'Highlighter' },
    { id: 'eraser', icon: <Eraser size={16} />, label: 'Eraser' },
    { id: 'line', icon: <Minus size={16} />, label: 'Line' },
    { id: 'rect', icon: <Square size={16} />, label: 'Rectangle' },
    { id: 'circle', icon: <Circle size={16} />, label: 'Ellipse' },
    { id: 'triangle', icon: <Triangle size={16} />, label: 'Triangle' },
    { id: 'text', icon: <Type size={16} />, label: 'Text' },
    { id: 'sticky', icon: <StickyNote size={16} />, label: 'Sticky Note' },
    { id: 'pan', icon: <Hand size={16} />, label: 'Pan' },
  ];

  return (
    <div className="whiteboard-container">
      {/* Toolbar */}
      <div className="whiteboard-toolbar">
        {/* Drawing tools */}
        <div className="whiteboard-toolbar-group">
          {tools.slice(0, 4).map(t => (
            <button
              key={t.id}
              className={`wb-tool-btn ${tool === t.id ? 'active' : ''}`}
              onClick={() => setTool(t.id)}
              title={t.label}
            >
              {t.icon}
            </button>
          ))}
        </div>

        {/* Shape tools */}
        <div className="whiteboard-toolbar-group">
          {tools.slice(4, 9).map(t => (
            <button
              key={t.id}
              className={`wb-tool-btn ${tool === t.id ? 'active' : ''}`}
              onClick={() => setTool(t.id)}
              title={t.label}
            >
              {t.icon}
            </button>
          ))}
        </div>

        {/* Special tools */}
        <div className="whiteboard-toolbar-group">
          {tools.slice(9).map(t => (
            <button
              key={t.id}
              className={`wb-tool-btn ${tool === t.id ? 'active' : ''}`}
              onClick={() => setTool(t.id)}
              title={t.label}
            >
              {t.icon}
            </button>
          ))}
        </div>

        {/* Colors */}
        <div className="whiteboard-toolbar-group">
          <div className="color-picker-row">
            {COLORS.map(c => (
              <div
                key={c}
                className={`color-swatch ${color === c ? 'selected' : ''}`}
                style={{ background: c, border: c === '#f5f5f5' ? '1px solid var(--color-border-strong)' : undefined }}
                onClick={() => setColor(c)}
                title={c}
              />
            ))}
          </div>
        </div>

        {/* Size */}
        <div className="whiteboard-toolbar-group">
          <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--color-text-tertiary)', marginRight: 4 }}>Size</span>
          <input
            type="range"
            className="size-slider"
            min="1"
            max="20"
            value={size}
            onChange={e => setSize(Number(e.target.value))}
          />
          <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--color-text-secondary)', minWidth: 16 }}>{size}</span>
        </div>

        {/* Actions */}
        <div className="whiteboard-toolbar-group">
          <button className="wb-tool-btn" onClick={undo} title="Undo (Ctrl+Z)"><Undo2 size={16} /></button>
          <button className="wb-tool-btn" onClick={redo} title="Redo (Ctrl+Y)"><Redo2 size={16} /></button>
        </div>

        <div className="whiteboard-toolbar-group">
          <button className="wb-tool-btn" onClick={clearCanvas} title="Clear Canvas">
            <Trash2 size={16} />
          </button>
          <button className="wb-tool-btn" onClick={handleSave} title="Save as PNG">
            <Download size={16} />
          </button>
          <button className="wb-tool-btn" title="Insert Image">
            <ImageIcon size={16} />
          </button>
        </div>

        {/* Participants */}
        <div className="whiteboard-toolbar-group" style={{ marginLeft: 'auto', borderRight: 'none' }}>
          <button
            className="wb-tool-btn"
            style={{ width: 'auto', padding: '0 8px', gap: 6 }}
            onClick={() => setShowParticipants(p => !p)}
            title="Participants"
          >
            <Users size={14} />
            <span style={{ fontSize: 'var(--font-size-sm)' }}>{PARTICIPANTS.filter(p => p.active).length}</span>
            <ChevronDown size={12} />
          </button>
        </div>
      </div>

      {/* Canvas Area */}
      <div
        ref={containerRef}
        className={`whiteboard-canvas-area ${tool === 'pan' ? 'pan-mode' : ''}`}
        style={{ background: theme === 'dark' ? '#141414' : '#f8f8f8' }}
      >
        {/* Grid pattern */}
        <svg style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', opacity: 0.06, pointerEvents: 'none' }}>
          <defs>
            <pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse">
              <path d="M 40 0 L 0 0 0 40" fill="none" stroke="currentColor" strokeWidth="0.5" />
            </pattern>
          </defs>
          <rect width="100%" height="100%" fill="url(#grid)" />
        </svg>

        <canvas ref={canvasRef} className="whiteboard-canvas" style={{ zIndex: 1 }} />
        <canvas
          ref={overlayRef}
          className="whiteboard-canvas"
          style={{ zIndex: 2, pointerEvents: 'auto' }}
          onMouseDown={onMouseDown}
          onMouseMove={onMouseMove}
          onMouseUp={onMouseUp}
          onMouseLeave={onMouseUp}
        />

        {/* Participants panel */}
        {showParticipants && (
          <div className="participants-panel">
            <div style={{ fontSize: 'var(--font-size-xs)', fontWeight: 600, color: 'var(--color-text-tertiary)', textTransform: 'uppercase', letterSpacing: '0.5px', marginBottom: 8 }}>
              Participants
            </div>
            {PARTICIPANTS.map((p, i) => (
              <div key={i} className="participant-item">
                <div className="participant-avatar" style={{ background: p.color }}>
                  {p.name[0]}
                </div>
                <span className="participant-name">{p.name}</span>
                <div className="participant-status" style={{ background: p.active ? 'var(--color-success)' : 'var(--color-text-tertiary)' }} />
              </div>
            ))}
          </div>
        )}

        {/* Zoom Controls */}
        <div className="zoom-controls">
          <button className="wb-tool-btn" onClick={zoomOut} style={{ width: 28, height: 28 }}>
            <ZoomOut size={14} />
          </button>
          <span className="zoom-label">{zoom}%</span>
          <button className="wb-tool-btn" onClick={zoomIn} style={{ width: 28, height: 28 }}>
            <ZoomIn size={14} />
          </button>
        </div>
      </div>
    </div>
  );
};

export default WhiteboardPage;
