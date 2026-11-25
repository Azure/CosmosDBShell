import json
import os
import sys
from pathlib import Path
from types import SimpleNamespace

import psutil
import pytest

from runner import ROOT, expand, load_cases, resolve_launch, run_case, safe_path, save_failure_artifacts


def case(**changes):
    return {
        "id": "runner.fake", "profiles": ["offline"],
        "sources": ["CosmosDBShell.Tests/Integration/ShellProcessTests.cs::Example"],
        "script": "irrelevant", "exitCode": 0, "files": {"stdout.txt": {"mode": "empty"}},
        **changes,
    }


def fake(code):
    return [sys.executable, "-c", code]


def test_process_gets_closed_stdin_and_isolated_paths_environment(tmp_path, monkeypatch):
    monkeypatch.setenv("COSMOSDB_SHELL_FORMAT", "csv")
    monkeypatch.setenv("COSMOSDB_SHELL_UI_LANGUAGE", "de")
    monkeypatch.setenv("AZURE_CLIENT_SECRET", "must-not-reach-child")
    work = tmp_path / "space and apostrophe's path"
    code = (
        "import os,sys,json; from pathlib import Path; "
        "assert sys.stdin.read() == ''; "
        "assert 'COSMOSDB_SHELL_FORMAT' not in os.environ; "
        "assert os.environ['COSMOSDB_SHELL_UI_LANGUAGE'] == 'en'; "
        "assert 'AZURE_CLIENT_SECRET' not in os.environ; "
        "assert Path(os.environ['COSMOSDB_SHELL_CONFIG_DIR']).parent == Path.cwd(); "
        "assert Path(os.environ['HOME']).parent == Path.cwd(); "
        "assert Path('input.json').read_text(encoding='utf-8') == '{\"id\":1}'; "
        "Path('result.json').write_text(json.dumps({'id':9007199254740993}),encoding='utf-8')"
    )
    run_case(case(
        fixtures={"input.json": '{"id":1}'},
        files={"result.json": {"mode": "json", "expected": {"id": 9007199254740993}},
               "stdout.txt": {"mode": "empty"}, "stderr.txt": {"mode": "empty"}},
    ), fake(code), work)
    assert (work / "case.json").is_file()
    assert "must-not-reach-child" not in (work / "case.json").read_text(encoding="utf-8")


def test_script_and_launch_arguments_are_not_interpreted_by_an_os_shell(tmp_path):
    script = "print 'hello'; $value = 'a & b | c'"
    code = "import sys,json; print(json.dumps(sys.argv[1:]), end='')"
    run_case(case(script=script, files={
        "stdout.txt": {"mode": "json", "expected": ["--output", "json", "-c", script]},
    }), fake(code), tmp_path)


def test_raw_cli_arguments_are_not_prefixed(tmp_path):
    definition = case(args=["--bad-option"], files={
        "stdout.txt": {"mode": "json", "expected": ["--bad-option"]},
    })
    del definition["script"]
    run_case(definition, fake("import sys,json; print(json.dumps(sys.argv[1:]),end='')"), tmp_path)


def test_stdin_delivers_expanded_utf8_bytes_and_eof(tmp_path):
    definition = case(args=["--literal"], stdin="Gr\u00fc\u00dfe ${VALUE}\r\nlast", files={
        "stdout.txt": {"mode": "bytes", "expected": list("Gr\u00fc\u00dfe a & b\r\nlast".encode("utf-8"))},
    })
    del definition["script"]
    code = "import sys; assert sys.argv[1:] == ['--literal']; sys.stdout.buffer.write(sys.stdin.buffer.read())"
    run_case(definition, fake(code), tmp_path, {"VALUE": "a & b"})


def test_timeout_when_child_does_not_read_stdin(tmp_path):
    definition = case(args=[], stdin="x" * (1024 * 1024), timeoutSeconds=1)
    del definition["script"]
    with pytest.raises(AssertionError, match="timed out"):
        run_case(definition, fake("import time; time.sleep(10)"), tmp_path)


@pytest.mark.parametrize("code", [
    "raise SystemExit(2)",
    "from pathlib import Path; Path('result.json').write_text('{\"id\":2}')",
    "pass",
])
def test_wrong_exit_data_or_missing_file_fail(tmp_path, code):
    with pytest.raises(AssertionError):
        run_case(case(files={"result.json": {"mode": "json", "expected": {"id": 1}}}), fake(code), tmp_path)


def test_prompt_is_ignored_only_when_outside_declared_result(tmp_path):
    run_case(case(files={"result.json": {"mode": "json", "expected": {"id": 1}}}), fake(
        "from pathlib import Path; print('completely-different-rust-prompt>'); "
        "Path('result.json').write_text('{\"id\":1}')"
    ), tmp_path)
    with pytest.raises(AssertionError):
        run_case(case(files={"result.json": {"mode": "json", "expected": {"id": 1}}}), fake(
            "from pathlib import Path; Path('result.json').write_text('prompt> {\"id\":1}')"
        ), tmp_path)


def test_timeout_kills_only_owned_child_tree(tmp_path):
    code = (
        "import subprocess,sys,time; from pathlib import Path; "
        "child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(60)']); "
        "Path('child.pid').write_text(str(child.pid)); time.sleep(60)"
    )
    with pytest.raises(AssertionError, match="timed out"):
        run_case(case(timeoutSeconds=1), fake(code), tmp_path)
    pid = int((tmp_path / "child.pid").read_text())
    try:
        process = psutil.Process(pid)
    except psutil.NoSuchProcess:
        return
    assert process.status() == psutil.STATUS_ZOMBIE


@pytest.mark.parametrize("path", ["../escape", "..\\escape", "/absolute", "C:\\absolute", ".", ""])
def test_artifact_paths_cannot_escape(tmp_path, path):
    with pytest.raises(ValueError):
        safe_path(tmp_path, path)


def test_artifact_symlinks_cannot_escape(tmp_path):
    if os.name == "nt":
        # Windows symlink creation requires developer mode/privileges; traversal
        # with either separator is covered on all platforms above.
        return
    outside = tmp_path.parent / f"{tmp_path.name}-outside"
    (tmp_path / "link").symlink_to(outside, target_is_directory=True)
    with pytest.raises(ValueError):
        safe_path(tmp_path, "link/result.json")


def test_expansion_is_explicit_not_shell_evaluation():
    assert expand({"script": "${DB} $variable {json}", "expected": ["${DB}"]}, {"DB": "owned"}) == {
        "script": "owned $variable {json}", "expected": ["owned"],
    }
    with pytest.raises(ValueError, match="Unknown placeholder"):
        expand("${UNKNOWN}", {})


def test_missing_executable_is_an_error(tmp_path):
    with pytest.raises(ValueError, match="not found"):
        resolve_launch(str(tmp_path / "missing"), [])


def test_failure_artifacts_include_only_declared_files_not_config_or_home(tmp_path):
    work = tmp_path / "work"
    work.mkdir()
    run_case(case(), fake("pass"), work)
    (work / "home" / "unrelated").write_text("do not upload")
    destination = tmp_path / "artifacts"
    save_failure_artifacts(case(), work, destination)
    assert {path.name for path in destination.iterdir()} == {"case.json", "stdout.txt", "stderr.txt"}
    with pytest.raises(FileExistsError, match="overwrite"):
        save_failure_artifacts(case(), work, destination)


@pytest.mark.parametrize("existing", [False, True])
def test_failure_artifact_handling_keeps_original_assertion(tmp_path, existing):
    from test_contracts import test_contract

    artifacts = tmp_path / "artifacts"
    destination = artifacts / case()["id"]
    if existing:
        destination.mkdir(parents=True)
        (destination / "stdout.txt").write_bytes(b"previous output")
    config = SimpleNamespace(getoption={"--contract-artifacts": artifacts}.__getitem__)
    with pytest.raises(AssertionError, match="stdout.txt: expected empty output") as failure:
        test_contract(case(), fake("print('unexpected output')"), tmp_path, config)
    note = failure.value.__notes__[0]
    assert str(destination) in note
    if existing:
        assert "Refusing to overwrite" in note
        assert (destination / "stdout.txt").read_bytes() == b"previous output"
    else:
        assert note.startswith("Failure artifacts:")
        assert b"unexpected output" in (destination / "stdout.txt").read_bytes()


def write_manifest(root, cases):
    (root / "schema.json").write_bytes((ROOT / "schema.json").read_bytes())
    (root / "cases").mkdir(exist_ok=True)
    (root / "cases" / "test.json").write_text(json.dumps({"schemaVersion": 1, "cases": cases}), encoding="utf-8")


@pytest.mark.parametrize("changes", [
    {"unknown": True}, {"implementation": "rust"}, {"profiles": ["rust-only"]},
    {"files": {"result": {"mode": "bad"}}}, {"fixtures": {"../escape": ""}},
    {"fixtures": {"stdout.txt": ""}}, {"files": {"result": {"mode": "text"}}},
    {"profiles": ["emulator-linux"]}, {"emulator": {}},
    {"stdin": "echo one"},
    {"environment": {"AZURE_CLIENT_SECRET": "not-allowed"}},
])
def test_malformed_manifests_are_errors(tmp_path, changes):
    write_manifest(tmp_path, [case(**changes)])
    with pytest.raises(ValueError):
        load_cases(tmp_path)


def test_duplicate_ids_fail(tmp_path):
    write_manifest(tmp_path, [case(), case()])
    with pytest.raises(ValueError, match="Duplicate case"):
        load_cases(tmp_path)


@pytest.mark.parametrize("stdin", ["", ["echo one"]])
def test_stdin_manifest_requires_nonempty_text(tmp_path, stdin):
    definition = case(args=[], stdin=stdin)
    del definition["script"]
    write_manifest(tmp_path, [definition])
    with pytest.raises(ValueError, match="Invalid manifest"):
        load_cases(tmp_path)
