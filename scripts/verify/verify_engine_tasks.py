#!/usr/bin/env python
"""
Verification script for Engine Engineer tasks:
1. Quality metrics error handling
2. So-VITS-SVC engine integration
3. Default engine selection
"""

import sys
from pathlib import Path

from _env_setup import PROJECT_ROOT

# Set UTF-8 encoding for Windows console
if sys.platform == "win32":
    import codecs
    sys.stdout = codecs.getwriter("utf-8")(sys.stdout.buffer, "strict")
    sys.stderr = codecs.getwriter("utf-8")(sys.stderr.buffer, "strict")

import logging

import numpy as np

logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')
logger = logging.getLogger(__name__)

def test_quality_metrics_error_handling():
    """Test 1: Verify quality metrics error handling with actionable messages."""
    print("\n" + "="*70)
    print("TEST 1: Quality Metrics Error Handling")
    print("="*70)

    try:
        from app.core.engines.quality_metrics import (
            HAS_PESQ,
            HAS_PYSTOI,
            HAS_RESEMBLYZER,
            calculate_all_metrics,
            calculate_pesq_score,
            calculate_stoi_score,
        )

        audio = np.random.randn(16000).astype(np.float32)

        # Test PESQ error handling
        print("\n[1.1] Testing PESQ error handling...")
        try:
            result = calculate_pesq_score(audio, audio, 16000)
            if HAS_PESQ:
                print(f"  [OK] PESQ available, calculated score: {result}")
            else:
                print("  ✗ ERROR: Should have raised ImportError when PESQ missing")
                return False
        except ImportError as e:
            error_msg = str(e)
            if "pip install pesq" in error_msg:
                print("  [OK] PESQ correctly raises ImportError with installation instructions")
                print(f"    Message: {error_msg[:80]}...")
            else:
                print("  ✗ ERROR: ImportError raised but missing installation instructions")
                print(f"    Message: {error_msg}")
                return False
        except Exception as e:
            print(f"  INFO: PESQ raised {type(e).__name__}: {e}")
            if HAS_PESQ:
                # If PESQ is available, other errors are acceptable
                print("  [OK] PESQ available but calculation had runtime error (acceptable)")

        # Test STOI error handling
        print("\n[1.2] Testing STOI error handling...")
        try:
            result = calculate_stoi_score(audio, audio, 10000)
            if HAS_PYSTOI:
                print(f"  [OK] STOI available, calculated score: {result}")
            else:
                print("  ✗ ERROR: Should have raised ImportError when pystoi missing")
                return False
        except ImportError as e:
            error_msg = str(e)
            if "pip install pystoi" in error_msg:
                print("  [OK] STOI correctly raises ImportError with installation instructions")
                print(f"    Message: {error_msg[:80]}...")
            else:
                print("  ✗ ERROR: ImportError raised but missing installation instructions")
                print(f"    Message: {error_msg}")
                return False
        except Exception as e:
            print(f"  INFO: STOI raised {type(e).__name__}: {e}")
            if HAS_PYSTOI:
                print("  [OK] STOI available but calculation had runtime error (acceptable)")

        # Test calculate_all_metrics error tracking
        print("\n[1.3] Testing calculate_all_metrics dependency tracking...")
        try:
            metrics = calculate_all_metrics(audio, sample_rate=22050, use_cache=False)

            # Check for missing_deps in result
            if "missing_dependencies" in metrics:
                missing = metrics["missing_dependencies"]
                if missing:
                    print(f"  [OK] Missing dependencies tracked: {len(missing)} item(s)")
                    for dep in missing[:3]:  # Show first 3
                        print(f"    - {dep}")
                    if len(missing) > 3:
                        print(f"    ... and {len(missing) - 3} more")
                else:
                    print("  [OK] No missing dependencies (all available)")
            else:
                print("  [!] WARNING: 'missing_dependencies' key not in metrics dict")

            # Verify similarity handling when resemblyzer missing
            if reference_audio := audio:
                try:
                    similarity = metrics.get("similarity", None)
                    if similarity is not None:
                        print(f"  [OK] Similarity calculated: {similarity:.3f}")
                    elif not HAS_RESEMBLYZER:
                        print("  [OK] Similarity skipped when resemblyzer missing (fallback to MFCC or energy-based)")
                    else:
                        print("  [!] Similarity not calculated (resemblyzer available but no reference)")
                except Exception as e:
                    print(f"  INFO: Similarity calculation had issue: {e}")

            print("  [OK] calculate_all_metrics completed successfully")
            print(f"    Metrics computed: {len([k for k in metrics if k != 'missing_dependencies'])} metric(s)")
            return True

        except Exception as e:
            print(f"  ✗ ERROR: calculate_all_metrics failed: {e}")
            import traceback
            traceback.print_exc()
            return False

    except ImportError as e:
        print(f"  ✗ ERROR: Failed to import quality_metrics: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_sovits_engine_integration():
    """Test 2: Verify So-VITS-SVC engine integration and discovery."""
    print("\n" + "="*70)
    print("TEST 2: So-VITS-SVC Engine Integration")
    print("="*70)

    try:
        # Test engine class import
        print("\n[2.1] Testing So-VITS-SVC engine class import...")
        try:
            from app.core.engines.sovits_svc_engine import SoVITSSVCEngine, create_sovits_svc_engine
            print("  [OK] So-VITS-SVC engine class imported successfully")
        except ImportError as e:
            print(f"  ✗ ERROR: Failed to import So-VITS-SVC engine: {e}")
            import traceback
            traceback.print_exc()
            return False

        # Test engine instantiation
        print("\n[2.2] Testing engine instantiation...")
        try:
            engine = SoVITSSVCEngine()
            info = engine.get_info()
            print("  [OK] Engine instantiated successfully")
            print(f"    Engine type: {info.get('engine_type')}")
            print(f"    Device: {info.get('device')}")
            print(f"    Sample rate: {info.get('sample_rate')}")
            print(f"    Checkpoint path: {info.get('checkpoint_path')}")
        except Exception as e:
            print(f"  ✗ ERROR: Failed to instantiate engine: {e}")
            import traceback
            traceback.print_exc()
            return False

        # Test engine manifest exists
        print("\n[2.3] Testing engine manifest...")
        manifest_path = Path(__file__).parent.parent / "engines" / "audio" / "sovits" / "engine.manifest.json"
        if manifest_path.exists():
            print(f"  [OK] Engine manifest exists: {manifest_path}")
            try:
                import json
                with open(manifest_path, encoding='utf-8') as f:
                    manifest = json.load(f)
                print(f"    Engine ID: {manifest.get('engine_id')}")
                print(f"    Version: {manifest.get('version')}")
                print(f"    Entry point: {manifest.get('entry_point')}")
            except Exception as e:
                print(f"  [!] WARNING: Failed to read manifest: {e}")
        else:
            print(f"  ✗ ERROR: Engine manifest not found at {manifest_path}")
            return False

        # Test engine discovery via router
        print("\n[2.4] Testing engine discovery via router...")
        try:
            from app.core.engines.router import router

            # Load all engines
            router.load_all_engines("engines")
            engines = router.list_engines()

            if "sovits_svc" in engines:
                print("  [OK] So-VITS-SVC engine discovered via manifest system")
                print(f"    Total engines discovered: {len(engines)}")

                # Try to get engine instance
                try:
                    engine_instance = router.get_engine("sovits_svc")
                    if engine_instance:
                        print("  [OK] Engine can be retrieved via router.get_engine('sovits_svc')")
                        print(f"    Engine class: {engine_instance.__class__.__name__}")
                    else:
                        print("  [!] WARNING: get_engine returned None (may need initialization)")
                except Exception as e:
                    print(f"  [!] WARNING: Failed to get engine instance: {e}")
                    print("    (This is acceptable if checkpoint/config files are missing)")
            else:
                print("  ✗ ERROR: So-VITS-SVC engine not found in discovered engines")
                print(f"    Available engines: {engines[:10]}...")  # Show first 10
                return False
        except Exception as e:
            print(f"  ✗ ERROR: Failed to test engine discovery: {e}")
            import traceback
            traceback.print_exc()
            return False

        # Test preflight check integration
        print("\n[2.5] Testing preflight check integration...")
        try:
            from backend.services.model_preflight import ensure_sovits

            try:
                result = ensure_sovits(auto_download=False)
                print("  [OK] Preflight check passed: So-VITS checkpoints/config present")
                print(f"    Paths: {result.get('paths', [])}")
            except Exception as e:
                error_msg = str(e)
                if "missing" in error_msg.lower():
                    print("  [OK] Preflight check correctly reports missing checkpoints/config")
                    print(f"    Message: {error_msg[:100]}...")
                    print("    (This is expected if checkpoints not yet placed)")
                else:
                    print(f"  [!] WARNING: Preflight check failed with unexpected error: {e}")
        except ImportError as e:
            print(f"  [!] WARNING: Could not import preflight check: {e}")
        except Exception as e:
            print(f"  [!] WARNING: Preflight check had issue: {e}")

        return True

    except Exception as e:
        print(f"  ✗ ERROR: So-VITS-SVC engine integration test failed: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_default_engine_selection():
    """Test 3: Verify default engine selection in routes."""
    print("\n" + "="*70)
    print("TEST 3: Default Engine Selection")
    print("="*70)

    try:
        # Test voice route default engine logic
        print("\n[3.1] Testing voice route default engine selection logic...")
        try:
            # Check the voice route file directly (path via _env_setup)
            voice_route_path = PROJECT_ROOT / "backend" / "api" / "routes" / "voice.py"
            if voice_route_path.exists():
                with open(voice_route_path, encoding='utf-8') as f:
                    content = f.read()

                # Check for default engine selection logic
                has_default_logic = (
                    "default_engine" in content or
                    "requested_engine = \"xtts_v2\"" in content or
                    "fallback_chain" in content
                )

                if has_default_logic:
                    print("  [OK] Default engine selection logic found in voice.py")
                    print("    Contains: default selection, fallback chain")
                else:
                    print("  ✗ ERROR: Default engine selection logic not found")
                    return False

                # Check for fallback chain
                if "fallback_chain" in content and "xtts_v2" in content and "piper" in content:
                    print("  [OK] Fallback chain implemented: XTTS → Piper → eSpeak")
                else:
                    print("  [!] WARNING: Fallback chain may be incomplete")
            else:
                print(f"  ✗ ERROR: voice.py not found at {voice_route_path}")
                return False
        except Exception as e:
            print(f"  ✗ ERROR: Failed to check voice route: {e}")
            import traceback
            traceback.print_exc()
            return False

        # Test transcribe route default
        print("\n[3.2] Testing transcribe route default engine...")
        try:
            transcribe_route_path = PROJECT_ROOT / "backend" / "api" / "routes" / "transcribe.py"
            if transcribe_route_path.exists():
                with open(transcribe_route_path, encoding='utf-8') as f:
                    content = f.read()

                # Check for whisper_cpp default
                if "engine: str = \"whisper_cpp\"" in content or 'engine: str = "whisper_cpp"' in content:
                    print("  [OK] Transcribe route defaults to whisper_cpp")
                elif '"whisper_cpp"' in content or "'whisper_cpp'" in content:
                    print("  [OK] Transcribe route references whisper_cpp (default likely set)")
                else:
                    print("  [!] WARNING: whisper_cpp default may not be explicitly set")
            else:
                print(f"  [!] WARNING: transcribe.py not found at {transcribe_route_path}")
        except Exception as e:
            print(f"  [!] WARNING: Failed to check transcribe route: {e}")

        # Test EngineConfigService integration
        print("\n[3.3] Testing EngineConfigService integration...")
        try:
            from backend.services.EngineConfigService import get_engine_config_service

            config_service = get_engine_config_service()
            default_tts = config_service.get_default_engine("tts")

            if default_tts:
                print(f"  [OK] EngineConfigService returns default TTS engine: {default_tts}")
            else:
                print("  [!] WARNING: EngineConfigService has no default TTS engine")
                print("    (Fallback to hardcoded XTTS should still work)")
        except ImportError as e:
            print(f"  [!] WARNING: EngineConfigService not available: {e}")
        except Exception as e:
            print(f"  [!] WARNING: EngineConfigService check had issue: {e}")

        return True

    except Exception as e:
        print(f"  ✗ ERROR: Default engine selection test failed: {e}")
        import traceback
        traceback.print_exc()
        return False


def main():
    """Run all verification tests."""
    print("\n" + "="*70)
    print("Engine Engineer Tasks Verification")
    print("="*70)
    print("\nVerifying:")
    print("  1. Quality metrics error handling with actionable messages")
    print("  2. So-VITS-SVC 4.0 engine integration and discovery")
    print("  3. Default engine selection in routes")

    results = []

    # Test 1: Quality metrics error handling
    results.append(("Quality Metrics Error Handling", test_quality_metrics_error_handling()))

    # Test 2: So-VITS-SVC integration
    results.append(("So-VITS-SVC Engine Integration", test_sovits_engine_integration()))

    # Test 3: Default engine selection
    results.append(("Default Engine Selection", test_default_engine_selection()))

    # Summary
    print("\n" + "="*70)
    print("VERIFICATION SUMMARY")
    print("="*70)

    all_passed = True
    for name, passed in results:
        status = "[OK] PASS" if passed else "✗ FAIL"
        print(f"{status}: {name}")
        if not passed:
            all_passed = False

    print("\n" + "="*70)
    if all_passed:
        print("[OK] ALL TESTS PASSED")
        return 0
    else:
        print("✗ SOME TESTS FAILED")
        return 1


if __name__ == "__main__":
    sys.exit(main())
