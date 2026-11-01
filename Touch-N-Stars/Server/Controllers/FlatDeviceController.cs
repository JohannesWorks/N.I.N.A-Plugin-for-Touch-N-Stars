using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TouchNStars.Server.Models;
using TouchNStars.Server.Services;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// Flat assistant runs executed by the plugin itself (see FlatWizardService), so they keep going
/// while the app is in the background. The status carries `Workflow` so the app can tell this
/// implementation apart from the earlier controller-side one.
/// </summary>
public class FlatDeviceController : WebApiController
{
    // ── POST /api/flats/multimode ────────────────────────────────────────────
    [Route(HttpVerbs.Post, "/flats/multimode")]
    public async Task<ApiResponse> StartRun()
    {
        FlatRunRequest payload;
        try
        {
            using var reader = new StreamReader(HttpContext.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            payload = JsonSerializer.Deserialize<FlatRunRequest>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            return Fail(400, $"Invalid request body: {ex.Message}");
        }

        try
        {
            var (statusCode, error) = FlatWizardService.Start(payload);
            if (error != null)
            {
                return Fail(statusCode, error);
            }
            return new ApiResponse { Success = true, Response = "Flat capture started", StatusCode = 200, Type = "Success" };
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return Fail(500, ex.Message);
        }
    }

    // ── GET /api/flats/status ─────────────────────────────────────────────────
    [Route(HttpVerbs.Get, "/flats/status")]
    public ApiResponse GetStatus()
    {
        try
        {
            return new ApiResponse { Success = true, Response = FlatWizardService.GetStatus(), StatusCode = 200, Type = "Success" };
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return Fail(500, ex.Message);
        }
    }

    // ── GET /api/flats/stop[?runId=...] ──────────────────────────────────────
    // Idempotent: stopping while nothing runs is fine. With runId, only that run is stopped.
    [Route(HttpVerbs.Get, "/flats/stop")]
    public ApiResponse StopRun([QueryField] string runId)
    {
        try
        {
            if (!FlatWizardService.Stop(runId))
            {
                return Fail(409, "That flat run is no longer the current one");
            }
            return new ApiResponse { Success = true, Response = "Flat capture stop requested", StatusCode = 200, Type = "Success" };
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return Fail(500, ex.Message);
        }
    }

    // ── GET /api/flats/confirm-cover?confirmed=true|false[&runId=...] ─────────
    // Answers the "cover the telescope" question a run waits on before its dark flats.
    [Route(HttpVerbs.Get, "/flats/confirm-cover")]
    public ApiResponse ConfirmCover([QueryField(true)] bool confirmed, [QueryField] string runId)
    {
        var (statusCode, error) = FlatWizardService.ConfirmCover(confirmed, runId);
        if (error != null)
        {
            return Fail(statusCode, error);
        }
        return new ApiResponse { Success = true, Response = confirmed ? "Dark flats started" : "Dark flats skipped", StatusCode = 200, Type = "Success" };
    }

    private ApiResponse Fail(int statusCode, string error)
    {
        HttpContext.Response.StatusCode = statusCode;
        return new ApiResponse { Success = false, Error = error, StatusCode = statusCode, Type = "Error" };
    }
}
