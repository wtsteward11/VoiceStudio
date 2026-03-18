# MainWindow.xaml Structure

> **Classification**: Reference — structure documentation. **For active execution, see [.cursor/STATE.md](../../.cursor/STATE.md).**

---

## Complete Shell Implementation

The MainWindow.xaml provides a complete, production-ready shell with:

### 1. Top Command Deck (Row 0)

#### MenuBar
- **File**: New Project, Open, Save, Save As, Exit
- **Edit**: Undo, Redo, Cut, Copy, Paste
- **View**: Reset Layout, Toggle Fullscreen Timeline
- **Modules**: Studio, Profiles, Library, Effects, Train, Analyze
- **Playback**: Play, Stop, Record
- **Tools**: Macros, Batch Processor
- **AI**: Suggest Chain, Analyze Voice
- **Help**: Documentation, About

#### Command Toolbar (48px height)
- **Column 0**: Transport controls (Play, Pause, Stop, Record, Loop)
- **Column 1**: Project name + Engine selector
- **Column 2**: Undo/Redo + Workspace dropdown
- **Column 3**: Performance HUD (CPU, GPU, Latency progress bars)

### 2. Main Workspace (Row 1)

#### Grid Structure
- **4 Columns**: Nav rail (64px) + Left (20%) + Center (55%) + Right (25%)
- **2 Rows**: Top band (*) + Bottom deck (18%)

#### Left Navigation Rail
- Vertical stack of toggle buttons
- Icons: Studio, Profiles, Library, Effects, Train, Analyze, Settings, Logs
- Background: #141820
- Spans both rows (full height)

#### Panel Hosts
- **LeftPanelHost** (Row 0, Column 1): Profiles/Library/etc
- **CenterPanelHost** (Row 0, Column 2): Timeline
- **RightPanelHost** (Row 0, Column 3): Mixer/Analyzer
- **BottomPanelHost** (Row 1, Columns 0-3): Macros/Diagnostics

### 3. Status Bar (Row 2)

#### 3-Column Layout
- **Left Column** (*): Status text ("Ready")
- **Center Column** (2*): Job progress (Job name + progress bar)
- **Right Column** (*): Mini meters (CPU, GPU, RAM percentages) + Clock

## Key Features

1. **Complete Menu System**: All major functions accessible
2. **Transport Controls**: Playback and recording controls
3. **Project Management**: Project name and engine selection
4. **Workspace Switching**: Multiple workspace modes
5. **Performance Monitoring**: Real-time CPU/GPU/Latency meters
6. **Navigation Rail**: Quick access to all major modules
7. **Panel Hosts**: All 4 regions ready for panel content
8. **Status Bar**: Job tracking and system metrics

## Panel Host Assignment

The MainWindow provides named PanelHost controls ready for content assignment:

```csharp
// In MainWindow.xaml.cs or ViewModel
LeftPanelHost.Content = new ProfilesView();
CenterPanelHost.Content = new TimelineView();
RightPanelHost.Content = new EffectsMixerView();
BottomPanelHost.Content = new MacroView();
```

## Design Tokens Used

- `VSQ.Window.Background` - Window background gradient
- `VSQ.Text.Body` - Standard text style
- `VSQ.Panel.BorderBrush` - Panel borders

## Dimensions

- **Window**: 1600×900 (default)
- **Nav Rail**: 64px width
- **Command Toolbar**: 48px height
- **Status Bar**: 26px height
- **Panel Margins**: 4-8px spacing

## Next Steps (Historical — Implementation Guidance)

*For current project next steps, see [.cursor/STATE.md](../../.cursor/STATE.md).*

1. Wire up navigation rail buttons to switch panel content
2. Connect transport controls to playback engine
3. Implement workspace switching logic
4. Add panel content to PanelHost controls
5. Connect status bar to backend services

