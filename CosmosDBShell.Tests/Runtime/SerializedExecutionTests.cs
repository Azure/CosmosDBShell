namespace CosmosShell.Tests.Runtime;

using Azure.Data.Cosmos.Shell.Core;

public class SerializedExecutionTests
{
    [Fact]
    public async Task RunSerializedAsync_WaitsForOtherExecutionButAllowsNestedCalls()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = shell.RunSerializedAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return await shell.RunSerializedAsync(() => Task.FromResult(42), CancellationToken.None);
        }, CancellationToken.None);
        await entered.Task;
        var secondEntered = false;
        var second = shell.RunSerializedAsync(() =>
        {
            secondEntered = true;
            return Task.FromResult(7);
        }, CancellationToken.None);
        try
        {
            Assert.False(secondEntered);
            Assert.False(second.IsCompleted);
        }
        finally
        {
            release.SetResult();
        }

        Assert.Equal(42, await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(7, await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunSerializedAsync_ReleasesGateAfterFailure()
    {
        using var shell = ShellInterpreter.CreateInstance();
        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.RunSerializedAsync<int>(
            () => throw new InvalidOperationException(), CancellationToken.None));
        Assert.Equal(42, await shell.RunSerializedAsync(() => Task.FromResult(42), CancellationToken.None));
    }

    [Fact]
    public async Task RunSerializedAsync_CancelledWaiterDoesNotExecuteOrReleaseAnotherOwnersGate()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = shell.RunSerializedAsync(async () =>
        {
            await release.Task;
            return 1;
        }, TestContext.Current.CancellationToken);
        var executed = false;
        var waiter = shell.RunSerializedAsync(() =>
        {
            executed = true;
            return Task.FromResult(2);
        }, cancellation.Token);
        try
        {
            await cancellation.CancelAsync();
            try
            {
                await waiter;
                Assert.Fail("The queued operation should have been cancelled.");
            }
            catch (OperationCanceledException)
            {
                Assert.True(cancellation.IsCancellationRequested);
            }

            Assert.False(executed);
            Assert.False(first.IsCompleted);
        }
        finally
        {
            release.SetResult();
            await first;
        }

        Assert.Equal(3, await shell.RunSerializedAsync(() => Task.FromResult(3), TestContext.Current.CancellationToken));
    }
}