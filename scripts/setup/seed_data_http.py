#!/usr/bin/env python3
"""
HTTP Seed Data Script
Populates the backend with demo data via API calls (does NOT touch in-memory globals).

Usage:
  python scripts/seed_data_http.py --backend http://localhost:8000

Requirements:
  pip install httpx
"""

import argparse
import asyncio
import sys

try:
    import httpx
except ImportError:
    print("ERROR: httpx not installed. Run: pip install httpx", file=sys.stderr)
    sys.exit(1)


async def check_health(client: httpx.AsyncClient) -> bool:
    try:
        resp = await client.get("/api/health")
        return resp.status_code == 200
    except Exception:
        return False


def _profile_payload(name: str, language: str, emotion: str | None = None) -> dict:
    return {
        "name": name,
        "language": language,
        "emotion": emotion,
        "tags": ["demo", "seed-data"],
    }


def _project_payload(name: str, profile_id: str | None) -> dict:
    payload = {
        "name": name,
        "description": f"Demo project: {name}",
        "metadata": {"source": "seed-data", "created_by": "seed_data_http"},
    }
    if profile_id:
        payload["default_profile_id"] = profile_id
    return payload


async def create_profile(client: httpx.AsyncClient, payload: dict) -> dict | None:
    try:
        resp = await client.post("/api/profiles", json=payload)
        if resp.status_code == 200:
            data = resp.json()
            print(
                f"[OK] Profile created: {data.get('id', 'unknown')} :: {payload['name']}"
            )
            return data
        print(
            f"[WARN] Failed to create profile {payload['name']} :: {resp.status_code}"
        )
    except Exception as e:
        print(f"[WARN] Exception creating profile {payload['name']}: {e}")
    return None


async def create_project(client: httpx.AsyncClient, payload: dict) -> dict | None:
    try:
        resp = await client.post("/api/projects", json=payload)
        if resp.status_code == 200:
            data = resp.json()
            print(
                f"[OK] Project created: {data.get('id', 'unknown')} :: {payload['name']}"
            )
            return data
        print(
            f"[WARN] Failed to create project {payload['name']} :: {resp.status_code}"
        )
    except Exception as e:
        print(f"[WARN] Exception creating project {payload['name']}: {e}")
    return None


async def main():
    parser = argparse.ArgumentParser(description="Seed data via backend HTTP API")
    parser.add_argument(
        "--backend", default="http://localhost:8000", help="Backend base URL"
    )
    parser.add_argument(
        "--timeout", type=float, default=30.0, help="HTTP timeout (seconds)"
    )
    args = parser.parse_args()

    async with httpx.AsyncClient(base_url=args.backend, timeout=args.timeout) as client:
        print(f"[INFO] Using backend: {args.backend}")
        if not await check_health(client):
            print(
                "[ERROR] Backend health check failed. Start backend first.",
                file=sys.stderr,
            )
            return 1

        # Create demo profiles
        profile_payloads = [
            _profile_payload("Demo Voice - English", "en", "neutral"),
            _profile_payload("Demo Voice - Spanish", "es", "neutral"),
            _profile_payload("Demo Voice - French", "fr", "neutral"),
            _profile_payload("Demo Voice - Happy", "en", "happy"),
            _profile_payload("Demo Voice - Professional", "en", "professional"),
        ]

        profiles = []
        for payload in profile_payloads:
            profile = await create_profile(client, payload)
            if profile:
                profiles.append(profile)
            await asyncio.sleep(0.2)

        default_profile_id = profiles[0].get("id") if profiles else None

        # Create demo projects
        project_names = [
            "Demo Project - Tutorial",
            "Demo Project - Voice Cloning",
            "Demo Project - Multi-Language",
            "Demo Project - Emotion Testing",
        ]

        for name in project_names:
            await create_project(client, _project_payload(name, default_profile_id))
            await asyncio.sleep(0.2)

        print(
            f"[DONE] Seed complete. Profiles: {len(profiles)}, Projects: {len(project_names)}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
