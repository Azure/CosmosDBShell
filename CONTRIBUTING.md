# Contributing

This project welcomes contributions and suggestions. Most contributions require you to
agree to a Contributor License Agreement (CLA) declaring that you have the right to,
and actually do, grant us the rights to use your contribution. For details, visit
the [CLA site](https://cla.microsoft.com).

When you submit a pull request, a CLA-bot will automatically determine whether you need to
provide a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the
instructions provided by the bot. You will only need to do this once across all repositories
using our CLA.

There are several ways you can contribute to the CosmosDBShell project:

- **Ideas, feature requests and bugs**: We are open to all ideas, and we want to get rid of bugs! Use the [Issues](../../issues) section to report a new issue, provide your ideas or contribute to existing threads.

- **Documentation**: Found a typo or strangely worded sentences? Submit a PR!

- **Code**: Contribute bug fixes, features or design changes:
  - **Prerequisites**: [.NET SDK 10.0+](https://dotnet.microsoft.com/download)
  - Clone the repository and open it in VS Code or your preferred IDE.
  - Restore dependencies: `dotnet restore CosmosDBShell.sln`
  - Build: `dotnet build CosmosDBShell.sln` (or use the VS Code build task with Ctrl+Shift+B).
  - Run tests: `dotnet test CosmosDBShell.sln`
  - Run the tool locally: `dotnet run --project CosmosDBShell/CosmosDBShell.csproj`
  - GitHub Actions runs CI and uploads NuGet package artifacts from [.github/workflows/validate-and-package.yml](.github/workflows/validate-and-package.yml).
  - Local builds and GitHub Actions use the default NuGet sources (nuget.org). The Azure DevOps pipeline uses [.pipelines/nuget.config](.pipelines/nuget.config) to restrict restores to the internal feed.
  - Azure Pipelines runs from [.pipelines/CosmosDB-Shell-Official.yml](.pipelines/CosmosDB-Shell-Official.yml) for signed builds and publishing from the `main` branch (and any manual runs configured there).

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/)
or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
