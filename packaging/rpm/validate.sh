#!/usr/bin/env bash
set -euo pipefail

package="${1:?Specify the RPM path}"
arch="${2:?Specify the expected RPM architecture}"
size="$(stat -c %s "$package")"
if (( size > 25000000 )); then
  echo "$(basename "$package") is $size bytes and exceeds the 25 MB Cloud Shell limit." >&2
  exit 1
fi
rpm -qp --queryformat '%{NAME}\n' "$package" | grep -Fx 'cosmosdbshell-lite'
rpm -qp --queryformat '%{ARCH}\n' "$package" | grep -Fx "$arch"
rpm -qpR "$package" | grep -Fx 'dotnet-runtime-10.0 >= 10.0'
files="$(rpm -qlp "$package")"
for required in /usr/bin/cosmosdbshell /usr/libexec/cosmosdbshell/CosmosDBShell /usr/libexec/cosmosdbshell/CosmosDBShell.dll; do
  grep -Fx "$required" <<<"$files"
done
for assembly in OpenTelemetry.dll OpenTelemetry.Api.dll OpenTelemetry.Api.ProviderBuilderExtensions.dll OpenTelemetry.Exporter.OpenTelemetryProtocol.dll; do
  grep -Fx "/usr/libexec/cosmosdbshell/$assembly" <<<"$files"
done
if grep -Eq 'libmsalruntime\.so|/(ModelContextProtocol|OmniSharp|Azure\.Identity\.Broker|Microsoft\.Identity\.Client\.Broker).*\.dll$' <<<"$files"; then
  echo "$(basename "$package") contains an excluded feature assembly or native MSAL runtime." >&2
  exit 1
fi

if [[ "$(uname -m)" == "$arch" ]]; then
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  extract_dir="$root/.rpm-smoke/$arch"
  rm -rf "$extract_dir"
  mkdir -p "$extract_dir"
  (cd "$extract_dir" && rpm2cpio "$package" | cpio -idm --quiet)
  test "$(readlink "$extract_dir/usr/bin/cosmosdbshell")" = /usr/libexec/cosmosdbshell/CosmosDBShell
  bash "$root/packaging/rpm/smoke-test.sh" "$extract_dir/usr/libexec/cosmosdbshell/CosmosDBShell" "$extract_dir/checks"
  rm -rf "$extract_dir"
else
  echo "Skipping execution of $arch on $(uname -m); native Linux CI covers this architecture."
fi
echo "Validated $(basename "$package") [$size bytes]"
