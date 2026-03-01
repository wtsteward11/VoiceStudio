#!/usr/bin/env python3
"""
Generate C# Client from OpenAPI Schema
Generates a typed C# client from the OpenAPI schema using openapi-generator or similar.
"""

import subprocess
import sys
from pathlib import Path

project_root = Path(__file__).parent.parent
openapi_file = project_root / "docs" / "api" / "openapi.json"
output_dir = project_root / "src" / "VoiceStudio.Core" / "Services" / "Generated"

# Check if openapi-generator is available
try:
    result = subprocess.run(
        ["openapi-generator", "version"], capture_output=True, text=True, check=True
    )
    has_openapi_generator = True
except (subprocess.CalledProcessError, FileNotFoundError):
    has_openapi_generator = False

if not has_openapi_generator:
    print(
        "[WARN] openapi-generator not found. Install with: npm install -g @openapitools/openapi-generator-cli"
    )
    print("[INFO] Alternative: Use NSwag or Swashbuckle to generate client")
    print("[INFO] For now, creating placeholder documentation...")

    # Create placeholder file with instructions
    output_dir.mkdir(parents=True, exist_ok=True)
    placeholder_file = output_dir / "BackendClient.generated.cs.instructions.md"
    with open(placeholder_file, "w", encoding="utf-8") as f:
        f.write(
            """# C# Client Generation Instructions

## Using OpenAPI Generator

1. Install openapi-generator:
   ```bash
   npm install -g @openapitools/openapi-generator-cli
   ```

2. Generate client:
   ```bash
   openapi-generator generate \\
     -i docs/api/openapi.json \\
     -g csharp-netcore \\
     -o src/VoiceStudio.Core/Services/Generated \\
     --additional-properties=packageName=VoiceStudio.Core.Services.Generated,netCoreProjectFile=true
   ```

## Using NSwag

1. Install NSwag:
   ```bash
   dotnet tool install -g NSwag.ConsoleCore
   ```

2. Generate client:
   ```bash
   nswag openapi2csclient \\
     /Input:docs/api/openapi.json \\
     /Output:src/VoiceStudio.Core/Services/Generated/BackendClient.generated.cs \\
     /Namespace:VoiceStudio.Core.Services.Generated \\
     /ClientClassAccessModifier:public \\
     /GenerateClientClasses:true \\
     /GenerateClientInterfaces:true
   ```

## Using Swashbuckle

Add to VoiceStudio.Core.csproj:
```xml
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />
```

Then use SwaggerClientGenerator or similar tool.
"""
        )
    print(f"[INFO] Created instructions at {placeholder_file}")
    sys.exit(0)

# Generate client using openapi-generator
try:
    output_dir.mkdir(parents=True, exist_ok=True)

    cmd = [
        "openapi-generator",
        "generate",
        "-i",
        str(openapi_file),
        "-g",
        "csharp-netcore",
        "-o",
        str(output_dir),
        "--additional-properties",
        "packageName=VoiceStudio.Core.Services.Generated,netCoreProjectFile=true,library=httpclient",
    ]

    result = subprocess.run(cmd, capture_output=True, text=True, check=True)
    print(f"[OK] C# client generated to {output_dir}")
    print(result.stdout)
    sys.exit(0)
except subprocess.CalledProcessError as e:
    print(f"[ERROR] Failed to generate C# client: {e}", file=sys.stderr)
    print(e.stderr, file=sys.stderr)
    sys.exit(1)
except Exception as e:
    print(f"[ERROR] Error: {e}", file=sys.stderr)
    sys.exit(1)
