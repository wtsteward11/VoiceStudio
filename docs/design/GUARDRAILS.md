# VoiceStudio Development Guardrails

## CRITICAL: Do NOT Simplify

This is a **professional-grade studio application**, not a toy. The architecture is intentionally complex and modular to support 100+ panels and enterprise-level functionality.

## Absolute Rules

### 1. Panel Count & Layout Complexity

❌ **DO NOT** reduce panel count or layout complexity  
✅ **MUST** keep all 6 primary panels (Profiles, Timeline, EffectsMixer, Analyzer, Macro, Diagnostics)  
✅ **MUST** maintain the 3-column + 2-row grid in MainWindow.xaml  
✅ **MUST** keep PanelHost per region (Left, Center, Right, Bottom)

**Rationale**: The layout is designed for professional audio production workflows. Each panel serves a specific purpose and cannot be merged or removed.

### 2. MVVM Separation

❌ **DO NOT** merge Views and ViewModels "to be faster"  
✅ **MUST** have separate files: `.xaml`, `.xaml.cs`, `ViewModel.cs` for every panel  
✅ **MUST** maintain strict MVVM pattern separation

**File Structure Required:**
```
Views/Panels/
  ├── ProfilesView.xaml
  ├── ProfilesView.xaml.cs
  ├── ProfilesViewModel.cs
  ├── TimelineView.xaml
  ├── TimelineView.xaml.cs
  ├── TimelineViewModel.cs
  └── ... (repeat for all 6 panels)
```

**Rationale**: This separation enables:
- Independent testing
- Code reusability
- Future extensibility to 100+ panels
- Team collaboration (UI/Logic split)

### 3. PanelHost Control

❌ **DO NOT** replace custom PanelHost with direct Grids  
❌ **DO NOT** inline panel content directly in MainWindow  
✅ **MUST** use PanelHost UserControl for all panels  
✅ **MUST** maintain PanelHost structure (header + content area)

**Rationale**: PanelHost provides:
- Consistent styling
- Header controls (pop-out, collapse, options)
- Future drag-docking support
- Panel swapping infrastructure

### 4. File Structure

❌ **DO NOT** rename or move files without explicit instruction  
❌ **DO NOT** consolidate files "for simplicity"  
✅ **MUST** follow the canonical file tree exactly

**Canonical Structure:**
```
src/
  VoiceStudio.App/
    Views/
      Shell/
        MainWindow.xaml
        StatusBarView.xaml
        NavigationView.xaml
      Panels/
        ProfilesView.xaml (+ .cs + ViewModel.cs)
        TimelineView.xaml (+ .cs + ViewModel.cs)
        EffectsMixerView.xaml (+ .cs + ViewModel.cs)
        AnalyzerView.xaml (+ .cs + ViewModel.cs)
        MacroView.xaml (+ .cs + ViewModel.cs)
        DiagnosticsView.xaml (+ .cs + ViewModel.cs)
    Controls/
      PanelHost.xaml (+ .cs)
      NavIconButton.xaml (+ .cs)
  VoiceStudio.Core/
    Panels/
    Models/
    Services/
```

**Rationale**: This structure supports:
- Clear separation of concerns
- Easy navigation
- Scalability to 100+ panels
- Team collaboration

### 5. Placeholder Regions

❌ **DO NOT** collapse or remove placeholder areas  
❌ **DO NOT** hide waveform, spectrogram, node graph, or chart regions  
✅ **MUST** keep all placeholder areas visibly distinct in the UI

**Required Placeholders:**
- **TimelineView**: Waveform lanes, spectrogram/visualizer area
- **EffectsMixerView**: Fader controls, FX chain area
- **AnalyzerView**: Chart placeholder for each tab
- **MacroView**: Node graph canvas area
- **DiagnosticsView**: Log list, metrics charts

**Rationale**: These placeholders:
- Show the intended UI structure
- Guide future implementation
- Maintain visual consistency
- Help users understand the application layout

### 6. Design Tokens

❌ **DO NOT** use random colors or hardcoded values  
❌ **DO NOT** create new color schemes  
✅ **MUST** use VSQ.* resources from DesignTokens.xaml  
✅ **MUST** follow the design system consistently

**Required Resources:**
- `VSQ.Background.Darker`, `VSQ.Background.Dark`
- `VSQ.Accent.Cyan`, `VSQ.Accent.Lime`, `VSQ.Accent.Magenta`
- `VSQ.Text.Primary`, `VSQ.Text.Secondary`
- `VSQ.Panel.BorderBrush`
- `VSQ.CornerRadius.Panel`, `VSQ.CornerRadius.Button`
- `VSQ.Text.Body`, `VSQ.Text.Caption`, `VSQ.Text.Title`, `VSQ.Text.Heading`

**Rationale**: Design tokens ensure:
- Visual consistency
- Easy theme switching
- Maintainable styling
- Professional appearance

### 7. Core Library Separation

❌ **DO NOT** put business logic in the App project  
❌ **DO NOT** duplicate interfaces or models  
✅ **MUST** use VoiceStudio.Core for shared code  
✅ **MUST** reference Core from App project

**Core Library Contains:**
- Panel registry interfaces
- Data models (VoiceProfile, AudioClip, MeterReading)
- Service interfaces (IBackendClient)
- Shared enums (PanelRegion)

**Rationale**: Separation enables:
- Independent testing of core logic
- Reuse across multiple frontends
- Clear API boundaries
- Future backend integration

### 8. Backend Architecture

❌ **DO NOT** implement backend logic in the frontend  
❌ **DO NOT** hardcode API endpoints  
✅ **MUST** use IBackendClient interface  
✅ **MUST** follow shared contract schemas

**Required Structure:**
```
backend/
  api/              # FastAPI/Express
  mcp_bridge/       # MCP integration
  models/           # TTS, VC, Whisper
shared/
  contracts/        # JSON schemas
```

**Rationale**: This architecture:
- Enables backend flexibility (Python/Node)
- Supports MCP integration
- Maintains API contracts
- Allows independent deployment

### 9. Transcript / clip mutation outcomes (GAP-045 / GAP-047)

Operations that mutate **transcript text**, **clip audio**, or **cross-consumer subtitle state** MUST be classifiable into one of these **three** buckets. A **fourth** bucket is **forbidden**.

| Bucket | Definition | Examples |
|--------|------------|----------|
| **Atomic success** | All steps that define “committed” for the operation complete; downstream consumers may observe success events or quiet refetch. | Coordinator: clip apply + persist + local clip/linkage + success events + undo. Transcribe VM: `err == null` path. Timeline: `coherentReloadAfterSegmentApply` with guards passing → quiet refetch. |
| **Explicit compensated rollback** | A forward mutation is **undone** by a defined compensating action; no success-path consumer events for partial commit. | Coordinator: transcript persist fails → `UpdateClipAsync` restores prior audio; no linkage removal, no success events, no undo registration. |
| **Operator-visible degraded** | No silent partial commit: operator sees an explicit error or degraded signal; consumers do not apply optimistic success semantics. | Coordinator: validation/job timeout/clip apply throws; persist fails and clip rollback also fails (combined message). Transcribe VM: `err != null`. Timeline: guard mismatch → no refetch (fail-closed). |

**Forbidden fourth bucket:** *“Mostly worked; let downstream consumers sort it out.”*  
If a change cannot be assigned to one of the three buckets above, the seam is **not ready** for closure.

**Local-only draft** (e.g. filler preview / draft text) is **not** a backend mutation bucket; it MUST NOT pretend to be persisted truth.

**References:** [TranscriptSegmentRegenerationCoordinator.cs](../../src/VoiceStudio.App/Services/TranscriptSegmentRegenerationCoordinator.cs); [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md).

## Implementation Checklist

When implementing or modifying code, verify:

- [ ] Transcript/clip/coherence changes map to **§9** outcome buckets (no forbidden fourth bucket)
- [ ] All 6 panels exist with separate View/ViewModel files
- [ ] MainWindow uses 3×2 grid layout
- [ ] All panels use PanelHost control
- [ ] All placeholders are visible and distinct
- [ ] All colors use VSQ.* design tokens
- [ ] Core library is separate and referenced
- [ ] File structure matches canonical tree
- [ ] No files merged "for simplicity"

## When in Doubt

If you're unsure whether to simplify something:

1. **Check this document first**
2. **Refer to the architecture specification**
3. **Maintain the structure as-is**
4. **Ask for explicit permission before simplifying**

## Reminder

This is a **professional studio application**. Complexity is intentional and necessary. Do not "help" by simplifying - the architecture is designed this way for a reason.

