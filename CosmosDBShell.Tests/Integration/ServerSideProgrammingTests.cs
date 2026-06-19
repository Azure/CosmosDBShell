// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Net;

using Azure.Data.Cosmos.Shell.Core;

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;

using Xunit;
using Xunit.Sdk;

using DataType = Azure.Data.Cosmos.Shell.Parser.DataType;

/// <summary>
/// Emulator integration tests for the server-side programming commands
/// (<c>sproc</c>, <c>udf</c>, <c>trigger</c>). Each test drives the command
/// surface through the shell and verifies the resulting script with the
/// side-channel <see cref="ConnectedEmulatorTestBase.CosmosClient"/>.
/// </summary>
public class ServerSideProgrammingTests : ConnectedEmulatorTestBase
{
    [Fact]
    public async Task Sproc_CreateListShowExists_AndDelete()
    {
        var (dbName, container) = await CreateContainerAsync();

        var bodyFile = WriteTempScript(
            "sproc",
            "function run() { var c = getContext(); c.getResponse().setBody('ok'); }");

        try
        {
            var create = await ExecuteAsync($"sproc create myProc \"{ShellPath(bodyFile)}\" --database {dbName} --container scripts");
            Assert.False(create.IsError, FormatError(create));

            var read = await container.Scripts.ReadStoredProcedureAsync("myProc", cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);

            var list = await ExecuteAsync($"sproc list --database {dbName} --container scripts");
            Assert.False(list.IsError, FormatError(list));

            var show = await ExecuteAsync($"sproc show myProc --database {dbName} --container scripts");
            Assert.False(show.IsError, FormatError(show));

            var exists = await ExecuteAsync($"sproc exists myProc --database {dbName} --container scripts");
            Assert.False(exists.IsError, FormatError(exists));
            Assert.True((bool)exists.Result!.ConvertShellObject(DataType.Boolean)!);

            var delete = await ExecuteAsync($"sproc delete myProc --database {dbName} --container scripts");
            Assert.False(delete.IsError, FormatError(delete));

            var ex = await Assert.ThrowsAsync<CosmosException>(
                () => container.Scripts.ReadStoredProcedureAsync("myProc", cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        }
        finally
        {
            File.Delete(bodyFile);
        }
    }

    [Fact]
    public async Task Sproc_Exec_ReturnsResultBody()
    {
        var (dbName, _) = await CreateContainerAsync();

        var bodyFile = WriteTempScript(
            "sproc",
            "function echo(input) { getContext().getResponse().setBody(input); }");

        try
        {
            var create = await ExecuteAsync($"sproc create echoProc \"{ShellPath(bodyFile)}\" --database {dbName} --container scripts");
            Assert.False(create.IsError, FormatError(create));

            CommandState exec;
            try
            {
                exec = await ExecuteAsync($"sproc exec echoProc '[\"hello\"]' --partition-key pk1 --database {dbName} --container scripts");
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.BadRequest
                && ex.Message.Contains("Server-side script execution is not supported", StringComparison.Ordinal))
            {
                throw SkipException.ForSkip("The Cosmos DB emulator does not support server-side script execution.");
            }

            Assert.False(exec.IsError, FormatError(exec));

            var result = IntegrationTestBase.GetJson(exec);
            Assert.Equal("hello", result.GetString());
        }
        finally
        {
            File.Delete(bodyFile);
        }
    }

    [Fact]
    public async Task Udf_CreateListShowExists_AndDelete()
    {
        var (dbName, container) = await CreateContainerAsync();

        var bodyFile = WriteTempScript("udf", "function tax(income) { return income * 0.1; }");

        try
        {
            var create = await ExecuteAsync($"udf create myFunc \"{ShellPath(bodyFile)}\" --database {dbName} --container scripts");
            Assert.False(create.IsError, FormatError(create));

            var read = await container.Scripts.ReadUserDefinedFunctionAsync("myFunc", cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);

            var list = await ExecuteAsync($"udf list --database {dbName} --container scripts");
            Assert.False(list.IsError, FormatError(list));

            var show = await ExecuteAsync($"udf show myFunc --database {dbName} --container scripts");
            Assert.False(show.IsError, FormatError(show));

            var exists = await ExecuteAsync($"udf exists myFunc --database {dbName} --container scripts");
            Assert.False(exists.IsError, FormatError(exists));
            Assert.True((bool)exists.Result!.ConvertShellObject(DataType.Boolean)!);

            var delete = await ExecuteAsync($"udf delete myFunc --database {dbName} --container scripts");
            Assert.False(delete.IsError, FormatError(delete));

            var ex = await Assert.ThrowsAsync<CosmosException>(
                () => container.Scripts.ReadUserDefinedFunctionAsync("myFunc", cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        }
        finally
        {
            File.Delete(bodyFile);
        }
    }

    [Fact]
    public async Task Trigger_CreateListShowExists_AndDelete()
    {
        var (dbName, container) = await CreateContainerAsync();

        var bodyFile = WriteTempScript(
            "trigger",
            "function trig() { var c = getContext(); var r = c.getRequest(); r.setBody(r.getBody()); }");

        try
        {
            var create = await ExecuteAsync($"trigger create myTrigger \"{ShellPath(bodyFile)}\" --type pre --operation create --database {dbName} --container scripts");
            Assert.False(create.IsError, FormatError(create));

            var read = await container.Scripts.ReadTriggerAsync("myTrigger", cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(TriggerType.Pre, read.Resource.TriggerType);
            Assert.Equal(TriggerOperation.Create, read.Resource.TriggerOperation);

            var list = await ExecuteAsync($"trigger list --database {dbName} --container scripts");
            Assert.False(list.IsError, FormatError(list));

            var show = await ExecuteAsync($"trigger show myTrigger --database {dbName} --container scripts");
            Assert.False(show.IsError, FormatError(show));

            var exists = await ExecuteAsync($"trigger exists myTrigger --database {dbName} --container scripts");
            Assert.False(exists.IsError, FormatError(exists));
            Assert.True((bool)exists.Result!.ConvertShellObject(DataType.Boolean)!);

            var delete = await ExecuteAsync($"trigger delete myTrigger --database {dbName} --container scripts");
            Assert.False(delete.IsError, FormatError(delete));

            var ex = await Assert.ThrowsAsync<CosmosException>(
                () => container.Scripts.ReadTriggerAsync("myTrigger", cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        }
        finally
        {
            File.Delete(bodyFile);
        }
    }

    private static string WriteTempScript(string prefix, string body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cosmos-{prefix}-{Guid.NewGuid():N}.js");
        File.WriteAllText(path, body);
        return path;
    }

    private async Task<(string Database, Container Container)> CreateContainerAsync()
    {
        var dbName = $"SspTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        var con = await ExecuteAsync("mkcon scripts /id");
        Assert.False(con.IsError, FormatError(con));

        return (dbName, CosmosClient.GetContainer(dbName, "scripts"));
    }
}
