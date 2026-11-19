from decimal import Decimal

import pytest

from comparisons import assert_json_equal, compare_file, json_text, parse_json, validate_comparison, validate_schema


@pytest.mark.parametrize("text", ['{"x":1,"x":2}', "NaN", "Infinity", "-Infinity"])
def test_invalid_json_rejected(text):
    with pytest.raises(ValueError):
        parse_json(text)


def test_precision_and_key_order(tmp_path):
    path = tmp_path / "result"
    path.write_text('{"b":1.0000000000000000001,"a":9007199254740993}', encoding="utf-8")
    compare_file(path, {"mode": "json", "expected": {
        "a": 9007199254740993, "b": Decimal("1.0000000000000000001"),
    }})
    with pytest.raises(AssertionError):
        compare_file(path, {"mode": "json", "expected": {"a": 9007199254740992, "b": 1}})


def test_recorded_case_preserves_numeric_types_and_precision():
    value = parse_json('{"expected":[1.0000000000000000001,9007199254740993,true,null,"Gr\\u00fc\\u00dfe"]}')
    assert_json_equal(parse_json(json_text(value)), value)


@pytest.mark.parametrize("actual,expected", [
    (True, 1), (False, 0), (1, "1"), (None, {}), ({}, {"a": None}),
    ([1, 2], [2, 1]), ({"x": 1, "extra": 2}, {"x": 1}),
])
def test_json_type_shape_and_order_are_strict(actual, expected):
    with pytest.raises(AssertionError):
        assert_json_equal(actual, expected)


def test_integral_decimals_are_json_numbers_not_python_types():
    assert_json_equal(parse_json("1.000"), 1)
    validate_schema(parse_json("1.000"), {"type": "integer"})
    with pytest.raises(AssertionError):
        validate_schema(True, {"type": "integer"})
    with pytest.raises(AssertionError):
        validate_schema(parse_json("1.01"), {"type": "integer"})


def test_local_schema_references_keep_their_own_root(tmp_path):
    path = tmp_path / "result"
    path.write_text('{"id":1}', encoding="utf-8")
    spec = {
        "mode": "json", "schema": {
            "$defs": {"id": {"type": "integer"}},
            "type": "object", "required": ["id"], "additionalProperties": False,
            "properties": {"id": {"$ref": "#/$defs/id"}},
        },
    }
    validate_comparison(spec)
    compare_file(path, spec)
    path.write_text('{"id":true}', encoding="utf-8")
    with pytest.raises(AssertionError):
        compare_file(path, spec)


def test_only_explicit_dynamic_paths_are_ignored(tmp_path):
    path = tmp_path / "result"
    path.write_text('{"id":"a","ru":2.5,"meta":{"etag":"abc"}}', encoding="utf-8")
    spec = {
        "mode": "json", "expected": {"id": "a", "ru": None, "meta": {"etag": None}},
        "dynamic": {
            "/ru": {"type": "number", "minimum": 0.1, "maximum": 10},
            "/meta/etag": {"type": "string", "minLength": 1},
        },
    }
    compare_file(path, spec)
    for text in [
        '{"id":"b","ru":2.5,"meta":{"etag":"abc"}}',
        '{"id":"a","ru":-1,"meta":{"etag":"abc"}}',
        '{"id":"a","ru":"2.5","meta":{"etag":"abc"}}',
        '{"id":"a","ru":2.5,"meta":{}}',
        '{"id":"a","ru":2.5,"meta":{"etag":""}}',
    ]:
        path.write_text(text, encoding="utf-8")
        with pytest.raises(AssertionError):
            compare_file(path, spec)


def test_unordered_arrays_preserve_types_and_duplicate_counts(tmp_path):
    path = tmp_path / "result"
    path.write_text('{"values":[2,1,1]}', encoding="utf-8")
    compare_file(path, {"mode": "json", "expected": {"values": [1, 2, 1]}, "unordered": ["/values"]})
    with pytest.raises(AssertionError):
        compare_file(path, {"mode": "json", "expected": {"values": [1, 2, 2]}, "unordered": ["/values"]})
    with pytest.raises(AssertionError):
        compare_file(path, {"mode": "json", "expected": {"values": [True, 2, 1]}, "unordered": ["/values"]})


def test_json_pointer_escaping(tmp_path):
    path = tmp_path / "result"
    path.write_text('{"a/b":{"~key":[42]}}', encoding="utf-8")
    compare_file(path, {
        "mode": "json", "expected": {"a/b": {"~key": [None]}},
        "dynamic": {"/a~1b/~0key/0": {"type": "integer"}},
    })


def test_jsonl_is_not_pretty_printed_json_or_multiple_json(tmp_path):
    path = tmp_path / "result"
    path.write_text('{"id":1}\n{"id":2}\n', encoding="utf-8")
    compare_file(path, {"mode": "jsonl", "expected": [{"id": 1}, {"id": 2}]})
    with pytest.raises(ValueError):
        compare_file(path, {"mode": "json", "expected": [{"id": 1}, {"id": 2}]})
    path.write_text('{"id":1}\n\n{"id":2}', encoding="utf-8")
    with pytest.raises(AssertionError):
        compare_file(path, {"mode": "jsonl", "expected": [{"id": 1}, {"id": 2}]})


def test_csv_preserves_header_order_spaces_empty_unicode_and_multiline(tmp_path):
    path = tmp_path / "result"
    path.write_bytes('name;value;empty\r\n"Grüße";" line 1\r\nline 2";\r\n'.encode("utf-8"))
    spec = {
        "mode": "csv", "delimiter": ";",
        "expected": [["name", "value", "empty"], ["Grüße", " line 1\r\nline 2", ""]],
    }
    compare_file(path, spec)
    with pytest.raises(AssertionError):
        compare_file(path, {**spec, "expected": [
            ["name", "value", "empty"], ["Grüße", "line 1\nline 2", ""],
        ]})
    with pytest.raises(AssertionError):
        compare_file(path, {**spec, "expected": [
            ["value", "name", "empty"], ["Grüße", " line 1\r\nline 2", ""],
        ]})


def test_text_semantics_and_duplicate_lines(tmp_path):
    path = tmp_path / "result"
    path.write_bytes(b"Result\r\nid  name\r\n1   A\r\n")
    compare_file(path, {"mode": "text", "contains": ["id", "name", "1   A"], "lineCounts": {"Result": 1}})
    path.write_bytes(b"Result\r\nResult\r\nid  name\r\n1   A\r\n")
    with pytest.raises(AssertionError):
        compare_file(path, {"mode": "text", "contains": ["id", "name"], "lineCounts": {"Result": 1}})
    with pytest.raises(AssertionError):
        compare_file(path, {"mode": "text", "occurrences": {"Result": 1}})


def test_no_prompt_or_ansi_is_stripped(tmp_path):
    path = tmp_path / "result"
    for text in ['prompt> {"id":1}', '\x1b[31m{"id":1}\x1b[0m', '{"id":1}\n{"id":1}']:
        path.write_text(text, encoding="utf-8")
        with pytest.raises(ValueError):
            compare_file(path, {"mode": "json", "expected": {"id": 1}})


@pytest.mark.parametrize("mode", ["json", "text", "csv", "bytes", "empty"])
def test_missing_artifact_fails(tmp_path, mode):
    specs = {"json": {"expected": {}}, "text": {"expected": ""}, "csv": {"expected": []}, "bytes": {"expected": []}}
    with pytest.raises(AssertionError, match="Missing"):
        compare_file(tmp_path / "missing", {"mode": mode, **specs.get(mode, {})})


@pytest.mark.parametrize("spec", [
    {"mode": "unknown"}, {"mode": "text"}, {"mode": "json"},
    {"mode": "csv", "expected": [["a", 1]]}, {"mode": "bytes", "expected": [256]},
    {"mode": "json", "schema": {}, "dynamic": {"/x": {}}},
    {"mode": "empty", "contains": ["x"]},
    {"mode": "json", "schema": {"$ref": "https://example.com/schema.json"}},
])
def test_invalid_comparison_is_not_success_shaped(spec):
    with pytest.raises(ValueError):
        validate_comparison(spec)


def test_empty_and_absent_are_different(tmp_path):
    path = tmp_path / "result"
    compare_file(path, {"mode": "absent"})
    path.touch()
    compare_file(path, {"mode": "empty"})
    with pytest.raises(AssertionError):
        compare_file(path, {"mode": "absent"})
