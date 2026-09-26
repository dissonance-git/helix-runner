#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import tarfile
import tempfile
import time
import uuid
from pathlib import Path
from urllib.parse import quote
from urllib.request import Request, urlopen

AUDIENCE = "helix-supabase-workspace"
PROJECT_URL = "https://fpzqpoaeaydcrlvbxgwo.supabase.co"
EDGE_URL = PROJECT_URL + "/functions/v1/helix-workspace"
REQUEST_FILE = Path(__file__).resolve().parent / "workspace-request.json"


def _json_request(url: str, payload: dict, headers: dict[str, str] | None = None) -> dict:
    req = Request(
        url,
        data=json.dumps(payload, separators=(",", ":")).encode("utf-8"),
        method="POST",
        headers={"content-type": "application/json", **(headers or {})},
    )
    with urlopen(req, timeout=90) as response:
        value = json.loads(response.read() or b"{}")
    if not isinstance(value, dict) or value.get("ok") is not True:
        raise RuntimeError(str(value.get("error") if isinstance(value, dict) else "invalid response"))
    return value


def _oidc() -> str:
    base = os.environ.get("ACTIONS_ID_TOKEN_REQUEST_URL", "")
    bearer = os.environ.get("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "")
    if not base or not bearer:
        raise RuntimeError("GitHub OIDC environment is unavailable")
    sep = "&" if "?" in base else "?"
    req = Request(
        base + sep + "audience=" + quote(AUDIENCE, safe=""),
        headers={"Authorization": "bearer " + bearer},
    )
    with urlopen(req, timeout=30) as response:
        value = json.loads(response.read() or b"{}")
    token = str(value.get("value") or "")
    if not token:
        raise RuntimeError("GitHub OIDC token was not issued")
    return token


def _edge(action: str, **payload) -> dict:
    return _json_request(EDGE_URL, {"action": action, "oidc_token": _oidc(), **payload})


def _job_id(raw: str) -> str:
    value = raw.strip()
    if not value:
        body = json.loads(REQUEST_FILE.read_text(encoding="utf-8"))
        value = str(body.get("job_id") or "")
    return str(uuid.UUID(value))


def _download(url: str, path: Path, expected_bytes: int, expected_sha256: str) -> None:
    digest = hashlib.sha256()
    size = 0
    with urlopen(url, timeout=180) as response, path.open("wb") as output:
        while True:
            block = response.read(1024 * 1024)
            if not block:
                break
            output.write(block)
            digest.update(block)
            size += len(block)
    if expected_bytes and size != expected_bytes:
        raise RuntimeError(f"source chunk size mismatch: expected {expected_bytes}, found {size}")
    if expected_sha256 and digest.hexdigest() != expected_sha256:
        raise RuntimeError("source chunk digest mismatch")


def _source(job_id: str) -> dict:
    deadline = time.monotonic() + 300
    while True:
        value = _edge("github_source", job_id=job_id)
        if not value.get("pending"):
            source = value.get("source")
            if not isinstance(source, dict):
                raise RuntimeError("workspace source metadata is missing")
            return source
        state = str(value.get("source_status") or "")
        if state in {"failed", "cancelled", "closed"}:
            raise RuntimeError(str(value.get("source_error") or f"source staging {state}"))
        if time.monotonic() >= deadline:
            raise TimeoutError("workspace source staging timed out")
        time.sleep(1)


def _safe_extract(bundle: Path, root: Path) -> None:
    root = root.resolve()
    with tarfile.open(bundle, "r:gz") as archive:
        for member in archive.getmembers():
            target = (root / member.name).resolve(strict=False)
            if not target.is_relative_to(root):
                raise RuntimeError("workspace bundle path escapes extraction root")
            if member.isdev() or member.isfifo():
                raise RuntimeError("workspace bundle contains a special file")
        archive.extractall(root, filter="data")


def _git(repo: Path, *args: str) -> None:
    subprocess.run(["git", *args], cwd=repo, check=True)


def _materialize(source: dict, root: Path) -> tuple[Path, dict[str, str]]:
    chunks = sorted(source.get("chunks") or [], key=lambda row: int(row.get("index") or 0))
    if not chunks:
        raise RuntimeError("workspace source contains no chunks")

    bundle = root / "source.tar.gz"
    whole = hashlib.sha256()
    with bundle.open("wb") as output:
        for row in chunks:
            part = root / f"part-{int(row['index']):05d}.bin"
            _download(
                str(row.get("signed_url") or ""),
                part,
                int(row.get("bytes") or 0),
                str(row.get("sha256") or ""),
            )
            data = part.read_bytes()
            output.write(data)
            whole.update(data)
            part.unlink()

    expected = str(source.get("sha256") or "")
    if expected and whole.hexdigest() != expected:
        raise RuntimeError("workspace source bundle digest mismatch")

    extracted = root / "source"
    extracted.mkdir()
    _safe_extract(bundle, extracted)
    manifest = json.loads((extracted / ".helix-workspace.json").read_text(encoding="utf-8"))
    repositories = manifest.get("repositories") or {}
    if "helix" not in repositories:
        raise RuntimeError("workspace source has no helix repository")

    mapping: dict[str, str] = {}
    for key, meta in repositories.items():
        repo = (extracted / str(key)).resolve()
        if not repo.is_dir():
            raise RuntimeError(f"workspace source repository is missing: {key}")
        _git(repo, "init", "-b", "main")
        _git(repo, "config", "user.name", "Helix GitHub Workspace")
        _git(repo, "config", "user.email", "workspace@helix.invalid")
        _git(repo, "add", "-f", "-A")
        subprocess.run(
            ["git", "commit", "--allow-empty", "-m", "Ephemeral workspace source " + str(meta.get("head") or "")],
            cwd=repo,
            check=True,
            stdout=subprocess.DEVNULL,
        )
        remote = str(meta.get("remote") or "").strip() or "https://invalid.example/helix-workspace"
        _git(repo, "remote", "add", "origin", remote)
        mapping[str(key)] = str(repo)
    return Path(mapping["helix"]), mapping


def main() -> int:
    job_id = _job_id(sys.argv[1] if len(sys.argv) > 1 else "")
    paired = _edge("github_pair")
    worker_id = str(paired.get("worker_id") or "")
    worker_token = str(paired.get("worker_token") or "")
    if not worker_id or not worker_token:
        raise RuntimeError("ephemeral worker identity was not issued")

    try:
        source = _source(job_id)
        with tempfile.TemporaryDirectory(prefix="helix-github-workspace-") as td:
            root = Path(td)
            helix, mapping = _materialize(source, root)
            env = os.environ.copy()
            env.update(
                {
                    "HELIX_WORKSPACE_URL": PROJECT_URL,
                    "HELIX_WORKSPACE_WORKER_ID": worker_id,
                    "HELIX_WORKSPACE_TOKEN": worker_token,
                    "HELIX_WORKSPACE_REPOS": json.dumps(mapping, separators=(",", ":")),
                    "HELIX_WORKSPACE_REPOSITORY_OWNERSHIP": "operator",
                    "HELIX_WORKSPACE_API_ROOT": str(helix),
                    "HELIX_WORKSPACE_ROOT": str(root),
                    "HELIX_MACHINE_ROOT": str(root / ".helix"),
                    "HELIX_STATE_ROOT": str(root / ".helix" / "state"),
                    "HELIX_TOOLS_ROOT": str(root / ".helix" / "tools"),
                    "HELIX_API_STATE": str(root / ".helix" / "api" / "state.json"),
                    "HELIX_WORKSPACE_SESSION_ROOT": str(root / "sessions"),
                }
            )
            completed = subprocess.run(
                [sys.executable, str(helix / "workspace" / "worker.py"), "once", "--job-id", job_id],
                env=env,
            )
            if completed.returncode != 0:
                raise RuntimeError(f"canonical workspace worker exited {completed.returncode}")
        return 0
    finally:
        try:
            _edge("github_retire", worker_token=worker_token)
        except Exception as exc:
            print(f"workspace retire warning: {exc}", file=sys.stderr)


if __name__ == "__main__":
    raise SystemExit(main())
