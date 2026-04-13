// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

using global::CommandLine;
using global::CommandLine.Text;

using Spectre.Console;

public class LocalizableSentenceBuilder : SentenceBuilder
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

    public override Func<string> RequiredWord => () => MessageService.GetString("help-RequiredWord");

    public override Func<string> ErrorsHeadingText => () => MessageService.GetString("help-ErrorsHeadingText");

    public override Func<string> UsageHeadingText => () => MessageService.GetString("help-UsageHeadingText");

    public override Func<string> OptionGroupWord => () => MessageService.GetString("help-OptionGroupWord");

    public override Func<bool, string> HelpCommandText
    {
        get
        {
            return isOption => isOption
                ? MessageService.GetString("help-HelpCommandScreenText")
                : MessageService.GetString("help-HelpCommandMoreText");
        }
    }

    public override Func<bool, string> VersionCommandText => (b) => MessageService.GetString("help-VersionCommandText");

    public override Func<Error, string> FormatError
    {
        get
        {
            return error =>
            {
                switch (error.Tag)
                {
                    case ErrorType.BadFormatTokenError:
                        return MessageService.GetString("help-error-BadFormatTokenError", new Dictionary<string, object> { { "token", ((BadFormatTokenError)error).Token } });
                    case ErrorType.MissingValueOptionError:
                        return MessageService.GetString("help-error-MissingValueOptionError", new Dictionary<string, object> { { "option", ((MissingValueOptionError)error).NameInfo.NameText } });
                    case ErrorType.UnknownOptionError:
                        return MessageService.GetString("help-error-UnknownOptionError", new Dictionary<string, object> { { "option", ((UnknownOptionError)error).Token } });
                    case ErrorType.MissingRequiredOptionError:
                        var errMisssing = (MissingRequiredOptionError)error;
                        return errMisssing.NameInfo.Equals(NameInfo.EmptyName)
                                   ? MessageService.GetString("help-error-MissingRequiredOptionError1")
                                   : MessageService.GetString("help-error-MissingRequiredOptionError2", new Dictionary<string, object> { { "option", errMisssing.NameInfo.NameText } });
                    case ErrorType.BadFormatConversionError:
                        var badFormat = (BadFormatConversionError)error;
                        return badFormat.NameInfo.Equals(NameInfo.EmptyName)
                                   ? MessageService.GetString("help-error-BadFormatConversionError1")
                                   : MessageService.GetString("help-error-BadFormatConversionError2", new Dictionary<string, object> { { "option", badFormat.NameInfo.NameText } });
                    case ErrorType.SequenceOutOfRangeError:
                        var seqOutRange = (SequenceOutOfRangeError)error;
                        return seqOutRange.NameInfo.Equals(NameInfo.EmptyName)
                                   ? MessageService.GetString("help-error-SequenceOutOfRangeError1")
                                   : MessageService.GetString("help-error-SequenceOutOfRangeError2", new Dictionary<string, object> { { "option", seqOutRange.NameInfo.NameText } });
                    case ErrorType.BadVerbSelectedError:
                        return MessageService.GetString("help-error-BadVerbSelectedError", new Dictionary<string, object> { { "token", ((BadVerbSelectedError)error).Token } });
                    case ErrorType.NoVerbSelectedError:
                        return MessageService.GetString("help-error-NoVerbSelectedError");
                    case ErrorType.RepeatedOptionError:
                        return MessageService.GetString("help-error-RepeatedOptionError", new Dictionary<string, object> { { "option", ((RepeatedOptionError)error).NameInfo.NameText } });
                    case ErrorType.SetValueExceptionError:
                        var setValueError = (SetValueExceptionError)error;
                        return MessageService.GetString("help-error-SetValueExceptionError", new Dictionary<string, object>
                        {
                            { "option", setValueError.NameInfo.NameText },
                            { "message", setValueError.Exception.Message },
                        });
                    case ErrorType.MissingGroupOptionError:
                        var missingGroupOptionError = (MissingGroupOptionError)error;
                        return MessageService.GetString("help-error-MissingGroupOptionError", new Dictionary<string, object>
                        {
                            { "option", missingGroupOptionError.Group },
                            { "req_options", string.Join(", ", missingGroupOptionError.Names.Select(n => n.NameText)) },
                        });
                    case ErrorType.GroupOptionAmbiguityError:
                        var groupOptionAmbiguityError = (GroupOptionAmbiguityError)error;
                        return MessageService.GetString("help-error-GroupOptionAmbiguityError", new Dictionary<string, object> { { "option", groupOptionAmbiguityError.Option.NameText } });
                    case ErrorType.MultipleDefaultVerbsError:
                        return MultipleDefaultVerbsError.ErrorMessage;
                }

                throw new InvalidOperationException();
            };
        }
    }

    public override Func<IEnumerable<MutuallyExclusiveSetError>, string> FormatMutuallyExclusiveSetErrors => errors =>
    {
        var bySet = from e in errors
                    group e by e.SetName into g
                    select new { SetName = g.Key, Errors = g.ToList() };

        var msgs = bySet.Select(
            set =>
            {
                var names = string.Join(string.Empty, from e in set.Errors select "'" + e.NameInfo.NameText + "', ");
                var namesCount = set.Errors.Count;

                var incompat = string.Join(
                    string.Empty,
                    (from x in (from s in bySet where !s.SetName.Equals(set.SetName) from e in s.Errors select e).Distinct()
                     select "'" + x.NameInfo.NameText + "', ").ToArray());

                return MessageService.GetString("help-error-FormatMutuallyExclusiveSetErrors", new Dictionary<string, object>
                {
                    { "count", namesCount },
                    { "option", names[..^2] },
                    { "incompat", incompat[..^2] },
                });
            }).ToArray();
        return string.Join(Environment.NewLine, msgs);
    };
}