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

print(f"New references ({len(new_refs)}):")
for r in new_refs:
    print(f"  {os.path.basename(r['FullPath'])}")

# First test: remove ALL new refs to confirm baseline works
test_base = dict(cur)
test_base['ReferenceAssemblies'] = [r for r in cur['ReferenceAssemblies'] if r['FullPath'] in feb_ref_paths]
inp = os.path.join(tmp, 'bisect_base.json')
out = os.path.join(tmp, 'bisect_base_out.json')
with open(inp, 'w') as f:
    json.dump(test_base, f)
if os.path.exists(out):
    os.remove(out)
p = subprocess.run([compiler, inp, out], cwd=wd, capture_output=True, timeout=120)
print(f"\nBaseline (no new refs): exit {p.returncode}")

if p.returncode != 0:
    print("ERROR: Baseline still fails! Issue is not in references alone.")
    exit(1)

# Binary search: add half the new refs at a time
def test_with_refs(extra_refs, label):
    test = dict(cur)
    test['ReferenceAssemblies'] = list(feb['ReferenceAssemblies']) + extra_refs
    inp_path = os.path.join(tmp, f'bisect_{label}.json')
    out_path = os.path.join(tmp, f'bisect_{label}_out.json')
    with open(inp_path, 'w') as f:
        json.dump(test, f)
    if os.path.exists(out_path):
        os.remove(out_path)
    p = subprocess.run([compiler, inp_path, out_path], cwd=wd, capture_output=True, timeout=120)
    os.remove(inp_path)
    if os.path.exists(out_path):
        os.remove(out_path)
    return p.returncode == 0

# Test each ref individually
print(f"\nTesting {len(new_refs)} refs individually...")
culprits = []
for i, ref in enumerate(new_refs):
    ok = test_with_refs([ref], f'single_{i}')
    name = os.path.basename(ref['FullPath'])
    status = "OK" if ok else "CRASH"
    print(f"  [{status}] {name}")
    if not ok:
        culprits.append(ref)

if culprits:
    print(f"\nCulprit references ({len(culprits)}):")
    for c in culprits:
        print(f"  {c['FullPath']}")
else:
    print("\nNo single ref crashes! Testing combinations...")
