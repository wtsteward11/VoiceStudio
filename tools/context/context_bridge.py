"""
Context Bridge -- connects the VoiceStudio context manager to Cursor's agent system.

Reads STATE.md, classifies the current task using task_keywords.json and skill_map.json,
and outputs structured context text that Cursor injects via hooks_context.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
CONFIG_DIR = Path(__file__).resolve().parent / "config"
STATE_PATH = REPO_ROOT / ".cursor" / "STATE.md"
SKILL_MAP_PATH = CONFIG_DIR / "skill_map.json"
TASK_KEYWORDS_PATH = CONFIG_DIR / "task_keywords.json"


def _load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    with path.open("r", encoding="utf-8") as f:
        result: dict[str, Any] = json.load(f)
    return result


def _read_state() -> dict[str, str]:
    """Extract key fields from STATE.md."""
    result: dict[str, str] = {
        "phase": "unknown",
        "active_task": "None",
        "blockers": "None",
        "next_step": "None",
    }
    if not STATE_PATH.exists():
        return result

    text = STATE_PATH.read_text(encoding="utf-8", errors="replace")
    lines = text.splitlines()

    for i, line in enumerate(lines):
        stripped = line.strip()
        if stripped.startswith("- **Phase**:"):
            result["phase"] = stripped.split(":", 1)[1].strip().strip("*")
        elif stripped.startswith("- **Master Plan Phase**:"):
            result["master_phase"] = stripped.split(":", 1)[1].strip().strip("*")
        elif stripped.startswith("- **Active Task**:") or stripped.startswith("## Active Task"):
            val = stripped.split(":", 1)[1].strip().strip("*") if ":" in stripped else ""
            if val:
                result["active_task"] = val
            elif i + 1 < len(lines) and lines[i + 1].strip():
                result["active_task"] = lines[i + 1].strip().lstrip("- ").strip("*")
        elif "blocker" in stripped.lower() and ":" in stripped:
            result["blockers"] = stripped.split(":", 1)[1].strip().strip("*")
        elif stripped.startswith("- **Next Step**:") or stripped.startswith("## Next"):
            val = stripped.split(":", 1)[1].strip().strip("*") if ":" in stripped else ""
            if val:
                result["next_step"] = val

    return result


def classify_task(text: str) -> list[dict[str, Any]]:
    """
    Classify a task description against task_keywords.json roles.

    Returns a sorted list of {role, score, keywords_matched} dicts,
    highest score first.
    """
    keywords_config = _load_json(TASK_KEYWORDS_PATH)
    roles = keywords_config.get("roles", {})
    thresholds = keywords_config.get("confidence_thresholds", {})
    boosters = keywords_config.get("boosters", {})
    min_match = thresholds.get("min_match", 0.2)

    text_lower = text.lower()
    results = []

    for role_name, role_data in roles.items():
        keywords = role_data.get("keywords", [])
        if not keywords:
            continue

        matched = [kw for kw in keywords if kw.lower() in text_lower]
        if not matched:
            continue

        score = len(matched) / len(keywords)

        if len(matched) > 1:
            score += boosters.get("multiple_keywords", 0.1)

        for kw in matched:
            if f" {kw.lower()} " in f" {text_lower} ":
                score += boosters.get("exact_match", 0.15)
                break

        file_patterns = role_data.get("file_patterns", [])
        for pat in file_patterns:
            pat_clean = pat.replace("*", "").replace("/", "\\").lower()
            if pat_clean and pat_clean in text_lower:
                score += boosters.get("file_pattern_match", 0.2)
                break

        if score >= min_match:
            results.append({
                "role": role_name,
                "display_name": role_data.get("display_name", role_name),
                "score": round(score, 3),
                "keywords_matched": matched,
            })

    results.sort(key=lambda x: x["score"], reverse=True)
    return results


def match_skills(text: str) -> dict[str, Any]:
    """
    Match text against skill_map.json triggers.

    Returns {trigger_name: {skills, tools, files, score}} for all matching triggers,
    plus a merged 'recommended' key with deduplicated lists.
    """
    skill_map = _load_json(SKILL_MAP_PATH)
    triggers = skill_map.get("triggers", {})
    fallback = skill_map.get("fallback", {})

    text_lower = text.lower()
    matches: dict[str, Any] = {}

    for trigger_name, trigger_data in triggers.items():
        patterns = trigger_data.get("patterns", [])
        matched_patterns = [p for p in patterns if p.lower() in text_lower]
        if not matched_patterns:
            continue
        matches[trigger_name] = {
            "score": len(matched_patterns) / max(len(patterns), 1),
            "patterns_matched": matched_patterns,
            "skills": trigger_data.get("skills", []),
            "tools": trigger_data.get("tools", []),
            "files": trigger_data.get("files", []),
        }

    all_skills: list[str] = []
    all_tools: list[str] = []
    all_files: list[str] = []

    if matches:
        sorted_triggers = sorted(matches.items(), key=lambda x: x[1]["score"], reverse=True)
        for _, data in sorted_triggers:
            all_skills.extend(data["skills"])
            all_tools.extend(data["tools"])
            all_files.extend(data["files"])
    else:
        all_skills = fallback.get("skills", [])
        all_tools = fallback.get("tools", [])
        all_files = fallback.get("files", [])

    def _dedup(lst: list[str]) -> list[str]:
        seen: set[str] = set()
        out: list[str] = []
        for item in lst:
            if item not in seen:
                seen.add(item)
                out.append(item)
        return out

    return {
        "triggers_matched": matches,
        "recommended": {
            "skills": _dedup(all_skills),
            "tools": _dedup(all_tools),
            "files": _dedup(all_files),
        },
    }


def detect_failure(shell_output: str) -> dict[str, Any] | None:
    """
    Detect build/test/lint failures from shell output.

    Returns a dict with failure_type, details, and recommended skills/tools,
    or None if no failure detected.
    """
    failure_patterns = [
        {
            "type": "csharp_build_error",
            "regex": r"error CS\d+",
            "label": "C# compiler error",
        },
        {
            "type": "xaml_silent_crash",
            "regex": r"exit code[:\s]+1",
            "label": "Possible XAML compiler silent crash",
        },
        {
            "type": "xaml_wmc_crash",
            "regex": r"WMC\d+",
            "label": "XAML compiler crash (WMC error)",
        },
        {
            "type": "msbuild_error",
            "regex": r"MSB\d{4}",
            "label": "MSBuild error",
        },
        {
            "type": "build_failed",
            "regex": r"Build FAILED",
            "label": "Build failed",
        },
        {
            "type": "pytest_failure",
            "regex": r"FAILED tests?/",
            "label": "Python test failure",
        },
        {
            "type": "dotnet_test_failure",
            "regex": r"Failed!\s+- Failed:",
            "label": ".NET test failure",
        },
        {
            "type": "ruff_error",
            "regex": r"Found \d+ error",
            "label": "Ruff lint error",
        },
        {
            "type": "mypy_error",
            "regex": r"error: .+ \[[\w-]+\]",
            "label": "Mypy type error",
        },
        {
            "type": "module_not_found",
            "regex": r"ModuleNotFoundError",
            "label": "Python module not found",
        },
        {
            "type": "backend_connection",
            "regex": r"(connection refused|ECONNREFUSED|port 8000.*in use)",
            "label": "Backend connection failure",
        },
        {
            "type": "app_crash",
            "regex": r"(UnhandledException|KERNELBASE\.dll|access violation|System\.Exception|Fatal error|FailFast)",
            "label": "Application crash",
        },
        {
            "type": "app_exit",
            "regex": r"(Process.*exited with code|exited unexpectedly|stopped working)",
            "label": "App exited unexpectedly",
        },
        {
            "type": "winui_crash",
            "regex": r"(Microsoft\.UI\.Xaml\.UnhandledException|XAML.*unhandled|XamlRoot.*null|COMException.*XAML)",
            "label": "WinUI framework crash",
        },
        {
            "type": "dll_missing",
            "regex": r"(DllNotFoundException|could not load.*\.dll|assembly.*not found|FileNotFoundException.*\.dll)",
            "label": "Missing DLL dependency",
        },
        {
            "type": "startup_failure",
            "regex": r"(initialization failed|startup.*failed|cannot start|app.*not.*respond)",
            "label": "Application startup failure",
        },
    ]

    for pattern in failure_patterns:
        match = re.search(pattern["regex"], shell_output, re.IGNORECASE)
        if match:
            skill_match = match_skills(shell_output)
            return {
                "failure_type": pattern["type"],
                "label": pattern["label"],
                "match": match.group(0),
                "recommended": skill_match["recommended"],
            }

    return None


def format_context_output(
    state: dict[str, str],
    role_matches: list[dict[str, Any]],
    skill_matches: dict[str, Any],
    failure: dict[str, Any] | None = None,
) -> str:
    """Format the context output for hooks_context injection."""
    lines: list[str] = []

    primary_role = role_matches[0]["display_name"] if role_matches else "general"
    phase = state.get("phase", "unknown")
    active_task = state.get("active_task", "None")

    lines.append(f"[Context Manager] Role: {primary_role} | Phase: {phase}")

    if active_task and active_task != "None":
        lines.append(f"Active task: {active_task}")

    if failure:
        lines.append(f"FAILURE DETECTED: {failure['label']} ({failure['match']})")

    recommended = skill_matches.get("recommended", {})
    skills = recommended.get("skills", [])
    tools = recommended.get("tools", [])
    files = recommended.get("files", [])

    if skills:
        lines.append(f"Relevant skills: {', '.join(skills[:5])}")
    if tools:
        lines.append(f"Key tools: {', '.join(tools[:5])}")
    if files:
        lines.append(f"Key files: {', '.join(files[:5])}")

    blockers = state.get("blockers", "None")
    if blockers and blockers != "None":
        lines.append(f"Blockers: {blockers}")

    return "\n".join(lines)


def run_session_context() -> str:
    """Generate context for a new session (sessionStart hook)."""
    state = _read_state()

    context_text = f"{state.get('phase', '')} {state.get('active_task', '')} {state.get('next_step', '')}"
    role_matches = classify_task(context_text)
    skill_matches = match_skills(context_text)

    return format_context_output(state, role_matches, skill_matches)


def run_shell_context(shell_output: str) -> str:
    """Generate context after a shell command (afterShellExecution hook)."""
    state = _read_state()
    failure = detect_failure(shell_output)

    if failure is None:
        return ""

    context_text = shell_output
    role_matches = classify_task(context_text)
    skill_matches = match_skills(context_text)

    if not skill_matches.get("recommended", {}).get("skills"):
        skill_matches = {"recommended": failure["recommended"]}

    return format_context_output(state, role_matches, skill_matches, failure)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--shell":
        if len(sys.argv) > 2:
            output = " ".join(sys.argv[2:])
        elif not sys.stdin.isatty():
            output = sys.stdin.read()
        else:
            output = ""
        result = run_shell_context(output)
        if result:
            print(result)
    elif len(sys.argv) > 1 and sys.argv[1] == "--classify":
        query = " ".join(sys.argv[2:]) if len(sys.argv) > 2 else ""
        roles = classify_task(query)
        skills = match_skills(query)
        print(json.dumps({"roles": roles, "skills": skills}, indent=2))
    else:
        print(run_session_context())
