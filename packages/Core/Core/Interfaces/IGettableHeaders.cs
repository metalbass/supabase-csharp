using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Supabase.Core.Interfaces;

/// <summary>
/// Used for classes that need to retrieve `Headers` externally.
/// </summary>
public interface IGettableHeaders
{
    /// <summary>
    /// An executable `Func` that returns a dictionary of headers to be appended onto a request.
    /// </summary>
    Func<CancellationToken, Task<Dictionary<string, string>>>? GetHeaders { get; set; }
}
