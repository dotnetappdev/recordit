import React, { useState, useEffect } from 'react';
import TitleBar from './components/TitleBar';
import Sidebar from './components/Sidebar';
import RecordingPage from './components/RecordingPage';
import WhiteboardPage from './components/WhiteboardPage';
import SettingsPage from './components/SettingsPage';
import LibraryPage from './components/LibraryPage';
import './styles/app.css';

export type Page = 'record' | 'whiteboard' | 'library' | 'settings';
export type Theme = 'dark' | 'light';

export interface AppState {
  theme: Theme;
  isRecording: boolean;
  recordingDuration: number;
  activePage: Page;
}

const App: React.FC = () => {
  const [theme, setTheme] = useState<Theme>('dark');
  const [activePage, setActivePage] = useState<Page>('record');
  const [isRecording, setIsRecording] = useState(false);
  const [recordingDuration, setRecordingDuration] = useState(0);

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
  }, [theme]);

  useEffect(() => {
    let interval: ReturnType<typeof setInterval>;
    if (isRecording) {
      interval = setInterval(() => {
        setRecordingDuration(d => d + 1);
      }, 1000);
    } else {
      setRecordingDuration(0);
    }
    return () => clearInterval(interval);
  }, [isRecording]);

  const toggleTheme = () => setTheme(t => t === 'dark' ? 'light' : 'dark');

  const appState: AppState = { theme, isRecording, recordingDuration, activePage };

  return (
    <div className={`app-root theme-${theme}`}>
      <TitleBar
        theme={theme}
        isRecording={isRecording}
        recordingDuration={recordingDuration}
        onToggleTheme={toggleTheme}
      />
      <div className="app-body">
        <Sidebar
          activePage={activePage}
          onNavigate={setActivePage}
          isRecording={isRecording}
          theme={theme}
        />
        <main className="app-content">
          {activePage === 'record' && (
            <RecordingPage
              isRecording={isRecording}
              onRecordingChange={setIsRecording}
              theme={theme}
            />
          )}
          {activePage === 'whiteboard' && (
            <WhiteboardPage theme={theme} />
          )}
          {activePage === 'library' && (
            <LibraryPage theme={theme} />
          )}
          {activePage === 'settings' && (
            <SettingsPage
              theme={theme}
              onToggleTheme={toggleTheme}
            />
          )}
        </main>
      </div>
    </div>
  );
};

export default App;
