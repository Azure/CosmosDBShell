import os
import re
import shutil
import subprocess
from pathlib import Path

import psutil

from comparisons import compare_file, json_text, parse_json, validate_comparison, validate_schema

ROOT = Path(__file__).resolve().parent
PLACEHOLDER = re.compile(r"\$\{([A-Z][A-Z0-9_]*)\}")
RESERVED = {"stdout.txt", "stderr.txt", "case.json"}


def expand(value, bindings):
    if isinstance(value, str):
        def replace(match):
            name = match.group(1)
            if name not in bindings:
                raise ValueError(f"Unknown placeholder: {name}")
            return str(bindings[name])
        return PLACEHOLDER.sub(replace, value)
    if isinstance(value, list):
        return [expand(item, bindings) for item in value]
    if isinstance(value, dict):
        return {key: expand(item, bindings) for key, item in value.items()}
    return value


def safe_path(root, relative):
    if not relative or Path(relative).is_absolute() or ":" in relative:
        raise ValueError(f"Expected a relative artifact path: {relative!r}")
    # Treat both separator styles alike even on Unix.
    result = (root / relative.replace("\\", "/")).resolve()
    if not result.is_relative_to(root.resolve()) or result == root.resolve():
        raise ValueError(f"Artifact path escapes case directory: {relative!r}")
    return result


def load_cases(root=ROOT):
    schema = parse_json((root / "schema.json").read_text(encoding="utf-8"))
    cases = []
    seen = set()
    for path in sorted((root / "cases").rglob("*.json")):
        document = parse_json(path.read_text(encoding="utf-8"))
        try:
            validate_schema(document, schema)
            for case in document["cases"]:
                if case["id"] in seen:
                    raise ValueError(f"Duplicate case ID: {case['id']}")
                seen.add(case["id"])
                for name, spec in case["files"].items():
                    safe_path(root, name)
                    validate_comparison(spec)
                for name in case.get("fixtures", {}):
                    safe_path(root, name)
                    if name in RESERVED:
                        raise ValueError(f"Fixture name is reserved: {name}")
                has_emulator = any(profile.startswith("emulator-") for profile in case["profiles"])
                if has_emulator != ("emulator" in case):
                    raise ValueError("Emulator profiles require emulator fixtures; offline cases cannot use them")
                if has_emulator and "offline" in case["profiles"]:
                    raise ValueError("Emulator cases cannot use the offline profile")
                cases.append(case)
        except (AssertionError, ValueError) as error:
            raise ValueError(f"Invalid manifest {path}: {error}") from error
    if not cases:
        raise ValueError(f"No contract manifests found in {root / 'cases'}")
    return cases


def resolve_launch(executable, prefix_args):
    resolved = shutil.which(executable)
    if resolved is None:
        path = Path(executable).expanduser().resolve()
        if not path.is_file():
            raise ValueError(f"Shell executable not found: {executable}")
        resolved = str(path)
    # Prefix paths (e.g. a .NET assembly) must survive the isolated child cwd.
    args = [str(Path(arg).resolve()) if Path(arg).is_file() else arg for arg in prefix_args]
    return [str(Path(resolved).resolve()), *args]


def _environment(work, overrides):
    environment = {
        key: value for key, value in os.environ.items()
        if not key.upper().startswith(("COSMOSDB_", "AZURE_", "OTEL_"))
    }
    home = work / "home"
    config = work / "config"
    temporary = work / "tmp"
    for directory in (home, config, temporary):
        directory.mkdir(parents=True, exist_ok=True)
    environment.update({
        "HOME": str(home), "USERPROFILE": str(home),
        "APPDATA": str(config), "LOCALAPPDATA": str(config),
        "XDG_CONFIG_HOME": str(config), "XDG_DATA_HOME": str(home / "data"),
        "COSMOSDB_SHELL_CONFIG_DIR": str(config),
        "COSMOSDB_SHELL_UI_LANGUAGE": "en",
        "TEMP": str(temporary), "TMP": str(temporary), "TMPDIR": str(temporary),
        "DOTNET_CLI_UI_LANGUAGE": "en", "NO_COLOR": "1", "TERM": "dumb", "TZ": "UTC",
    })
    environment.update(overrides)
    return environment


def _kill_owned_tree(process):
    try:
        parent = psutil.Process(process.pid)
        children = parent.children(recursive=True)
    except psutil.NoSuchProcess:
        return
    for owned in [*reversed(children), parent]:
        try:
            owned.kill()
        except psutil.NoSuchProcess:
            pass
    psutil.wait_procs([*children, parent], timeout=5)


def save_failure_artifacts(case, work, destination):
    if destination.exists():
        raise FileExistsError(f"Refusing to overwrite previous failure artifacts: {destination}")
    destination.mkdir(parents=True)
    names = RESERVED | set(case.get("fixtures", {})) | set(case["files"])
    for relative in sorted(names):
        source = safe_path(work, relative)
        if source.is_file():
            target = safe_path(destination, relative)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)


def run_case(case, launch, work, bindings=None):
    work.mkdir(parents=True, exist_ok=True)
    variables = {
        "WORK": str(work), "WORK_SHELL": work.as_posix().replace("'", "''"),
        **(bindings or {}),
    }
    case = expand(case, variables)
    for relative, content in case.get("fixtures", {}).items():
        destination = safe_path(work, relative)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(content, encoding="utf-8", newline="")
    # Only declarative, repository-owned test data are recorded, not inherited environment.
    (work / "case.json").write_text(json_text(case), encoding="utf-8")
    args = case.get("args")
    if args is None:
        args = ["--output", case.get("format", "json"), "-c", case["script"]]
    timeout = float(case.get("timeoutSeconds", 45))
    with (work / "stdout.txt").open("wb") as stdout, (work / "stderr.txt").open("wb") as stderr:
        process = subprocess.Popen(
            [*launch, *args], cwd=work, env=_environment(work, case.get("environment", {})),
            stdin=subprocess.PIPE, stdout=stdout, stderr=stderr,
        )
        try:
            process.communicate(input=b"", timeout=timeout)
        except subprocess.TimeoutExpired as error:
            _kill_owned_tree(process)
            process.communicate(timeout=10)
            raise AssertionError(f"{case['id']}: timed out after {timeout:g}s; output in {work}") from error
        except BaseException:
            _kill_owned_tree(process)
            process.wait(timeout=10)
            raise
    assert process.returncode == case["exitCode"], (
        f"{case['id']}: exit code {process.returncode} != {case['exitCode']}\n"
        f"stdout: {(work / 'stdout.txt').read_text(encoding='utf-8-sig')}\n"
        f"stderr: {(work / 'stderr.txt').read_text(encoding='utf-8-sig')}"
    )
    for relative, spec in case["files"].items():
        try:
            compare_file(safe_path(work, relative), spec)
        except (AssertionError, ValueError) as error:
            raise AssertionError(f"{case['id']} / {relative}: {error}") from error
