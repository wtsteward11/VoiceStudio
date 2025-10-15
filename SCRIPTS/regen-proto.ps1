param([string]$Proto = "src/IPC/vsd.proto")

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path "src/IPC/generated/csharp" | Out-Null
New-Item -ItemType Directory -Force -Path "workers/python/vsdml/vsd_rpc" | Out-Null

# C#
protoc -I src/IPC `
  --csharp_out=src/IPC/generated/csharp `
  --grpc_out=src/IPC/generated/csharp `
  $Proto

# Python
python -m grpc_tools.protoc -I src/IPC `
  --python_out=workers/python/vsdml/vsd_rpc `
  --grpc_python_out=workers/python/vsdml/vsd_rpc `
  $Proto

Write-Host "✅ Regenerated stubs."
