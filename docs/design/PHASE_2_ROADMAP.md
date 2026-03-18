# VoiceStudio Phase 2 Roadmap

> **Classification**: Vision/Spec — phase 2 feature guidance. **For active execution, see [.cursor/STATE.md](../../.cursor/STATE.md) and active hardening plans.**

---

## Overview

Phase 1 delivers the complete UI shell and structure. Phase 2 adds the advanced features and polish.

## Phase 2A: Visuals & Rendering

### Goals
Transform placeholder regions into real audio visualizations.

### Tasks

#### 2A.1 Timeline Waveform Rendering
- **Technology:** Win2D or DirectX
- **Requirements:**
  - Custom waveform control
  - Real-time audio data rendering
  - Zoom and pan functionality
  - Multi-track waveform display
- **Deliverable:** Working waveform visualization in TimelineView

#### 2A.2 Spectrogram Visualization
- **Technology:** Win2D or DirectX
- **Requirements:**
  - FFT-based spectrogram rendering
  - Color mapping (frequency → color)
  - Real-time updates
  - Zoom and scroll
- **Deliverable:** Working spectrogram in TimelineView visualizer area

#### 2A.3 Analyzer Charts
- **Technology:** Win2D, LiveCharts, or custom rendering
- **Requirements:**
  - Waveform chart (time domain)
  - Spectral chart (frequency domain)
  - Radar chart (polar frequency response)
  - Loudness chart (LUFS over time)
  - Phase chart (phase relationships)
- **Deliverable:** Working charts in AnalyzerView tabs

#### 2A.4 "Orbs" Particle Visualizer
- **Technology:** Win2D or DirectX
- **Requirements:**
  - Particle system for audio visualization
  - Frequency-reactive particles
  - Smooth animations
- **Deliverable:** Optional visualizer in TimelineView

### Success Criteria
- [ ] Waveforms render in timeline
- [ ] Spectrogram displays real audio data
- [ ] All analyzer charts functional
- [ ] Real-time updates work smoothly
- [ ] Performance is acceptable (60fps target)

---

## Phase 2B: Docking & Layout

### Goals
Transform static PanelHost into draggable, dockable panels.

### Tasks

#### 2B.1 Drag-Dock Infrastructure
- **Requirements:**
  - Drag detection on PanelHost header
  - Drop zones for docking
  - Visual feedback during drag
  - Dock preview indicators
- **Deliverable:** PanelHost supports drag-dock

#### 2B.2 Resizable Panels
- **Requirements:**
  - Resize handles on panel borders
  - Minimum/maximum size constraints
  - Snap-to-grid option
- **Deliverable:** Panels can be resized

#### 2B.3 Floating Windows
- **Requirements:**
  - Panels can float in separate windows
  - Floating windows can be docked back
  - Window management
- **Deliverable:** Panels can float and re-dock

#### 2B.4 Layout Persistence
- **Requirements:**
  - Save panel positions and sizes
  - Restore layout on startup
  - Multiple layout presets
- **Deliverable:** Layout state saved/loaded

### Success Criteria
- [ ] Panels can be dragged and docked
- [ ] Panels can be resized
- [ ] Panels can float in windows
- [ ] Layout persists across sessions

---

## Phase 2C: Panel Registry & Navigation

### Goals
Wire PanelRegistry to navigation and enable dynamic panel switching.

### Tasks

#### 2C.1 Panel Registry Implementation
- **Requirements:**
  - Register all panels in PanelRegistry
  - Panel discovery system
  - Region-based panel lookup
- **Deliverable:** PanelRegistry fully functional

#### 2C.2 Navigation Wiring
- **Requirements:**
  - Nav rail buttons switch panels via registry
  - Active panel indication
  - Panel history/back navigation
- **Deliverable:** Navigation uses PanelRegistry

#### 2C.3 Panel State Management
- **Requirements:**
  - Save panel state (selected items, scroll position)
  - Restore panel state on switch
  - Panel-specific settings
- **Deliverable:** Panel state persists

#### 2C.4 Workspace Presets
- **Requirements:**
  - Define workspace configurations
  - Switch between workspaces
  - Custom workspace creation
- **Deliverable:** Workspace selector functional

### Success Criteria
- [ ] PanelRegistry manages all panels
- [ ] Navigation switches panels dynamically
- [ ] Panel state is preserved
- [ ] Workspace switching works

---

## Phase 2D: Animations & Polish

### Goals
Add micro-interactions and visual polish.

### Tasks

#### 2D.1 Hover/Press Animations
- **Requirements:**
  - Button hover states with transitions
  - Press animations
  - Smooth color transitions
- **Deliverable:** All buttons have smooth animations

#### 2D.2 Glassmorphism Effects
- **Technology:** WinUI 3 BackdropMaterial
- **Requirements:**
  - Mica/Acrylic backgrounds
  - Glass-like panel appearance
  - Blur effects
- **Deliverable:** Panels use glassmorphism

#### 2D.3 Transitions
- **Requirements:**
  - Panel switching animations
  - Fade in/out transitions
  - Smooth layout changes
- **Deliverable:** Smooth transitions throughout

#### 2D.4 Visual Polish Pass
- **Requirements:**
  - Refine spacing and alignment
  - Add subtle shadows
  - Improve typography hierarchy
  - Color refinement
- **Deliverable:** Polished, professional appearance

### Success Criteria
- [ ] All interactions feel smooth
- [ ] Glassmorphism effects applied
- [ ] Transitions are polished
- [ ] Visual quality matches professional DAWs

---

## Phase 2E: Backend Integration

### Goals
Connect frontend to backend services and MCP servers.

### Tasks

#### 2E.1 Backend Client Implementation
- **Requirements:**
  - HTTP REST client
  - WebSocket client
  - Request/response handling
  - Error handling and retries
- **Deliverable:** IBackendClient implementation

#### 2E.2 MCP Bridge Integration
- **Requirements:**
  - MCP protocol implementation
  - Server connection management
  - Request routing
  - Response normalization
- **Deliverable:** MCP bridge functional

#### 2E.3 Real-Time Data Updates
- **Requirements:**
  - WebSocket data streaming
  - UI updates from backend
  - Performance metrics updates
  - Job progress updates
- **Deliverable:** Real-time updates working

#### 2E.4 TTS/VC/Whisper Integration
- **Requirements:**
  - Voice cloning API integration
  - Transcription API integration
  - Model selection and configuration
  - Audio processing pipeline
- **Deliverable:** Core audio features functional

### Success Criteria
- [ ] Backend client connects successfully
- [ ] MCP servers accessible
- [ ] Real-time updates work
- [ ] Audio features functional

---

## Implementation Priority

### Recommended Order

1. **Phase 2C** (Panel Registry) - Enables dynamic UI
2. **Phase 2B** (Docking) - Improves UX significantly
3. **Phase 2A** (Visuals) - Adds visual appeal
4. **Phase 2D** (Polish) - Final visual refinement
5. **Phase 2E** (Backend) - Core functionality

### Alternative: Backend First

If backend is critical:
1. **Phase 2E** (Backend) - Core functionality first
2. **Phase 2C** (Panel Registry) - Dynamic UI
3. **Phase 2A** (Visuals) - Real data visualization
4. **Phase 2B** (Docking) - UX improvements
5. **Phase 2D** (Polish) - Final polish

---

## Phase 2 Specifications

When ready, create detailed specs for:
- **Phase 2A Visuals Spec** - How to upgrade placeholders into real waveforms, spectrograms, and charts
- **Phase 2B Docking Spec** - How to turn PanelHost into real draggable/dockable panels
- **Phase 2C Registry Spec** - How to wire PanelRegistry to navigation
- **Phase 2D Polish Spec** - How to add animations and glassmorphism
- **Phase 2E Backend Spec** - How to integrate backend and MCP services

---

## Current Status

**Phase 1:** ✅ Complete
- UI shell structure
- All 6 panels
- Design system
- PanelHost infrastructure

**Phase 2:** ⏳ Ready to begin
- Choose focus area
- Create detailed Phase 2 spec
- Implement incrementally

