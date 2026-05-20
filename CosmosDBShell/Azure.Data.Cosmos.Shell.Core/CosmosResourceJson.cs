// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal static class CosmosResourceJson
{
    private static readonly System.Text.Json.JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public static string IndentJson(string compact)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(compact);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, IndentedJsonOptions);
        }
        catch (System.Text.Json.JsonException)
        {
            return compact;
        }
    }
}