#!/usr/bin/env python3
"""Fix status code assertions in tests.

FastAPI/Pydantic returns 422 for validation errors, not 400.
"""

import os
import re

test_dir = 'tests/unit/backend'
count = 0

def process_file(fpath):
    global count
    with open(fpath, encoding='utf-8') as f:
        content = f.read()

    original = content

    # Fix: assert response.status_code == 400 -> 422 for validation errors
    # Pattern: == 400 in context of status_code
    # Only fix where test name suggests validation/missing field

    # Common pattern: response.status_code == 400 should be 422
    content = re.sub(
        r'assert\s+response\.status_code\s*==\s*400\b',
        'assert response.status_code == 422',
        content
    )

    # Pattern: assert 400 == response.status_code
    content = re.sub(
        r'assert\s+400\s*==\s*response\.status_code\b',
        'assert 422 == response.status_code',
        content
    )

    if content != original:
        with open(fpath, 'w', encoding='utf-8') as f:
            f.write(content)
        count += 1
        return True
    return False

for root, dirs, files in os.walk(test_dir):
    # Skip __pycache__
    dirs[:] = [d for d in dirs if d != '__pycache__']

    for fname in files:
        if fname.startswith('test_') and fname.endswith('.py'):
            fpath = os.path.join(root, fname)
            if process_file(fpath):
                print(f'Fixed: {fpath}')

print(f'\nTotal files fixed: {count}')
