"""
Verify MCP server availability from cursor.mcp.json (or path in VOICESTUDIO_MCP_CONFIG).

For each configured server, runs a minimal connectivity check (start process, short timeout).
Output: pass/fail per server. Use --json for CI. Exit 0 only if all checked servers pass.
Skip servers whose key starts with "//" (comment keys).
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path


def find_mcp_config() -> Path:
    path_env = os.environ.get("VOICESTUDIO_MCP_CONFIG")
    if path_env:
        p = Path(path_env)
        if p.exists():
            return p
    root = Path(__file__).resolve().parents[1]
    return root / "cursor.mcp.json"


def load_servers(config_path: Path) -> list[tuple[str, dict]]:
    if not config_path.exists():
        return []
    raw = json.loads(config_path.read_text(encoding="utf-8"))
    servers = raw.get("mcpServers") or {}
    out = []
    for key, val in servers.items():
        if key.startswith("//") or not isinstance(val, dict):
            continue
        cmd = val.get("command")
        if not cmd:
            continue
        out.append((key, val))
    return out


def check_server(name: str, spec: dict, timeout_seconds: float = 3.0) -> tuple[bool, str]:
    cmd = spec.get("command")
    args = spec.get("args") or []
    env = os.environ.copy()
    for k, v in (spec.get("env") or {}).items():
        if isinstance(v, str) and v.startswith("${") and v.endswith("}"):
            env[k] = os.environ.get(v[2:-1], "")
        else:
            env[k] = str(v) if v is not None else ""
    try:
        proc = subprocess.Popen(
            [cmd] + [str(a) for a in args],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=env,
            cwd=Path(__file__).resolve().parents[1],
        )
        proc.wait(timeout=timeout_seconds)
        if proc.returncode == 0:
            return True, "ok"
        return False, f"exit {proc.returncode}"
    except subprocess.TimeoutExpired:
        proc.kill()
        proc.wait()
        return True, "ok (started)"
    except FileNotFoundError:
        return False, "command not found"
    except Exception as e:
        return False, str(e)


def main() -> int:
    ap = argparse.ArgumentParser(description="Verify MCP servers from cursor.mcp.json")
    ap.add_argument("--json", action="store_true", help="Emit JSON for CI")
    ap.add_argument("--config", type=Path, default=None, help="Path to MCP config (default: cursor.mcp.json)")
    ap.add_argument("--timeout", type=float, default=3.0, help="Per-server timeout seconds")
    args = ap.parse_args()
    config_path = args.config or find_mcp_config()
    servers = load_servers(config_path)
    if not servers:
        if args.json:
            print(json.dumps({"ok": True, "servers": [], "message": "no servers in config"}))
        else:
            print("No MCP servers found in", config_path)
        return 0
    results = []
    all_ok = True
    for name, spec in servers:
        ok, msg = check_server(name, spec, args.timeout)
        results.append({"name": name, "ok": ok, "message": msg})
        if not ok:
            all_ok = False
    if args.json:
        print(json.dumps({"ok": all_ok, "servers": results}, indent=2))
    else:
        for r in results:
            status = "PASS" if r["ok"] else "FAIL"
            print(f"  {status} {r['name']}: {r['message']}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
