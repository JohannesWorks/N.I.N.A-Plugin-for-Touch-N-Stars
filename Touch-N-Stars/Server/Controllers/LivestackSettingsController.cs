using System;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using TouchNStars.Server.Models;
using TouchNStars.Server.Services;

namespace TouchNStars.Server.Controllers;

public class LivestackSettingsBody
{
    public bool? SaveStackedLights { get; set; }
}

/// <summary>
/// Options of the Livestack plugin that the app needs to change itself:
///   GET  /api/livestack/settings
///   POST /api/livestack/settings  { "SaveStackedLights": true }
/// The raw stack data behind the Livestack histogram only exists while SaveStackedLights is on.
/// </summary>
public class LivestackSettingsController : WebApiController
{
    [Route(HttpVerbs.Get, "/livestack/settings")]
    public ApiResponse GetSettings() => Wrap(Current);

    [Route(HttpVerbs.Post, "/livestack/settings")]
    public async Task<ApiResponse> SaveSettings()
    {
        var body = await HttpContext.GetRequestDataAsync<LivestackSettingsBody>();
        if (body?.SaveStackedLights == null)
        {
            HttpContext.Response.StatusCode = 400;
            return new ApiResponse { Success = false, Error = "SaveStackedLights is required", StatusCode = 400, Type = "Error" };
        }
        return Wrap(() =>
        {
            LivestackSettingsService.SetSaveStackedLights(body.SaveStackedLights.Value);
            return Current();
        });
    }

    private static object Current() => new { SaveStackedLights = LivestackSettingsService.GetSaveStackedLights() };

    private ApiResponse Wrap(Func<object> action)
    {
        try
        {
            return new ApiResponse { Success = true, Response = action(), StatusCode = 200, Type = "LivestackSettings" };
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            HttpContext.Response.StatusCode = 500;
            return new ApiResponse { Success = false, Error = ex.Message, StatusCode = 500, Type = "Error" };
        }
    }
}
