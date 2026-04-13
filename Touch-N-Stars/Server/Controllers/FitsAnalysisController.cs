using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.PlateSolving;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TouchNStars.Server.Models;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// Controller for FITS file analysis and plate solving.
/// Routes:
///   GET /api/fits/analyze?path=... — read WCS headers or trigger plate solve
/// </summary>
internal class FitsAnalysisController : WebApiController
{
    private Task SendJson(object data, int statusCode = 200)
    {
        HttpContext.Response.StatusCode = statusCode;
        string json = JsonConvert.SerializeObject(data);
        return HttpContext.SendStringAsync(json, "application/json", Encoding.UTF8);
    }

    // -------------------------------------------------------------------------
    // GET /api/fits/analyze?path=...
    // -------------------------------------------------------------------------
    [Route(HttpVerbs.Get, "/fits/analyze")]
    public async Task Analyze()
    {
        try
        {
            // --- 1. Pfad validieren ---
            string pathParam = HttpContext.Request.QueryString["path"];
            if (string.IsNullOrWhiteSpace(pathParam))
            {
                await SendJson(new FitsSolveResult { Success = false, Error = "Missing 'path' query parameter" }, 400);
                return;
            }

            string fullPath = Path.GetFullPath(Uri.UnescapeDataString(pathParam));

            if (!File.Exists(fullPath))
            {
                await SendJson(new FitsSolveResult { Success = false, Error = "File does not exist" }, 404);
                return;
            }

            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (ext != ".fits" && ext != ".fit" && ext != ".fts")
            {
                await SendJson(new FitsSolveResult { Success = false, Error = "File must be a FITS file (.fits, .fit, .fts)" }, 400);
                return;
            }

            // --- 2. FITS laden ---
            Logger.Info($"[FitsAnalysisController] Loading FITS file: {fullPath}");
            var imageData = await TouchNStars.Mediators.ImageDataFactory
                .CreateFromFile(fullPath, 16, false, RawConverterEnum.FREEIMAGE);

            if (imageData == null)
            {
                await SendJson(new FitsSolveResult { Success = false, Error = "Failed to load FITS file" }, 500);
                return;
            }

            // --- 3. WCS-Header prüfen (schneller Pfad) ---
            var wcs = imageData.MetaData.WorldCoordinateSystem;
            if (wcs?.Coordinates != null)
            {
                Logger.Info($"[FitsAnalysisController] WCS headers found, returning directly");
                var coords = wcs.Coordinates;
                await SendJson(new FitsSolveResult
                {
                    Success = true,
                    Ra = coords.RADegrees,
                    Dec = coords.Dec,
                    RaString = coords.RAString,
                    DecString = coords.DecString,
                    Rotation = wcs.Rotation,
                    PixelScale = wcs.PixelScaleX,
                    SolvedFromWcs = true
                });
                return;
            }

            // --- 4. Kein WCS → Plate Solve ---
            Logger.Info($"[FitsAnalysisController] No WCS headers found, starting plate solve");

            // Koordinaten-Hint aus FITS-Metadaten (Teleskop-Pointing oder Target)
            Coordinates hint = imageData.MetaData.Telescope.Coordinates
                               ?? imageData.MetaData.Target.Coordinates;

            var profile = TouchNStars.Mediators.Profile.ActiveProfile;
            var solveSettings = profile.PlateSolveSettings;

            var parameter = new PlateSolveParameter
            {
                FocalLength = profile.TelescopeSettings.FocalLength,
                PixelSize = profile.CameraSettings.PixelSize,
                Binning = 1,
                Coordinates = hint,
                SearchRadius = solveSettings.SearchRadius,
                DownSampleFactor = solveSettings.DownSampleFactor,
                MaxObjects = solveSettings.MaxObjects,
                Regions = solveSettings.Regions,
                BlindFailoverEnabled = solveSettings.BlindFailoverEnabled
            };

            // --- 5. Solver ausführen ---
            var factory = TouchNStars.Mediators.PlateSolverFactory;
            var plateSolver = factory.GetPlateSolver(solveSettings);
            var blindSolver = factory.GetBlindSolver(solveSettings);
            var imageSolver = factory.GetImageSolver(plateSolver, blindSolver);

            var progress = new Progress<ApplicationStatus>();
            var result = await imageSolver.Solve(imageData, parameter, progress, CancellationToken.None);

            // --- 6. Ergebnis zurückgeben ---
            if (result.Success)
            {
                Logger.Info($"[FitsAnalysisController] Plate solve successful: RA={result.Coordinates.RADegrees:F4} Dec={result.Coordinates.Dec:F4} PA={result.PositionAngle:F2}");
                var coords = result.Coordinates;
                await SendJson(new FitsSolveResult
                {
                    Success = true,
                    Ra = coords.RADegrees,
                    Dec = coords.Dec,
                    RaString = coords.RAString,
                    DecString = coords.DecString,
                    Rotation = result.PositionAngle,
                    PixelScale = result.Pixscale,
                    SolvedFromWcs = false
                });
            }
            else
            {
                Logger.Warning($"[FitsAnalysisController] Plate solve failed for: {fullPath}");
                await SendJson(new FitsSolveResult
                {
                    Success = false,
                    Error = "Plate solving failed. Check NINA plate solver settings."
                }, 422);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[FitsAnalysisController.Analyze] {ex.Message}", ex);
            await SendJson(new FitsSolveResult { Success = false, Error = ex.Message }, 500);
        }
    }
}
