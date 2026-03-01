import json, glob, os

current_paths = glob.glob(r'E:\VoiceStudio\src\VoiceStudio.App\obj\**\input.json', recursive=True)
with open(current_paths[0]) as f:
    cur = json.load(f)

refs = cur['ReferenceAssemblies']
print(f'Total refs: {len(refs)}')

for r in refs:
    p = r['FullPath']
    bn = os.path.basename(p)
    if '\\9.0.' in p or '\\10.0.' in p:
        print(f'  HIGH: {bn}')
    if 'VoiceStudio.Core' in p:
        print(f'  CORE: {p}')
    if 'MessagePack' in p:
        print(f'  MSGPACK: {p}')

pages = cur['XamlPages']
print(f'Total pages: {len(pages)}')
