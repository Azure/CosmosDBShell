// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;

internal static class ShellLocation
{
    public static string NotConnectedText => MessageService.GetString("command-pwd-not-connected");

    public static string GetCurrentLocation(State shellState)
    {
        return shellState switch
        {
            ContainerState containerState => $"/{containerState.DatabaseName}/{containerState.ContainerName}",
            DatabaseState databaseState => $"/{databaseState.DatabaseName}",
            ConnectedState => "/",
            DisconnectedState => NotConnectedText,
            _ => NotConnectedText,
        };
    }

    public static string GetCurrentLocationMarkup(State shellState)
    {
        return shellState switch
        {
            ContainerState containerState =>
                $"{Theme.ConnectedStatePromt("/")}{Theme.DatabaseNamePromt(containerState.DatabaseName)}{Theme.ConnectedStatePromt("/")}{Theme.ContainerNamePromt(containerState.ContainerName)}",
            DatabaseState databaseState =>
                $"{Theme.ConnectedStatePromt("/")}{Theme.DatabaseNamePromt(databaseState.DatabaseName)}",
            ConnectedState => Theme.ConnectedStatePromt("/"),
            DisconnectedState => Theme.DisconnectedStatePromt(NotConnectedText),
            _ => Theme.DisconnectedStatePromt(NotConnectedText),
        };
    }
}