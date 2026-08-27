Name:           cosmosdbshell
Version:        %{package_version}
Release:        %{package_release}%{?dist}
Summary:        Interactive shell for Azure Cosmos DB
License:        MIT
URL:            https://github.com/Azure/CosmosDBShell
Source0:        CosmosDBShell
Source1:        LICENSE.md
Source2:        NOTICE.html
Requires:       dotnet-runtime-10.0 >= 10.0
%define _binary_payload w19.zstdio

%description
Azure Cosmos DB Shell is a command-line tool for interactive navigation,
queries, scripting, and MCP server workflows with Azure Cosmos DB.

%prep

%build

%install
install -D -m 0755 %{SOURCE0} %{buildroot}%{_libexecdir}/cosmosdbshell/CosmosDBShell
install -D -m 0644 %{SOURCE1} %{buildroot}%{_licensedir}/%{name}/LICENSE.md
install -D -m 0644 %{SOURCE2} %{buildroot}%{_licensedir}/%{name}/NOTICE.html
mkdir -p %{buildroot}%{_bindir}
ln -s %{_libexecdir}/cosmosdbshell/CosmosDBShell %{buildroot}%{_bindir}/cosmosdbshell

%files
%{_bindir}/cosmosdbshell
%dir %{_libexecdir}/cosmosdbshell
%{_libexecdir}/cosmosdbshell/CosmosDBShell
%license %{_licensedir}/%{name}/LICENSE.md
%license %{_licensedir}/%{name}/NOTICE.html

%changelog
* Thu Aug 27 2026 Microsoft Corporation <cosmosdbshell@microsoft.com> - %{package_version}-%{package_release}
- Build from the framework-dependent .NET 10 publish output.