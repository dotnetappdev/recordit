# RecordIt

> Professional Screen Recording & Whiteboard App — powered by **Alvonia UI**

[![GitHub Pages](https://img.shields.io/badge/docs-GitHub%20Pages-blue)](https://dotnetappdev.github.io/recordit)
[![WinUI 3](https://img.shields.io/badge/WinUI-3.0-indigo)](winui/)
[![Electron](https://img.shields.io/badge/Electron-26-cyan)](alvonia-ui/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Build](https://img.shields.io/github/actions/workflow/status/dotnetappdev/recordit/build.yml)](https://github.com/dotnetappdev/recordit/actions)

RecordIt combines a professional-grade screen recorder with an **MS Teams-style collaborative whiteboard**. It ships as two apps sharing the same **Alvonia UI** design system — a native Windows WinUI 3 app and a cross-platform Electron app — with identical UI and features on both platforms.

---

## ✨ Features

### 🔴 Screen Recording
- **4K/1080p/720p** recording at 15–60 fps
- **Multiple capture sources**: full screen, specific monitor, application window
- **Microphone + system audio** recording with mixing controls
- **Webcam overlay** (picture-in-picture) during recording
- **Output formats**: MP4 (H.264), WebM (VP9), MKV
- Hardware-accelerated encoding via GPU
- Countdown timer before recording starts
- System tray integration and global hotkeys

### ✏️ Whiteboard (MS Teams-style)
- **Drawing tools**: Pen, highlighter, eraser with adjustable brush sizes
- **Shapes**: Line, rectangle, ellipse, triangle
- **Text & sticky notes** for annotations and collaboration
- **10-color palette** plus custom color picker
- **Undo/Redo** with full history (Ctrl+Z / Ctrl+Y)
- **Participants panel** showing active collaborators (like MS Teams)
- **Zoom controls** (25%–400%) with pan/drag
- Grid background for alignment
- Export whiteboard to PNG

### 📁 Recording Library
- Grid view of all recordings and whiteboards
- Search and filter by type (video / whiteboard)
- Storage usage statistics

### ⚙️ Settings
- **Dark & Light themes** with system preference sync
- Per-session quality, format, and frame rate configuration
- Keyboard shortcut reference
- Language selection (English, German, French, Spanish, Japanese)

---

## 🏗️ Architecture: Two Apps, One UI

RecordIt is built with the **Alvonia UI** design system ensuring visual parity across both implementations:

```
recordit/
├── alvonia-ui/              # 🌐 Electron + React (cross-platform)
│   ├── electron/
│   │   ├── main.js          # Electron main process
│   │   └── preload.js       # Context bridge API
│   ├── src/
│   │   ├── App.tsx
│   │   ├── components/
│   │   │   ├── TitleBar.tsx
│   │   │   ├── Sidebar.tsx
│   │   │   ├── RecordingPage.tsx
│   │   │   ├── WhiteboardPage.tsx
│   │   │   ├── LibraryPage.tsx
│   │   │   └── SettingsPage.tsx
│   │   └── styles/
│   │       ├── global.css   # Alvonia UI design tokens
│   │       └── app.css      # Component styles
│   └── package.json
│
├── winui/                   # 🪟 WinUI 3 C# (Windows-native)
│   ├── RecordIt/
│   │   ├── MainWindow.xaml(.cs)
│   │   ├── Pages/           # Record, Whiteboard, Library, Settings
│   │   ├── Services/        # ScreenRecordingService.cs
│   │   └── Styles/
│   │       └── AlvoniaTheme.xaml  # XAML design system
│   └── RecordIt.Package/    # MSIX packaging project
│       └── Package.appxmanifest
│
├── docs/                    # 📄 GitHub Pages static site
│   ├── index.html
│   ├── features.html
│   ├── installation.html
│   ├── docs.html
│   └── assets/
│
└── .github/workflows/
    ├── docs.yml             # Auto-deploy to GitHub Pages
    └── build.yml            # Build MSIX + NSIS releases
```

---

## 🚀 Getting Started

### Electron App (Cross-Platform)

```bash
git clone https://github.com/dotnetappdev/recordit.git
cd recordit/alvonia-ui
npm install
npm run electron:dev         # Development
npm run electron:build:win   # Build for Windows
```

### WinUI 3 App (Windows-native)

1. Open `winui/RecordIt.sln` in **Visual Studio 2022**
2. Ensure **Windows App SDK** workload is installed
3. Set startup project to `RecordIt.Package`
4. Select `Debug | x64` → Press **F5**

### Build MSIX

```powershell
cd winui
msbuild RecordIt.Package\RecordIt.Package.wapproj /p:Configuration=Release /p:Platform=x64
```

---

## 🎨 Alvonia UI Design System

Both apps share identical design tokens:

| Token | Value | Usage |
|-------|-------|-------|
| `brand-primary` | `#6366f1` | Buttons, active states |
| `brand-secondary` | `#8b5cf6` | Accents |
| `bg-base` | `#0f0f0f` | App background |
| `bg-surface` | `#1a1a1a` | Sidebar, title bar |
| `bg-elevated` | `#222222` | Inputs, cards |
| `text-primary` | `#f5f5f5` | Main text |
| `recording` | `#ef4444` | Recording indicator |

- **Electron/React**: CSS custom properties in `src/styles/global.css`
- **WinUI 3**: XAML resources in `Styles/AlvoniaTheme.xaml`

---

## 📦 Installation

```powershell
# Windows (via winget)
winget install Alvonia.RecordIt
```

Or download from [GitHub Releases](https://github.com/dotnetappdev/recordit/releases/latest):
- `RecordIt.msix` — WinUI 3 MSIX installer (Windows 10/11)
- `RecordIt-Setup-*.exe` — Electron NSIS installer (Windows)
- `RecordIt-*.dmg` — Electron DMG (macOS)
- `RecordIt-*.AppImage` — Electron AppImage (Linux)

---

## ⌨️ Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| Start/Stop Recording | `Ctrl+Shift+R` |
| Pause Recording | `Ctrl+Shift+P` |
| Open Whiteboard | `Ctrl+Shift+W` |
| Take Screenshot | `Ctrl+Shift+S` |
| Minimize to Tray | `Ctrl+M` |
| Undo (Whiteboard) | `Ctrl+Z` |
| Redo (Whiteboard) | `Ctrl+Y` |

---

## 📖 Documentation

Full docs: **[dotnetappdev.github.io/recordit](https://dotnetappdev.github.io/recordit)**

- [Features](https://dotnetappdev.github.io/recordit/features.html)
- [Installation Guide](https://dotnetappdev.github.io/recordit/installation.html)
- [Developer Docs](https://dotnetappdev.github.io/recordit/docs.html)

---

## 🤝 Contributing

1. Fork & create a branch: `git checkout -b feature/xyz`
2. Commit: `git commit -m 'feat: description'`
3. Push & open a PR

**Guidelines:** Maintain UI parity between both apps and follow Alvonia UI design tokens.

---

## 🏢 Tech Stack

| | Technology |
|-|------------|
| **Windows App** | WinUI 3, Windows App SDK, C#, MSIX |
| **Cross-Platform App** | Electron, React, TypeScript |
| **Design System** | Alvonia UI (shared CSS/XAML tokens) |
| **Icons** | Lucide React, Segoe MDL2 Assets |
| **CI/CD** | GitHub Actions |
| **Docs** | GitHub Pages (static HTML) |

---

<p align="center">Made with ❤️ by <strong>Alvonia</strong></p>
