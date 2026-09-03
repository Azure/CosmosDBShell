// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Threading;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Collects request charges from shared data-plane helpers during one command execution.
/// </summary>
internal static class RequestChargeContext
{
    private const string ExceptionChargeKey = "CosmosDBShell.RequestCharge";

    private static readonly AsyncLocal<Scope?> CurrentScope = new();

    internal static double CurrentRequestCharge => CurrentScope.Value?.RequestCharge ?? 0;

    internal static Scope Begin()
    {
        var scope = new Scope(CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    internal static void Record(double requestCharge)
    {
        if (requestCharge > 0 && CurrentScope.Value is { } scope)
        {
            scope.RequestCharge += requestCharge;
        }
    }

    internal static double GetCosmosExceptionCharge(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is CosmosException cosmosException && cosmosException.RequestCharge > 0)
            {
                return cosmosException.RequestCharge;
            }
        }

        return 0;
    }

    internal static void SetExceptionCharge(Exception exception, double requestCharge)
    {
        if (requestCharge > 0)
        {
            exception.Data[ExceptionChargeKey] = requestCharge;
        }
    }

    internal static double? GetExceptionCharge(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Data[ExceptionChargeKey] is double requestCharge)
            {
                return requestCharge;
            }
        }

        return null;
    }

    internal sealed class Scope : IDisposable
    {
        private readonly Scope? parent;
        private bool disposed;

        internal Scope(Scope? parent)
        {
            this.parent = parent;
        }

        internal double RequestCharge { get; set; }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            CurrentScope.Value = this.parent;
        }
    }
}
