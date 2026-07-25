using System.Collections.Generic;

namespace TouchNStars.Server.Models;

/// <summary>
/// Body of PUT /api/livestack/combinations/{target}.
/// Each value is the name of a filter that already has a live stack for that target.
/// </summary>
public class LivestackCombinationRequest
{
    public string Red { get; set; }
    public string Green { get; set; }
    public string Blue { get; set; }
}

/// <summary>
/// A filter that can be used as a channel, i.e. one that already has a mono stack.
/// </summary>
public class LivestackFilterInfo
{
    public string Filter { get; set; }
    public int StackCount { get; set; }
}

/// <summary>
/// The RGB channel assignment currently active for a target.
/// </summary>
public class LivestackCombinationInfo
{
    public string Red { get; set; }
    public string Green { get; set; }
    public string Blue { get; set; }

    /// <summary>
    /// True for OSC stacks, where the channels are already congruent and must not be re-aligned.
    /// </summary>
    public bool ChannelsAlreadyAligned { get; set; }

    public int StackCountRed { get; set; }
    public int StackCountGreen { get; set; }
    public int StackCountBlue { get; set; }
}

public class LivestackTargetInfo
{
    public string Target { get; set; }
    public List<LivestackFilterInfo> AvailableFilters { get; set; } = new List<LivestackFilterInfo>();

    /// <summary>Null when no color combination exists for this target yet.</summary>
    public LivestackCombinationInfo Combination { get; set; }
}

public class LivestackCombinationsResponse
{
    /// <summary>False when the Livestack plugin is not loaded or incompatible.</summary>
    public bool Available { get; set; }

    public List<LivestackTargetInfo> Targets { get; set; } = new List<LivestackTargetInfo>();
}

/// <summary>
/// Outcome of a mutating bridge call, mapped to an HTTP status by the controller.
/// </summary>
public class LivestackOperationResult
{
    public bool Ok { get; set; }
    public string Error { get; set; }
    public int StatusCode { get; set; }
    public LivestackTargetInfo Target { get; set; }
}
