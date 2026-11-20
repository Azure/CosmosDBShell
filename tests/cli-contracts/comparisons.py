import copy
import csv
import io
import json
from decimal import Decimal
from pathlib import Path

from jsonschema import Draft202012Validator, validators


def _integer(_checker, value):
    return type(value) is int or (type(value) is Decimal and value == value.to_integral_value())


ContractValidator = validators.extend(
    Draft202012Validator,
    type_checker=Draft202012Validator.TYPE_CHECKER.redefine("integer", _integer),
)


def _unique_object(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise ValueError(f"Duplicate JSON property: {key!r}")
        value[key] = item
    return value


def parse_json(text):
    def reject_constant(value):
        raise ValueError(f"Non-finite JSON number: {value}")

    return json.loads(
        text, parse_float=Decimal, parse_constant=reject_constant,
        object_pairs_hook=_unique_object,
    )


def json_text(value):
    if type(value) is Decimal:
        if not value.is_finite():
            raise ValueError("JSON numbers must be finite")
        return str(value)
    if isinstance(value, dict):
        return "{" + ",".join(json.dumps(key) + ":" + json_text(item) for key, item in value.items()) + "}"
    if isinstance(value, list):
        return "[" + ",".join(json_text(item) for item in value) + "]"
    return json.dumps(value, ensure_ascii=True, allow_nan=False)


def check_schema(schema):
    Draft202012Validator.check_schema(schema)
    # Contracts are self-contained; a schema must never fetch a remote reference.
    def check_references(node):
        if isinstance(node, dict):
            for key, value in node.items():
                if key in ("$ref", "$dynamicRef") and not value.startswith("#"):
                    raise ValueError("Only local JSON Schema references are allowed")
                check_references(value)
        elif isinstance(node, list):
            for value in node:
                check_references(value)

    check_references(schema)


def validate_schema(instance, schema):
    check_schema(schema)
    errors = sorted(ContractValidator(schema).iter_errors(instance), key=lambda e: str(e.path))
    if errors:
        raise AssertionError("\n".join(f"{list(e.path)}: {e.message}" for e in errors))


def assert_json_equal(actual, expected, path=""):
    numeric = (int, Decimal)
    if type(actual) in numeric and type(expected) in numeric:
        assert actual == expected, f"{path or '/'}: {actual!r} != {expected!r}"
        return
    assert type(actual) is type(expected), (
        f"{path or '/'}: {type(actual).__name__} != {type(expected).__name__}"
    )
    if isinstance(expected, dict):
        assert actual.keys() == expected.keys(), f"{path or '/'}: keys {actual.keys()} != {expected.keys()}"
        for key in expected:
            assert_json_equal(actual[key], expected[key], f"{path}/{key}")
    elif isinstance(expected, list):
        assert len(actual) == len(expected), f"{path or '/'}: lengths {len(actual)} != {len(expected)}"
        for index, (left, right) in enumerate(zip(actual, expected)):
            assert_json_equal(left, right, f"{path}/{index}")
    else:
        assert actual == expected, f"{path or '/'}: {actual!r} != {expected!r}"


def _location(value, pointer):
    if pointer == "":
        return None, None, value
    if not pointer.startswith("/"):
        raise ValueError(f"Invalid JSON pointer: {pointer!r}")
    parent = None
    key = None
    for part in pointer[1:].split("/"):
        parent = value
        key = part.replace("~1", "/").replace("~0", "~")
        if isinstance(parent, list):
            if not key.isdecimal() or (key.startswith("0") and key != "0"):
                raise ValueError(f"Invalid array index in {pointer!r}")
            key = int(key)
        if not isinstance(parent, (list, dict)):
            raise AssertionError(f"Missing JSON pointer {pointer!r}")
        try:
            value = parent[key]
        except (KeyError, IndexError, TypeError) as error:
            raise AssertionError(f"Missing JSON pointer {pointer!r}") from error
    return parent, key, value


def _compare_json(actual, spec):
    if "schema" in spec:
        validate_schema(actual, spec["schema"])
    if "expected" not in spec:
        return
    actual = copy.deepcopy(actual)
    expected = copy.deepcopy(spec["expected"])
    for pointer, schema in spec.get("dynamic", {}).items():
        parent, key, value = _location(actual, pointer)
        _, _, placeholder = _location(expected, pointer)
        validate_schema(value, schema)
        parent[key] = placeholder
    for pointer in spec.get("unordered", []):
        _, _, left = _location(actual, pointer)
        _, _, right = _location(expected, pointer)
        assert isinstance(left, list) and isinstance(right, list), f"{pointer}: unordered requires arrays"
        assert len(left) == len(right), f"{pointer}: different array lengths"
        # Matching, rather than JSON string sorting, preserves precise numeric equivalence.
        remaining = list(right)
        for value in left:
            for index, candidate in enumerate(remaining):
                try:
                    assert_json_equal(value, candidate, pointer)
                except AssertionError:
                    continue
                remaining.pop(index)
                break
            else:
                raise AssertionError(f"{pointer}: unmatched array value {value!r}")
        left[:] = right
    assert_json_equal(actual, expected)


def validate_comparison(spec):
    mode = spec["mode"]
    allowed = {
        "json": {"expected", "schema", "dynamic", "unordered"},
        "jsonl": {"expected", "schema", "dynamic", "unordered"},
        "csv": {"expected", "delimiter"},
        "text": {"expected", "contains", "notContains", "lineCounts", "occurrences"},
        "bytes": {"expected"}, "empty": set(), "absent": set(),
    }
    if mode not in allowed:
        raise ValueError(f"Unknown comparison mode: {mode}")
    unknown = spec.keys() - allowed[mode] - {"mode"}
    if unknown:
        raise ValueError(f"Unsupported {mode} comparison options: {sorted(unknown)}")
    if mode in ("json", "jsonl") and not ({"expected", "schema"} & spec.keys()):
        raise ValueError(f"{mode} requires expected or schema")
    if (spec.get("dynamic") or spec.get("unordered")) and "expected" not in spec:
        raise ValueError("Dynamic/unordered comparisons require expected")
    if mode in ("csv", "bytes") and "expected" not in spec:
        raise ValueError(f"{mode} requires expected")
    if mode == "csv" and not (
        isinstance(spec["expected"], list)
        and all(isinstance(row, list) and all(isinstance(cell, str) for cell in row) for row in spec["expected"])
    ):
        raise ValueError("CSV expected must be an array of arrays of strings")
    if mode == "bytes" and not (
        isinstance(spec["expected"], list)
        and all(type(value) is int and 0 <= value <= 255 for value in spec["expected"])
    ):
        raise ValueError("Bytes expected must contain byte values")
    if mode == "text" and not ({"expected", "contains", "notContains", "lineCounts", "occurrences"} & spec.keys()):
        raise ValueError("Text comparison requires assertions")
    if mode == "text" and "expected" in spec and not isinstance(spec["expected"], str):
        raise ValueError("Text expected must be a string")
    if "schema" in spec:
        check_schema(spec["schema"])
    for schema in spec.get("dynamic", {}).values():
        check_schema(schema)


def compare_file(path: Path, spec: dict):
    validate_comparison(spec)
    mode = spec["mode"]
    if mode == "absent":
        assert not path.exists(), f"Unexpected output file: {path.name}"
        return
    assert path.is_file(), f"Missing output file: {path.name}"
    data = path.read_bytes()
    if mode == "empty":
        assert data == b"", f"{path.name}: expected empty output, got {data!r}"
    elif mode == "bytes":
        assert data == bytes(spec["expected"]), f"{path.name}: different bytes"
    else:
        text = data.decode("utf-8-sig")
        if mode == "json":
            _compare_json(parse_json(text), spec)
        elif mode == "jsonl":
            lines = text.split("\n")
            if lines[-1] == "":
                lines.pop()
            assert all(line.strip() for line in lines), "JSONL contains an empty record"
            _compare_json([parse_json(line) for line in lines], spec)
        elif mode == "csv":
            rows = list(csv.reader(io.StringIO(text, newline=""), delimiter=spec.get("delimiter", ","), strict=True))
            assert rows == spec["expected"], f"{path.name}: CSV rows {rows!r} != {spec['expected']!r}"
        elif mode == "text":
            if "expected" in spec:
                assert text == spec["expected"], f"{path.name}: {text!r} != {spec['expected']!r}"
            for value in spec.get("contains", []):
                assert value in text, f"{path.name}: missing {value!r}"
            for value in spec.get("notContains", []):
                assert value not in text, f"{path.name}: unexpected {value!r}"
            for line, count in spec.get("lineCounts", {}).items():
                assert text.splitlines().count(line) == count, f"{path.name}: incorrect count for line {line!r}"
            for value, count in spec.get("occurrences", {}).items():
                assert text.count(value) == count, f"{path.name}: incorrect occurrence count for {value!r}"
