"""Fixture lifecycle tests run offline and do not require azure-cosmos."""

from contextlib import redirect_stderr
import io
from pathlib import Path
import unittest
from unittest.mock import MagicMock, patch

import emulator
from comparisons import parse_json


class ServiceError(Exception):
    def __init__(self, status_code):
        super().__init__(f"service status {status_code}")
        self.status_code = status_code


class EmulatorFixtureTests(unittest.TestCase):
    def client(self):
        client = MagicMock()
        database = client.get_database_client.return_value
        database.read.side_effect = ServiceError(404)
        client.create_database.return_value.read.return_value = {"id": "fixture"}
        client.create_database.return_value.list_containers.return_value = []
        return client

    def test_loopback_origins(self):
        for endpoint in (
            "https://localhost:8081", "http://127.0.0.1:8081/",
        ):
            with self.subTest(endpoint=endpoint):
                self.assertEqual(endpoint.rstrip("/"), emulator.validate_endpoint(endpoint))

    def test_unsafe_endpoints_never_construct_client(self):
        for endpoint in (
            "https://account.documents.azure.com", "https://localhost.evil.test",
            "https://localhost@evil.test", "https://key@localhost:8081",
            "https://localhost:8081/dbs", "https://localhost:8081?key=x",
            "https://localhost:8081/#fragment", "ftp://localhost",
            "https://192.168.1.1", "https://0.0.0.0", "https://[::]",
            "https://[::1]:8081", "http://127.2.3.4",
            "http://169.254.169.254", "https://localhost:0", "https://localhost:bad",
            "https://localhost\\@evil.test", " https://localhost", "",
        ):
            with self.subTest(endpoint=endpoint), patch.object(emulator, "_client") as factory:
                with self.assertRaises(ValueError):
                    with emulator.prepare_emulator({}, Path.cwd(), endpoint):
                        self.fail("unsafe endpoint yielded")
                factory.assert_not_called()

    def test_success_verifies_and_cleans_generated_names(self):
        client = self.client()
        with patch.object(emulator, "_client", return_value=client):
            with emulator.prepare_emulator({}, Path.cwd(), "https://localhost:8081") as values:
                self.assertTrue(values["DB"].startswith("cli_contract_"))
                self.assertNotEqual(values["DB"], values["DB_NEW"])
                self.assertEqual("https://localhost:8081", values["ENDPOINT"])
        client.create_database.return_value.list_containers.assert_called_once()
        self.assertEqual(
            [values["DB_NEW"], values["DB"]],
            [call.args[0] for call in client.delete_database.call_args_list],
        )
        client.close.assert_called_once()

    def test_body_failure_skips_verification_but_cleans(self):
        client = self.client()
        primary = RuntimeError("shell contract failed")
        with patch.object(emulator, "_client", return_value=client):
            with self.assertRaisesRegex(RuntimeError, "shell contract failed") as caught:
                with emulator.prepare_emulator({}, Path.cwd(), "http://localhost:8081"):
                    raise primary
        self.assertIs(primary, caught.exception)
        client.create_database.return_value.list_containers.assert_not_called()
        self.assertEqual(2, client.delete_database.call_count)
        client.close.assert_called_once()

    def test_verification_failure_still_cleans(self):
        client = self.client()
        client.create_database.return_value.list_containers.return_value = [{"id": "unexpected"}]
        with patch.object(emulator, "_client", return_value=client):
            with self.assertRaisesRegex(AssertionError, "container inventory"):
                with emulator.prepare_emulator({}, Path.cwd(), "http://localhost:8081"):
                    pass
        self.assertEqual(2, client.delete_database.call_count)

    def test_setup_failure_still_cleans_reserved_names(self):
        client = self.client()
        client.create_database.side_effect = RuntimeError("setup failed")
        with patch.object(emulator, "_client", return_value=client):
            with self.assertRaisesRegex(RuntimeError, "setup failed"):
                with emulator.prepare_emulator({}, Path.cwd(), "http://localhost:8081"):
                    self.fail("setup failure yielded")
        self.assertEqual(2, client.delete_database.call_count)

    def test_collision_never_claims_or_deletes_database(self):
        client = self.client()
        client.get_database_client.return_value.read.side_effect = None
        with patch.object(emulator, "_client", return_value=client):
            with self.assertRaisesRegex(RuntimeError, "already exists"):
                with emulator.prepare_emulator({}, Path.cwd(), "http://localhost:8081"):
                    self.fail("collision yielded")
        client.delete_database.assert_not_called()
        client.create_database.assert_not_called()
        client.close.assert_called_once()

    def test_cleanup_failure_is_visible_without_replacing_primary(self):
        client = self.client()
        client.delete_database.side_effect = RuntimeError("cleanup unavailable")
        output = io.StringIO()
        primary = AssertionError("primary")
        with patch.object(emulator, "_client", return_value=client), redirect_stderr(output):
            with self.assertRaises(AssertionError) as caught:
                with emulator.prepare_emulator({}, Path.cwd(), "http://localhost:8081"):
                    raise primary
        self.assertIs(primary, caught.exception)
        self.assertIn("cleanup failed", output.getvalue())
        self.assertIn("cleanup unavailable", primary.__notes__[0])
        self.assertEqual(2, client.delete_database.call_count)
        client.close.assert_called_once()

    def test_cleanup_failure_fails_otherwise_successful_case(self):
        client = self.client()
        client.delete_database.side_effect = ServiceError(503)
        with patch.object(emulator, "_client", return_value=client):
            with self.assertRaisesRegex(RuntimeError, "cleanup failed"):
                with emulator.prepare_emulator({}, Path.cwd(), "http://localhost:8081"):
                    pass

    def test_deleted_database_cleanup_accepts_only_not_found(self):
        client = self.client()
        client.delete_database.side_effect = ServiceError(404)
        with patch.object(emulator, "_client", return_value=client):
            with emulator.prepare_emulator({}, Path.cwd(), "http://localhost:8081"):
                pass
        client.close.assert_called_once()

    def test_bad_fixture_is_rejected_before_network(self):
        invalid = (
            {"other": True},
            {"containers": [{"id": "wrong"}]},
            {"verify": {"databaseExists": 1}},
            {"verify": {"query": "not a fixture language"}},
            {"containers": [{"id": "../outside", "partitionKey": ["/pk"], "items": []}]},
            {"containers": [{"id": "items", "partitionKey": [], "items": []}]},
            {"containers": [{"id": "items", "partitionKey": ["/pk"], "items": [{"id": "a", "_etag": "x"}]}]},
        )
        for spec in invalid:
            with self.subTest(spec=spec), patch.object(emulator, "_client") as factory:
                with self.assertRaises(ValueError):
                    with emulator.prepare_emulator({"emulator": spec}, Path.cwd(), "http://localhost"):
                        self.fail("bad fixture yielded")
                factory.assert_not_called()

    def test_verification_is_type_aware_and_preserves_nested_metadata(self):
        database = MagicMock()
        database.list_containers.return_value = [{"id": "items"}]
        container = database.get_container_client.return_value
        container.read.return_value = {"partitionKey": {"paths": ["/pk"]}}
        spec = [{"id": "items", "partitionKey": ["/pk"], "items": [{"id": "a", "value": 1}]}]
        for actual in (
            {"id": "a", "value": True},
            {"id": "a", "value": 1, "nested": {"_etag": "real data"}},
        ):
            with self.subTest(actual=actual):
                container.read_all_items.return_value = [actual]
                with self.assertRaisesRegex(AssertionError, "SDK final items"):
                    emulator._verify_database(database, spec)
        container.read_all_items.return_value = [{"id": "a", "value": 1, "_etag": "SDK"}]
        emulator._verify_database(database, spec)

    def test_seed_conversion_refuses_silent_decimal_rounding(self):
        item = parse_json('{"id":"a","value":680.5,"integer":9007199254740993}')
        self.assertEqual(680.5, emulator._sdk_item(item)["value"])
        self.assertEqual(9007199254740993, emulator._sdk_item(item)["integer"])
        with self.assertRaises(AssertionError):
            emulator._sdk_item(parse_json('{"id":"a","value":1.0000000000000000001}'))


if __name__ == "__main__":
    unittest.main()
