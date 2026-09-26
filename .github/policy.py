from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS = ROOT / ".github" / "workflows"
ALLOWED_IMAGES = {"windows-2025", "ubuntu-24.04"}
HEX40 = re.compile(r"^[0-9a-f]{40}$")
RETIRED_PATTERNS = (
    re.compile("gitlab" + r"[\s_-]*" + "runner", re.IGNORECASE),
    re.compile("self" + r"[\s_-]*" + "hosted" + r"[\s_-]+(?:github[\s_-]+)?" + "runner", re.IGNORECASE),
)


def fail(message: str) -> None:
    raise SystemExit(f"policy: {message}")


def active_text() -> str:
    chunks: list[str] = []
    for path in ROOT.rglob("*"):
        if not path.is_file() or ".git" in path.parts:
            continue
        if path.suffix.lower() not in {".md", ".py", ".ps1", ".yml", ".yaml", ".json"}:
            continue
        chunks.append(path.read_text(encoding="utf-8", errors="replace"))
    return "\n".join(chunks)


if (ROOT / ".gitlab-ci.yml").exists():
    fail("retired provider CI file is present")

corpus = active_text()
for pattern in RETIRED_PATTERNS:
    if pattern.search(corpus):
        fail("retired local execution terminology was reintroduced")

for path in sorted(WORKFLOWS.glob("*.y*ml")):
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()

    if "pull_request_target:" in text:
        fail(f"{path.name}: pull_request_target is not admitted")
    if "write-all" in text or "secrets: inherit" in text:
        fail(f"{path.name}: overly broad workflow authority")
    if "permissions:" not in text:
        fail(f"{path.name}: explicit permissions are required")

    runs = re.findall(r"(?m)^\s*runs-on:\s*([^#\n]+)", text)
    timeouts = re.findall(r"(?m)^\s*timeout-minutes:\s*(\d+)", text)
    if len(runs) != len(timeouts):
        fail(f"{path.name}: every job must have a timeout")
    for image in (value.strip().strip("'\"") for value in runs):
        if image not in ALLOWED_IMAGES:
            fail(f"{path.name}: execution image is not pinned/admitted: {image}")

    for match in re.finditer(r"(?m)^\s*uses:\s*([^\s#]+)", text):
        ref = match.group(1)
        if ref.startswith("./") or ref.startswith("docker://"):
            continue
        if "@" not in ref:
            fail(f"{path.name}: action without immutable ref: {ref}")
        revision = ref.rsplit("@", 1)[1]
        if not HEX40.fullmatch(revision):
            fail(f"{path.name}: action is not pinned to a full commit SHA: {ref}")

    for index, line in enumerate(lines):
        if re.search(r"uses:\s*actions/checkout@", line):
            window = "\n".join(lines[index + 1 : index + 7])
            if not re.search(r"persist-credentials:\s*false", window):
                fail(f"{path.name}: checkout must disable credential persistence")

    for days in re.findall(r"retention-days:\s*(\d+)", text):
        if int(days) > 1:
            fail(f"{path.name}: artifact retention exceeds one day")

print(f"policy: ok ({len(list(WORKFLOWS.glob('*.y*ml')))} workflows)")
