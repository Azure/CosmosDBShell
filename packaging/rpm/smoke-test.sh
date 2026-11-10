#!/usr/bin/env bash
set -euo pipefail

launcher="${1:?Specify the native lightweight launcher}"
scratch="${2:?Specify a smoke-test working directory}"
mkdir -p "$scratch"
export HOME="$scratch"
export XDG_CONFIG_HOME="$scratch/config"
export XDG_DATA_HOME="$scratch/data"
export XDG_STATE_HOME="$scratch/state"

timeout 30 "$launcher" --version
timeout 30 "$launcher" -c version
for option in --mcp --lsp --stdio; do
  feature=LSP
  [[ "$option" != --mcp ]] || feature=MCP
  for scenario in plain json quiet command connect; do
    args=("$option")
    case "$scenario" in
      json) args+=(--output json) ;;
      quiet) args+=(--quiet) ;;
      command) args+=(-c version) ;;
      connect) args+=(--output json --connect invalid-connection --diagnostics "$scratch/diagnostics.log") ;;
    esac
    set +e
    timeout 30 "$launcher" "${args[@]}" >"$scratch/stdout" 2>"$scratch/stderr"
    status=$?
    set -e
    if [[ $status -ne 2 ]]; then
      echo "$option ($scenario) returned $status instead of usage error 2." >&2
      cat "$scratch/stdout" "$scratch/stderr" >&2
      exit 1
    fi
    if [[ "$scenario" == plain ]]; then
      grep -Fx "$feature support is not included in this build." "$scratch/stdout"
      test ! -s "$scratch/stderr"
    else
      test ! -s "$scratch/stdout"
      grep -Fx "{\"status\":\"error\",\"error\":\"$feature support is not included in this build.\"}" "$scratch/stderr"
    fi
    test ! -e "$scratch/diagnostics.log"
  done

  set +e
  timeout 30 "$launcher" --output json --otel invalid-endpoint >"$scratch/stdout" 2>"$scratch/stderr"
  status=$?
  set -e
  if [[ $status -ne 2 || -s "$scratch/stdout" ]]; then
    echo "Malformed --otel endpoint did not return a machine-mode usage error." >&2
    cat "$scratch/stdout" "$scratch/stderr" >&2
    exit 1
  fi
  grep -Fx '{"status":"error","error":"Error: --otel endpoint \u0027invalid-endpoint\u0027 is not a valid absolute URI."}' "$scratch/stderr"
done
