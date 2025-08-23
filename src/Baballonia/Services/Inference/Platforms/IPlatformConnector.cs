using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.Services.Inference.Platforms;

public interface IPlatformConnector
{
    /// <summary>
    /// Dynamic collection of Capture types, their identifying strings as well as prefix/suffix controls
    /// Add (or remove) from this collection to support platform specific connectors at runtime
    /// Or support weird hardware setups
    /// </summary>
    public Dictionary<HashSet<Regex>, Type> Captures { get; set; }

    /// <summary>
    /// A Platform may have many Capture sources, but only one may ever be active at a time.
    /// This represents the current (and a valid) Capture source for this Platform
    /// </summary>
    public Capture? Capture { get; set; }
}
