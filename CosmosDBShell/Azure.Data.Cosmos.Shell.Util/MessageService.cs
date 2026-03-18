// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

using System.Globalization;
using System.Reflection;
using Azure.Data.Cosmos.Shell.Core;
using global::Fluent.Net;

internal static class MessageService
{
    private static readonly IEnumerable<MessageContext> Contexts;

    static MessageService()
    {
        var contexts = new List<MessageContext>();

        if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "en")
        {
            var lang = LoadMessageContext(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            if (lang != null)
            {
                contexts.Add(lang);
            }
        }

        var fallback_lang = LoadMessageContext("en");
        if (fallback_lang != null)
        {
            contexts.Add(fallback_lang);
        }
        else
        {
            throw new InvalidOperationException("Executable invalid - fallback language not found.");
        }

        MessageService.Contexts = contexts;
    }

    public static Dictionary<string, object> Args(string? name, object value, params object[] args)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentNullException(nameof(name));
        }

        ArgumentNullException.ThrowIfNull(value);
        if (args.Length % 2 != 0)
        {
            throw new ArgumentException("Expected a comma separated list not a multiple of two.", nameof(args));
        }

        var argsDic = new Dictionary<string, object>
        {
            { name, value },
        };

        for (int i = 0; i < args.Length; i += 2)
        {
            name = args[i] as string;
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException(
                    $"Expected the argument at index {i} to be a non-empty string",
                    nameof(args));
            }

            value = args[i + 1];
            if (value == null)
            {
                throw new ArgumentNullException(nameof(args), $"Expected the argument at index {i + 1} to be a non-null value");
            }

            argsDic.Add(name, value);
        }

        return argsDic;
    }

    public static string GetString(string id, IDictionary<string, object>? args = null, ICollection<FluentError>? errors = null)
    {
        foreach (var context in Contexts)
        {
            var msg = context.GetMessage(id);
            if (msg != null)
            {
                return context.Format(msg, args, errors);
            }
        }

        throw new InvalidOperationException($"Translation key '{id}' not found.");
    }

    public static string GetArgsString(string id, params object[] args)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentNullException(nameof(id));
        }

        if (args.Length % 2 != 0)
        {
            throw new ArgumentException("Expected a comma separated list not a multiple of two.", nameof(args));
        }

        var argsDic = new Dictionary<string, object>();
        for (int i = 0; i < args.Length; i += 2)
        {
            var name = args[i] as string;
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException(
                    $"Expected the argument at index {i} to be a non-empty string",
                    nameof(args));
            }

            var value = args[i + 1];
            if (value == null)
            {
                throw new ArgumentNullException(nameof(args), $"Expected the argument at index {i + 1} to be a non-null value");
            }

            argsDic.Add(name, value);
        }

        return GetString(id, argsDic);
    }

    private static MessageContext? LoadMessageContext(string resourceName)
    {
        var options = new MessageContextOptions { UseIsolating = false };
        foreach (var res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            if (!res.EndsWith($"{resourceName}.ftl"))
            {
                continue;
            }

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(res);
            if (stream == null)
            {
                return null;
            }

            using var sr = new StreamReader(stream);
            var mc = new MessageContext(resourceName, options);
            var errors = mc.AddMessages(sr);
            if (errors.Any())
            {
                ShellInterpreter.WriteLine("Error loading localization resource: " + resourceName);
            }

            foreach (var error in errors)
            {
                ShellInterpreter.WriteLine(error.ToString());
            }

            return mc;
        }

        return null;
    }
}
