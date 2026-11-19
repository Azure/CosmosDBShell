"""Independent, loopback-only Cosmos fixtures for the shared CLI corpus.

``emulator.containers`` declares seed containers (id, partitionKey path list,
items). ``emulator.verify`` declares their complete final state with the same
shape. Optional databaseExists/newDatabaseExists assert resource lifecycle.
No setup command is executed through the shell under test.
"""

from contextlib import contextmanager
import ipaddress
import json
from pathlib import Path
import sys
from urllib.parse import urlsplit
import uuid

from comparisons import assert_json_equal, json_text, parse_json

EMULATOR_KEY = (
    "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
)
_SYSTEM_PROPERTIES = frozenset({"_rid", "_self", "_etag", "_attachments", "_ts"})
_PREFIX = "cli_contract_"


def validate_endpoint(endpoint: str) -> str:
    """Reject nonlocal targets before importing the SDK or making any request."""
    try:
        parsed = urlsplit(endpoint)
        host = parsed.hostname
        local = host == "localhost" or (
            host is not None and ipaddress.ip_address(host).is_loopback
        )
        port = parsed.port
    except (ValueError, TypeError):
        local = False
        port = None
    if (
        not local
        or parsed.scheme not in ("http", "https")
        or parsed.username is not None
        or parsed.password is not None
        or parsed.path not in ("", "/")
        or parsed.query
        or parsed.fragment
        or port == 0
        or any(character.isspace() for character in endpoint)
        or "\\" in endpoint
    ):
        raise ValueError("Emulator endpoint must be a loopback HTTP(S) origin.")
    return endpoint.rstrip("/")


def _client(endpoint):
    try:
        from azure.cosmos import CosmosClient
        from azure.core.pipeline.transport import RequestsTransport
    except ImportError as error:
        raise RuntimeError(
            "Emulator profile requires requirements-emulator.txt; it cannot be skipped."
        ) from error
    return CosmosClient(
        endpoint,
        credential=EMULATOR_KEY,
        connection_verify=False,
        enable_endpoint_discovery=False,
        connection_timeout=5,
        read_timeout=10,
        retry_total=0,
        retry_connect=0,
        retry_read=0,
        retry_status=0,
        transport=RequestsTransport(use_env_settings=False),
    )


def _not_found(error):
    return getattr(error, "status_code", None) == 404


def _exists(database):
    try:
        database.read()
        return True
    except Exception as error:
        if _not_found(error):
            return False
        raise


def _validate_containers(containers):
    if not isinstance(containers, list):
        raise ValueError("emulator containers must be a list")
    ids = set()
    for container in containers:
        if set(container) != {"id", "partitionKey", "items"}:
            raise ValueError("Each emulator container needs id, partitionKey, items.")
        name, paths, items = (
            container["id"], container["partitionKey"], container["items"]
        )
        if (
            not isinstance(name, str)
            or not name
            or any(character in name for character in "/\\?#")
            or name in ids
        ):
            raise ValueError("Invalid or duplicate emulator container id.")
        ids.add(name)
        if (
            not isinstance(paths, list)
            or not 1 <= len(paths) <= 3
            or any(not isinstance(path, str) or not path.startswith("/") for path in paths)
        ):
            raise ValueError("partitionKey must contain one to three absolute paths.")
        if not isinstance(items, list):
            raise ValueError("emulator items must be a list")
        for item in items:
            if (
                not isinstance(item, dict)
                or not isinstance(item.get("id"), str)
                or not item["id"]
                or _SYSTEM_PROPERTIES.intersection(item)
            ):
                raise ValueError("Seed items need a string id and no SDK metadata.")


def _sdk_item(item):
    value = json.loads(json_text(item))
    assert_json_equal(parse_json(json.dumps(value)), item)
    return value


def _verify_database(database, containers):
    actual_ids = sorted(container["id"] for container in database.list_containers())
    expected_ids = sorted(container["id"] for container in containers)
    if actual_ids != expected_ids:
        raise AssertionError(f"SDK container inventory: {actual_ids!r} != {expected_ids!r}")
    for spec in containers:
        container = database.get_container_client(spec["id"])
        paths = container.read()["partitionKey"]["paths"]
        if paths != spec["partitionKey"]:
            raise AssertionError(f"SDK partition key paths: {paths!r} != {spec['partitionKey']!r}")
        # Only the service's five documented root metadata fields are excluded.
        actual = [
            {key: value for key, value in item.items() if key not in _SYSTEM_PROPERTIES}
            for item in container.read_all_items()
        ]
        remaining = list(spec["items"])
        for item in actual:
            for index, expected in enumerate(remaining):
                try:
                    assert_json_equal(parse_json(json.dumps(item)), expected)
                except AssertionError:
                    continue
                remaining.pop(index)
                break
            else:
                raise AssertionError(f"SDK final items for {spec['id']}: unexpected {item!r}")
        if remaining:
            raise AssertionError(f"SDK final items for {spec['id']}: missing {remaining!r}")


@contextmanager
def prepare_emulator(case: dict, work: Path, endpoint: str):
    """Yield shell bindings, verify mutations on success, and always clean up.

    Both database names are generated and checked absent before claiming them.
    DB_NEW is reserved for shell database-creation cases, never an external name.
    """
    endpoint = validate_endpoint(endpoint)
    spec = case.get("emulator", {})
    if set(spec) - {"containers", "verify"}:
        raise ValueError("Unknown emulator fixture field.")
    containers = spec.get("containers", [])
    verify = spec.get("verify", {"containers": containers})
    if set(verify) - {"containers", "databaseExists", "newDatabaseExists"}:
        raise ValueError("Unknown emulator verification field.")
    _validate_containers(containers)
    _validate_containers(verify.get("containers", []))
    for field in ("databaseExists", "newDatabaseExists"):
        if field in verify and type(verify[field]) is not bool:
            raise ValueError(f"{field} must be a boolean")
    client = None
    owned = []
    primary = None
    try:
        client = _client(endpoint)
        unique = _PREFIX + uuid.uuid4().hex
        database_name, new_name = unique + "_main", unique + "_new"
        database = client.get_database_client(database_name)
        new_database = client.get_database_client(new_name)
        if _exists(database) or _exists(new_database):
            raise RuntimeError("Generated emulator fixture database already exists.")
        # Claim only generated names proven absent; also cover interrupted creation.
        owned.extend((database_name, new_name))
        database = client.create_database(database_name)
        if containers:
            from azure.cosmos import PartitionKey

            for container_spec in containers:
                paths = container_spec["partitionKey"]
                partition_key = (
                    PartitionKey(path=paths[0])
                    if len(paths) == 1
                    else PartitionKey(path=paths, kind="MultiHash")
                )
                container = database.create_container(
                    id=container_spec["id"], partition_key=partition_key
                )
                for item in container_spec["items"]:
                    container.create_item(body=_sdk_item(item))
        yield {
            "DB": database_name,
            "DB_NEW": new_name,
            "CONTAINER": containers[0]["id"] if containers else "items",
            "ENDPOINT": endpoint,
            "ENDPOINT_HOST": urlsplit(endpoint).hostname,
            "EMULATOR_KEY": EMULATOR_KEY,
        }
        expected_exists = verify.get("databaseExists", True)
        if _exists(database) != expected_exists:
            raise AssertionError(f"SDK database existence must be {expected_exists}.")
        if expected_exists:
            _verify_database(database, verify.get("containers", containers))
        if _exists(new_database) != verify.get("newDatabaseExists", False):
            raise AssertionError("SDK new database existence differs from expectation.")
    except BaseException as error:
        primary = error
        raise
    finally:
        failures = []
        if client is not None:
            for name in reversed(owned):
                try:
                    client.delete_database(name)
                except Exception as error:
                    if not _not_found(error):
                        failures.append(f"{name}: {error}")
            try:
                client.close()
            except Exception as error:
                failures.append(f"client close: {error}")
        if failures:
            message = "Emulator fixture cleanup failed: " + "; ".join(failures)
            if primary is not None:
                primary.add_note(message)
                print(message, file=sys.stderr)
            else:
                raise RuntimeError(message)
