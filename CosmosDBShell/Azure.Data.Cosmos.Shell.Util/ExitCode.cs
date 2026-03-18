// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

internal class ExitCode
{
    public ExitCode(int code)
    {
        this.Code = code;
    }

    public int Code { get; }

    public static implicit operator int(ExitCode code) => code.Code;

    public static implicit operator ExitCode(int c) => new(c);
}
