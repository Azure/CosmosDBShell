#!/usr/bin/env bash
set -euo pipefail

rid="${1:?Specify linux-x64 or linux-arm64}"
version="${2:?Specify the RPM version}"
release="${3:?Specify the RPM release}"
configuration="${4:-Release}"
case "$rid" in
  linux-x64) arch=x86_64 ;;
  linux-arm64) arch=aarch64 ;;
  *) echo "Unsupported RPM runtime: $rid" >&2; exit 1 ;;
esac

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
rpm_root="$root/.rpmbuild/$rid"
publish_dir="$root/.rpm-publish/$rid"
restore_args=()
if [[ -n "${5:-}" ]]; then
  restore_args+=(--configfile "$5")
fi

mkdir -p "$rpm_root"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS} "$root/out/rpm"
cp "$root/packaging/rpm/cosmosdbshell-lite.spec" "$rpm_root/SPECS/"
cp "$root/LICENSE.md" "$root/NOTICE.html" "$rpm_root/SOURCES/"

rm -rf "$publish_dir"
dotnet publish "$root/CosmosDBShell/CosmosDBShell.csproj" \
  --configuration "$configuration" --runtime "$rid" --self-contained false \
  -p:PublishSingleFile=false -p:PackAsTool=false \
  -p:CosmosDBShellExcludeMsalRuntime=true -p:CosmosDBShellExcludeMcpAndLsp=true \
  --output "$publish_dir" "${restore_args[@]}"
rm -f "$publish_dir/libmsalruntime.so"

tar -czf "$rpm_root/SOURCES/cosmosdbshell-lite-payload.tar.gz" -C "$publish_dir" .
rpmbuild -bb "$rpm_root/SPECS/cosmosdbshell-lite.spec" \
  --target "$arch" --define "_topdir $rpm_root" \
  --define "package_version $version" --define "package_release $release"

mapfile -t packages < <(find "$rpm_root/RPMS" -type f -name "cosmosdbshell-lite-$version-$release*.$arch.rpm")
if [[ ${#packages[@]} -ne 1 ]]; then
  echo "Expected one $arch RPM; found ${#packages[@]}." >&2
  exit 1
fi
bash "$root/packaging/rpm/validate.sh" "${packages[0]}" "$arch"
cp "${packages[0]}" "$root/out/rpm/"
