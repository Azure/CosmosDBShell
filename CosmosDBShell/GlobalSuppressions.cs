// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.
using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Naming", "CZ0002:WriteLine", Justification = "Intentional I need to use Console.WriteLine at one point.", Scope = "member", Target = "~M:Azure.Data.Cosmos.Shell.Core.ShellInterpreter.WriteLine(System.String,System.Object[])")]
[assembly: SuppressMessage("Naming", "CZ0002:WriteLine", Justification = "Intentional I need to use Console.WriteLine at one point.", Scope = "member", Target = "~M:Azure.Data.Cosmos.Shell.Core.ShellInterpreter.WriteLine(System.String)")]
[assembly: SuppressMessage("Naming", "CZ0002:WriteLine", Justification = "Intentional I need to use Console.WriteLine at one point.", Scope = "member", Target = "~M:Azure.Data.Cosmos.Shell.Core.ShellInterpreter.WriteLine")]
[assembly: SuppressMessage("Naming", "CZ0002:Write", Justification = "Intentional I need to use Console.WriteLine at one point.", Scope = "member", Target = "~M:Azure.Data.Cosmos.Shell.Core.ShellInterpreter.Write(System.String,System.Object[])")]
[assembly: SuppressMessage("Naming", "CZ0002:Write", Justification = "Intentional I need to use Console.WriteLine at one point.", Scope = "member", Target = "~M:Azure.Data.Cosmos.Shell.Core.ShellInterpreter.Write(System.String)")]
[assembly: SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "No API, but required public for the localization system.", Scope = "type", Target = "~T:Azure.Data.Cosmos.Shell.Util.LocalizableSentenceBuilder")]
