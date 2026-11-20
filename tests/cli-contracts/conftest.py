from pathlib import Path

import pytest

from runner import load_cases, resolve_launch


def pytest_addoption(parser):
    group = parser.getgroup("CLI contracts")
    group.addoption("--shell-executable", help="Shell executable path (or dotnet with --shell-prefix-arg assembly.dll)")
    group.addoption("--shell-prefix-arg", action="append", default=[], help="Repeatable launcher arguments, not interpreted by an OS shell")
    group.addoption("--profile", action="append", choices=["offline", "emulator-linux", "emulator-windows"])
    group.addoption("--emulator-endpoint", default="https://localhost:8081")
    group.addoption("--contract-artifacts", type=Path, help="Keep declared synthetic case data and actual output on failure")


def pytest_generate_tests(metafunc):
    if "contract_case" not in metafunc.fixturenames:
        return
    profiles = set(metafunc.config.getoption("--profile") or ["offline"])
    cases = [case for case in load_cases() if profiles.intersection(case["profiles"])]
    if not cases:
        raise pytest.UsageError(f"No contracts for profiles {sorted(profiles)}")
    metafunc.parametrize("contract_case", cases, ids=[case["id"] for case in cases])


@pytest.fixture(scope="session")
def shell_launch(pytestconfig):
    executable = pytestconfig.getoption("--shell-executable")
    if not executable:
        raise pytest.UsageError("Command contracts require --shell-executable; self-tests can run test_runner.py/test_comparisons.py separately")
    return resolve_launch(executable, pytestconfig.getoption("--shell-prefix-arg"))
