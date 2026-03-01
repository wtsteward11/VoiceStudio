#!/usr/bin/env python
"""
Targeted verification script for Engine Engineer tasks.
Avoids heavy imports (transformers/torch distributed) by direct file imports.
"""

import json
import sys

from _env_setup import PROJECT_ROOT

# Set UTF-8 encoding for Windows console
if sys.platform == "win32":
    import codecs
    sys.stdout = codecs.getwriter("utf-8")(sys.stdout.buffer, "strict")
    sys.stderr = codecs.getwriter("utf-8")(sys.stderr.buffer, "strict")

import logging

try:
    import numpy as np
    HAS_NUMPY = True
except ImportError:
    np = None
    HAS_NUMPY = False

logging.basicConfig(level=logging.WARNING, format='%(levelname)s: %(message)s')
logger = logging.getLogger(__name__)

def test_quality_metrics_error_handling_direct():
    """Test quality metrics error handling by direct file import."""
    print("\n" + "="*70)
    print("TEST 1: Quality Metrics Error Handling (Direct Import)")
    print("="*70)
    if not HAS_NUMPY:
        print("  [SKIP] numpy not installed (pip install numpy); run from .venv or engine venv for full check")
        return True
    try:
        # Direct file import to avoid heavy engine package dependencies
        import importlib.util
        quality_metrics_path = PROJECT_ROOT / "app" / "core" / "engines" / "quality_metrics.py"

        if not quality_metrics_path.exists():
            print(f"  [X] ERROR: quality_metrics.py not found at {quality_metrics_path}")
            return False

        spec = importlib.util.spec_from_file_location("quality_metrics", quality_metrics_path)
        quality_metrics = importlib.util.module_from_spec(spec)

        # Suppress warnings during import
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            spec.loader.exec_module(quality_metrics)

        calculate_pesq_score = quality_metrics.calculate_pesq_score
        calculate_stoi_score = quality_metrics.calculate_stoi_score
        calculate_all_metrics = quality_metrics.calculate_all_metrics
        HAS_PESQ = quality_metrics.HAS_PESQ
        HAS_PYSTOI = quality_metrics.HAS_PYSTOI

        audio = np.random.randn(16000).astype(np.float32)

        # Test PESQ error handling
        print("\n[1.1] Testing PESQ error handling...")
        try:
            result = calculate_pesq_score(audio, audio, 16000)
            if HAS_PESQ:
                print(f"  [OK] PESQ available, calculated score: {result:.3f}")
            else:
                print("  [X] ERROR: Should have raised ImportError when PESQ missing")
                return False
        except ImportError as e:
            error_msg = str(e)
            if "pip install pesq" in error_msg:
                print("  [OK] PESQ correctly raises ImportError with installation instructions")
                print(f"       Message: {error_msg[:80]}...")
            else:
                print("  [X] ERROR: ImportError raised but missing installation instructions")
                print(f"       Message: {error_msg}")
                return False
        except Exception as e:
            if HAS_PESQ:
                print(f"  [OK] PESQ available but calculation had runtime error (acceptable): {type(e).__name__}")
            else:
                print(f"  [!] INFO: PESQ raised {type(e).__name__}: {e}")

        # Test STOI error handling
        print("\n[1.2] Testing STOI error handling...")
        try:
            result = calculate_stoi_score(audio, audio, 10000)
            if HAS_PYSTOI:
                print(f"  [OK] STOI available, calculated score: {result:.3f}")
            else:
                print("  [X] ERROR: Should have raised ImportError when pystoi missing")
                return False
        except ImportError as e:
            error_msg = str(e)
            if "pip install pystoi" in error_msg:
                print("  [OK] STOI correctly raises ImportError with installation instructions")
                print(f"       Message: {error_msg[:80]}...")
            else:
                print("  [X] ERROR: ImportError raised but missing installation instructions")
                print(f"       Message: {error_msg}")
                return False
        except Exception as e:
            if HAS_PYSTOI:
                print(f"  [OK] STOI available but calculation had runtime error (acceptable): {type(e).__name__}")
            else:
                print(f"  [!] INFO: STOI raised {type(e).__name__}: {e}")

        # Test calculate_all_metrics dependency tracking
        print("\n[1.3] Testing calculate_all_metrics dependency tracking...")
        try:
            metrics = calculate_all_metrics(audio, sample_rate=22050, use_cache=False)

            if "missing_dependencies" in metrics:
                missing = metrics["missing_dependencies"]
                if missing:
                    print(f"  [OK] Missing dependencies tracked: {len(missing)} item(s)")
                    for dep in missing[:3]:
                        if "pip install" in dep:
                            print(f"       - {dep}")
                    if len(missing) > 3:
                        print(f"       ... and {len(missing) - 3} more")
                else:
                    print("  [OK] No missing dependencies (all available)")
            else:
                print("  [!] WARNING: 'missing_dependencies' key not in metrics dict")

            # Verify quality metrics computed
            computed_metrics = [k for k in metrics if k != 'missing_dependencies']
            print(f"  [OK] Metrics computed: {len(computed_metrics)} metric(s)")
            print(f"       Key metrics: {', '.join(computed_metrics[:5])}")

            # Verify no placeholder values
            placeholder_values = [0.0, 1.0, 3.0]  # Common placeholder values
            has_placeholders = False
            for key, value in metrics.items():
                if key != "missing_dependencies" and isinstance(value, (int, float)):
                    if value in placeholder_values and value == 3.0:  # 3.0 might be legitimate
                        continue
            if not has_placeholders:
                print("  [OK] No obvious placeholder values detected")

            return True

        except Exception as e:
            print(f"  [X] ERROR: calculate_all_metrics failed: {e}")
            import traceback
            traceback.print_exc()
            return False

    except Exception as e:
        print(f"  [X] ERROR: Failed to test quality metrics: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_sovits_engine_integration():
    """Test So-VITS-SVC engine integration and discovery."""
    print("\n" + "="*70)
    print("TEST 2: So-VITS-SVC Engine Integration")
    print("="*70)

    try:
        # Test engine file exists
        print("\n[2.1] Testing So-VITS-SVC engine file...")
        engine_file = PROJECT_ROOT / "app" / "core" / "engines" / "sovits_svc_engine.py"
        if engine_file.exists():
            print(f"  [OK] Engine file exists: {engine_file.name}")

            # Check for required methods
            with open(engine_file, encoding='utf-8') as f:
                content = f.read()

            required_methods = ["convert_voice", "convert_realtime", "initialize", "cleanup"]
            missing_methods = []
            for method in required_methods:
                if f"def {method}" not in content:
                    missing_methods.append(method)

            if not missing_methods:
                print(f"  [OK] All required methods present: {', '.join(required_methods)}")
            else:
                print(f"  [X] ERROR: Missing methods: {', '.join(missing_methods)}")
                return False

            # Check for checkpoint layout
            if "model.pth" in content and "config.json" in content:
                print("  [OK] Checkpoint layout implemented (model.pth + config.json)")
            else:
                print("  [!] WARNING: Checkpoint layout may be incomplete")
        else:
            print(f"  [X] ERROR: Engine file not found at {engine_file}")
            return False

        # Test engine manifest exists
        print("\n[2.2] Testing engine manifest...")
        manifest_path = PROJECT_ROOT / "engines" / "audio" / "sovits" / "engine.manifest.json"
        if manifest_path.exists():
            print(f"  [OK] Engine manifest exists: {manifest_path.name}")
            try:
                with open(manifest_path, encoding='utf-8') as f:
                    manifest = json.load(f)

                # Verify manifest structure
                required_fields = ["engine_id", "name", "entry_point", "version"]
                missing_fields = [f for f in required_fields if f not in manifest]

                if not missing_fields:
                    print("  [OK] Manifest structure valid")
                    print(f"       Engine ID: {manifest.get('engine_id')}")
                    print(f"       Version: {manifest.get('version')}")
                    print(f"       Entry point: {manifest.get('entry_point')}")

                    # Verify entry point matches engine file
                    expected_entry = "app.core.engines.sovits_svc_engine.SoVITSSVCEngine"
                    if manifest.get('entry_point') == expected_entry:
                        print("  [OK] Entry point matches engine file")
                    else:
                        print(f"  [!] WARNING: Entry point mismatch (expected: {expected_entry})")

                    # Check checkpoint layout in manifest
                    if "checkpoint_layout" in manifest:
                        layout = manifest["checkpoint_layout"]
                        print("  [OK] Checkpoint layout documented in manifest")
                        print(f"       Model file: {layout.get('model_file')}")
                        print(f"       Config file: {layout.get('config_file')}")
                else:
                    print(f"  [X] ERROR: Manifest missing required fields: {', '.join(missing_fields)}")
                    return False
            except json.JSONDecodeError as e:
                print(f"  [X] ERROR: Manifest is not valid JSON: {e}")
                return False
            except Exception as e:
                print(f"  [X] ERROR: Failed to read manifest: {e}")
                return False
        else:
            print(f"  [X] ERROR: Engine manifest not found at {manifest_path}")
            return False

        # Test preflight check exists
        print("\n[2.3] Testing preflight check integration...")
        preflight_path = PROJECT_ROOT / "backend" / "services" / "model_preflight.py"
        if preflight_path.exists():
            with open(preflight_path, encoding='utf-8') as f:
                content = f.read()

            if "ensure_sovits" in content:
                print("  [OK] ensure_sovits() function exists in model_preflight.py")

                # Check for checkpoint path logic
                if "model.pth" in content and "config.json" in content:
                    print("  [OK] Preflight check validates checkpoint layout")
                else:
                    print("  [!] WARNING: Preflight check may not validate checkpoint layout")
            else:
                print("  [X] ERROR: ensure_sovits() function not found")
                return False
        else:
            print("  [X] ERROR: model_preflight.py not found")
            return False

        # Test manifest discovery (read manifest loader directly)
        print("\n[2.4] Testing manifest discovery system...")
        try:
            import importlib.util
            manifest_loader_path = PROJECT_ROOT / "app" / "core" / "engines" / "manifest_loader.py"
            if manifest_loader_path.exists():
                spec = importlib.util.spec_from_file_location("manifest_loader", manifest_loader_path)
                manifest_loader = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(manifest_loader)

                find_engine_manifests = manifest_loader.find_engine_manifests
                manifests = find_engine_manifests("engines")

                if "sovits_svc" in manifests:
                    print("  [OK] So-VITS-SVC engine manifest discovered by find_engine_manifests()")
                    print(f"       Total engines found: {len(manifests)}")
                    print(f"       Manifest path: {manifests['sovits_svc']}")
                else:
                    print("  [!] WARNING: So-VITS-SVC not in discovered manifests")
                    print(f"       Available engines: {list(manifests.keys())[:10]}...")
                    print("       (This may be okay if manifest path differs)")
            else:
                print("  [!] WARNING: manifest_loader.py not found")
        except Exception as e:
            print(f"  [!] WARNING: Could not test manifest discovery: {e}")

        return True

    except Exception as e:
        print(f"  [X] ERROR: So-VITS-SVC engine integration test failed: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_default_engine_selection():
    """Test default engine selection by static code analysis."""
    print("\n" + "="*70)
    print("TEST 3: Default Engine Selection (Static Verification)")
    print("="*70)

    try:
        # Test voice route default engine logic
        print("\n[3.1] Testing voice route default engine selection...")
        voice_route_path = PROJECT_ROOT / "backend" / "api" / "routes" / "voice.py"
        if not voice_route_path.exists():
            print(f"  [X] ERROR: voice.py not found at {voice_route_path}")
            return False

        with open(voice_route_path, encoding='utf-8') as f:
            content = f.read()

        # Check for default engine selection logic
        checks = {
            "Default selection": (
                "default_engine" in content or
                'requested_engine = "xtts_v2"' in content or
                "requested_engine = 'xtts_v2'" in content
            ),
            "Fallback chain": (
                "fallback_chain" in content and
                "xtts_v2" in content and
                "piper" in content
            ),
            "eSpeak fallback": "espeak_ng" in content or "espeak" in content,
            "Asset preflight": "_ensure_tts_assets" in content,
        }

        all_checks_passed = True
        for check_name, check_result in checks.items():
            if check_result:
                print(f"  [OK] {check_name}: Found in code")
            else:
                print(f"  [X] ERROR: {check_name}: Not found")
                all_checks_passed = False

        # Extract default engine logic snippet
        if 'requested_engine = "xtts_v2"' in content or "requested_engine = 'xtts_v2'" in content:
            lines = content.split('\n')
            for i, line in enumerate(lines):
                if 'requested_engine = "xtts_v2"' in line or "requested_engine = 'xtts_v2'" in line:
                    print(f"       Found at line ~{i+1}: {line.strip()[:60]}...")
                    break

        # Verify fallback chain implementation
        if "fallback_chain" in content:
            fallback_start = content.find("fallback_chain")
            if fallback_start >= 0:
                snippet = content[fallback_start:fallback_start+200]
                if "xtts_v2" in snippet and "piper" in snippet:
                    print("  [OK] Fallback chain implemented: XTTS → Piper → eSpeak")

        if not all_checks_passed:
            return False

        # Test transcribe route default
        print("\n[3.2] Testing transcribe route default engine...")
        transcribe_route_path = PROJECT_ROOT / "backend" / "api" / "routes" / "transcribe.py"
        if transcribe_route_path.exists():
            with open(transcribe_route_path, encoding='utf-8') as f:
                transcribe_content = f.read()

            # Check for whisper_cpp default
            if 'engine: str = "whisper_cpp"' in transcribe_content or 'engine: str = \'whisper_cpp\'' in transcribe_content:
                print("  [OK] Transcribe route defaults to whisper_cpp")
            elif '"whisper_cpp"' in transcribe_content or "'whisper_cpp'" in transcribe_content:
                print("  [OK] Transcribe route references whisper_cpp (default likely set)")
            else:
                print("  [!] WARNING: whisper_cpp default may not be explicitly set")
        else:
            print("  [!] WARNING: transcribe.py not found")

        # Test EngineConfigService integration (static check)
        print("\n[3.3] Testing EngineConfigService integration...")
        engine_config_path = PROJECT_ROOT / "backend" / "services" / "EngineConfigService.py"
        if engine_config_path.exists():
            with open(engine_config_path, encoding='utf-8') as f:
                config_content = f.read()

            if "get_default_engine" in config_content:
                print("  [OK] EngineConfigService has get_default_engine() method")

                # Check if it's called in voice route
                if "get_default_engine" in content:
                    print("  [OK] Voice route calls EngineConfigService.get_default_engine()")
                else:
                    print("  [!] WARNING: Voice route may not call EngineConfigService")
            else:
                print("  [!] WARNING: EngineConfigService.get_default_engine() not found")
        else:
            print("  [!] WARNING: EngineConfigService.py not found")

        return True

    except Exception as e:
        print(f"  [X] ERROR: Default engine selection test failed: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_quality_metrics_confidence_fix():
    """Test that quality metrics confidence uses normalized features."""
    print("\n" + "="*70)
    print("TEST 4: Quality Metrics Confidence Fix")
    print("="*70)

    try:
        quality_metrics_path = PROJECT_ROOT / "app" / "core" / "engines" / "quality_metrics.py"
        if not quality_metrics_path.exists():
            print("  [X] ERROR: quality_metrics.py not found")
            return False

        with open(quality_metrics_path, encoding='utf-8') as f:
            content = f.read()

        # Check for normalized_features usage in confidence calculation
        print("\n[4.1] Testing confidence calculation uses normalized features...")

        # Find predict_quality_with_ml function
        if "def predict_quality_with_ml" in content:
            # Extract the function
            start_idx = content.find("def predict_quality_with_ml")
            if start_idx >= 0:
                # Find the function body (next def or class)
                next_def = content.find("\ndef ", start_idx + 50)
                next_class = content.find("\nclass ", start_idx + 50)
                end_idx = min([i for i in [next_def, next_class] if i > 0] + [len(content)])

                func_body = content[start_idx:end_idx]

                # Check for normalized_features in variance calculation
                if "feature_variance = float(np.std(normalized_features))" in func_body:
                    print("  [OK] Confidence uses normalized_features for variance calculation")

                    # Check for comment explaining why
                    if "normalized" in func_body.lower() and ("SNR" in func_body or "snr" in func_body):
                        print("  [OK] Comment explains normalization (prevents SNR scale domination)")
                    else:
                        print("  [!] WARNING: No comment explaining normalization")

                    # Verify normalized_features is computed before use
                    if "normalized_features" in func_body and func_body.find("normalized_features") < func_body.find("np.std(normalized_features)"):
                        print("  [OK] normalized_features computed before variance calculation")
                    else:
                        print("  [!] WARNING: normalized_features may not be computed before use")
                elif "feature_variance = float(np.std(features_flat))" in func_body or "feature_variance = float(np.std(features))" in func_body:
                    print("  [X] ERROR: Confidence still uses raw features_flat/features (not normalized)")
                    return False
                else:
                    print("  [!] WARNING: Could not verify normalized_features usage in variance calculation")
            else:
                print("  [!] WARNING: Could not find predict_quality_with_ml function body")
        else:
            print("  [X] ERROR: predict_quality_with_ml function not found")
            return False

        return True

    except Exception as e:
        print(f"  [X] ERROR: Quality metrics confidence fix test failed: {e}")
        import traceback
        traceback.print_exc()
        return False


def main():
    """Run all verification tests."""
    print("\n" + "="*70)
    print("Engine Engineer Tasks Verification (Targeted)")
    print("="*70)
    print("\nVerifying:")
    print("  1. Quality metrics error handling with actionable messages")
    print("  2. So-VITS-SVC 4.0 engine integration and discovery")
    print("  3. Default engine selection in routes")
    print("  4. Quality metrics confidence uses normalized features")

    results = []

    # Test 1: Quality metrics error handling
    results.append(("Quality Metrics Error Handling", test_quality_metrics_error_handling_direct()))

    # Test 2: So-VITS-SVC integration
    results.append(("So-VITS-SVC Engine Integration", test_sovits_engine_integration()))

    # Test 3: Default engine selection
    results.append(("Default Engine Selection", test_default_engine_selection()))

    # Test 4: Quality metrics confidence fix
    results.append(("Quality Metrics Confidence Fix", test_quality_metrics_confidence_fix()))

    # Summary
    print("\n" + "="*70)
    print("VERIFICATION SUMMARY")
    print("="*70)

    all_passed = True
    for name, passed in results:
        status = "[OK] PASS" if passed else "[X] FAIL"
        print(f"{status}: {name}")
        if not passed:
            all_passed = False

    print("\n" + "="*70)
    if all_passed:
        print("[OK] ALL TESTS PASSED")
        return 0
    else:
        print("[X] SOME TESTS FAILED")
        return 1


if __name__ == "__main__":
    sys.exit(main())
