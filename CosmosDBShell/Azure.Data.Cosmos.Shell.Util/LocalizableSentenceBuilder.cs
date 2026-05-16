// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Static accessors for localized CLI option descriptions and usage strings.
/// Previously implemented <c>CommandLine.Text.SentenceBuilder</c>; after the
/// migration to <c>System.CommandLine</c> these strings are pulled directly
/// by the option builder in <c>Program.cs</c>.
/// </summary>
public static class LocalizableSentenceBuilder
{
    public static string ExecuteAndContinue => MessageService.GetString("help-ExecuteAndContinue");

    public static string ExecuteAndQuit => MessageService.GetString("help-ExecuteAndQuit");

    public static string ColorSystem => MessageService.GetString("help-ColorSystem");

    public static string ClearHistory => MessageService.GetString("help-ClearHistory");

    public static string ConnectionString => MessageService.GetString("help-ConnectionString");

    public static string ConnectionMode => MessageService.GetString("help-ConnectionMode");

    public static string ConnectTenant => MessageService.GetString("help-ConnectTenant");

    public static string ConnectHint => MessageService.GetString("help-ConnectHint");

    public static string ConnectAuthorityHost => MessageService.GetString("help-ConnectAuthorityHost");

    public static string ConnectManagedIdentity => MessageService.GetString("help-ConnectManagedIdentity");

    public static string ConnectVSCodeCredential => MessageService.GetString("help-ConnectVSCodeCredential");

    public static string Command => MessageService.GetString("help-cmd");

    public static string EnableMcpServer => MessageService.GetString("help-EnableMcpServer");

    public static string McpPort => MessageService.GetString("help-McpPort");

    public static string EnableLspServer => MessageService.GetString("help-EnableLspServer");

    public static string Verbose => MessageService.GetString("help-Verbose");

    public static string UsageHeadingText => MessageService.GetString("help-UsageHeadingText");
}
