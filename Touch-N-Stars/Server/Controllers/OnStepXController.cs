using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyTelescope;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TouchNStars.Server.Models;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// Settings of pins' native OnStepX mount driver that live in the controller itself: altitude and meridian limits, the
/// preferred pier side, set home, and the guide rate and firmware for display. Every route answers 404 unless NINA's
/// mount is the native OnStepX driver and connected; a value the controller refuses answers 400 with its reason.
/// </summary>
public class OnStepXController : WebApiController
{
    /// <summary>GET /api/onstepx/settings - The controller's settings (OnStepXMountSettings).</summary>
    [Route(HttpVerbs.Get, "/onstepx/settings")]
    public async Task<ApiResponse> GetSettings()
    {
        if (INDIController.NativeOnStepXMount() is not { } mount)
        {
            return NotConnected();
        }
        return await Run(() => Ok(mount.GetMountSettings(), "OnStepXSettings"), "reading the OnStepX settings");
    }

    /// <summary>
    /// POST /api/onstepx/altitude-limits - Body { "min": -10, "max": 85 }: the lowest (-30 to 30) and highest (60 to
    /// 90) altitude in whole degrees, stored by the controller.
    /// </summary>
    [Route(HttpVerbs.Post, "/onstepx/altitude-limits")]
    public async Task<ApiResponse> SetAltitudeLimits()
    {
        if (INDIController.NativeOnStepXMount() is not { } mount)
        {
            return NotConnected();
        }
        var body = await HttpContext.GetRequestDataAsync<Dictionary<string, object>>();
        if (!TryGetInt(body, "min", out int min) || !TryGetInt(body, "max", out int max))
        {
            return Error("Body must contain integer 'min' and 'max' (degrees)", 400);
        }
        return await Run(() =>
        {
            mount.SetAltitudeLimits(min, max);
            return Ok(new { min, max }, "OnStepXAltitudeLimits");
        }, "setting the OnStepX altitude limits");
    }

    /// <summary>
    /// POST /api/onstepx/meridian-limits - Body { "east": 15, "west": 10 }: degrees past the meridian the mount may
    /// track on the east and on the west side of the pier (-360 to 360, rounded to 0.25°), stored by the controller.
    /// </summary>
    [Route(HttpVerbs.Post, "/onstepx/meridian-limits")]
    public async Task<ApiResponse> SetMeridianLimits()
    {
        if (INDIController.NativeOnStepXMount() is not { } mount)
        {
            return NotConnected();
        }
        var body = await HttpContext.GetRequestDataAsync<Dictionary<string, object>>();
        if (!TryGetDouble(body, "east", out double east) || !TryGetDouble(body, "west", out double west))
        {
            return Error("Body must contain numeric 'east' and 'west' (degrees)", 400);
        }
        return await Run(() =>
        {
            mount.SetMeridianLimits(east, west);
            return Ok(new { east, west }, "OnStepXMeridianLimits");
        }, "setting the OnStepX meridian limits");
    }

    /// <summary>
    /// POST /api/onstepx/preferred-pier-side - Body { "side": "East" | "West" | "Best" | "" }: the profile setting
    /// TelescopeSettings.PreferredPierSide, which the driver sets on the mount at once and at every connect; "" leaves
    /// the mount's own.
    /// </summary>
    [Route(HttpVerbs.Post, "/onstepx/preferred-pier-side")]
    public async Task<ApiResponse> SetPreferredPierSide()
    {
        if (INDIController.NativeOnStepXMount() is null)
        {
            return NotConnected();
        }
        var body = await HttpContext.GetRequestDataAsync<Dictionary<string, object>>();
        string side = body != null && body.TryGetValue("side", out var s) ? s?.ToString()?.Trim() ?? string.Empty : null;
        if (side == null || (side.Length > 0 && side != "East" && side != "West" && side != "Best"))
        {
            return Error("Body must contain 'side': \"East\", \"West\", \"Best\" or \"\"", 400);
        }
        var settings = TouchNStars.Mediators?.Profile?.ActiveProfile?.TelescopeSettings;
        if (settings == null)
        {
            return Error("No active profile", 500);
        }
        return await Run(() =>
        {
            settings.PreferredPierSide = side;
            return Ok(new { side }, "OnStepXPreferredPierSide");
        }, "setting the preferred pier side");
    }

    /// <summary>
    /// POST /api/onstepx/set-home - Makes the current position the home position (:hF#). The mount must point at the
    /// pole (a GEM with the counterweights down) and stand still; the controller stops tracking and clears the park
    /// state, the alignment and custom tracking rates.
    /// </summary>
    [Route(HttpVerbs.Post, "/onstepx/set-home")]
    public async Task<ApiResponse> SetHome()
    {
        if (INDIController.NativeOnStepXMount() is not { } mount)
        {
            return NotConnected();
        }
        return await Run(() =>
        {
            mount.SetHome();
            return Ok("Home set", "OnStepXSetHome");
        }, "setting home");
    }

    private async Task<ApiResponse> Run(Func<ApiResponse> action, string what)
    {
        try
        {
            // serial commands block for a few hundred ms
            return await Task.Run(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Error(ex.Message, 400);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error {what}: {ex}");
            return Error($"An unexpected error occurred while {what}");
        }
    }

    private static bool TryGetInt(Dictionary<string, object> body, string key, out int value)
    {
        value = 0;
        return body != null && body.TryGetValue(key, out var raw)
            && int.TryParse(raw?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetDouble(Dictionary<string, object> body, string key, out double value)
    {
        value = 0;
        return body != null && body.TryGetValue(key, out var raw)
            && double.TryParse(raw?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private ApiResponse Ok(object response, string type)
    {
        HttpContext.Response.StatusCode = 200;
        return new ApiResponse { Success = true, Response = response, StatusCode = 200, Type = type };
    }

    private ApiResponse NotConnected() => Error("No native OnStepX mount is connected", 404);

    private ApiResponse Error(string error, int statusCode = 500)
    {
        HttpContext.Response.StatusCode = statusCode;
        return new ApiResponse { Success = false, Error = error, StatusCode = statusCode, Type = "Error" };
    }
}
