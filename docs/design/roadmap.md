# VoiceStudio Development Roadmap

> **Classification**: Vision/Spec — architectural and phase guidance. **For active execution, see [.cursor/STATE.md](../../.cursor/STATE.md) and active hardening plans.**

---

## ✅ MainWindow Shell Complete

The complete MainWindow.xaml skeleton is now implemented with:
- Top command deck (MenuBar + Toolbar)
- Left navigation rail (8 toggle buttons)
- 4-region panel hosts (Left, Center, Right, Bottom)
- Status bar with 3-column layout
- All structural elements in place

See [MAINWINDOW_STRUCTURE.md](MAINWINDOW_STRUCTURE.md) for details.

---

## Architecture Overview

```
[ Figma / MCP servers / AI engines ]
         ↓   (design tokens, model calls)

[ Backend service layer (Python/Node) ]
         ↓   (REST or WebSocket)

[ Native frontend (WinUI3, Qt, SwiftUI) ]
```

## Phase 0: Foundation & Tech Stack

### Frontend: WinUI 3 Application
- **Technology**: C# / .NET 8 + WinUI 3 (Windows App SDK)
- **Pattern**: MVVM (Model-View-ViewModel)
- **Project**: VoiceStudio.App (WinUI 3 desktop app, UI only)

### Backend
- Python/Node backend service layer
- Communication via REST/WebSocket
- **Note**: Backend implementation out of scope for initial frontend spec

## Phase 1: App Shell & Top Command Deck

### 1.1 Window & Root
- **MainWindow.xaml**: Root `<Window>` with dark gradient background
- **Root Grid**: 3 rows
  - Row 0: Top Command Deck (Auto height)
  - Row 1: Main Workspace (*)
  - Row 2: Status Bar (Auto height)

### 1.2 Top Command Deck (Row 0)
- **Total Height**: ~80px
- **MenuBar** (32px): Standard WinUI MenuBar
  - Menus: File, Edit, View, Modules, Playback, Tools, AI, Help
- **Command Toolbar** (48px): StackPanel/CommandBar
  - **Transport**: Play / Pause / Stop / Record / Loop
  - **Project**: Project name, workspace dropdown
  - **Engine**: Model selector (XTTS / RVC / etc.) - ComboBox
  - **Performance HUD**: GPU/CPU/Latency indicators (ProgressBars + TextBlocks)
  - **Undo/Redo** + "History" button

### 1.3 Design Tokens
- **DesignTokens.xaml**: ResourceDictionary merged into App.xaml
- **Colors**: Background (Darker/Dark), Accents (Cyan/Lime/Magenta), Text (Primary/Secondary), Border, Warn, Error
- **Brushes**: Window background gradient, text brushes, accent brushes, panel border brush
- **Typography**: Inter (or Segoe UI fallback)
  - Caption: 10px
  - Body: 12px
  - Title: 16px
  - Heading: 20px
- **Corner Radius**: Panel (8px), Button (4px)
- **Animation Durations**: Fast (100ms), Medium (150ms)
- **Future**: Acrylic/Mica brushes using WinUI 3 BackdropMaterial for glassmorphism

### 1.4 Main Workspace Layout (Row 1)
- **3×2 Grid Layout**:
  - **Columns**: Left dock (20%), Center (55%), Right dock (25%)
  - **Rows**: Top main band (*), Bottom deck (18% height)
- **Layout Distribution**:
  - Left: Navigation + side stack (~18-20% width)
  - Center: Production area (~55-60%)
  - Right: Rack (~22-25%)
  - Bottom: Deck across full width (~15% height)

### Implementation Goals

#### 1. App Shell
- Window chrome
- Menu system
- Navigation
- Status bar

#### 2. Main Layout
- 3×2 grid layout system

#### 3. Panel System
- Dockable panel host controls
- Panel management infrastructure

#### 4. Initial Panels
- ProfilesPanel
- TimelinePanel
- EffectsMixerPanel
- AnalyzerPanel
- MacroPanel
- DiagnosticsPanel

#### 5. Extensibility
- Additional panels (transcribe, training, etc.) pluggable into the system

## Phase 2: Panel System & Primary Panels

### 3.2 Dockable Panel Hosts
- **PanelHost UserControl**: Reusable control to host each panel stack
  - Title area (icon + label + optional tabs)
  - Content area (ContentPresenter)
  - Header buttons: pop-out, collapse, options
  - Future: Support for tabbed stacks

### 4. Primary Panel Families

#### 4.1 Navigation + Profiles/Library (Left)
- **ProfilesPanel**: 
  - Tabs: Profiles, Library
  - Profiles: Avatar grid (uniform grid of profile cards)
  - Library: TreeView for folders + ListView for items
  - Future: Voice Profiles, Asset Library, Batch presets, Model templates

#### 4.2 TimelinePanel (Center)
- Timeline toolbar (track controls, zoom, grid settings)
- Multi-track waveform/spectrogram control area
- Bottom: spectrogram/visualizer area

#### 4.3 EffectsMixerPanel (Right)
- Upper half: Mixer (vertical faders)
- Lower half: Node view or FX chain list

#### 4.4 AnalyzerPanel (Right, alternative view or tabs)
- Tabs: Waveform, Spectral, Radar, Loudness, Phase
- Each tab: placeholder chart (to be implemented with Win2D/chart library)

#### 4.5 MacroPanel (Bottom)
- Node graph canvas (placeholder nodes for now)
- Header row for mode tabs: Macros, Automation

#### 4.6 DiagnosticsPanel (Bottom alt / tab)
- ListView for log entries
- Small charts for performance metrics

### 5. Panel Host & Panel Registry

#### 5.1 Interface
- **IPanelView**: Interface for all panels
  - `PanelId`: Unique identifier
  - `DisplayName`: Display name
  - `Region`: PanelRegion enum (Left, Center, Right, Bottom, Floating)

#### 5.2 Registry
- **IPanelRegistry**: Interface for panel registration
  - `GetPanelsForRegion(PanelRegion region)`: Get all panels for a region
  - `GetDefaultPanel(PanelRegion region)`: Get default panel for a region
  - `RegisterPanel(IPanelView panel)`: Register a panel

#### 5.3 Default Panels
At startup, registered:
- ProfilesView for Left (default)
- TimelineView for Center (default)
- EffectsMixerView for Right (default)
- MacroView for Bottom (default)
- Analyzer, Diagnostics (additional panels)

#### 5.4 PanelHost ViewModel Wiring
- PanelHost gets assigned a ViewModel that points it to the active panel
- Content property bound to panel content
- Title and IconGlyph properties for customization

## Phase 3: Interactions & Micro-Behaviors

### 6.1 Hover/Focus States
- **VSQ.Button.Style**: Standard button style
  - Normal: dark background, no border
  - Hover: slightly lighter + cyan border
  - Pressed: darker background + inner shadow look
- **VSQ.Button.NavToggle**: Toggle button style for navigation
  - Active item uses cyan accent
  - Hover and pressed states

### 6.2 Panel Header Controls
Each PanelHost header bar includes:
- **Title**: Bound to panel name
- **Icons**:
  - Pop-out (future; stub implemented)
  - Collapse (minimize height - implemented)
  - Options (opens small flyout - implemented)

### 6.3 Docking (Phase 1)
- Static grid positions for now
- PanelHost designed for future drag-docking
- Region assignment via PanelRegion enum
- Separate UserControls for each panel host

### 6.4 Navigation Sidebar
- Vertical navigation sidebar (48px width) in left dock
- Toggle buttons for:
  - Profiles (default, active)
  - Library
  - Batch (placeholder)
- Navigation buttons switch left panel content dynamically
- Active item uses cyan accent via VSQ.Button.NavToggle style
- LibraryPanel created as placeholder view

## Phase 4: Status Bar

### 7. Status Bar (Row 2)
**StatusBarView.xaml**: 3-column layout
- **Left Column** (*): Status text
- **Center Column** (2*): Active job + progress bar
- **Right Column** (*): CPU/GPU/RAM mini meters + clock
- Simple ProgressBars for mini meters (data wired later)
- Clock updates every minute

## Project Structure

```
VoiceStudio.App/
├── Models/              # Data models
│   ├── PanelRegion.cs   # Panel region enum
│   └── IPanelView.cs    # Panel interface
├── ViewModels/          # MVVM ViewModels
│   └── PanelViewModels.cs  # Panel view models
├── Views/               # XAML views
│   ├── Shell/          # App shell components
│   │   ├── MainWindow.xaml
│   │   └── StatusBarView.xaml
│   ├── Panels/          # Panel views
│   │   ├── ProfilesPanel.xaml
│   │   ├── LibraryPanel.xaml
│   │   ├── TimelinePanel.xaml
│   │   ├── EffectsMixerPanel.xaml
│   │   ├── AnalyzerPanel.xaml
│   │   ├── MacroPanel.xaml
│   │   └── DiagnosticsPanel.xaml
│   └── Controls/        # Custom controls
│       └── PanelHost.xaml
├── Services/            # Services (API clients, etc.)
│   ├── IPanelRegistry.cs
│   ├── PanelRegistry.cs
│   └── PanelService.cs
├── Converters/          # Value converters
├── Resources/           # Resources, styles, themes
│   └── DesignTokens.xaml
└── App.xaml             # Application entry point
```

