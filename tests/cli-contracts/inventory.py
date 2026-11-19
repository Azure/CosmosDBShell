#!/usr/bin/env python3
"""Read-only xUnit source/corpus inventory. Links are evidence, not migration completion."""

import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[2]
SUITE = Path(__file__).resolve().parent
SCOPES = ("CommandTests", "Contract", "Integration", "Parser", "Runtime")
DISPOSITIONS = {"retained_implementation", "pending_external", "environment_specific"}
LEXICAL = re.compile(
    r'(?P<raw>"{3,}).*?(?P=raw)|@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"'
    r"|'(?:\\.|[^'\\])*'|//[^\n]*|/\*.*?\*/",
    re.DOTALL,
)
TEST = re.compile(r"\[(?:Xunit\.)?(Fact|Theory)(?:Attribute)?\s*(?:\(|\])")
METHOD = re.compile(
    r"^\s*public\s+(?:(?:async|static|virtual|override|new|sealed)\s+)*"
    r"[\w.<>,?\[\]]+\s+(\w+)\s*(?:<[^>\n]+>)?\s*\(",
    re.MULTILINE,
)


def discover_methods(text):
    """Find attributed public methods, ignoring comments and C# string literals.

    Counts are source declarations, not executed rows. MemberData/ClassData row counts
    require running .NET and remain explicitly unknown.
    """
    clean = LEXICAL.sub(lambda match: re.sub(r"[^\n]", " ", match.group()), text)
    attributes = list(TEST.finditer(clean))
    result = []
    for index, attribute in enumerate(attributes):
        end = attributes[index + 1].start() if index + 1 < len(attributes) else len(clean)
        method = METHOD.search(clean, attribute.end(), end)
        if method is None:
            raise ValueError(f"Test attribute without public method at line {clean.count(chr(10), 0, attribute.start()) + 1}")
        decoration = clean[attribute.start():method.start()]
        result.append({
            "method": method.group(1),
            "kind": attribute.group(1),
            "line": clean.count("\n", 0, method.start()) + 1,
            "inlineVariants": len(re.findall(r"\[(?:Xunit\.)?InlineData(?:Attribute)?\s*\(", decoration)),
            "hasDynamicData": bool(re.search(r"\[(?:Xunit\.)?(?:MemberData|ClassData)(?:Attribute)?\s*\(", decoration)),
        })
    return result


def build_report(root=ROOT, suite=SUITE):
    mapping = json.loads((suite / "migration-map.json").read_text(encoding="utf-8"))
    errors = []
    policies = {}
    for group in mapping["groups"]:
        disposition, reason = group["disposition"], group["reason"]
        if disposition not in DISPOSITIONS or not reason.strip():
            errors.append("Invalid disposition or empty reason in migration map")
        for name in group["files"]:
            if name in policies:
                errors.append(f"Duplicate file policy: {name}")
            policies[name] = {"disposition": disposition, "reason": reason}

    files = []
    methods = {}
    for scope in SCOPES:
        directory = root / "CosmosDBShell.Tests" / scope
        if not directory.is_dir():
            errors.append(f"Missing inventory scope: CosmosDBShell.Tests/{scope}")
        for source in sorted(directory.rglob("*.cs")):
            name = source.relative_to(root).as_posix()
            policy = policies.get(name)
            if policy is None:
                errors.append(f"Missing explicit file policy: {name}")
                policy = {"disposition": "pending_external", "reason": "Uncategorized source; inventory validation fails."}
            found = discover_methods(source.read_text(encoding="utf-8-sig"))
            files.append({"file": name, **policy, "methodCount": len(found)})
            for entry in found:
                key = name + "::" + entry.pop("method")
                if key in methods:
                    errors.append(f"Ambiguous overloaded test method: {key}")
                methods[key] = {"source": key, **entry, **policy, "cases": []}
    actual_files = {entry["file"] for entry in files}
    for name in sorted(policies.keys() - actual_files):
        errors.append(f"Stale file policy: {name}")

    case_ids = set()
    profile_cases = Counter()
    for path in sorted((suite / "cases").rglob("*.json")):
        corpus = json.loads(path.read_text(encoding="utf-8"))
        if corpus.get("schemaVersion") != 1 or not isinstance(corpus.get("cases"), list):
            errors.append(f"Invalid corpus envelope: {path.relative_to(suite).as_posix()}")
            continue
        for case in corpus["cases"]:
            case_id = case["id"]
            if case_id in case_ids:
                errors.append(f"Duplicate case id: {case_id}")
            case_ids.add(case_id)
            profile_cases.update(set(case["profiles"]))
            if not case.get("sources"):
                errors.append(f"Case has no source evidence: {case_id}")
            for source in case.get("sources", []):
                if source not in methods:
                    errors.append(f"Stale or undiscovered source: {case_id}: {source}")
                else:
                    methods[source]["cases"].append(case_id)

    statuses = Counter()
    dispositions = Counter()
    for method in methods.values():
        method["cases"] = sorted(set(method["cases"]))
        method["status"] = "linked_partial" if method["cases"] else method["disposition"]
        statuses[method["status"]] += 1
        dispositions[method["disposition"]] += 1
    totals = {
        "files": len(files),
        "methods": len(methods),
        "facts": sum(method["kind"] == "Fact" for method in methods.values()),
        "theories": sum(method["kind"] == "Theory" for method in methods.values()),
        "inlineVariants": sum(method["inlineVariants"] for method in methods.values()),
        "dynamicDataMethods": sum(method["hasDynamicData"] for method in methods.values()),
        "cases": len(case_ids),
        "casesByProfile": dict(sorted(profile_cases.items())),
        "methodsByStatus": dict(sorted(statuses.items())),
        "methodsByDisposition": dict(sorted(dispositions.items())),
    }
    return {
        "schemaVersion": 1,
        "scope": ["CosmosDBShell.Tests/" + name for name in SCOPES],
        "semantics": {
            "linked_partial": "At least one corpus case cites this method. Not proof that every assertion, branch or theory row migrated.",
            "retained_implementation": "C# implementation-level coverage is retained; links may separately cover observable behavior.",
            "pending_external": "Observable behavior still needs complete language-neutral coverage.",
            "environment_specific": "Needs emulator, Azure, interactive terminal or external tool; any offline subset is only a partial link.",
            "counts": "Fact/Theory declarations and literal InlineData attributes only; MemberData/ClassData cardinality is unknown.",
            "retention": "All original xUnit tests remain. No method is declared fully migrated.",
        },
        "totals": totals,
        "files": files,
        "methods": [methods[key] for key in sorted(methods)],
        "errors": sorted(errors),
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="Write deterministic JSON here instead of stdout")
    args = parser.parse_args(argv)
    try:
        report = build_report()
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"inventory: {error}", file=sys.stderr)
        return 1
    rendered = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8")
    else:
        sys.stdout.write(rendered)
    return 1 if report["errors"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
