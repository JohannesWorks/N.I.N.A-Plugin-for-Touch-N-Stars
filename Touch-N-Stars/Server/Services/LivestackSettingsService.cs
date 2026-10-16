using System;
using System.IO;
using NINA.Profile;

namespace TouchNStars.Server.Services {

    /// <summary>
    /// Access to the options of the Livestack plugin (isbeorn/nina.plugin.livestack) in the active
    /// profile. PINS has no WPF options page, so the app toggles them through here.
    ///
    /// The plugin reads its options straight from the profile on every access, so a write takes
    /// effect with the next stacked frame - no restart needed.
    /// </summary>
    public static class LivestackSettingsService {
        public static readonly Guid PluginId = Guid.Parse("10bc1716-54af-425e-b307-c0ca1ce10600");

        private const string SaveStackedLightsKey = "SaveStackedLights";

        private static PluginOptionsAccessor Options() => new(TouchNStars.Mediators.Profile, PluginId);

        public static bool GetSaveStackedLights() => Options().GetValueBoolean(SaveStackedLightsKey, false);

        public static void SetSaveStackedLights(bool value) => Options().SetValueBoolean(SaveStackedLightsKey, value);

        /// <summary>The folder Livestack writes its stacks to.</summary>
        public static string GetStackFolder() =>
            Path.Combine(Options().GetValueString("WorkingDirectory", Path.GetTempPath()), "stacks");
    }
}
