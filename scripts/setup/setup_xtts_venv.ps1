# XTTS Isolated Virtual Environment Setup
# Creates a separate venv with numpy 1.26.4 for XTTS compatibility

$XTTSPath = "$PSScriptRoot\..\runtime\xtts_service"
$VenvPath = "$XTTSPath\.venv"

Write-Host "Setting up isolated XTTS environment..." -ForegroundColor Cyan

# Create directory
if (-not (Test-Path $XTTSPath)) {
    New-Item -ItemType Directory -Path $XTTSPath -Force | Out-Null
}

# Check if already set up
if (Test-Path "$XTTSPath\.setup_complete") {
    Write-Host "XTTS environment already set up." -ForegroundColor Green
    Write-Host "To reinstall, delete: $XTTSPath\.setup_complete"
    exit 0
}

# Create virtual environment with copies (not symlinks) to avoid permission issues
Write-Host "Creating virtual environment..." -ForegroundColor Yellow
if (Test-Path $VenvPath) {
    Remove-Item -Recurse -Force $VenvPath
}
python -m venv $VenvPath --copies

# Activate and install packages
$pythonPath = "$VenvPath\Scripts\python.exe"
$pipPath = "$VenvPath\Scripts\pip.exe"

if (-not (Test-Path $pythonPath)) {
    Write-Host "ERROR: Failed to create virtual environment" -ForegroundColor Red
    exit 1
}

Write-Host "Upgrading pip..." -ForegroundColor Yellow
& $pythonPath -m pip install --upgrade pip

Write-Host "Installing numpy 1.26.4 (XTTS compatible)..." -ForegroundColor Yellow
& $pythonPath -m pip install "numpy==1.26.4"

Write-Host "Installing PyTorch with CUDA..." -ForegroundColor Yellow
& $pythonPath -m pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu121

Write-Host "Installing Coqui TTS..." -ForegroundColor Yellow
& $pythonPath -m pip install "coqui-tts==0.25.3"

Write-Host "Installing additional dependencies..." -ForegroundColor Yellow
& $pythonPath -m pip install soundfile scipy fastapi uvicorn

# Copy the XTTS service script
$serviceScript = @'
"""
XTTS Microservice
Runs XTTS in an isolated environment with compatible numpy version.
Communicates via HTTP (FastAPI) or stdin/stdout.
"""

import argparse
import json
import logging
import sys
import tempfile

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

_tts_instance = None

def get_tts():
    """Lazy load TTS instance."""
    global _tts_instance
    if _tts_instance is None:
        logger.info("Loading XTTS model...")
        from TTS.api import TTS
        _tts_instance = TTS("tts_models/multilingual/multi-dataset/xtts_v2")
        if hasattr(_tts_instance, 'to') and hasattr(torch, 'cuda') and torch.cuda.is_available():
            _tts_instance.to('cuda')
        logger.info("XTTS model loaded successfully")
    return _tts_instance

import torch

def synthesize(text: str, speaker_wav: str, language: str = "en", output_path: str = None) -> dict:
    """Synthesize speech using XTTS."""
    try:
        tts = get_tts()
        if output_path is None:
            output_path = tempfile.mktemp(suffix=".wav")
        tts.tts_to_file(text=text, speaker_wav=speaker_wav, language=language, file_path=output_path)
        return {"success": True, "output_path": output_path, "message": "Synthesis completed"}
    except Exception as e:
        logger.error("Synthesis failed: %s", e)
        return {"success": False, "error": str(e)}

def run_http_server(host: str = "127.0.0.1", port: int = 8081):
    """Run as HTTP microservice."""
    import uvicorn
    from fastapi import FastAPI, Request
    from fastapi.responses import FileResponse, JSONResponse
    app = FastAPI()
    @app.get("/health")
    def health():
        return {"status": "ok", "service": "xtts"}
    @app.post("/synthesize")
    async def api_synthesize(request):
        data = await request.json()
        return synthesize(text=data.get("text", ""), speaker_wav=data.get("speaker_wav", ""), language=data.get("language", "en"), output_path=data.get("output_path"))
    @app.post("/synthesize_and_return")
    async def api_synthesize_and_return(request):
        data = await request.json()
        result = synthesize(text=data.get("text", ""), speaker_wav=data.get("speaker_wav", ""), language=data.get("language", "en"))
        if result.get("success"):
            return FileResponse(result["output_path"], media_type="audio/wav")
        return JSONResponse(result, status_code=500)
    logger.info("Starting XTTS service on %s:%s", host, port)
    uvicorn.run(app, host=host, port=port)

def run_stdio():
    """Run in stdio mode for subprocess communication."""
    logger.info("XTTS service starting in stdio mode...")
    
    # Preload model
    get_tts()
    
    print("READY", flush=True)
    
    for line in sys.stdin:
        try:
            request = json.loads(line.strip())
            action = request.get("action")
            
            if action == "synthesize":
                result = synthesize(
                    text=request.get("text", ""),
                    speaker_wav=request.get("speaker_wav", ""),
                    language=request.get("language", "en"),
                    output_path=request.get("output_path")
                )
                print(json.dumps(result), flush=True)
            elif action == "health":
                print(json.dumps({"status": "ok"}), flush=True)
            elif action == "exit":
                print(json.dumps({"status": "exiting"}), flush=True)
                break
            else:
                print(json.dumps({"error": f"Unknown action: {action}"}), flush=True)
        except Exception as e:
            print(json.dumps({"error": str(e)}), flush=True)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="XTTS Microservice")
    parser.add_argument("--mode", choices=["http", "stdio"], default="http")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8081)
    args = parser.parse_args()
    
    if args.mode == "http":
        run_http_server(args.host, args.port)
    else:
        run_stdio()
'@

$serviceScript | Out-File "$XTTSPath\xtts_service.py" -Encoding UTF8

# Create marker file
"XTTS Service - Setup completed on $(Get-Date)" | Out-File "$XTTSPath\.setup_complete" -Encoding UTF8

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "XTTS Environment Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Location: $XTTSPath"
Write-Host "Python: $pythonPath"
Write-Host ""
Write-Host "To start XTTS service:" -ForegroundColor Cyan
Write-Host "  HTTP mode: .\scripts\start_xtts_service.ps1"
Write-Host "  Manual:    $pythonPath $XTTSPath\xtts_service.py --mode http --port 8081"
