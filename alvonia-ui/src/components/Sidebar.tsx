import React from 'react';
import { Video, PenTool, FolderOpen, Settings, Circle } from 'lucide-react';
import { Page, Theme } from '../App';

interface SidebarProps {
  activePage: Page;
  onNavigate: (page: Page) => void;
  isRecording: boolean;
  theme: Theme;
}

const navItems: { id: Page; icon: React.ReactNode; label: string }[] = [
  { id: 'record', icon: <Video size={18} />, label: 'Record' },
  { id: 'whiteboard', icon: <PenTool size={18} />, label: 'Whiteboard' },
  { id: 'library', icon: <FolderOpen size={18} />, label: 'Library' },
];

const Sidebar: React.FC<SidebarProps> = ({ activePage, onNavigate, isRecording }) => {
  return (
    <nav className="sidebar">
      {navItems.map(item => (
        <button
          key={item.id}
          className={[
            'sidebar-nav-item tooltip',
            activePage === item.id ? 'active' : '',
            item.id === 'record' && isRecording ? 'recording-active' : ''
          ].filter(Boolean).join(' ')}
          onClick={() => onNavigate(item.id)}
          data-tooltip={item.label}
          title={item.label}
        >
          {item.id === 'record' && isRecording
            ? <Circle size={18} fill="currentColor" />
            : item.icon
          }
        </button>
      ))}

      <div className="sidebar-spacer" />
      <div className="sidebar-divider" />

      <button
        className={`sidebar-nav-item tooltip ${activePage === 'settings' ? 'active' : ''}`}
        onClick={() => onNavigate('settings')}
        data-tooltip="Settings"
        title="Settings"
      >
        <Settings size={18} />
      </button>
    </nav>
  );
};

export default Sidebar;
