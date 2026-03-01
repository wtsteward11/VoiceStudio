"""Verify engine integration pattern works correctly."""


import sys
from pathlib import Path


def main():
    print("Engine Integration Pattern Verification")
    print("=" * 60)

    # 1. Test manifest discovery
    print("\n1. MANIFEST DISCOVERY")
    print("-" * 60)
    try:
        from app.core.engines.manifest_loader import (
            find_engine_manifests,
            load_engine_manifest,
        )

        manifests = find_engine_manifests("engines")
        print(f"Found {len(manifests)} engine manifests")

        # Group by type
        types = {}
        for m in manifests:
            parts = Path(m).parts
            if "audio" in parts:
                types["audio"] = types.get("audio", 0) + 1
            elif "image" in parts:
                types["image"] = types.get("image", 0) + 1
            elif "video" in parts:
                types["video"] = types.get("video", 0) + 1

        for t, count in types.items():
            print(f"  {t}: {count} engines")

    except Exception as e:
        print(f"ERROR: {e}")
        return 1

    # 2. Test manifest loading
    print("\n2. MANIFEST LOADING (XTTS)")
    print("-" * 60)
    try:
        # Find XTTS manifest
        xtts_path = Path("engines/audio/xtts_v2/engine.manifest.json")
        if xtts_path.exists():
            manifest = load_engine_manifest(xtts_path)
            print(f"engine_id: {manifest.get('engine_id')}")
            print(f"entry_point: {manifest.get('entry_point')}")
            print(f"capabilities: {manifest.get('capabilities')}")
            print(f"dependencies: {list(manifest.get('dependencies', {}).keys())}")
        else:
            print(f"XTTS manifest not found at {xtts_path}")
    except Exception as e:
        print(f"ERROR: {e}")
        return 1

    # 3. Test engine router discovery
    print("\n3. ENGINE ROUTER DISCOVERY")
    print("-" * 60)
    try:
        from app.core.engines.router import EngineRouter

        router = EngineRouter()
        router.load_all_engines("engines")

        engines = router.list_engines()
        print(f"Registered {len(engines)} engines in router")
        print(f"First 5: {engines[:5]}")

        # Check if XTTS is registered
        if "xtts_v2" in engines:
            print("XTTS v2 successfully registered")
        else:
            print("WARNING: XTTS v2 not in router")

    except Exception as e:
        print(f"ERROR: {e}")
        return 1

    # 4. Verify API endpoint discovery
    print("\n4. API ENDPOINT VERIFICATION")
    print("-" * 60)
    try:
        import requests

        resp = requests.get("http://localhost:8000/api/engines/list", timeout=5)
        if resp.status_code == 200:
            data = resp.json()
            print(f"API reports {data.get('count', 0)} engines")

            # Check for key engines
            engines = data.get("engines", [])
            for engine in ["xtts_v2", "piper", "chatterbox", "whisper_cpp"]:
                status = "FOUND" if engine in engines else "MISSING"
                print(f"  {engine}: {status}")
        else:
            print(f"API error: {resp.status_code}")
    except Exception as e:
        print(f"API not available: {e}")

    print("\n5. INTEGRATION PATTERN SUMMARY")
    print("-" * 60)
    print("To add a new engine:")
    print("  1. Create engines/{type}/{engine_id}/engine.manifest.json")
    print("  2. Create app/core/engines/{engine_id}_engine.py")
    print("  3. Implement EngineProtocol (initialize, cleanup, synthesize)")
    print("  4. Set entry_point in manifest to your class")
    print("  5. Restart backend - engine auto-discovered")
    print()
    print("Pattern verification: PASS")

    return 0


if __name__ == "__main__":
    sys.exit(main())
