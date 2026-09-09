Name:           cosmosdbshell-lite
Version:        %{package_version}
Release:        %{package_release}%{?dist}
Summary:        Lightweight interactive shell for Azure Cosmos DB
License:        MIT
URL:            https://github.com/Azure/CosmosDBShell
Source0:        cosmosdbshell-lite-payload.tar.gz
Source1:        LICENSE.md
Source2:        NOTICE.html
Requires:       dotnet-runtime-10.0 >= 10.0
# The payload is prebuilt and only needs the .NET runtime, so skip the ELF scan
# that would otherwise derive dependencies from the build host.
AutoReqProv:    no

%global _binary_payload w19.zstdio
%{!?_licensedir: %global _licensedir %{_datadir}/licenses}

# Prebuilt binaries are shipped as published; stripping them breaks the .NET host.
%global debug_package %{nil}
%global __os_install_post %{nil}

%description
Azure Cosmos DB Shell is a command-line tool for interactive navigation,
queries, and scripting with Azure Cosmos DB. This lightweight build excludes
MCP, LSP, and brokered Visual Studio Code authentication.

%prep

%build

%install
mkdir -p %{buildroot}%{_libexecdir}/cosmosdbshell
tar -xzf %{SOURCE0} -C %{buildroot}%{_libexecdir}/cosmosdbshell
chmod 0755 %{buildroot}%{_libexecdir}/cosmosdbshell/CosmosDBShell
install -D -m 0644 %{SOURCE1} %{buildroot}%{_licensedir}/%{name}/LICENSE.md
install -D -m 0644 %{SOURCE2} %{buildroot}%{_licensedir}/%{name}/NOTICE.html
mkdir -p %{buildroot}%{_bindir}
ln -s %{_libexecdir}/cosmosdbshell/CosmosDBShell %{buildroot}%{_bindir}/cosmosdbshell

%files
%{_bindir}/cosmosdbshell
%{_libexecdir}/cosmosdbshell
%license %{_licensedir}/%{name}/LICENSE.md
%license %{_licensedir}/%{name}/NOTICE.html

%changelog
* Thu Aug 27 2026 Microsoft Corporation <cosmosdbshell@microsoft.com> - %{package_version}-%{package_release}
- Build from the framework-dependent .NET 10 publish output.