import importlib.util
from pathlib import Path
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


if __name__ == "__main__":
    unittest.main()
