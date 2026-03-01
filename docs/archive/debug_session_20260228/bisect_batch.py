import json, glob, subprocess, os

compiler = r'C:\Users\Tyler\.nuget\packages\microsoft.windowsappsdk.winui\1.8.251105000\tools\net472\XamlCompiler.exe'
wd = r'E:\VoiceStudio\src\VoiceStudio.App'
tmp = os.environ['TEMP']

feb13_path = r'E:\VoiceStudio-feb13\src\VoiceStudio.App\obj\x64\Debug\net8.0-windows10.0.19041.0\input.json'
current_paths = glob.glob(r'E:\VoiceStudio\src\VoiceStudio.App\obj\**\input.json', recursive=True)

with open(feb13_path) as f:
    feb = json.load(f)
with open(current_paths[0]) as f:
    cur = json.load(f)

feb_ref_paths = {r['FullPath'] for r in feb['ReferenceAssemblies']}
new_refs = [r for r in cur['ReferenceAssemblies'] if r['FullPath'] not in feb_ref_paths]

def test_refs(extra_refs, label):
    test = dict(cur)
    test['ReferenceAssemblies'] = list(feb['ReferenceAssemblies']) + extra_refs
    inp = os.path.join(tmp, f'batch_{label}.json')
    out = os.path.join(tmp, f'batch_{label}_out.json')
    with open(inp, 'w') as f:
        json.dump(test, f)
    if os.path.exists(out):
        os.remove(out)
    p = subprocess.run([compiler, inp, out], cwd=wd, capture_output=True, timeout=120)
    os.remove(inp)
    if os.path.exists(out):
        os.remove(out)
    return p.returncode

# Group by category
groups = {
    'extensions': [],
    'sqlite': [],
    'other': [],
}
for r in new_refs:
    name = os.path.basename(r['FullPath'])
    if 'Extensions' in name:
        groups['extensions'].append(r)
    elif 'SQLite' in name or 'Sqlite' in name:
        groups['sqlite'].append(r)
    else:
        groups['other'].append(r)

for gname, grefs in groups.items():
    names = [os.path.basename(r['FullPath']) for r in grefs]
    rc = test_refs(grefs, gname)
    status = "OK" if rc == 0 else "CRASH"
    print(f"[{status}] {gname} ({len(grefs)} refs): {', '.join(names)}")

# Now test individual refs in the crashing group(s)
for gname, grefs in groups.items():
    rc = test_refs(grefs, f'{gname}_check')
    if rc != 0:
        print(f"\nDrilling into {gname}...")
        for i, ref in enumerate(grefs):
            name = os.path.basename(ref['FullPath'])
            rc2 = test_refs([ref], f'{gname}_{i}')
            status2 = "OK" if rc2 == 0 else "CRASH"
            print(f"  [{status2}] {name}")
