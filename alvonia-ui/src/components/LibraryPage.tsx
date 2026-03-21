import React, { useState } from 'react';
import { Video, Image, FolderOpen, Play, Download, Trash2, Search, Filter, Clock, HardDrive } from 'lucide-react';
import { Theme } from '../App';

interface Recording {
  id: string;
  name: string;
  type: 'video' | 'whiteboard';
  duration?: string;
  size: string;
  date: string;
  thumbnail?: string;
}

const DEMO_RECORDINGS: Recording[] = [
  { id: '1', name: 'Product Demo Recording', type: 'video', duration: '5:32', size: '124 MB', date: 'Today, 2:30 PM' },
  { id: '2', name: 'Whiteboard Session - Architecture', type: 'whiteboard', size: '2.1 MB', date: 'Today, 11:15 AM' },
  { id: '3', name: 'Team Standup - March 21', type: 'video', duration: '12:04', size: '287 MB', date: 'Yesterday' },
  { id: '4', name: 'UI Design Whiteboard', type: 'whiteboard', size: '1.8 MB', date: 'Mar 20' },
  { id: '5', name: 'Bug Reproduction Steps', type: 'video', duration: '3:17', size: '78 MB', date: 'Mar 19' },
  { id: '6', name: 'System Architecture Board', type: 'whiteboard', size: '3.2 MB', date: 'Mar 18' },
];

interface LibraryPageProps {
  theme: Theme;
}

const LibraryPage: React.FC<LibraryPageProps> = ({ theme }) => {
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<'all' | 'video' | 'whiteboard'>('all');
  const [selected, setSelected] = useState<string | null>(null);

  const filtered = DEMO_RECORDINGS.filter(r => {
    if (filter !== 'all' && r.type !== filter) return false;
    if (search && !r.name.toLowerCase().includes(search.toLowerCase())) return false;
    return true;
  });

  const totalSize = DEMO_RECORDINGS.reduce((acc, r) => {
    const mb = parseFloat(r.size);
    return acc + mb;
  }, 0);

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <h1 className="page-title">Library</h1>
          <p className="page-subtitle">{DEMO_RECORDINGS.length} recordings · {totalSize.toFixed(0)} MB used</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-ghost">
            <Filter size={14} />
            Filter
          </button>
        </div>
      </div>

      <div className="page-body">
        {/* Stats bar */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 12, marginBottom: 20 }}>
          {[
            { icon: <Video size={16} />, label: 'Videos', value: DEMO_RECORDINGS.filter(r => r.type === 'video').length },
            { icon: <Image size={16} />, label: 'Whiteboards', value: DEMO_RECORDINGS.filter(r => r.type === 'whiteboard').length },
            { icon: <HardDrive size={16} />, label: 'Storage Used', value: `${totalSize.toFixed(0)} MB` },
          ].map((s, i) => (
            <div key={i} className="card" style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '12px 16px' }}>
              <div style={{ color: 'var(--color-brand-primary)' }}>{s.icon}</div>
              <div>
                <div style={{ fontSize: 'var(--font-size-xl)', fontWeight: 700, color: 'var(--color-text-primary)' }}>{s.value}</div>
                <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--color-text-tertiary)' }}>{s.label}</div>
              </div>
            </div>
          ))}
        </div>

        {/* Search & filters */}
        <div style={{ display: 'flex', gap: 8, marginBottom: 16, alignItems: 'center' }}>
          <div style={{ position: 'relative', flex: 1 }}>
            <Search size={14} style={{ position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--color-text-tertiary)' }} />
            <input
              className="form-input"
              style={{ paddingLeft: 32 }}
              placeholder="Search recordings..."
              value={search}
              onChange={e => setSearch(e.target.value)}
            />
          </div>
          {(['all', 'video', 'whiteboard'] as const).map(f => (
            <button
              key={f}
              className={`btn ${filter === f ? 'btn-primary' : 'btn-ghost'}`}
              onClick={() => setFilter(f)}
            >
              {f === 'all' ? 'All' : f === 'video' ? 'Videos' : 'Whiteboards'}
            </button>
          ))}
        </div>

        {/* Grid */}
        {filtered.length === 0 ? (
          <div className="empty-state">
            <FolderOpen size={48} className="empty-state-icon" />
            <h2 className="empty-state-title">No recordings found</h2>
            <p className="empty-state-description">
              {search ? `No results for "${search}"` : 'Your recordings will appear here after you record your screen or save a whiteboard.'}
            </p>
          </div>
        ) : (
          <div className="library-grid">
            {filtered.map(rec => (
              <div
                key={rec.id}
                className="library-card"
                onClick={() => setSelected(selected === rec.id ? null : rec.id)}
                style={{ outline: selected === rec.id ? '2px solid var(--color-brand-primary)' : undefined }}
              >
                <div className="library-card-thumb">
                  {rec.type === 'video' ? (
                    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
                      <Play size={32} style={{ opacity: 0.4 }} />
                      {rec.duration && (
                        <span style={{ fontSize: 'var(--font-size-xs)', background: 'rgba(0,0,0,0.5)', padding: '2px 6px', borderRadius: 4 }}>
                          {rec.duration}
                        </span>
                      )}
                    </div>
                  ) : (
                    <Image size={32} style={{ opacity: 0.4 }} />
                  )}
                </div>
                <div className="library-card-info">
                  <div className="library-card-title">{rec.name}</div>
                  <div className="library-card-meta" style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                    <span style={{ display: 'flex', alignItems: 'center', gap: 3 }}>
                      <Clock size={10} />
                      {rec.date}
                    </span>
                    <span>·</span>
                    <span>{rec.size}</span>
                  </div>
                  {selected === rec.id && (
                    <div style={{ display: 'flex', gap: 6, marginTop: 8 }}>
                      <button className="btn btn-primary" style={{ flex: 1, padding: '4px 8px', fontSize: 12 }}>
                        <Play size={12} />
                        Open
                      </button>
                      <button className="btn btn-ghost" style={{ padding: '4px 8px' }} title="Download">
                        <Download size={12} />
                      </button>
                      <button className="btn btn-ghost" style={{ padding: '4px 8px', color: 'var(--color-error)' }} title="Delete">
                        <Trash2 size={12} />
                      </button>
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};

export default LibraryPage;
