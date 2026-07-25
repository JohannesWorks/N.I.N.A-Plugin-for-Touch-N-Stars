using System;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using TouchNStars.Server.Models;
using TouchNStars.Server.Services;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// Read and change the RGB channel assignment of the Livestack plugin, which otherwise is only
/// reachable through a modal wizard on the NINA machine.
///
/// Only filters that already have a live stack for the target can be used as a channel - that is
/// the same constraint the Livestack wizard enforces.
/// </summary>
public class LivestackController : WebApiController
{
    [Route(HttpVerbs.Get, "/livestack/combinations")]
    public ApiResponse GetCombinations()
    {
        try
        {
            // Available = false (with 200) rather than an error, so the app can hide the section
            // when the Livestack plugin is not installed.
            return new ApiResponse
            {
                Success = true,
                Response = LivestackBridge.GetCombinations(),
                StatusCode = 200,
                Type = "LivestackCombinations"
            };
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    [Route(HttpVerbs.Put, "/livestack/combinations/{target}")]
    public async Task<ApiResponse> SetCombination(string target)
    {
        try
        {
            LivestackCombinationRequest request = await HttpContext.GetRequestDataAsync<LivestackCombinationRequest>();
            LivestackOperationResult result = await LivestackBridge.SetCombinationAsync(target, request);

            return Respond(result, "LivestackCombination");
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    [Route(HttpVerbs.Delete, "/livestack/combinations/{target}")]
    public async Task<ApiResponse> RemoveCombination(string target)
    {
        try
        {
            LivestackOperationResult result = await LivestackBridge.RemoveCombinationAsync(target);

            return Respond(result, "LivestackCombination");
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    private ApiResponse Respond(LivestackOperationResult result, string type)
    {
        if (!result.Ok)
        {
            // 400/404/409 are ordinary domain states here (filter not stacked, target gone,
            // stack currently rendering) - the app surfaces them as guidance, not as a failure.
            HttpContext.Response.StatusCode = result.StatusCode;

            return new ApiResponse
            {
                Success = false,
                Error = result.Error,
                StatusCode = result.StatusCode,
                Type = "Error"
            };
        }

        return new ApiResponse
        {
            Success = true,
            Response = result.Target,
            StatusCode = 200,
            Type = type
        };
    }

    private ApiResponse Error(Exception ex)
    {
        Logger.Error(ex);
        HttpContext.Response.StatusCode = 500;

        return new ApiResponse
        {
            Success = false,
            Error = ex.Message,
            StatusCode = 500,
            Type = "Error"
        };
    }
}
