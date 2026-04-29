"""JsonFileStore root must not resolve to repo backend/data during pytest (artifact hygiene)."""


def test_effect_chain_store_not_under_repo_backend_data() -> None:
    from pathlib import Path

    from backend.audio.effects.effect_chain_store import get_effect_chain_store

    store = get_effect_chain_store()
    root = Path(store._store._root).resolve()
    root_s = str(root).replace("\\", "/").lower()
    assert "backend/data/stores/effect_chains" not in root_s
    assert root_s.endswith("effect_chains")
