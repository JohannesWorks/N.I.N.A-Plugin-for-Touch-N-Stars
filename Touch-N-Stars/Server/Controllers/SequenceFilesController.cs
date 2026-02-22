using TouchNStars.Server.Models;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// API Controller for saving sequence files to the NINA sequences directory
/// </summary>
public class SequenceFilesController : WebApiController
{
    private static string NinaSequencesDir =>
        TouchNStars.Mediators.Profile.ActiveProfile.SequenceSettings.DefaultSequenceFolder;

    /// <summary>
    /// POST /api/sequence-files - Save a NINA sequence JSON to the NINA sequences directory
    /// </summary>
    [Route(HttpVerbs.Post, "/sequence-files")]
    public async Task<ApiResponse> SaveSequenceFile()
    {
        try
        {
            var request = await HttpContext.GetRequestDataAsync<SequenceFileSaveRequest>();

            if (string.IsNullOrWhiteSpace(request?.Filename))
            {
                HttpContext.Response.StatusCode = 400;
                return new ApiResponse
                {
                    Success = false,
                    Error = "Filename is required",
                    StatusCode = 400,
                    Type = "Error"
                };
            }

            if (string.IsNullOrWhiteSpace(request?.Content))
            {
                HttpContext.Response.StatusCode = 400;
                return new ApiResponse
                {
                    Success = false,
                    Error = "Content is required",
                    StatusCode = 400,
                    Type = "Error"
                };
            }

            string sanitizedFilename = SanitizeFilename(request.Filename);
            if (!sanitizedFilename.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                sanitizedFilename += ".json";

            string sequencesDir = NinaSequencesDir;
            Directory.CreateDirectory(sequencesDir);

            string filePath = Path.Combine(sequencesDir, sanitizedFilename);
            await File.WriteAllTextAsync(filePath, request.Content);

            Logger.Info($"Sequence file saved: {filePath}");

            return new ApiResponse
            {
                Success = true,
                Response = filePath,
                StatusCode = 200,
                Type = "SequenceFile"
            };
        }
        catch (Exception ex)
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

    private static string SanitizeFilename(string filename)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(filename.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
