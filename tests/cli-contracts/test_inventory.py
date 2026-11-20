import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


SPEC = importlib.util.spec_from_file_location("cli_inventory", Path(__file__).with_name("inventory.py"))
inventory = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(inventory)


class InventoryTests(unittest.TestCase):
    def test_discovers_facts_theories_and_inline_rows_without_comments_or_strings(self):
        source = '''
// [Fact] public void Fake() {}
/* [Theory] public void AlsoFake() {} */
public class Tests {
    private string sample = "[Fact] public void NotATest() {}";
    [Fact]
    public async Task RealFact() { }
    [Theory]
    [InlineData("] // [Fact]")]
    [InlineData("""[Fact] public void FakeRaw() {}""")]
    public void Rows(string value) { }
    [Theory]
    [MemberData(nameof(Data))]
    public Task Dynamic(int value) => Task.CompletedTask;
}
'''
        found = inventory.discover_methods(source)
        self.assertEqual(["RealFact", "Rows", "Dynamic"], [method["method"] for method in found])
        self.assertEqual([0, 2, 0], [method["inlineVariants"] for method in found])
        self.assertEqual([False, False, True], [method["hasDynamicData"] for method in found])

    def test_every_source_and_file_policy_is_valid(self):
        report = inventory.build_report()
        self.assertEqual([], report["errors"])
        self.assertGreater(report["totals"]["methods"], 100)
        self.assertEqual(report, inventory.build_report())

    def test_links_do_not_claim_complete_migration(self):
        report = inventory.build_report()
        linked = [method for method in report["methods"] if method["cases"]]
        self.assertTrue(linked)
        self.assertTrue(all(method["status"] == "linked_partial" for method in linked))
        self.assertTrue(all(method["disposition"] in inventory.DISPOSITIONS for method in report["methods"]))
        self.assertNotIn("migrated", report["totals"]["methodsByStatus"])

    def migration_fixture(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        suite = root / "suite"
        (suite / "cases").mkdir(parents=True)
        for scope in inventory.SCOPES:
            (root / "CosmosDBShell.Tests" / scope).mkdir(parents=True)
        source = "CosmosDBShell.Tests/CommandTests/ExampleTests.cs"
        (root / source).write_text(
            "public class ExampleTests { [Fact]\npublic void Active() {} }", encoding="utf-8"
        )
        retired = source + "::Retired"
        mapping = {
            "groups": [{"disposition": "retained_implementation", "reason": "Internal state.", "files": [source]}],
            "migratedMethods": [{"source": retired, "cases": ["example.retired"], "reason": "All observable assertions."}],
        }
        corpus = {"schemaVersion": 1, "cases": [
            {"id": "example.retired", "profiles": ["offline"], "sources": [retired]},
        ]}
        return root, suite, mapping, corpus

    def fixture_report(self, root, suite, mapping, corpus):
        (suite / "migration-map.json").write_text(json.dumps(mapping), encoding="utf-8")
        (suite / "cases" / "example.json").write_text(json.dumps(corpus), encoding="utf-8")
        return inventory.build_report(root, suite)

    def test_reviewed_removed_methods_are_reported_separately(self):
        report = self.fixture_report(*self.migration_fixture())
        self.assertEqual([], report["errors"])
        self.assertEqual(1, report["totals"]["methods"])
        self.assertEqual(1, report["totals"]["migratedMethods"])
        self.assertEqual(["example.retired"], report["migratedMethods"][0]["cases"])

    def test_migration_cannot_hide_active_method(self):
        root, suite, mapping, corpus = self.migration_fixture()
        mapping["migratedMethods"][0]["source"] = corpus["cases"][0]["sources"][0] = (
            "CosmosDBShell.Tests/CommandTests/ExampleTests.cs::Active"
        )
        errors = self.fixture_report(root, suite, mapping, corpus)["errors"]
        self.assertTrue(any("Migrated source still exists" in error for error in errors))

    def test_unknown_removed_source_is_still_rejected(self):
        root, suite, mapping, corpus = self.migration_fixture()
        mapping["migratedMethods"] = []
        errors = self.fixture_report(root, suite, mapping, corpus)["errors"]
        self.assertTrue(any("Stale or undiscovered source" in error for error in errors))

    def test_missing_replacement_case_is_rejected(self):
        root, suite, mapping, corpus = self.migration_fixture()
        corpus["cases"] = []
        errors = self.fixture_report(root, suite, mapping, corpus)["errors"]
        self.assertTrue(any("Missing migration case" in error for error in errors))

    def test_removed_source_requires_exact_reviewed_links(self):
        for mutation in ("extra", "unlinked"):
            with self.subTest(mutation=mutation):
                root, suite, mapping, corpus = self.migration_fixture()
                if mutation == "extra":
                    corpus["cases"].append({
                        "id": "example.extra", "profiles": ["offline"],
                        "sources": corpus["cases"][0]["sources"],
                    })
                else:
                    corpus["cases"][0]["sources"] = [
                        "CosmosDBShell.Tests/CommandTests/ExampleTests.cs::Active"
                    ]
                errors = self.fixture_report(root, suite, mapping, corpus)["errors"]
                self.assertTrue(any("links do not match" in error for error in errors))

    def test_migration_requires_unique_sources_cases_and_reason(self):
        for mutation in ("duplicate-source", "duplicate-case", "empty-cases", "empty-reason", "invalid-source"):
            with self.subTest(mutation=mutation):
                root, suite, mapping, corpus = self.migration_fixture()
                entry = mapping["migratedMethods"][0]
                if mutation == "duplicate-source":
                    mapping["migratedMethods"].append(entry.copy())
                elif mutation == "duplicate-case":
                    entry["cases"] *= 2
                elif mutation == "empty-cases":
                    entry["cases"] = []
                elif mutation == "empty-reason":
                    entry["reason"] = " "
                else:
                    entry["source"] = "not-a-source"
                self.assertTrue(self.fixture_report(root, suite, mapping, corpus)["errors"])


if __name__ == "__main__":
    unittest.main()
