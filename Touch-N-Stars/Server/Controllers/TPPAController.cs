using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TouchNStars.Server.Infrastructure;
using TouchNStars.Server.Models;
using TouchNStars.Utility;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// Controller for TPPA (Two-Point Polar Alignment) / PolarAlignment plugin integration endpoints
/// </summary>
public class TPPAController : WebApiController
{
    /// <summary>
    /// Gets the PolarAlignment Settings type
    /// </summary>
    private Type GetPolarAlignmentSettingsType()
    {
        return Type.GetType("NINA.Plugins.PolarAlignment.Properties.Settings, NINA.Plugins.PolarAlignment");
    }

    /// <summary>
    /// Gets the Default settings instance from PolarAlignment plugin
    /// </summary>
    private object GetPolarAlignmentSettingsInstance()
    {
        var settingsType = GetPolarAlignmentSettingsType();
        if (settingsType == null)
        {
            return null;
        }

        var defaultProperty = settingsType.GetProperty("Default",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        return defaultProperty?.GetValue(null);
    }

    /// <summary>
    /// Gets all available TPPA/PolarAlignment options and their current values
    /// </summary>
    [Route(HttpVerbs.Get, "/tppa/options")]
    public object GetTPPAOptions()
    {
        try
        {
            var settingsInstance = GetPolarAlignmentSettingsInstance();
            if (settingsInstance == null)
            {
                HttpContext.Response.StatusCode = 503;
                return new Dictionary<string, object>()
                {
                    { "Success", false },
                    { "Error", "PolarAlignment plugin not loaded" }
                };
            }

            var settingsType = settingsInstance.GetType();
            var options = new Dictionary<string, object>();

            // Define which options to include (excluding colors and WPF-specific options)
            var optionProperties = new[]
            {
                // Movement & Control
                "DefaultMoveRate",
                "DefaultEastDirection",
                "MoveTimeoutFactor",

                // Targeting Parameters
                "DefaultTargetDistance",
                "DefaultSearchRadius",
                "DefaultAzimuthOffset",
                "DefaultAltitudeOffset",

                // Alignment Control
                "AlignmentTolerance",
                "RefractionAdjustment",
                "StopTrackingWhenDone",

                // Automation Options
                "AutomatedAdjustmentSettleTime",
                "AutoPause",
                "LogError"
            };

            foreach (var optionName in optionProperties)
            {
                var property = settingsType.GetProperty(optionName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (property != null && property.CanRead)
                {
                    try
                    {
                        var value = property.GetValue(settingsInstance);
                        options[optionName] = new Dictionary<string, object>()
                        {
                            { "Value", value ?? "" },
                            { "Type", property.PropertyType.Name }
                        };
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to read option {optionName}: {ex.Message}");
                        options[optionName] = new Dictionary<string, object>()
                        {
                            { "Value", null },
                            { "Type", property.PropertyType.Name },
                            { "Error", ex.Message }
                        };
                    }
                }
            }

            HttpContext.Response.StatusCode = 200;
            return new Dictionary<string, object>()
            {
                { "Success", true },
                { "Options", options }
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            HttpContext.Response.StatusCode = 500;
            return new Dictionary<string, object>()
            {
                { "Success", false },
                { "Error", $"Failed to fetch TPPA options: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Sets multiple TPPA/PolarAlignment options at once via JSON body
    /// POST /tppa/options
    /// Request body: { "DefaultMoveRate": 5, "StopTrackingWhenDone": true, ... }
    /// </summary>
    [Route(HttpVerbs.Post, "/tppa/options")]
    public async Task<object> SetTPPAOptions()
    {
        try
        {
            var settingsInstance = GetPolarAlignmentSettingsInstance();
            if (settingsInstance == null)
            {
                HttpContext.Response.StatusCode = 503;
                return new Dictionary<string, object>()
                {
                    { "Success", false },
                    { "Error", "PolarAlignment plugin not loaded" }
                };
            }

            var settingsType = settingsInstance.GetType();

            // Parse the request body to get options dictionary
            var requestData = await HttpContext.GetRequestDataAsync<Dictionary<string, object>>();
            if (requestData == null || requestData.Count == 0)
            {
                HttpContext.Response.StatusCode = 400;
                return new Dictionary<string, object>()
                {
                    { "Success", false },
                    { "Error", "Request body must contain at least one option" }
                };
            }

            var setCount = 0;
            var failures = new Dictionary<string, string>();

            foreach (var kvp in requestData)
            {
                var optionName = kvp.Key;
                var newValue = kvp.Value;

                try
                {
                    var property = settingsType.GetProperty(optionName,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                    if (property == null)
                    {
                        failures[optionName] = $"Option '{optionName}' not found";
                        continue;
                    }

                    if (!property.CanWrite)
                    {
                        failures[optionName] = $"Option '{optionName}' is read-only";
                        continue;
                    }

                    // Convert the value to the correct type
                    object convertedValue = newValue;
                    if (newValue != null)
                    {
                        var targetType = property.PropertyType;

                        if (targetType == typeof(bool) && newValue is string strBool)
                        {
                            convertedValue = bool.Parse(strBool);
                        }
                        else if (targetType == typeof(int) && newValue is not int)
                        {
                            convertedValue = Convert.ToInt32(newValue);
                        }
                        else if (targetType == typeof(double) && newValue is not double)
                        {
                            convertedValue = Convert.ToDouble(newValue);
                        }
                        else if (targetType == typeof(float) && newValue is not float)
                        {
                            convertedValue = Convert.ToSingle(newValue);
                        }
                    }

                    property.SetValue(settingsInstance, convertedValue);
                    setCount++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to set option {optionName}: {ex.Message}");
                    failures[optionName] = ex.Message;
                }
            }

            // Save the settings using the Save method if it exists
            try
            {
                var saveMethod = settingsType.GetMethod("Save",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (saveMethod != null)
                {
                    saveMethod.Invoke(settingsInstance, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not explicitly save settings: {ex.Message}");
            }

            HttpContext.Response.StatusCode = 200;
            return new Dictionary<string, object>()
            {
                { "Success", setCount > 0 },
                { "Message", $"Set {setCount} option(s)" },
                { "SetCount", setCount },
                { "Failures", failures.Count > 0 ? failures : null }
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            HttpContext.Response.StatusCode = 500;
            return new Dictionary<string, object>()
            {
                { "Success", false },
                { "Error", $"Failed to set TPPA options: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Resets all TPPA/PolarAlignment options to their default values
    /// POST /tppa/reset
    /// </summary>
    [Route(HttpVerbs.Post, "/tppa/reset")]
    public object ResetTPPAOptions()
    {
        try
        {
            var settingsInstance = GetPolarAlignmentSettingsInstance();
            if (settingsInstance == null)
            {
                HttpContext.Response.StatusCode = 503;
                return new Dictionary<string, object>()
                {
                    { "Success", false },
                    { "Error", "PolarAlignment plugin not loaded" }
                };
            }

            var settingsType = settingsInstance.GetType();

            // Define default values for each option
            var defaults = new Dictionary<string, object>()
            {
                // Movement & Control
                { "DefaultMoveRate", 3.0 },
                { "DefaultEastDirection", true },
                { "MoveTimeoutFactor", 2.0 },

                // Targeting Parameters
                { "DefaultTargetDistance", 10 },
                { "DefaultSearchRadius", 10.0 },
                { "DefaultAzimuthOffset", 1.0 },
                { "DefaultAltitudeOffset", 2.0 },

                // Alignment Control
                { "AlignmentTolerance", 0.0 },
                { "RefractionAdjustment", false },
                { "StopTrackingWhenDone", true },

                // Automation Options
                { "AutomatedAdjustmentSettleTime", 2.0 },
                { "AutoPause", false },
                { "LogError", false }
            };

            var resetCount = 0;
            var failedResets = new List<string>();

            foreach (var kvp in defaults)
            {
                try
                {
                    var property = settingsType.GetProperty(kvp.Key,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                    if (property != null && property.CanWrite)
                    {
                        // Convert the default value to the correct type if needed
                        var convertedValue = kvp.Value;
                        if (kvp.Value != null && property.PropertyType != kvp.Value.GetType())
                        {
                            convertedValue = Convert.ChangeType(kvp.Value, property.PropertyType);
                        }

                        property.SetValue(settingsInstance, convertedValue);
                        resetCount++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to reset option {kvp.Key}: {ex.Message}");
                    failedResets.Add(kvp.Key);
                }
            }

            // Save the settings
            try
            {
                var saveMethod = settingsType.GetMethod("Save",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (saveMethod != null)
                {
                    saveMethod.Invoke(settingsInstance, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not explicitly save settings after reset: {ex.Message}");
            }

            HttpContext.Response.StatusCode = 200;
            return new Dictionary<string, object>()
            {
                { "Success", true },
                { "Message", $"Reset {resetCount} TPPA options to defaults" },
                { "ResetCount", resetCount },
                { "FailedResets", failedResets.Count > 0 ? failedResets : null }
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            HttpContext.Response.StatusCode = 500;
            return new Dictionary<string, object>()
            {
                { "Success", false },
                { "Error", $"Failed to reset TPPA options: {ex.Message}" }
            };
        }
    }
}
