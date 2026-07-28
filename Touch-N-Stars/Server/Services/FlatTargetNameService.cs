using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Equipment.Model;
using NINA.WPF.Base.Interfaces.Mediator;
using TouchNStars.Server.Controllers;

namespace TouchNStars.Server.Services;

/// <summary>
/// Stamps a user-configured target name onto flat frames that have none.
///
/// Flats taken through the Touch-N-Stars flat assistant are captured by sequencer
/// items that are instantiated standalone (no parent container), so NINA's
/// FillTargetMetaData finds no deep sky object and leaves Target.Name empty -
/// which makes $$TARGETNAME$$ resolve to nothing in the file name pattern.
///
/// Frames that already carry a target name are never touched, so sequence flats
/// keep their deep sky object name and NINA's own flat wizard keeps "FlatWizard".
/// </summary>
public static class FlatTargetNameService
{
    private const string SettingsKey = "flats_settings";
    private const int MaxNameLength = 64;

    private static readonly object gate = new();
    private static Func<object, BeforeImageSavedEventArgs, Task> handler;

    // Parse cache, keyed on the raw JSON string the settings store returns.
    private static string cachedRaw;
    private static (bool Enabled, string Name) cachedConfig;

    public static void Start()
    {
        lock (gate)
        {
            if (handler != null) return;

            var mediator = TouchNStars.Mediators?.ImageSaveMediator;
            if (mediator == null)
            {
                Logger.Warning("FlatTargetNameService: no image save mediator available");
                return;
            }

            handler = OnBeforeImageSaved;
            mediator.BeforeImageSaved += handler;
            Logger.Debug("FlatTargetNameService started");
        }
    }

    public static void Stop()
    {
        lock (gate)
        {
            if (handler == null) return;

            var mediator = TouchNStars.Mediators?.ImageSaveMediator;
            if (mediator != null)
            {
                mediator.BeforeImageSaved -= handler;
            }

            handler = null;
            cachedRaw = null;
            Logger.Debug("FlatTargetNameService stopped");
        }
    }

    /// <summary>
    /// Runs on the image save pipeline right before the file name pattern is resolved.
    /// Never throws and never awaits - a failure here must not break saving an image.
    /// </summary>
    private static Task OnBeforeImageSaved(object sender, BeforeImageSavedEventArgs e)
    {
        try
        {
            var metaData = e?.Image?.MetaData;
            if (metaData?.Target == null) return Task.CompletedTask;

            if (!SettingsController.TryGetRawSetting(SettingsKey, out var raw)) return Task.CompletedTask;

            if (TryResolveTargetName(raw, metaData.Image?.ImageType, metaData.Target.Name, out var name))
            {
                metaData.Target.Name = name;
                Logger.Debug($"FlatTargetNameService: stamped target name '{name}'");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"FlatTargetNameService: failed to stamp target name: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Pure decision logic, kept free of NINA mediators so it can be unit tested.
    /// </summary>
    public static bool TryResolveTargetName(string flatsSettingsJson, string imageType, string existingTargetName, out string name)
    {
        name = null;

        // Never clobber a name somebody else already set.
        if (!string.IsNullOrWhiteSpace(existingTargetName)) return false;

        // Flats only. Dark flats are captured as ImageType DARK, which would also
        // cover regular dark libraries, so they are deliberately left alone.
        if (!string.Equals(imageType, CaptureSequence.ImageTypes.FLAT, StringComparison.OrdinalIgnoreCase)) return false;

        var config = ParseConfig(flatsSettingsJson);
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.Name)) return false;

        name = Sanitize(config.Name);
        return !string.IsNullOrEmpty(name);
    }

    private static (bool Enabled, string Name) ParseConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (false, null);

        // Reference check first: the settings blob is usually the very same string instance.
        if (ReferenceEquals(json, cachedRaw) || string.Equals(json, cachedRaw, StringComparison.Ordinal))
        {
            return cachedConfig;
        }

        var parsed = (Enabled: false, Name: (string)null);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("targetNameEnabled", out var enabled) &&
                    (enabled.ValueKind == JsonValueKind.True || enabled.ValueKind == JsonValueKind.False))
                {
                    parsed.Enabled = enabled.GetBoolean();
                }

                if (root.TryGetProperty("targetName", out var targetName) && targetName.ValueKind == JsonValueKind.String)
                {
                    parsed.Name = targetName.GetString();
                }
            }
        }
        catch (JsonException)
        {
            parsed = (false, null);
        }

        cachedRaw = json;
        cachedConfig = parsed;
        return parsed;
    }

    /// <summary>
    /// The value arrives from a network client and ends up in a file path, so strip
    /// anything that cannot appear in a file name and cap the length.
    /// </summary>
    private static string Sanitize(string value)
    {
        var sanitized = CoreUtil.ReplaceAllInvalidFilenameChars(value.Trim())
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_')
            .Trim();

        if (sanitized.Length > MaxNameLength)
        {
            sanitized = sanitized.Substring(0, MaxNameLength).Trim();
        }

        return sanitized;
    }
}
