from contextlib import nullcontext

from runner import run_case, save_failure_artifacts


def test_contract(contract_case, shell_launch, tmp_path, pytestconfig):
    work = tmp_path / "case files"
    work.mkdir()
    context = nullcontext({})
    if "emulator" in contract_case:
        from emulator import prepare_emulator

        context = prepare_emulator(contract_case, work, pytestconfig.getoption("--emulator-endpoint"))
    try:
        with context as bindings:
            run_case(contract_case, shell_launch, work, bindings)
    except Exception as error:
        artifacts = pytestconfig.getoption("--contract-artifacts")
        if artifacts is not None:
            destination = artifacts / contract_case["id"]
            try:
                destination = artifacts.resolve() / contract_case["id"]
                save_failure_artifacts(contract_case, work, destination)
            except (OSError, RuntimeError, ValueError) as artifact_error:
                error.add_note(f"Could not save failure artifacts to {destination}: {artifact_error}")
            else:
                error.add_note(f"Failure artifacts: {destination}")
        raise
