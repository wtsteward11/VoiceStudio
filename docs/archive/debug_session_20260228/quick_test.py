import json, glob, subprocess, os, sys

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

def test_refs(extra_refs):
    test = dict(cur)
    test['ReferenceAssemblies'] = list(feb['ReferenceAssemblies']) + extra_refs
    inp = os.path.join(tmp, 'qt.json')
    out = os.path.join(tmp, 'qt_out.json')
    with open(inp, 'w') as f:
        json.dump(test, f)
    if os.path.exists(out):
        os.remove(out)
    p = subprocess.run([compiler, inp, out], cwd=wd, capture_output=True, timeout=60)
    return p.returncode

mode = sys.argv[1] if len(sys.argv) > 1 else 'groups'

if mode == 'groups':
    ext = [r for r in new_refs if 'Extensions' in os.path.basename(r['FullPath'])]
    sqlt = [r for r in new_refs if 'SQLite' in os.path.basename(r['FullPath']) or 'Sqlite' in os.path.basename(r['FullPath'])]
    other = [r for r in new_refs if r not in ext and r not in sqlt]
    
    print(f"extensions ({len(ext)}): ", end='', flush=True)
    rc = test_refs(ext)
    print(f"exit {rc}", flush=True)
    
    print(f"sqlite ({len(sqlt)}): ", end='', flush=True)
    rc = test_refs(sqlt)
    print(f"exit {rc}", flush=True)
    
    print(f"other ({len(other)}): ", end='', flush=True)
    for r in other:
        print(f"  {os.path.basename(r['FullPath'])}", flush=True)
    rc = test_refs(other)
    print(f"exit {rc}", flush=True)

elif mode == 'each':
    group_name = sys.argv[2]
    if group_name == 'extensions':
        refs = [r for r in new_refs if 'Extensions' in os.path.basename(r['FullPath'])]
    elif group_name == 'sqlite':
        refs = [r for r in new_refs if 'SQLite' in os.path.basename(r['FullPath']) or 'Sqlite' in os.path.basename(r['FullPath'])]
    else:
        ext = [r for r in new_refs if 'Extensions' in os.path.basename(r['FullPath'])]
        sqlt = [r for r in new_refs if 'SQLite' in os.path.basename(r['FullPath']) or 'Sqlite' in os.path.basename(r['FullPath'])]
        refs = [r for r in new_refs if r not in ext and r not in sqlt]
    
    for ref in refs:
        name = os.path.basename(ref['FullPath'])
        print(f"  {name}: ", end='', flush=True)
        rc = test_refs([ref])
        print(f"exit {rc}", flush=True)
