# VoiceStudio Complete Phase Roadmap
## From Shell to 100% Studio Functionality

> **Classification**: Vision/Spec — phase and task guidance. **For active execution, see [.cursor/STATE.md](../../.cursor/STATE.md) and active hardening plans.**

---

## 🚦 Global Guardrails (Remind Cursor Constantly)

**Put this near the top of any big instruction you give Cursor:**

```
Do NOT simplify the UI layout or collapse panels.

Keep the 3-column + nav + bottom deck layout and PanelHost controls.

Do NOT merge Views and ViewModels. Each panel = .xaml + .xaml.cs + ViewModel.cs.

Do NOT remove placeholder areas (waveform, spectrogram, analyzers, macros, logs). 
Those are future advanced controls, not decoration.

Use DesignTokens.xaml for all colors/typography; no hardcoded one-off values unless clearly temporary.

Treat this as a professional DAW-grade app, not a sample or tutorial.
```

**You already saw how Cursor will happily "optimize away" the complexity; these rules are your seatbelt.**

---

## Phase 1 – Shell & Layout (UI Skeleton) ✅

**Status:** Complete

**What You Have:**
- WinUI 3 app, DesignTokens.xaml, PanelHost, MainWindow with:
  - Top Menu + Command Deck
  - Left nav rail
  - 3 panel hosts (Left/Center/Right)
  - Bottom deck host
  - Status bar
- Panel skeletons:
  - ProfilesView, TimelineView, EffectsMixerView, AnalyzerView, MacroView, DiagnosticsView

**Overseer Job Now:**
Make sure Cursor actually finishes Phase 1 exactly as in UI_IMPLEMENTATION_SPEC.md before touching anything below.

---

## Phase 2 – Styling, Micro-Interactions, and Nav Behavior

### Goal

Make the shell feel like a premium app, not a demo. Basic interactivity, hover, selection, and nav-driven panel swapping.

### Build in This Phase

#### 2.1 Centralized Styles

**Create:**
- `Resources/Styles/Controls.xaml`
- `Resources/Styles/Text.xaml`
- `Resources/Styles/Panels.xaml`

**Move button, text, and panel styling there:**
- Nav icon buttons: cyan glow on active, subtle hover, rounded
- Panel headers: consistent background, typography, icons

#### 2.2 NavIconButton Control

**Replace raw ToggleButton in nav rail with NavIconButton:**

**Dependency Properties:**
- `Icon` (string)
- `Tooltip` (string)
- `IsSelected` (bool)

**Template:**
- Circular/rounded square
- Neon border on active (cyan glow)
- Subtle hover state

**One NavIconButton per module:**
- Studio / Profiles / Library / Effects / Train / Analyze / Settings / Logs

#### 2.3 Panel Swapping on Nav Click

**For now:** Clicking nav buttons only affects the LeftPanelHost:
- Profiles → ProfilesView
- Library → LibraryView (temporary stub)

**Later:** More complex per-region switching

#### 2.4 Micro-Interactions

**Use VisualStateManager or Styles for:**
- Button hover (border + background change)
- Panel header hover on action icons
- Keep animation durations from `VSQ.Animation.*`

### Overseer Instructions to Cursor

- Don't change MainWindow's layout grid
- Do not add more "helper windows"; everything stays in the defined shell
- Implement NavIconButton as a reusable control; do not restyle each nav button separately
- Apply styles via StaticResource from Controls.xaml, not inline

### Deep Research?

**Not needed yet.** This is pure WinUI 3 styling.

---

## Phase 3 – Docking & Layout Persistence (Internal)

### Goal

Start evolving towards the "Adobe/Resolve-style" control surface: panels can be swapped, and layout saved/restored. No fancy drag-docking yet, just logical docking and persistence.

### Build in This Phase

#### 3.1 PanelRegistry Implemented

**Fill PanelRegistry with real entries:**

```csharp
_panels.Add(new PanelDescriptor {
    PanelId = "profiles",
    DisplayName = "Profiles",
    Region = PanelRegion.Left,
    ViewType = typeof(ProfilesView),
    ViewModelType = typeof(ProfilesViewModel)
});
```

**Add entries for:**
- timeline, mixer, analyzer, macro, diagnostics, etc.

#### 3.2 Bootstrap Defaults

**On MainWindow load, use PanelRegistry.GetDefaultPanel(region) to create and assign views to hosts:**
- Left → Profiles
- Center → Timeline
- Right → EffectsMixer (or Analyzer)
- Bottom → Macro

#### 3.3 Layout Persistence

**Create a simple LayoutState model (C#) mirroring the JSON schema:**

```csharp
public class LayoutState
{
    public string Version { get; set; }
    public List<RegionState> Regions { get; set; }
}

public class RegionState
{
    public PanelRegion Region { get; set; }
    public string ActivePanelId { get; set; }
    public List<string> OpenedPanels { get; set; }
}
```

**Save layout to JSON file:**
- Location: `%AppData%\VoiceStudio\layout.json`
- Save when app exits
- Restore layout at startup

#### 3.4 Nav Integration w/ PanelRegistry

**Nav buttons now call PanelRegistry to switch active panel in region** instead of directly instantiating views in MainWindow.xaml.cs.

### Overseer Instructions

- Do not introduce drag-docking yet; this phase is about logical panel switching + persistence, not full docking UI
- Keep PanelHost as the only container; PanelRegistry controls which panel goes inside

### Deep Research?

**Not required.** This is all custom logic.

---

## Phase 4 – Project, Asset, and Profile Data Model

### Goal

Give the UI real data instead of static placeholders: projects, clips, profiles, assets.

### Build in This Phase

#### 4.1 Core Models (VoiceStudio.Core.Models)

**Project:**
```csharp
public class Project
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int SampleRate { get; set; }
    public int BitDepth { get; set; }
    public List<Track> Tracks { get; set; }
    // etc.
}
```

**Track:**
```csharp
public class Track
{
    public string Id { get; set; }
    public string Name { get; set; }
    public TrackType Type { get; set; } // Audio/TTS/FX
    public List<Clip> Clips { get; set; }
}
```

**Clip:**
```csharp
public class Clip
{
    public string Id { get; set; }
    public string FilePath { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan Duration { get; set; }
}
```

**VoiceProfile:**
```csharp
public class VoiceProfile
{
    public string Id { get; set; }
    public string Name { get; set; }
    public List<string> Tags { get; set; }
    public string Language { get; set; }
    public string Emotion { get; set; }
    public double QualityScore { get; set; }
    public string AvatarPath { get; set; }
}
```

**LibraryAsset:**
```csharp
public class LibraryAsset
{
    public string Id { get; set; }
    public string FilePath { get; set; }
    public AssetType Type { get; set; }
    public List<string> Tags { get; set; }
    public TimeSpan Duration { get; set; }
}
```

#### 4.2 Basic Local Persistence

**Serialize/deserialize Project to/from `.voiceproj` JSON file:**
- File → New/Open/Save should operate on that
- Use System.Text.Json or Newtonsoft.Json

#### 4.3 Bind Panels to Models

**ProfilesView:**
- Bind to `ObservableCollection<VoiceProfile>`
- Display real profile data

**TimelineView:**
- Bind to `ObservableCollection<Track>`
- Display real track data

**DiagnosticsView:**
- Display real log entries from a simple logging service (in-memory + file)

### Overseer Instructions

- Focus only on local model and serialization, not backend/MCP yet
- Panels must start using real ViewModels & models, no more pure dummy text

### Deep Research?

**Not needed.** This is straightforward C# modeling.

---

## Phase 5 – Backend API Skeleton (Python/Node) + C# Client

### Goal

Introduce a real backend API boundary so MCP & models can live behind it later. UI still mostly uses local data, but the pipe is open.

### Build in This Phase

#### 5.1 Backend Project (folder backend/api)

**Python FastAPI recommended for your audio/ML stack:**

**Endpoints:**
- `/api/health` – returns status
- `/api/projects/open`, `/api/projects/save` – stub
- `/api/voices/analyze` – stub that returns sample JSON with metrics

**Example FastAPI structure:**
```python
from fastapi import FastAPI

app = FastAPI()

@app.get("/api/health")
async def health():
    return {"status": "ok"}

@app.post("/api/voices/analyze")
async def analyze_voice(request: AnalyzeVoiceRequest):
    # Stub implementation
    return {"lufs": -14.5, "snr": 42.0}
```

#### 5.2 C# Backend Client (IBackendClient)

**Methods like:**
```csharp
Task<bool> CheckHealthAsync()
Task<AnalyzeVoiceResult> AnalyzeVoiceAsync(AnalyzeVoiceRequest request)
```

**Implementation:**
- `BackendClient` using `HttpClient` to `http://localhost:PORT`
- Error handling and retries
- Configuration via `BackendClientConfig`

#### 5.3 Wire DiagnosticsView to Backend Health

**Show status indicator:**
- API up/down
- Poll `/health` every N seconds
- Display in DiagnosticsView

### Overseer Instructions

- Treat the backend as local microservice, not optional fluff
- Implement interfaces in Core, implementation in App, so we can swap later

### Deep Research?

**Optional, but this is a decent moment if you want best practices:**

**Deep Research Prompt:**
```
Research best practices and code examples for a WinUI 3 (.NET 8) desktop app 
communicating with a local Python FastAPI backend over HTTP and optionally WebSockets. 
Focus on: robust HttpClient usage, error handling, and patterns for auto-reconnect 
to a local service. Provide minimal example code in C# and Python.
```

---

## Phase 6 – MCP Bridge Layer & Operations

### Goal

Allow the backend to call your MCP servers via a unified bridge, and expose those features as clean endpoints for the UI.

### Build in This Phase

#### 6.1 backend/mcp_bridge Module

**Functions like:**
```python
def run_mcp_operation(operation: str, payload: dict) -> dict:
    # Calls MCP server based on operation
    # Returns normalized response
```

**For example:**
- `operation="analyze_voice"` → calls TTS/analysis MCP server
- `operation="suggest_effect_chain"` → calls chain suggestion MCP

#### 6.2 Map MCP Operations to API Endpoints

**Backend API endpoints:**
- `/api/voices/analyze` calls MCP `analyze_voice`
- `/api/chains/suggest` calls MCP `suggest_effect_chain`

**Structure inputs/outputs** according to the JSON schemas we defined in `shared/contracts/`.

#### 6.3 Wire AnalyzerView to Real Analysis Data

**Fetch metrics:**
- LUFS, SNR, similarity, etc. from backend
- Show in text fields or simple list
- Text only initially (charts come in Phase 7)

### Overseer Instructions

- Keep the MCP integration inside the backend, not in the UI
- UI must only talk to the backend API client (IBackendClient), never directly to MCPs

### Deep Research?

**Only if you want specific MCP examples;** otherwise your existing MCP docs are enough.

---

## Phase 7 – Audio Engines & I/O Integration

### Goal

Make the app actually generate, process, and play audio: TTS, voice cloning, playback, basic routing.

### Build in This Phase

#### 7.1 Choose Audio Pipeline (Python Side)

**Use your existing engines:**
- XTTS / RVC / Whisper etc.

**Expose operations:**
- `/api/audio/synthesize`
- `/api/audio/clone`
- `/api/audio/playback` (maybe just returns a file path or stream)

#### 7.2 UI Hooks

**Timeline:**
- "Play"/"Stop" call backend to start/stop playback of current project or region

**Profiles:**
- "Preview" button plays a sample of that voice

**Macro:**
- Add stub macro "Render current selection with Voice X"

#### 7.3 Audio Backend on Windows

**The actual playback can be done:**
- In Python (PortAudio), or
- In C# (NAudio / WASAPI) once you have final audio buffers/paths from backend

### Overseer Instructions

- Do not try to implement full DAW-level editing yet
- Focus on: project → backend → audio out, end-to-end, even if crude

### Deep Research?

**This is another good Deep Research hook (audio libs):**

**Deep Research Prompt:**
```
Research recommended approaches for audio playback and low-latency audio routing 
for a WinUI 3 (.NET 8) application on Windows, including whether to use NAudio, 
WASAPI directly, or a Python-based audio playback service (e.g., PortAudio) with 
a local API. Provide trade-offs and minimal code snippets.
```

---

## Phase 8 – Visuals: Waveforms, Spectrograms, Meters & Analyzers

### Goal

Replace placeholders with real-time or near-real-time visuals that match the concept UI.

### Build in This Phase

#### 8.1 Custom Waveform Control

**Implement a WaveformControl using:**
- Win2D, or
- A drawing surface in WinUI 3

**TimelineView uses this control for each track lane:**
- Display audio waveform for each clip
- Support zoom and pan
- Show playhead position

#### 8.2 Spectrogram/Visualizer

**Implement SpectrogramControl that renders FFT data:**
- Real-time spectrogram rendering
- If real-time is too heavy initially, start with offline precomputed spectrogram images
- Display in TimelineView bottom visualizer area

#### 8.3 Meters & Analyzers

**VU meters on mixer channels:**
- Real-time level meters in EffectsMixerView
- Peak and RMS indicators
- Visual feedback for each channel

**LUFS/loudness graph in AnalyzerView:**
- Display loudness over time
- Show LUFS values
- Visual representation of audio characteristics

#### 8.4 Backend Data

**Backend should provide downsampled PCM/FFT data** so visuals don't compute everything in UI:
- `/api/audio/waveform` – returns downsampled waveform data
- `/api/audio/spectrogram` – returns FFT data or precomputed spectrogram
- `/api/audio/meters` – returns real-time meter readings

### Overseer Instructions

- Replace placeholder rectangles for waveforms and analyzers with real custom controls, not just nicer rectangles
- Make sure visuals are bound to real audio/analysis data, even if refresh rate is modest
- Use Win2D or equivalent for performance; avoid CPU-heavy rendering

### Deep Research?

**Yes, recommended here for Win2D & FFT:**

**Deep Research Prompt:**
```
Research examples and best practices for implementing real-time audio waveforms, 
spectrograms, and level meters in a WinUI 3 app, using Win2D or other compatible 
drawing libraries. Include guidance on:

1. Win2D integration with WinUI 3
2. Performance optimization for real-time rendering
3. Downsampling strategies for large audio files
4. How to feed FFT data from a backend into visuals
5. Best practices for:
   - Waveform rendering (peak/RMS)
   - Spectrogram rendering (FFT visualization)
   - Real-time meter updates
   - Memory management for large audio buffers

Provide minimal working code examples for:
- Win2D CanvasControl for waveform rendering
- FFT data visualization
- Real-time meter updates
```

---

## Phase 9 – Macros, Automation & Scripting

### Goal

Turn the MacroView into a functional automation graph that can control the pipeline.

### Build in This Phase

#### 9.1 Macro Data Model

**Nodes:**
- Source (audio input, TTS input, etc.)
- Processor (effects, filters, etc.)
- Control (parameters, switches)
- Conditional (if/then logic)
- Output (render, export, etc.)

**Connections:**
- Edges between node IDs
- Data flow representation

**Macro Graph Model:**
```csharp
public class MacroGraph
{
    public string Id { get; set; }
    public string Name { get; set; }
    public List<MacroNode> Nodes { get; set; }
    public List<MacroConnection> Connections { get; set; }
}
```

#### 9.2 UI

**MacroView shows draggable nodes on a canvas:**
- Canvas-based node editor
- Simple node template: title, ports, delete button
- Drag nodes to position
- Connect nodes by dragging from output to input ports
- Zoom and pan support

#### 9.3 Execution

**Backend receives macro graph JSON and executes it:**
- `/api/macros/execute` – runs macro graph
- Start simple: run macros as sequences
- Then evolve to graph-based execution
- Support parameter passing between nodes

#### 9.4 Automation

**Automation curves:**
- Tie to track parameters (volume, pan, pitch, etc.)
- Allow recording parameter moves over time
- Display automation lanes in TimelineView
- Keyframe editing

### Overseer Instructions

- Do not reduce Macros to "a text box for commands". It must be a visual node editor that matches the layout
- Preserve bottom deck's split between Macros and Diagnostics
- Node editor must be functional, not just visual

### Deep Research?

**Optional. Good if you want references for node editors:**

**Deep Research Prompt:**
```
Research best practices and code examples for implementing a visual node-based editor 
in WinUI 3, including:

1. Canvas-based node positioning and dragging
2. Connection drawing between nodes
3. Port/connector system for node inputs/outputs
4. Zoom and pan functionality
5. Node selection and multi-selection
6. Serialization of node graphs to JSON

Provide minimal working examples for:
- Draggable nodes on Canvas
- Connection lines between nodes
- Port interaction (drag to connect)
- Graph serialization/deserialization
```

---

## Phase 10 – Advanced Modules, Training, Batch, QA & Packaging

### Goal

Reach the "100% studio" point: training workflows, batch jobs, transcribe, settings, and packaging into an installer.

### Build in This Phase

#### 10.1 Training Module

**New panels (reusing PanelHost):**

**Dataset Manager:**
- List of clips, transcripts, quality scores
- Upload/manage training datasets
- Preview clips and transcripts

**Training Monitor:**
- Logs, loss curves, ETA
- Real-time training progress
- Stop/pause training

**Backend endpoints:**
- `/api/train/start` – start training job
- `/api/train/status` – get training status
- `/api/train/cancel` – cancel training
- `/api/train/logs` – get training logs

#### 10.2 Batch Processor

**Panel with a job queue:**
- Inputs (files or project selections)
- Chosen voice profile
- Output path
- Operations: add, remove, start, pause, re-run
- Progress tracking per job

**Backend endpoints:**
- `/api/batch/add` – add job to queue
- `/api/batch/status` – get queue status
- `/api/batch/cancel` – cancel job

#### 10.3 Transcribe / Whisper

**Panel that:**
- Uploads an audio file
- Shows transcript
- Allows editing
- Binds transcript to a project
- Supports multiple languages

**Backend endpoints:**
- `/api/transcribe/upload` – upload audio for transcription
- `/api/transcribe/result` – get transcription result
- `/api/transcribe/languages` – get supported languages

#### 10.4 Settings & Logs

**Settings panel:**
- Audio device selection
- Engine preferences (XTTS/RVC/etc.)
- GPU/CPU preferences
- Backend connection settings
- Theme preferences

**Logs panel:**
- Filterable logs (info/warn/error)
- Export logs bundle
- Clear logs
- Real-time log streaming

#### 10.5 Installer & Packaging

**MSIX or traditional installer:**
- Package WinUI 3 app
- Include backend dependencies
- Register file associations:
  - `.voiceproj` → VoiceStudio
  - `.vprofile` → VoiceStudio
- App icon, Start Menu entry
- Auto-update support (optional)

### Overseer Instructions

- Do not "stub" these as empty pages. Each module must have real controls and bind to real backend endpoints, even if engine logic is basic at first
- Ensure consistent PanelHost usage and design tokens for all new panels
- All new panels follow the same structure: View.xaml, View.xaml.cs, ViewModel.cs

### Deep Research?

**Optional for packaging:**

**Deep Research Prompt:**
```
Research best practices for packaging a WinUI 3 (.NET 8) desktop application with 
a Python backend service for Windows distribution, including:

1. MSIX vs traditional installer trade-offs
2. Bundling Python runtime and dependencies
3. File association registration
4. Auto-start backend service
5. Update mechanisms

Provide guidance on:
- MSIX packaging tools
- Python bundling (PyInstaller, cx_Freeze, etc.)
- Installer creation (WiX, Inno Setup, etc.)
- File association setup
```

---

## Phase Summary

| Phase | Focus | Deep Research? |
|-------|-------|----------------|
| **Phase 1** | Shell & Layout | ✅ Complete |
| **Phase 2** | Styling & Nav | ❌ Not needed |
| **Phase 3** | Layout Persistence | ❌ Not needed |
| **Phase 4** | Data Models | ❌ Not needed |
| **Phase 5** | Backend API | ✅ Optional (HTTP/WebSocket patterns) |
| **Phase 6** | MCP Bridge | ❌ Not needed (unless MCP-specific) |
| **Phase 7** | Audio I/O | ✅ Recommended (audio library selection) |
| **Phase 8** | Visuals & Meters | ✅ Recommended (Win2D & FFT) |
| **Phase 9** | Macros & Automation | ⚠️ Optional (node editor patterns) |
| **Phase 10** | Advanced Modules & Packaging | ⚠️ Optional (packaging tools) |

---

## Implementation Strategy

### Sequential Phases
1. Complete Phase 1 fully before starting Phase 2
2. Each phase builds on the previous
3. Overseer verifies each phase before proceeding

### Parallel Work (Within Phases)
- Phase 2: Workers can work on styles, NavIconButton, and panel swapping in parallel
- Phase 4: Workers can implement different models in parallel
- Phase 5: Backend and C# client can be developed in parallel

### Deep Research Timing
- **Phase 5:** Good time for HTTP/WebSocket patterns
- **Phase 7:** Critical time for audio library selection
- **Phase 8:** Recommended for Win2D & FFT visualization
- **Phase 9:** Optional for node editor patterns
- **Phase 10:** Optional for packaging tools

---

## Success Criteria by Phase

### Phase 2
- [ ] NavIconButton control works
- [ ] Nav buttons switch panels
- [ ] Hover states work
- [ ] Styles are centralized

### Phase 3
- [ ] PanelRegistry has all panels registered
- [ ] Layout saves/restores
- [ ] Nav uses PanelRegistry

### Phase 4
- [ ] All models exist
- [ ] Project serialization works
- [ ] Panels display real data

### Phase 5
- [ ] Backend API runs
- [ ] C# client connects
- [ ] Health check works

### Phase 6
- [ ] MCP bridge functional
- [ ] Backend calls MCP servers
- [ ] AnalyzerView shows real data

### Phase 7
- [ ] Audio synthesis works
- [ ] Playback functional
- [ ] End-to-end audio flow

### Phase 8
- [ ] WaveformControl implemented
- [ ] SpectrogramControl functional
- [ ] VU meters working
- [ ] AnalyzerView shows real charts
- [ ] Backend provides visualization data

### Phase 9
- [ ] Macro node editor functional
- [ ] Nodes can be connected
- [ ] Macro execution works
- [ ] Automation curves editable
- [ ] Timeline shows automation lanes

### Phase 10
- [ ] Training module complete
- [ ] Batch processor functional
- [ ] Transcribe panel works
- [ ] Settings panel complete
- [ ] Logs panel functional
- [ ] Installer packages app
- [ ] File associations registered

---

## Next Steps

1. **Verify Phase 1 Complete** - Ensure all Phase 1 deliverables are done
2. **Choose Phase 2 Start** - Begin with styling and nav behavior
3. **Follow Phase-by-Phase** - Don't skip ahead
4. **Use Deep Research** - When recommended (Phase 5, Phase 7, Phase 8)

**This roadmap takes you from shell to full studio functionality in 10 phases.**

