# RecordIt

> Professional Screen Recording Studio & Whiteboard App — powered by **Alvonia UI**

[![GitHub Pages](https://img.shields.io/badge/docs-GitHub%20Pages-blue)](https://dotnetappdev.github.io/recordit)
[![WinUI 3](https://img.shields.io/badge/WinUI-3.0-indigo)](winui/)
[![.NET](https://img.shields.io/badge/.NET-10-purple)](winui/)
[![Avalonia](https://img.shields.io/badge/Avalonia-11-teal)](RecordIt.Avalonia/)
[![Electron](https://img.shields.io/badge/Electron-26-cyan)](alvonia-ui/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Build](https://img.shields.io/github/actions/workflow/status/dotnetappdev/recordit/build.yml)](https://github.com/dotnetappdev/recordit/actions)

RecordIt is a **studio-grade screen recording app** with an OBS-inspired dockable multi-panel interface, built on **Alvonia UI** — our own modern Windows 11 / Fluent Design design system. It combines a professional recording studio layout with an **MS Teams-style collaborative whiteboard** and ships as three apps that all look and feel identical:

- **RecordIt WinUI** — Native Windows app (WinUI 3 / Windows App SDK, .NET 10, C#)
- **RecordIt Avalonia** — Cross-platform .NET app (Avalonia UI 11, C#) for Windows, macOS, Linux
- **RecordIt Electron** — Web-tech cross-platform app (Electron + React + TypeScript)

---

## ✨ Features

### 🎛️ Studio Interface (OBS-inspired, Alvonia-styled)
- **5 dockable panels** — Scenes, Sources, Audio Mixer, Options, Controls
- **Full menu bar**: File / Edit / View / Docks / Tools / Help with panel toggle support
- **Scene management**: multiple scenes with reordering and quick-switch bar on the preview
- **Source list**: add/remove/reorder capture sources with type icons (screen, window, camera, audio)
- **Add Source dialog** enumerates all capture types + all live audio sources including desktop loopback
- Panel visibility toggled per-panel from the View menu
- Modern Windows 11 Fluent Design aesthetic — rounded corners, layered surfaces, indigo accent

### 🔴 Screen Recording
- **4K/1080p/720p** recording at 15–60 fps
- **Multiple capture sources**: full screen, all displays, specific monitor, application window
- **Audio capture**: desktop loopback (WASAPI), microphone/line-in with per-device selection
- **Webcam overlay** (picture-in-picture) during recording, selectable device
- **Output formats**: MP4 (H.264 / libx264), WebM (VP9)
- ffmpeg backend with correct audio stream mapping (fixed multi-input index bug)
- Countdown timer before recording starts
- System tray integration and global hotkeys

### 🎚️ Audio Mixer
- **Real-time VU meters** — green→yellow→red gradient per channel, 50 ms refresh via Core Audio
- **Per-channel volume fader** (0–100%, dB readout)
- **Per-channel mute button**
- **Desktop Audio** channel — peak via `IAudioMeterInformation` on the default render endpoint
- **Mic/Aux** channel — peak via `IAudioMeterInformation` on the default capture endpoint
- Additional audio channels added dynamically from detected dshow devices
- All real audio devices enumerated separately (video vs audio) — no more mixed device lists

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

## 🏗️ Architecture: Three Apps, One UI

RecordIt is built with the **Alvonia UI** design system ensuring identical look and feel across all implementations:

```
recordit/
├── winui/                   # 🪟 WinUI 3 / .NET 10 (Windows-native)
│   ├── RecordIt/
│   │   ├── MainWindow.xaml(.cs)
│   │   ├── Pages/
│   │   │   ├── RecordPage.xaml(.cs)    # OBS-style studio interface
│   │   │   ├── WhiteboardPage.xaml(.cs)
│   │   │   ├── LibraryPage.xaml(.cs)
│   │   │   └── SettingsPage.xaml(.cs)
│   │   ├── Services/
│   │   │   └── AudioMeterService.cs    # Core Audio COM interop (VU metering)
│   │   └── Styles/
│   │       └── AlvoniaTheme.xaml       # XAML design tokens + styles
│   └── RecordIt.Package/               # MSIX packaging
│
├── RecordIt.Avalonia/       # 🖥️ Avalonia UI / .NET (cross-platform)
│   └── ...                             # Mirrors WinUI structure
│
├── RecordIt.Core/           # 📦 Shared .NET core library
│   ├── Services/
│   │   ├── ScreenRecordingService.cs   # ffmpeg backend (fixed stream index bug)
│   │   ├── CaptionService.cs
│   │   └── ExportService.cs
│   └── Models/
│
├── alvonia-ui/              # 🌐 Electron + React (cross-platform)
│   ├── electron/
│   │   ├── main.js
│   │   └── preload.js
│   ├── src/
│   │   ├── components/      # Recording, Whiteboard, Library, Settings pages
│   │   └── styles/
│   │       └── global.css   # Alvonia UI CSS design tokens
│   └── package.json
│
├── docs/                    # 📄 GitHub Pages static site
│   ├── index.html
│   ├── features.html
│   ├── installation.html
│   ├── docs.html
│   └── assets/
│
├── tools/
│   └── ffmpeg/              # 🔧 Drop ffmpeg.exe here for local dev; CI downloads automatically
│
├── installers/
│   └── RecordIt.Avalonia.iss # 📦 Inno Setup script for Avalonia Windows installer
│
└── .github/workflows/
    ├── docs.yml             # Auto-deploy to GitHub Pages
    └── build.yml            # Build MSIX + Inno Setup installers (FFmpeg bundled)
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

All Windows installers include **FFmpeg bundled** — no separate FFmpeg install needed.

Download from [GitHub Releases](https://github.com/dotnetappdev/recordit/releases/latest):

| File | App | Platform |
|------|-----|----------|
| `RecordIt.msix` | WinUI 3 (native) | Windows 10/11 |
| `RecordIt-Avalonia-*-Setup.exe` | Avalonia (.NET cross-platform) | Windows 10/11 |
| `RecordIt-Setup-*.exe` | Electron | Windows |
| `RecordIt-*.dmg` | Electron | macOS |
| `RecordIt-*.AppImage` | Electron | Linux |

### Building the installers locally

**WinUI 3 MSIX** — requires Visual Studio 2022 with Windows App SDK workload:
```powershell
# 1. Place ffmpeg.exe in tools\ffmpeg\  (downloaded by CI automatically)
winget install Gyan.FFmpeg   # then copy ffmpeg.exe to tools\ffmpeg\

# 2. Build MSIX
cd winui
msbuild RecordIt.sln /p:Configuration=Release /p:Platform=x64 /p:AppxBundle=Never /p:UapAppxPackageBuildMode=SideloadOnly
```

**Avalonia NSIS installer** — requires .NET 10 SDK + [Inno Setup 6](https://jrsoftware.org/isdl.php):
```powershell
# 1. Place ffmpeg.exe in tools\ffmpeg\  (same binary as above)

# 2. Publish self-contained
dotnet publish RecordIt.Avalonia/RecordIt.Avalonia.csproj -c Release -r win-x64 --self-contained true

# 3. Build installer
iscc installers\RecordIt.Avalonia.iss
# Output: installers\output\RecordIt-Avalonia-*-Setup.exe
```

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
