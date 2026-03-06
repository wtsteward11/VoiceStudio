# Panel Surface Classification

Classification of unregistered panel XAML surfaces (Views/Panels/*.xaml not in Core/Advanced panel registration).

**Source**: Release Truth Hardening Plan, Task 7.  
**Date**: 2026-03-06.

## Classification Legend

| Tag | Meaning |
|-----|---------|
| **active-unregistered** | Has ViewModel + navigation/reference (MainWindow menu or smoke test) |
| **deprecated** | Has ViewModel, no navigation references |
| **dead** | No ViewModel |

## Top 10 Unregistered Panel XAML Surfaces (Alphabetical)

| # | XAML File | ViewModel | References | Classification |
|---|-----------|-----------|------------|----------------|
| 1 | ABTestingView.xaml | ABTestingViewModel | MainWindow (A/B Testing menu), SmokeTestBase, PanelNavigationSmokeTests | **active-unregistered** |
| 2 | AdvancedRealTimeVisualizationView.xaml | AdvancedRealTimeVisualizationViewModel | SmokeTestBase, PanelNavigationSmokeTests | **active-unregistered** |
| 3 | AdvancedSearchView.xaml | AdvancedSearchViewModel | SmokeTestBase, PanelNavigationSmokeTests | **active-unregistered** |
| 4 | AnalyticsDashboardView.xaml | AnalyticsDashboardViewModel | SmokeTestBase, PanelNavigationSmokeTests | **active-unregistered** |
| 5 | AssistantView.xaml | AssistantViewModel | SmokeTestBase, PanelNavigationSmokeTests | **active-unregistered** |
| 6 | AudioMonitoringDashboardView.xaml | AudioMonitoringDashboardViewModel | SmokeTestBase, PanelNavigationSmokeTests | **active-unregistered** |
| 7 | AutomationView.xaml | AutomationViewModel | MainWindow (Automation menu), SmokeTestBase | **active-unregistered** |
| 8 | BackupRestoreView.xaml | BackupRestoreViewModel | MainWindow (Backup & Restore menu), SmokeTestBase | **active-unregistered** |
| 9 | EmotionStyleControlView.xaml | EmotionStyleControlViewModel | MainWindow (Emotion Style menu), SmokeTestBase, PanelNavigationSmokeTests | **active-unregistered** |
| 10 | EmotionStylePresetEditorView.xaml | EmotionStylePresetEditorViewModel | SmokeTestBase, PanelNavigationSmokeTests | **active-unregistered** |

## Summary

- **active-unregistered**: 10
- **deprecated**: 0
- **dead**: 0

All 10 are reachable via MainWindow menu or smoke test navigation. Consider registering them in CorePanelRegistrationService or AdvancedPanelRegistrationService for full parity.

## Future Cleanup

No dead panels identified in this slice. Document remaining unregistered surfaces for future audit if needed.
