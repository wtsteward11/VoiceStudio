import json, glob

feb13_path = r'E:\VoiceStudio-feb13\src\VoiceStudio.App\obj\x64\Debug\net8.0-windows10.0.19041.0\input.json'
current_paths = glob.glob(r'E:\VoiceStudio\src\VoiceStudio.App\obj\**\input.json', recursive=True)
current_path = current_paths[0]
tmp = r'C:\Users\Tyler\AppData\Local\Temp'

with open(feb13_path) as f:
    feb = json.load(f)
with open(current_path) as f:
    cur = json.load(f)

new_page_specs = {
    r'Views\Dialogs\PluginPermissionDialog.xaml',
    r'Views\Panels\AdvancedSpectrogramVisualizationView.xaml',
    r'Views\Panels\AdvancedWaveformVisualizationView.xaml',
    r'Views\Panels\EngineSetupWizardView.xaml',
    r'Views\Panels\OrchestrationPanel.xaml',
    r'Views\Panels\PluginHealthDashboardView.xaml',
    r'Views\Panels\RenderQueuePanel.xaml',
    r'Views\Panels\StrategyPresetsPanel.xaml',
}

feb_ref_paths = {r['FullPath'] for r in feb['ReferenceAssemblies']}

# Test 3: current minus 8 new pages
test3 = dict(cur)
test3['XamlPages'] = [p for p in cur['XamlPages'] if p['ItemSpec'] not in new_page_specs]
print(f"Test3: {len(test3['XamlPages'])} pages (removed {len(cur['XamlPages']) - len(test3['XamlPages'])})")
with open(f'{tmp}\\test3_no_new_pages.json', 'w') as f:
    json.dump(test3, f)

# Test 4: current minus new refs
test4 = dict(cur)
test4['ReferenceAssemblies'] = [r for r in cur['ReferenceAssemblies'] if r['FullPath'] in feb_ref_paths]
print(f"Test4: {len(test4['ReferenceAssemblies'])} refs (removed {len(cur['ReferenceAssemblies']) - len(test4['ReferenceAssemblies'])})")
with open(f'{tmp}\\test4_no_new_refs.json', 'w') as f:
    json.dump(test4, f)

print("Done")
