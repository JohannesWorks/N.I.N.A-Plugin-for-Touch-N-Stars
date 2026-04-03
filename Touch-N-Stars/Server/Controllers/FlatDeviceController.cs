using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Sequencer.SequenceItem.FlatDevice;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TouchNStars.Server.Models;

namespace TouchNStars.Server.Controllers;

public class FlatDeviceController : WebApiController
{
    private static Task _flatTask;
    private static CancellationTokenSource _flatCts;
    private static int _totalIterations;
    private static int _priorCompletedIterations;   // sum of fully-finished filters
    private static Func<int> _liveCompleted;         // reads CompletedIterations from active flat
    private static Func<double?> _liveADU;           // reads DeterminedHistogramADU from active flat
    private static double? _lastADU;                 // last ADU after a filter finishes
    private static int _completedFilters;
    private static int _totalFilters;

    // ── POST /api/flats/multimode ────────────────────────────────────────────
    [Route(HttpVerbs.Post, "/flats/multimode")]
    public async Task<ApiResponse> StartMultiMode()
    {
        MultimodeRequest payload;
        try
        {
            using var reader = new StreamReader(HttpContext.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            payload = JsonSerializer.Deserialize<MultimodeRequest>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            HttpContext.Response.StatusCode = 400;
            return new ApiResponse { Success = false, Error = $"Invalid request body: {ex.Message}", StatusCode = 400, Type = "Error" };
        }

        if (payload?.Filters == null || payload.Filters.Count == 0)
        {
            HttpContext.Response.StatusCode = 400;
            return new ApiResponse { Success = false, Error = "No filters specified", StatusCode = 400, Type = "Error" };
        }

        if (_flatTask != null && !_flatTask.IsCompleted)
        {
            HttpContext.Response.StatusCode = 409;
            return new ApiResponse { Success = false, Error = "Flat capture already running", StatusCode = 409, Type = "Error" };
        }

        var profile = TouchNStars.Mediators?.Profile?.ActiveProfile;
        if (profile == null)
        {
            HttpContext.Response.StatusCode = 503;
            return new ApiResponse { Success = false, Error = "Profile not available", StatusCode = 503, Type = "Error" };
        }

        // Validate filter IDs
        var filterList = profile.FilterWheelSettings.FilterWheelFilters;
        foreach (var fc in payload.Filters)
        {
            if (fc.FilterId < 0 || fc.FilterId >= filterList.Count)
            {
                HttpContext.Response.StatusCode = 400;
                return new ApiResponse { Success = false, Error = $"Filter id {fc.FilterId} is not available", StatusCode = 400, Type = "Error" };
            }
        }

        try
        {
            _flatCts?.Dispose();
            _flatCts = new CancellationTokenSource();
            var token = _flatCts.Token;

            var progress = new Progress<ApplicationStatus>();
            var mode = payload.Mode ?? "AutoExposure";
            var keepClosed = payload.KeepClosed;
            var filters = payload.Filters;

            _totalIterations = filters.Sum(fc => fc.Count);
            _priorCompletedIterations = 0;
            _liveCompleted = null;
            _liveADU = null;
            _lastADU = null;
            _completedFilters = 0;
            _totalFilters = filters.Count;

            _flatTask = Task.Run(async () =>
            {
                foreach (var fc in filters)
                {
                    token.ThrowIfCancellationRequested();

                    bool isLast = fc == filters[filters.Count - 1];
                    bool panelClosed = !isLast || keepClosed;

                    switch (mode)
                    {
                        case "AutoBrightness":
                            await RunAutoBrightness(fc, filterList[fc.FilterId], panelClosed, progress, token);
                            break;
                        case "SkyFlat":
                            await RunSkyFlat(fc, filterList[fc.FilterId], progress, token);
                            break;
                        default: // AutoExposure
                            await RunAutoExposure(fc, filterList[fc.FilterId], panelClosed, progress, token);
                            break;
                    }
                    _priorCompletedIterations += fc.Count;
                    _completedFilters++;
                }
            }, token);

            return new ApiResponse { Success = true, Response = "Multi-mode flat capture started", StatusCode = 200, Type = "Success" };
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            HttpContext.Response.StatusCode = 500;
            return new ApiResponse { Success = false, Error = ex.Message, StatusCode = 500, Type = "Error" };
        }
    }

    // ── GET /api/flats/status ─────────────────────────────────────────────────
    [Route(HttpVerbs.Get, "/flats/status")]
    public ApiResponse GetMultiModeStatus()
    {
        bool running = _flatTask != null && !_flatTask.IsCompleted;
        int completed = _priorCompletedIterations + (_liveCompleted?.Invoke() ?? 0);
        double? adu = _liveADU?.Invoke() ?? _lastADU;
        return new ApiResponse
        {
            Success = true,
            StatusCode = 200,
            Type = "Success",
            Response = new
            {
                State = running ? "Running" : "Finished",
                TotalIterations = running ? _totalIterations : -1,
                CompletedIterations = running ? completed : -1,
                TotalFilters = running ? _totalFilters : -1,
                CompletedFilters = running ? _completedFilters : -1,
                CurrentADU = adu,
            },
        };
    }

    // ── GET /api/flats/stop ──────────────────────────────────────────────────
    [Route(HttpVerbs.Get, "/flats/stop")]
    public ApiResponse StopFlats()
    {
        try
        {
            _flatCts?.Cancel();
            return new ApiResponse { Success = true, Response = "Flat capture stop requested", StatusCode = 200, Type = "Success" };
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            HttpContext.Response.StatusCode = 500;
            return new ApiResponse { Success = false, Error = ex.Message, StatusCode = 500, Type = "Error" };
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task RunAutoExposure(FilterConfig fc, NINA.Core.Model.Equipment.FilterInfo filter,
        bool keepPanelClosed, IProgress<ApplicationStatus> progress, CancellationToken token)
    {
        var flat = new AutoExposureFlat(
            TouchNStars.Mediators.Profile,
            TouchNStars.Mediators.Camera,
            TouchNStars.Mediators.Imaging,
            TouchNStars.Mediators.ImageSaveMediator,
            TouchNStars.Mediators.ImageHistory,
            TouchNStars.Mediators.FilterWheel,
            TouchNStars.Mediators.FlatDevice);

        flat.GetIterations().Iterations = fc.Count;
        flat.MinExposure = fc.MinExposure;
        flat.MaxExposure = fc.MaxExposure;
        flat.GetSetBrightnessItem().Brightness = fc.Brightness;
        flat.HistogramTargetPercentage = fc.HistogramMean / 100.0;
        flat.HistogramTolerancePercentage = fc.MeanTolerance / 100.0;
        flat.GetExposureItem().Gain = fc.Gain;
        flat.GetExposureItem().Offset = fc.Offset;
        flat.GetSwitchFilterItem().Filter = filter;
        flat.KeepPanelClosed = keepPanelClosed;
        SetBinning(flat.GetExposureItem(), fc.Binning);

        var issues = flat.Validate() ? null : flat.Issues;
        if (issues?.Count > 0)
            Logger.Warning($"AutoExposureFlat validation issues for filter {filter?.Name}: {string.Join(", ", issues)}");

        _liveCompleted = () => flat.GetIterations().CompletedIterations;
        _liveADU = () => flat.DeterminedHistogramADU > 0 ? flat.DeterminedHistogramADU : (double?)null;
        await flat.Execute(progress, token);
        _lastADU = flat.DeterminedHistogramADU > 0 ? flat.DeterminedHistogramADU : (double?)null;
        _liveCompleted = null;
        _liveADU = null;
    }

    private static async Task RunAutoBrightness(FilterConfig fc, NINA.Core.Model.Equipment.FilterInfo filter,
        bool keepPanelClosed, IProgress<ApplicationStatus> progress, CancellationToken token)
    {
        var flat = new AutoBrightnessFlat(
            TouchNStars.Mediators.Profile,
            TouchNStars.Mediators.Camera,
            TouchNStars.Mediators.Imaging,
            TouchNStars.Mediators.ImageSaveMediator,
            TouchNStars.Mediators.ImageHistory,
            TouchNStars.Mediators.FilterWheel,
            TouchNStars.Mediators.FlatDevice);

        flat.GetIterations().Iterations = fc.Count;
        flat.GetExposureItem().ExposureTime = fc.ExposureTime;
        flat.MaxBrightness = fc.MaxBrightness;
        flat.MinBrightness = fc.MinBrightness;
        flat.HistogramTargetPercentage = fc.HistogramMean / 100.0;
        flat.HistogramTolerancePercentage = fc.MeanTolerance / 100.0;
        flat.GetExposureItem().Gain = fc.Gain;
        flat.GetExposureItem().Offset = fc.Offset;
        flat.GetSwitchFilterItem().Filter = filter;
        flat.KeepPanelClosed = keepPanelClosed;
        SetBinning(flat.GetExposureItem(), fc.Binning);

        // Expand the expression range so Validate() accepts values beyond the default 100-cap
        var deviceInfo = TouchNStars.Mediators.FlatDevice?.GetInfo();
        double deviceMax = (deviceInfo?.Connected == true) ? deviceInfo.MaxBrightness : 0;
        flat.MaxBrightnessExpression.Range = [0, deviceMax, 0];
        flat.MinBrightnessExpression.Range = [0, deviceMax, 0];

        var issues = flat.Validate() ? null : flat.Issues;
        if (issues?.Count > 0)
            Logger.Warning($"AutoBrightnessFlat validation issues for filter {filter?.Name}: {string.Join(", ", issues)}");

        _liveCompleted = () => flat.GetIterations().CompletedIterations;
        _liveADU = () => flat.DeterminedHistogramADU > 0 ? flat.DeterminedHistogramADU : (double?)null;
        await flat.Execute(progress, token);
        _lastADU = flat.DeterminedHistogramADU > 0 ? flat.DeterminedHistogramADU : (double?)null;
        _liveCompleted = null;
        _liveADU = null;
    }

    private static async Task RunSkyFlat(FilterConfig fc, NINA.Core.Model.Equipment.FilterInfo filter,
        IProgress<ApplicationStatus> progress, CancellationToken token)
    {
        var flat = new SkyFlat(
            TouchNStars.Mediators.Profile,
            TouchNStars.Mediators.Camera,
            TouchNStars.Mediators.Telescope,
            TouchNStars.Mediators.Imaging,
            TouchNStars.Mediators.ImageSaveMediator,
            TouchNStars.Mediators.ImageHistory,
            TouchNStars.Mediators.FilterWheel,
            TouchNStars.Mediators.TwilightCalculator,
            TouchNStars.Mediators.SymbolBroker);

        flat.GetIterations().Iterations = fc.Count;
        flat.MinExposure = fc.MinExposure;
        flat.MaxExposure = fc.MaxExposure;
        flat.HistogramTargetPercentage = fc.HistogramMean / 100.0;
        flat.HistogramTolerancePercentage = fc.MeanTolerance / 100.0;
        flat.GetExposureItem().Gain = fc.Gain;
        flat.GetExposureItem().Offset = fc.Offset;
        flat.GetSwitchFilterItem().Filter = filter;
        SetBinning(flat.GetExposureItem(), fc.Binning);

        var issues = flat.Validate() ? null : flat.Issues;
        if (issues?.Count > 0)
            Logger.Warning($"SkyFlat validation issues for filter {filter?.Name}: {string.Join(", ", issues)}");

        _liveCompleted = () => flat.GetIterations().CompletedIterations;
        _liveADU = () => flat.DeterminedHistogramADU > 0 ? flat.DeterminedHistogramADU : (double?)null;
        await flat.Execute(progress, token);
        _lastADU = flat.DeterminedHistogramADU > 0 ? flat.DeterminedHistogramADU : (double?)null;
        _liveCompleted = null;
        _liveADU = null;
    }

    private static void SetBinning(NINA.Sequencer.SequenceItem.Imaging.TakeExposure exposure, string binningName)
    {
        if (string.IsNullOrEmpty(binningName)) return;
        var parts = binningName.Split('x');
        if (parts.Length == 2 && short.TryParse(parts[0], out short x) && short.TryParse(parts[1], out short y))
            exposure.Binning = new BinningMode(x, y);
    }
}

// ── Request models ────────────────────────────────────────────────────────────

public class MultimodeRequest
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; }

    [JsonPropertyName("keepClosed")]
    public bool KeepClosed { get; set; }

    [JsonPropertyName("filters")]
    public List<FilterConfig> Filters { get; set; }
}

public class FilterConfig
{
    [JsonPropertyName("filterId")]
    public int FilterId { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; } = 20;

    [JsonPropertyName("gain")]
    public int Gain { get; set; } = -1;

    [JsonPropertyName("offset")]
    public int Offset { get; set; } = -1;

    [JsonPropertyName("binning")]
    public string Binning { get; set; } = "1x1";

    /// <summary>0–100 % — converted to 0–1 fraction before passing to NINA.</summary>
    [JsonPropertyName("histogramMean")]
    public double HistogramMean { get; set; } = 50;

    /// <summary>0–100 % — converted to 0–1 fraction before passing to NINA.</summary>
    [JsonPropertyName("meanTolerance")]
    public double MeanTolerance { get; set; } = 10;

    // AutoExposure & SkyFlat
    [JsonPropertyName("minExposure")]
    public double MinExposure { get; set; } = 0.01;

    [JsonPropertyName("maxExposure")]
    public double MaxExposure { get; set; } = 20;

    // AutoExposure only
    [JsonPropertyName("brightness")]
    public int Brightness { get; set; } = 50;

    // AutoBrightness only
    [JsonPropertyName("exposureTime")]
    public double ExposureTime { get; set; } = 2;

    [JsonPropertyName("minBrightness")]
    public int MinBrightness { get; set; } = 0;

    [JsonPropertyName("maxBrightness")]
    public int MaxBrightness { get; set; } = 32000;
}
