using System;
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
/// which makes $$TARGETNAME$$ resolve to nothing in the file name pattern. The
/// single-filter modes run through ninaAPI rather than this plugin, so the handler
/// is deliberately attached for the lifetime of the server instead of being scoped
/// to a capture run: it applies to any FLAT frame with no target name, whoever took it.
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

            // Cheapest guards first: this keeps every non-flat frame - the vast
            // majority - away from the settings file entirely.
            if (!IsStampableFlat(metaData.Image?.ImageType, metaData.Target.Name)) return Task.CompletedTask;

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
    /// True when this frame is a flat that nobody has named yet.
    /// </summary>
    public static bool IsStampableFlat(string imageType, string existingTargetName)
    {
        // Never clobber a name somebody else already set.
        if (!string.IsNullOrWhiteSpace(existingTargetName)) return false;

        // Flats only. Dark flats are captured as ImageType DARK, which would also
        // cover regular dark libraries, so they are deliberately left alone.
        return string.Equals(imageType, CaptureSequence.ImageTypes.FLAT, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pure decision logic, kept free of NINA mediators so it can be unit tested.
    /// </summary>
    public static bool TryResolveTargetName(string flatsSettingsJson, string imageType, string existingTargetName, out string name)
    {
        name = null;

        if (!IsStampableFlat(imageType, existingTargetName)) return false;

        var config = ParseConfig(flatsSettingsJson);
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.Name)) return false;

        name = Sanitize(config.Name);
        return !string.IsNullOrEmpty(name);
    }

    /// <summary>
    /// Parses the two fields this service cares about out of the flat assistant's
    /// settings blob, tolerating every other field the frontend stores alongside them.
    /// Deliberately uncached: this runs once per flat frame and parsing a few hundred
    /// bytes is cheaper than getting the cross-thread publication of a cache right.
    /// </summary>
    private static (bool Enabled, string Name) ParseConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (false, null);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (false, null);

            var enabled = root.TryGetProperty("targetNameEnabled", out var enabledElement) &&
                          enabledElement.ValueKind == JsonValueKind.True;

            var name = root.TryGetProperty("targetName", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;

            return (enabled, name);
        }
        catch (JsonException)
        {
            return (false, null);
        }
    }

    /// <summary>
    /// The value arrives from a network client and ends up in a file path, so strip
    /// anything that cannot appear in a file name and cap the length. Note that the
    /// invalid character set is OS dependent - this is a guard rail, not a promise
    /// that any particular character survives.
    /// </summary>
    private static string Sanitize(string value)
    {
        // ReplaceAllInvalidFilenameChars already maps both slashes to a hyphen.
        var sanitized = CoreUtil.ReplaceAllInvalidFilenameChars(value.Trim()).Trim();

        if (sanitized.Length > MaxNameLength)
        {
            var cut = MaxNameLength;
            // Do not cut a surrogate pair in half and leave a lone surrogate in a path.
            if (char.IsHighSurrogate(sanitized[cut - 1])) cut--;
            sanitized = sanitized.Substring(0, cut).Trim();
        }

        return sanitized;
    }
}
