//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;

using Spectre.Console;

[CosmosCommand("bucket")]
[CosmosExample("bucket", Description = "Display current throughput bucket setting")]
[CosmosExample("bucket 3", Description = "Set throughput bucket to 3")]
[CosmosExample("bucket 0", Description = "Clear throughput bucket setting")]
internal class BucketCommand : CosmosCommand, IStateVisitor<CommandState, ShellInterpreter>
{
    [CosmosParameter("bucket", IsRequired = false)]
    public int? Bucket { get; init; }

    public static bool CheckBucket(int bucket)
    {
        var isValid = bucket >= 0 && bucket <= 5;
        if (!isValid)
        {
            AnsiConsole.MarkupLine(MessageService.GetString("error-invalid_bucket_value", new Dictionary<string, object> { { "bucket", bucket } }));
        }

        return isValid;
    }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        return await shell.State.AcceptAsync(this, shell, token);
    }

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDisconnectedStateAsync(DisconnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new NotConnectedException("bucket");
    }

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitConnectedStateAsync(ConnectedState state, ShellInterpreter shell, CancellationToken token)
    {
        throw new NotInDatabaseException("bucket");
    }

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitDatabaseStateAsync(DatabaseState state, ShellInterpreter shell, CancellationToken token)
    {
        return Task.FromResult(this.RunCommand(state.Client));
    }

    Task<CommandState> IStateVisitor<CommandState, ShellInterpreter>.VisitContainerStateAsync(ContainerState state, ShellInterpreter shell, CancellationToken token)
    {
        return Task.FromResult(this.RunCommand(state.Client));
    }

    private CommandState RunCommand(CosmosClient client)
    {
        if (this.Bucket.HasValue)
        {
            if (!CheckBucket(this.Bucket.Value))
            {
                return new CommandState(); // Invalid bucket value, return empty state
            }

            if (this.Bucket == 0)
            {
                client.ClientOptions.ThroughputBucket = null;
                AnsiConsole.MarkupLine(MessageService.GetString("command-bucket-reset_bucket"));
            }
            else
            {
                client.ClientOptions.ThroughputBucket = this.Bucket;
                AnsiConsole.MarkupLine(MessageService.GetString("command-bucket-switched_bucket", new Dictionary<string, object> { { "bucket", "[" + Theme.TableValueColorName + "]" + client.ClientOptions.ThroughputBucket + "[/]" } }));
            }
        }
        else
        {
            if (client.ClientOptions.ThroughputBucket.HasValue)
            {
                AnsiConsole.MarkupLine(MessageService.GetString("command-bucket-currrent", new Dictionary<string, object> { { "bucket", "[" + Theme.TableValueColorName + "]" + client.ClientOptions.ThroughputBucket + "[/]" } }));
            }
            else
            {
                AnsiConsole.MarkupLine(MessageService.GetString("command-bucket-no_bucket"));
            }
        }

        var commandState = new CommandState();

        // commandState.IsPrinted = true;
        // var jsonString = $"{{\"bucket\": \"{client.ClientOptions.ThroughputBucket}\"}}";
        // using var jsonDoc = JsonDocument.Parse(jsonString);
        // commandState.AddResult(jsonDoc.RootElement.Clone());
        return commandState;
    }
}
