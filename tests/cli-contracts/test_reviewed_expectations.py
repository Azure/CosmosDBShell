from pathlib import Path

import pytest

from comparisons import compare_file, json_text, parse_json


CASES = Path(__file__).with_name("cases") / "offline"


def _comparison(manifest, case_id, filename):
    cases = parse_json((CASES / manifest).read_text(encoding="utf-8"))["cases"]
    return next(case for case in cases if case["id"] == case_id)["files"][filename]


@pytest.mark.parametrize("case_id", ["contract.help.shortcuts", "contract.help.shortcuts.plain"])
def test_shortcut_contract_rejects_malformed_metadata(tmp_path, case_id):
    spec = _comparison("contracts.json", case_id, "result.json")
    shortcuts = [
        {"shortcut": key, "group": "Keys", "description": "older matches / newer matches"}
        for key in (
            "Up / Down", "Ctrl+P / Ctrl+N", "Ctrl+R", "Ctrl+S", "Ctrl+B / Ctrl+F",
            "Ctrl+A", "Ctrl+E", "Tab / Ctrl+Tab", "Ctrl+U", "Ctrl+K", "Ctrl+W", "Esc",
            "Ctrl+L", "Ctrl+C", "Ctrl+D",
        )
    ]
    result = tmp_path / "result.json"
    payload = {"topic": "shortcuts", "description": "Shortcuts", "historySearch": "Ctrl+G"}
    result.write_text(json_text({**payload, "keyboardShortcuts": shortcuts}), encoding="utf-8")
    compare_file(result, spec)

    for invalid in (
        [None] * 15,
        [{"shortcut": "Up / Down", "group": "Keys"}, *shortcuts[1:]],
        [{**shortcuts[0], "description": " "}, *shortcuts[1:]],
        [shortcuts[1], shortcuts[0], *shortcuts[2:]],
    ):
        result.write_text(json_text({**payload, "keyboardShortcuts": invalid}), encoding="utf-8")
        with pytest.raises(AssertionError):
            compare_file(result, spec)


def test_user_redirect_contract_rejects_json_with_whitespace(tmp_path):
    spec = _comparison("echo-and-files.json", "redirect.stderr.user", "error.txt")
    message = (
        "'nonexistent_cmd_for_error' is not recognized as an internal or external command, "
        "operable program or batch file."
    )
    result = tmp_path / "error.txt"
    result.write_text(message, encoding="utf-8")
    compare_file(result, spec)
    result.write_text(f'{{"status": "error", "error": "{message}"}}', encoding="utf-8")
    with pytest.raises(AssertionError, match="unexpected"):
        compare_file(result, spec)
