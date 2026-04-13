using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Image.ImageData;
using NINA.PlateSolving;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TouchNStars.Server.Models;
using TouchNStars.Utility;

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
            if (ext != ".fits" && ext != ".fit" && ext != ".fts" && ext != ".fz")
            {
                await SendJson(new FitsSolveResult { Success = false, Error = "File must be a FITS file (.fits, .fit, .fts, .fz)" }, 400);
                return;
            }

            // --- 2. FITS laden (NINA parst dabei automatisch alle Header) ---
            Logger.Info($"[FitsAnalysisController] Loading FITS file: {fullPath}");
            var imageData = await TouchNStars.Mediators.ImageDataFactory
                .CreateFromFile(fullPath, 16, false, RawConverterEnum.FREEIMAGE);

            if (imageData == null)
            {
                await SendJson(new FitsSolveResult { Success = false, Error = "Failed to load FITS file" }, 500);
                return;
            }

            // --- 3. WCS-Header prüfen (schneller Pfad — bereits gelöstes Bild) ---
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

            // Koordinaten-Hint: NINA parst RA/DEC und OBJCTRA/OBJCTDEC automatisch beim Laden
            Coordinates hint = imageData.MetaData.Telescope.Coordinates
                               ?? imageData.MetaData.Target.Coordinates
                               ?? ExtractCoordinatesFromGenericHeaders(imageData.MetaData.GenericHeaders);

            if (hint != null)
                Logger.Info($"[FitsAnalysisController] Using coordinate hint: RA={hint.RADegrees:F4} Dec={hint.Dec:F4}");
            else
                Logger.Info($"[FitsAnalysisController] No coordinate hint found — using blind solve");

            // FocalLength: erst aus Profil, Fallback aus FITS-Header (FOCALLEN)
            var profile = TouchNStars.Mediators.Profile.ActiveProfile;
            double focalLength = profile.TelescopeSettings.FocalLength;
            double pixelSize = profile.CameraSettings.PixelSize;

            if (focalLength <= 0 || double.IsNaN(focalLength))
            {
                double fitsFL = imageData.MetaData.Telescope.FocalLength;
                if (fitsFL > 0 && !double.IsNaN(fitsFL))
                {
                    focalLength = fitsFL;
                    Logger.Info($"[FitsAnalysisController] FocalLength from FITS header: {focalLength} mm");
                }
                else
                {
                    await SendJson(new FitsSolveResult
                    {
                        Success = false,
                        Error = "FocalLength not set in NINA profile and not found in FITS header (FOCALLEN). Please configure it in NINA settings."
                    }, 422);
                    return;
                }
            }

            // PixelSize: erst aus Profil, Fallback aus FITS-Header (XPIXSZ)
            if (pixelSize <= 0 || double.IsNaN(pixelSize))
            {
                double fitsPS = GetDoubleFromGenericHeaders(imageData.MetaData.GenericHeaders, "XPIXSZ");
                if (fitsPS > 0)
                {
                    pixelSize = fitsPS;
                    Logger.Info($"[FitsAnalysisController] PixelSize from FITS header: {pixelSize} µm");
                }
            }

            var solveSettings = profile.PlateSolveSettings;

            var parameter = new PlateSolveParameter
            {
                FocalLength = focalLength,
                PixelSize = pixelSize,
                Binning = 1,
                Coordinates = hint,
                SearchRadius = solveSettings.SearchRadius,
                DownSampleFactor = solveSettings.DownSampleFactor,
                MaxObjects = solveSettings.MaxObjects,
                Regions = solveSettings.Regions,
                BlindFailoverEnabled = solveSettings.BlindFailoverEnabled
            };

            // --- 5. Solver ausführen — genau wie NINA intern (statische Factory) ---
            var plateSolver = PlateSolverFactory.GetPlateSolver(solveSettings);
            var blindSolver = PlateSolverFactory.GetBlindSolver(solveSettings);
            var imageSolver = new ImageSolver(plateSolver, blindSolver);

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
                    Error = "Plate solving failed. Check NINA plate solver settings (ASTAP/Astrometry.net path and configuration)."
                }, 422);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[FitsAnalysisController.Analyze] {ex.Message}", ex);
            await SendJson(new FitsSolveResult { Success = false, Error = ex.Message }, 500);
        }
    }

    /// <summary>
    /// Liest Koordinaten aus rohen FITS-Headern als letzter Fallback.
    /// NINA parst RA/DEC und OBJCTRA/OBJCTDEC bereits beim Laden in MetaData —
    /// diese Methode greift nur auf GenericHeaders zurück falls MetaData leer ist.
    /// </summary>
    private static Coordinates ExtractCoordinatesFromGenericHeaders(List<IGenericMetaDataHeader> headers)
    {
        if (headers == null || headers.Count == 0)
            return null;

        var headerDict = headers.ToDictionary(h => h.Key, h => h, StringComparer.OrdinalIgnoreCase);

        // Variante 1: RA + DEC als Dezimalgrad (NINA-Standard beim Capture)
        if (headerDict.TryGetValue("RA", out var raHeader) &&
            headerDict.TryGetValue("DEC", out var decHeader))
        {
            if (TryGetDouble(raHeader, out double ra) && TryGetDouble(decHeader, out double dec))
            {
                Logger.Debug($"[FitsAnalysisController] Hint from RA/DEC headers: {ra:F4} / {dec:F4}");
                return new Coordinates(Angle.ByDegree(ra), Angle.ByDegree(dec), Epoch.J2000);
            }
        }

        // Variante 2: OBJCTRA + OBJCTDEC als HMS/DMS String (NINA-Standard für Target)
        if (headerDict.TryGetValue("OBJCTRA", out var objRaHeader) &&
            headerDict.TryGetValue("OBJCTDEC", out var objDecHeader))
        {
            string raStr = GetStringValue(objRaHeader)?.Trim();
            string decStr = GetStringValue(objDecHeader)?.Trim();
            if (!string.IsNullOrWhiteSpace(raStr) && !string.IsNullOrWhiteSpace(decStr))
            {
                try
                {
                    // NINA schreibt "HH MM SS.s" → zu "HH:MM:SS" konvertieren
                    double raDeg = CoreUtility.HmsToDegrees(raStr.Replace(' ', ':'));
                    double decDeg = CoreUtility.DmsToDegrees(decStr.Replace(' ', ':'));
                    Logger.Debug($"[FitsAnalysisController] Hint from OBJCTRA/OBJCTDEC: {raDeg:F4} / {decDeg:F4}");
                    return new Coordinates(Angle.ByDegree(raDeg), Angle.ByDegree(decDeg), Epoch.J2000);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[FitsAnalysisController] Could not parse OBJCTRA/OBJCTDEC: {ex.Message}");
                }
            }
        }

        return null;
    }

    private static double GetDoubleFromGenericHeaders(List<IGenericMetaDataHeader> headers, string key)
    {
        if (headers == null) return 0;
        var header = headers.FirstOrDefault(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase));
        if (header != null && TryGetDouble(header, out double val)) return val;
        return 0;
    }

    private static bool TryGetDouble(IGenericMetaDataHeader header, out double value)
    {
        if (header is IGenericMetaDataHeader<double> dh) { value = dh.Value; return true; }
        if (header is IGenericMetaDataHeader<string> sh &&
            double.TryParse(sh.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
        value = 0;
        return false;
    }

    private static string GetStringValue(IGenericMetaDataHeader header)
    {
        if (header is IGenericMetaDataHeader<string> sh) return sh.Value;
        if (header is IGenericMetaDataHeader<double> dh) return dh.Value.ToString(CultureInfo.InvariantCulture);
        return null;
    }
}
