import ast
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXCLUDE_DIRS = {
    ".git",
    ".venv",
    "venv",
    "__pycache__",
    "site-packages",
    "build",
    "dist",
    "tests",
    ".mypy_cache",
    ".pytest_cache",
}


def iter_py_files(root: pathlib.Path):
    for p in root.rglob("*.py"):
        if any(part in EXCLUDE_DIRS for part in p.parts):
            continue
        yield p


def test_all_python_files_parse():
    failures = []
    for p in iter_py_files(ROOT):
        try:
            ast.parse(p.read_text(encoding="utf-8"), filename=str(p))
        except Exception as e:
            failures.append((str(p), repr(e)))
    if failures:
        msg = "\n".join(f"{path}: {err}" for path, err in failures)
        raise AssertionError(f"Syntax errors found:\n{msg}")
