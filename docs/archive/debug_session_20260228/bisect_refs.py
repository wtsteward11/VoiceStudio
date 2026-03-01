import json, glob, subprocess, os, sys

tmp = os.environ['TEMP']
compiler = r'C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.winui\1.8.251105000\tools\net472\XamlCompiler.exe'
wd = r'E:\VoiceStudio\src\VoiceStudio.App'

feb13_path = r'E:\VoiceStudio-feb13\src\VoiceStudio.App\obj\x64\Debug\net8.0-windows10.0.19041.0\input.json'
current_paths = glob.glob(r'E:\VoiceStudio\src\VoiceStudio.App\obj\**\input.json', recursive=True)

with open(feb13_path) as f:
    feb = json.load(f)
with open(current_paths[0]) as f:
    cur = json.load(f)

feb_ref_paths = {r['FullPath'] for r in feb['ReferenceAssemblies']}
new_refs = [r for r in cur['ReferenceAssemblies'] if r['FullPath'] not in feb_ref_paths]
print(f"Testing {len(new_refs)} new references one at a time...")

for i, ref in enumerate(new_refs):
    test = dict(cur)
    test['ReferenceAssemblies'] = list(feb['ReferenceAssemblies']) + [ref]
    inp = os.path.join(tmp, f'bisect_ref_{i}.json')
    out = os.path.join(tmp, f'bisect_ref_{i}_out.json')
    with open(inp, 'w') as f:
        json.dump(test, f)
    if os.path.exists(out):
        os.remove(out)
    
    p = subprocess.run([compiler, inp, out], cwd=wd, capture_output=True, timeout=60)
    status = "OK" if p.returncode == 0 else "FAIL"
    name = os.path.basename(ref['FullPath'])
    print(f"  [{status}] {name} (exit {p.returncode})")
    
    os.remove(inp)
    if os.path.exists(out):
        os.remove(out)
    
    if p.returncode != 0:
        print(f"\n  FOUND CULPRIT: {ref['FullPath']}")
        sys.exit(0)

print("\nNo single reference causes the crash - it might be a combination.")
