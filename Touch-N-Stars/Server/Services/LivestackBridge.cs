using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NINA.Core.Utility;
using TouchNStars.Server.Models;

namespace TouchNStars.Server.Services;

/// <summary>
/// Reflection bridge to the third party Livestack plugin (nina.plugin.livestack).
///
/// Livestack does not persist its RGB channel assignment anywhere and only offers a modal WPF
/// wizard to change it, so the only way to read or set it from the outside is to reach into the
/// live object graph:
///
///   LivestackMediator.LiveStackDockable.Tabs  ->  one ColorCombinationTab per target,
///   holding its three channels in the private fields red/green/blue (each a LiveStackTab).
///
/// Every lookup is null checked. If Livestack is missing or its internals changed, the bridge
/// degrades to "not available" instead of throwing, so the feature simply disappears from the UI.
/// </summary>
public static class LivestackBridge
{
    private const string LivestackAssemblyName = "nina.plugin.livestack";
    private const string MediatorTypeName = "NINA.Plugin.Livestack.LivestackMediator";
    private const string ColorCombinationTabTypeName = "NINA.Plugin.Livestack.LivestackDockables.ColorCombinationTab";
    private const string LiveStackTabTypeName = "NINA.Plugin.Livestack.LivestackDockables.LiveStackTab";

    /// <summary>
    /// Pseudo filter names Livestack assigns to the debayered channels of a one shot color camera
    /// (LiveStackBag.RED_OSC / GREEN_OSC / BLUE_OSC).
    /// </summary>
    private static readonly string[] OscFilters = { "R_OSC", "G_OSC", "B_OSC" };

    /// <summary>How long a PUT/DELETE waits for a tab that is currently rendering.</summary>
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo> PropertyCache = new();
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo> FieldCache = new();

    private static LivestackBinding cachedBinding;
    private static bool bindingWarningLogged;

    /// <summary>Resolved Livestack types. Cached once the plugin is loaded.</summary>
    private sealed class LivestackBinding
    {
        public PropertyInfo DockableProperty;
        public Type ColorCombinationTabType;
        public Type LiveStackTabType;
    }

    public static bool IsAvailable() => TryGetBinding(out LivestackBinding binding) && GetDockable(binding) != null;

    /// <summary>
    /// Current channel assignment and the selectable filters, grouped by target.
    /// Returns Available = false instead of throwing when Livestack is unavailable.
    /// </summary>
    public static LivestackCombinationsResponse GetCombinations()
    {
        var response = new LivestackCombinationsResponse { Available = false };

        try
        {
            if (!TryGetBinding(out LivestackBinding binding))
            {
                return response;
            }

            object dockable = GetDockable(binding);
            if (dockable == null)
            {
                return response;
            }

            response.Available = true;

            // Preserve the tab order so the app lists filters the same way NINA does.
            foreach (IGrouping<string, object> group in GetTabs(dockable).GroupBy(GetTabTarget))
            {
                if (string.IsNullOrEmpty(group.Key))
                {
                    continue;
                }

                var targetInfo = new LivestackTargetInfo { Target = group.Key };

                foreach (object tab in group)
                {
                    if (binding.LiveStackTabType.IsInstanceOfType(tab))
                    {
                        targetInfo.AvailableFilters.Add(new LivestackFilterInfo
                        {
                            Filter = GetTabFilter(tab),
                            StackCount = GetIntProperty(tab, "StackCount")
                        });
                    }
                    else if (binding.ColorCombinationTabType.IsInstanceOfType(tab))
                    {
                        targetInfo.Combination = DescribeCombination(tab);
                    }
                }

                response.Targets.Add(targetInfo);
            }
        }
        catch (Exception ex)
        {
            LogBindingProblem($"Failed to read Livestack color combinations: {ex.Message}");
            return new LivestackCombinationsResponse { Available = false };
        }

        return response;
    }

    /// <summary>
    /// Replaces (or creates) the color combination of a target. Mirrors what the Livestack wizard
    /// does: remove the existing ColorCombinationTab, build a new one from the chosen mono stacks
    /// and render it. Unlike the wizard this also works for targets that already have one.
    /// </summary>
    public static async Task<LivestackOperationResult> SetCombinationAsync(string target, LivestackCombinationRequest request)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return Failure(400, "Target is required");
        }

        if (request == null
            || string.IsNullOrWhiteSpace(request.Red)
            || string.IsNullOrWhiteSpace(request.Green)
            || string.IsNullOrWhiteSpace(request.Blue))
        {
            return Failure(400, "Red, Green and Blue filters are required");
        }

        try
        {
            if (!TryGetBinding(out LivestackBinding binding))
            {
                return Failure(503, "The Livestack plugin is not available");
            }

            object dockable = GetDockable(binding);
            if (dockable == null)
            {
                return Failure(503, "The Livestack plugin is not available");
            }

            if (!(GetProperty(dockable, "Tabs") is IList tabs))
            {
                return Failure(503, "The Livestack plugin is not available");
            }

            List<object> allTabs = GetTabs(dockable).ToList();
            string resolvedTarget = ResolveName(allTabs.Select(GetTabTarget), target);
            if (resolvedTarget == null)
            {
                return Failure(404, $"No live stack exists for target '{target}'");
            }

            List<object> monoTabs = allTabs
                .Where(t => binding.LiveStackTabType.IsInstanceOfType(t)
                            && string.Equals(GetTabTarget(t), resolvedTarget, StringComparison.Ordinal))
                .ToList();

            if (monoTabs.Count == 0)
            {
                return Failure(404, $"No stacked filters exist for target '{resolvedTarget}'");
            }

            object red = FindTabByFilter(monoTabs, request.Red);
            object green = FindTabByFilter(monoTabs, request.Green);
            object blue = FindTabByFilter(monoTabs, request.Blue);

            List<string> missing = new List<string>();
            if (red == null) { missing.Add(request.Red); }
            if (green == null) { missing.Add(request.Green); }
            if (blue == null) { missing.Add(request.Blue); }

            if (missing.Count > 0)
            {
                string known = string.Join(", ", monoTabs.Select(GetTabFilter));
                return Failure(400, $"No stack exists for filter(s) {string.Join(", ", missing)} on target '{resolvedTarget}'. Available: {known}");
            }

            object existing = allTabs.FirstOrDefault(t => binding.ColorCombinationTabType.IsInstanceOfType(t)
                                                          && string.Equals(GetTabTarget(t), resolvedTarget, StringComparison.Ordinal));

            if (existing != null && !await WaitForUnlockAsync(existing).ConfigureAwait(false))
            {
                return Failure(409, "The color combination is currently being rendered. Please try again.");
            }

            // OSC channels are already congruent - re-aligning them would degrade the image.
            bool channelsAlreadyAligned = IsOscCombination(GetTabFilter(red), GetTabFilter(green), GetTabFilter(blue));

            object colorTab = InvokeOnUiThread(() =>
            {
                if (existing != null)
                {
                    tabs.Remove(existing);
                }

                object created = Activator.CreateInstance(
                    binding.ColorCombinationTabType,
                    TouchNStars.Mediators.Profile,
                    red,
                    green,
                    blue,
                    channelsAlreadyAligned);

                tabs.Add(created);
                return created;
            });

            if (colorTab == null)
            {
                return Failure(500, "Failed to create the color combination");
            }

            await RefreshAsync(colorTab).ConfigureAwait(false);

            return new LivestackOperationResult
            {
                Ok = true,
                StatusCode = 200,
                Target = DescribeTarget(binding, dockable, resolvedTarget)
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to set Livestack color combination for '{target}': {ex}");
            return Failure(500, ex.Message);
        }
    }

    /// <summary>
    /// Removes the color combination of a target. The mono stacks of the individual filters stay.
    /// </summary>
    public static async Task<LivestackOperationResult> RemoveCombinationAsync(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return Failure(400, "Target is required");
        }

        try
        {
            if (!TryGetBinding(out LivestackBinding binding))
            {
                return Failure(503, "The Livestack plugin is not available");
            }

            object dockable = GetDockable(binding);
            if (dockable == null)
            {
                return Failure(503, "The Livestack plugin is not available");
            }

            if (!(GetProperty(dockable, "Tabs") is IList tabs))
            {
                return Failure(503, "The Livestack plugin is not available");
            }

            List<object> allTabs = GetTabs(dockable).ToList();
            string resolvedTarget = ResolveName(allTabs.Select(GetTabTarget), target);

            object existing = resolvedTarget == null
                ? null
                : allTabs.FirstOrDefault(t => binding.ColorCombinationTabType.IsInstanceOfType(t)
                                              && string.Equals(GetTabTarget(t), resolvedTarget, StringComparison.Ordinal));

            if (existing == null)
            {
                return Failure(404, $"No color combination exists for target '{target}'");
            }

            if (!await WaitForUnlockAsync(existing).ConfigureAwait(false))
            {
                return Failure(409, "The color combination is currently being rendered. Please try again.");
            }

            InvokeOnUiThread(() =>
            {
                tabs.Remove(existing);
                return null;
            });

            return new LivestackOperationResult
            {
                Ok = true,
                StatusCode = 200,
                Target = DescribeTarget(binding, dockable, resolvedTarget)
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to remove Livestack color combination for '{target}': {ex}");
            return Failure(500, ex.Message);
        }
    }

    #region pure helpers

    /// <summary>
    /// True when all three channels are the debayered channels of a one shot color camera, which
    /// Livestack stacks pre-aligned (see LivestackDockable.StackOSC).
    /// </summary>
    internal static bool IsOscCombination(string red, string green, string blue)
    {
        return IsOscFilter(red) && IsOscFilter(green) && IsOscFilter(blue);
    }

    internal static bool IsOscFilter(string filter)
    {
        return filter != null && OscFilters.Contains(filter, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a requested name against the known ones: exact match first, then case insensitive.
    /// Returns null when nothing matches. Keeps URL/JSON input tolerant without ever guessing.
    /// </summary>
    internal static string ResolveName(IEnumerable<string> known, string requested)
    {
        if (requested == null || known == null)
        {
            return null;
        }

        List<string> candidates = known.Where(k => !string.IsNullOrEmpty(k)).ToList();

        return candidates.FirstOrDefault(k => string.Equals(k, requested, StringComparison.Ordinal))
               ?? candidates.FirstOrDefault(k => string.Equals(k, requested, StringComparison.OrdinalIgnoreCase));
    }

    private static LivestackOperationResult Failure(int statusCode, string error)
    {
        return new LivestackOperationResult { Ok = false, StatusCode = statusCode, Error = error };
    }

    #endregion pure helpers

    #region reflection plumbing

    private static bool TryGetBinding(out LivestackBinding binding)
    {
        binding = cachedBinding;
        if (binding != null)
        {
            return true;
        }

        // Not cached yet - Livestack may still be loaded later, so never cache a failure.
        Assembly assembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, LivestackAssemblyName, StringComparison.OrdinalIgnoreCase));

        if (assembly == null)
        {
            return false;
        }

        Type mediatorType = assembly.GetType(MediatorTypeName);
        PropertyInfo dockableProperty = mediatorType?.GetProperty("LiveStackDockable", BindingFlags.Public | BindingFlags.Static);
        Type colorTabType = assembly.GetType(ColorCombinationTabTypeName);
        Type liveStackTabType = assembly.GetType(LiveStackTabTypeName);

        if (dockableProperty == null || colorTabType == null || liveStackTabType == null)
        {
            LogBindingProblem("The loaded Livestack plugin does not expose the expected color combination internals. Color combination control is disabled.");
            return false;
        }

        binding = new LivestackBinding
        {
            DockableProperty = dockableProperty,
            ColorCombinationTabType = colorTabType,
            LiveStackTabType = liveStackTabType
        };

        cachedBinding = binding;
        return true;
    }

    private static object GetDockable(LivestackBinding binding)
    {
        // Null until the Livestack dockable has been constructed by NINA.
        return binding?.DockableProperty.GetValue(null);
    }

    private static IEnumerable<object> GetTabs(object dockable)
    {
        if (!(GetProperty(dockable, "Tabs") is IEnumerable tabs))
        {
            return Enumerable.Empty<object>();
        }

        return tabs.Cast<object>().Where(t => t != null);
    }

    private static object FindTabByFilter(IEnumerable<object> tabs, string filter)
    {
        List<object> candidates = tabs.ToList();
        string resolved = ResolveName(candidates.Select(GetTabFilter), filter);

        return resolved == null
            ? null
            : candidates.FirstOrDefault(t => string.Equals(GetTabFilter(t), resolved, StringComparison.Ordinal));
    }

    private static LivestackTargetInfo DescribeTarget(LivestackBinding binding, object dockable, string target)
    {
        var info = new LivestackTargetInfo { Target = target };

        foreach (object tab in GetTabs(dockable).Where(t => string.Equals(GetTabTarget(t), target, StringComparison.Ordinal)))
        {
            if (binding.LiveStackTabType.IsInstanceOfType(tab))
            {
                info.AvailableFilters.Add(new LivestackFilterInfo
                {
                    Filter = GetTabFilter(tab),
                    StackCount = GetIntProperty(tab, "StackCount")
                });
            }
            else if (binding.ColorCombinationTabType.IsInstanceOfType(tab))
            {
                info.Combination = DescribeCombination(tab);
            }
        }

        return info;
    }

    private static LivestackCombinationInfo DescribeCombination(object colorTab)
    {
        // red/green/blue are private readonly fields on ColorCombinationTab - there is no public
        // accessor for them, so the channel assignment can only be read this way.
        return new LivestackCombinationInfo
        {
            Red = GetTabFilter(GetField(colorTab, "red")),
            Green = GetTabFilter(GetField(colorTab, "green")),
            Blue = GetTabFilter(GetField(colorTab, "blue")),
            ChannelsAlreadyAligned = GetField(colorTab, "channelsAlreadyAligned") as bool? ?? false,
            StackCountRed = GetIntProperty(colorTab, "StackCountRed"),
            StackCountGreen = GetIntProperty(colorTab, "StackCountGreen"),
            StackCountBlue = GetIntProperty(colorTab, "StackCountBlue")
        };
    }

    /// <summary>
    /// Waits until a tab finishes rendering. Livestack sets Locked for the duration of Refresh and
    /// its own RemoveTab polls the same way; the timeout keeps the API thread from hanging.
    /// </summary>
    private static async Task<bool> WaitForUnlockAsync(object tab)
    {
        DateTime deadline = DateTime.UtcNow + LockWaitTimeout;

        while (GetProperty(tab, "Locked") as bool? == true)
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        return true;
    }

    private static async Task RefreshAsync(object colorTab)
    {
        MethodInfo refresh = colorTab.GetType().GetMethod("Refresh", new[] { typeof(CancellationToken) });
        if (refresh == null)
        {
            LogBindingProblem("Livestack ColorCombinationTab.Refresh(CancellationToken) not found - the combined image may not update until the next frame.");
            return;
        }

        if (refresh.Invoke(colorTab, new object[] { CancellationToken.None }) is Task task)
        {
            await task.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs a mutation on the WPF dispatcher. The tab collection marshals itself, but constructing
    /// a view model touches WPF state, so the whole mutation is dispatched.
    /// </summary>
    private static object InvokeOnUiThread(Func<object> action)
    {
        if (Application.Current?.Dispatcher != null)
        {
            return Application.Current.Dispatcher.Invoke(action);
        }

        return action();
    }

    private static string GetTabTarget(object tab) => GetProperty(tab, "Target") as string;

    private static string GetTabFilter(object tab) => GetProperty(tab, "Filter") as string;

    private static int GetIntProperty(object instance, string name) => GetProperty(instance, name) as int? ?? 0;

    private static object GetProperty(object instance, string name)
    {
        if (instance == null)
        {
            return null;
        }

        PropertyInfo property = PropertyCache.GetOrAdd(
            (instance.GetType(), name),
            key => key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

        return property?.GetValue(instance);
    }

    private static object GetField(object instance, string name)
    {
        if (instance == null)
        {
            return null;
        }

        FieldInfo field = FieldCache.GetOrAdd(
            (instance.GetType(), name),
            key => key.Item1.GetField(key.Item2, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

        return field?.GetValue(instance);
    }

    /// <summary>Logs once - a version mismatch would otherwise spam the log on every request.</summary>
    private static void LogBindingProblem(string message)
    {
        if (bindingWarningLogged)
        {
            return;
        }

        bindingWarningLogged = true;
        Logger.Warning(message);
    }

    #endregion reflection plumbing
}
