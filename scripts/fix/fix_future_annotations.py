"""Add 'from __future__ import annotations' to Python files that need it."""

import os
import re
import sys

# Directories to check
directories = [
    r'E:\VoiceStudio\tools\overseer',
    r'E:\VoiceStudio\backend',
    r'E:\VoiceStudio\app',
]
future_import = 'from __future__ import annotations'
files_fixed = []

for directory in directories:
  if not os.path.exists(directory):
      continue
  for root, dirs, files in os.walk(directory):
    for fname in files:
        if fname.endswith('.py'):
            fpath = os.path.join(root, fname)
            with open(fpath, encoding='utf-8') as f:
                content = f.read()
            
            # Skip if already has the import
            if future_import in content:
                continue
            
            # Check if file uses Python 3.10+ type hints
            pattern = r':\s*\w+\s*\|\s*\w+|:\s*\w+\s*\|\s*None|:\s*list\[|:\s*dict\[|:\s*tuple\[|:\s*set\['
            if re.search(pattern, content):
                # Find insertion point after docstring/comments
                lines = content.split('\n')
                insert_idx = 0
                
                # Skip leading comments and docstrings
                i = 0
                while i < len(lines):
                    line = lines[i].strip()
                    if line.startswith('#') or line == '':
                        i += 1
                        continue
                    if line.startswith('"""') or line.startswith("'''"):
                        # Find end of docstring
                        quote = line[:3]
                        if line.count(quote) >= 2 and len(line) > 3:
                            i += 1
                        else:
                            i += 1
                            while i < len(lines) and quote not in lines[i]:
                                i += 1
                            i += 1
                        insert_idx = i
                        break
                    else:
                        insert_idx = i
                        break
                
                # Insert the future import
                lines.insert(insert_idx, '')
                lines.insert(insert_idx + 1, future_import)
                
                new_content = '\n'.join(lines)
                with open(fpath, 'w', encoding='utf-8') as f:
                    f.write(new_content)
                files_fixed.append(fpath)

print(f'Fixed {len(files_fixed)} files:')
for f in files_fixed[:30]:
    print(f'  {f}')
if len(files_fixed) > 30:
    print(f'  ... and {len(files_fixed) - 30} more')
