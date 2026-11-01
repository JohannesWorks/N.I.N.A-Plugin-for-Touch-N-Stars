using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem.FlatDevice;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TouchNStars.Server.Services;

/// <summary>
/// Runs the Touch-N-Stars flat assistant on the NINA side, so a run keeps going while the app
/// is closed or the phone is locked. One run covers one or more filters: flats for every filter
/// first, then (optionally) dark flats for every filter whose flats succeeded, the way NINA's own
/// flat wizard orders them. The app only starts, observes and stops a run.
///
/// Dark flats are captured directly through the imaging mediator rather than with NINA's
/// TrainedDarkFlatExposure: that item needs a connected flat device for its brightness step and
/// deliberately skips its filter change, which labels every dark with the wrong filter.
/// </summary>
public static class FlatWizardService
{
    public const int WorkflowVersion = 2;

    private const string StatusSource = "Touch 'N' Stars Flats";

    // Without a client the cover question would otherwise hold the run (and block every new one) forever.
    private static readonly TimeSpan CoverConfirmationTimeout = TimeSpan.FromMinutes(30);

    private static readonly object gate = new();

    private static CancellationTokenSource runCts;
    private static TaskCompletionSource<bool> coverConfirmation;

    // Run state, guarded by `gate`. Survives until the next run starts so an app that reconnects
    // after the run ended can still show its result.
    private static string runId;
    private static string mode;
    private static string phase = "idle";
    private static bool running;
    private static bool stopRequested;
    private static string runError;
    private static int darkCount;
    private static DateTime? startedAt;
    private static DateTime? finishedAt;
    private static int currentIndex = -1;
    private static List<FilterRunState> filterStates = new();
    private static Func<double?> liveADU;
    private static double? lastADU;
    private static int lastBitDepth;
    private static string targetName;
    // Target name the run's last flat was saved with; its dark flats are saved under the same name.
    private static string flatTargetName;

    public static bool IsRunning
    {
        get { lock (gate) return running; }
    }

    /// <summary>
    /// Validates the request and starts the run in the background. Returns status 200 on success,
    /// otherwise the HTTP status and the reason it was refused.
    /// </summary>
    public static (int StatusCode, string Error) Start(FlatRunRequest request)
    {
        var mediators = TouchNStars.Mediators;
        var profile = mediators?.Profile?.ActiveProfile;
        if (profile == null)
        {
            return (503, "Profile not available");
        }

        if (request?.Filters == null || request.Filters.Count == 0)
        {
            return (400, "No filters specified");
        }

        var filterList = profile.FilterWheelSettings.FilterWheelFilters;
        foreach (var fc in request.Filters)
        {
            if (fc.FilterId.HasValue && (fc.FilterId < 0 || fc.FilterId >= filterList.Count))
            {
                return (400, $"Filter id {fc.FilterId} is not available");
            }
            if (fc.Count <= 0)
            {
                return (400, "Count must be greater than zero");
            }
            if (!string.IsNullOrEmpty(fc.Binning) && ParseBinning(fc.Binning) == null)
            {
                return (400, $"Invalid binning '{fc.Binning}'");
            }
        }

        var requestedMode = request.Mode switch
        {
            "AutoBrightness" => "AutoBrightness",
            "SkyFlat" => "SkyFlat",
            _ => "AutoExposure",
        };

        // Sky flats change their exposure time from frame to frame, a single dark time cannot match them.
        if (requestedMode == "SkyFlat" && request.DarkCount > 0)
        {
            return (400, "Dark flats are not supported for sky flats");
        }

        if (mediators.Camera?.GetInfo()?.Connected != true)
        {
            return (409, "Camera not connected");
        }

        // The run would switch filters and the panel light under the sequence's lights.
        if (ImagingActivity.IsSequenceRunning())
        {
            return (409, "A sequence is running. Stop it before starting a flat run.");
        }

        lock (gate)
        {
            if (running)
            {
                return (409, "Flat capture already running");
            }

            runCts?.Dispose();
            runCts = new CancellationTokenSource();
            coverConfirmation = null;

            runId = Guid.NewGuid().ToString("N");
            mode = requestedMode;
            phase = "flats";
            running = true;
            stopRequested = false;
            runError = null;
            darkCount = Math.Max(0, request.DarkCount);
            startedAt = DateTime.UtcNow;
            finishedAt = null;
            currentIndex = -1;
            liveADU = null;
            lastADU = null;
            lastBitDepth = 0;
            targetName = FlatTargetNameService.SanitizeTargetName(request.TargetName);
            flatTargetName = null;
            // The filter is resolved once here: looking the index up again later would pick another
            // filter after a profile change or a removed filter.
            filterStates = request.Filters.Select(fc =>
            {
                var filter = fc.FilterId.HasValue ? filterList[fc.FilterId.Value] : null;
                return new FilterRunState
                {
                    Config = fc,
                    FilterId = fc.FilterId,
                    Filter = filter,
                    FilterName = filter?.Name,
                    State = "pending",
                    Count = fc.Count,
                    DarkCount = darkCount,
                    DarkState = darkCount > 0 ? "pending" : "none",
                };
            }).ToList();

            var token = runCts.Token;
            var keepClosed = request.KeepClosed;
            _ = Task.Run(() => RunAsync(keepClosed, token));
        }

        return (200, null);
    }

    /// <summary>
    /// Stops the run. With a runId only that run is stopped; returns false when it is not the
    /// current run.
    /// </summary>
    public static bool Stop(string forRunId = null)
    {
        CancellationTokenSource cts;
        TaskCompletionSource<bool> confirmation;
        lock (gate)
        {
            if (!IsCurrentRun(forRunId)) return false;
            if (!running) return true;
            stopRequested = true;
            cts = runCts;
            confirmation = coverConfirmation;
        }

        // Outside the lock: cancelling runs the drivers' abort callbacks synchronously (an HTTP call
        // for Alpaca), which must not hold up the status or NINA's save queue.
        confirmation?.TrySetResult(false);
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run ended and a new Start disposed it meanwhile.
        }
        return true;
    }

    /// <summary>
    /// Answers the "cover the telescope" question the run waits on before its dark flats.
    /// </summary>
    public static (int StatusCode, string Error) ConfirmCover(bool confirmed, string forRunId = null)
    {
        lock (gate)
        {
            if (!IsCurrentRun(forRunId)) return (409, "That flat run is no longer the current one");
            if (coverConfirmation == null || !coverConfirmation.TrySetResult(confirmed))
                return (409, "No flat run is waiting for the telescope to be covered");
            return (200, null);
        }
    }

    // Caller holds `gate`.
    private static bool IsCurrentRun(string forRunId) => string.IsNullOrEmpty(forRunId) || forRunId == runId;

    public static object GetStatus()
    {
        lock (gate)
        {
            var current = currentIndex >= 0 && currentIndex < filterStates.Count ? filterStates[currentIndex] : null;
            var inDarks = phase == "darks" || phase == "waiting-for-cover";
            var phaseFilters = inDarks ? filterStates.Where(f => f.DarkState != "none" && f.DarkState != "skipped").ToList() : filterStates;
            var adu = liveADU?.Invoke() ?? lastADU;

            int total, completed;
            if (current == null)
            {
                total = filterStates.Sum(f => f.Count);
                completed = filterStates.Sum(f => f.Completed);
            }
            else if (inDarks)
            {
                total = current.DarkCount;
                completed = current.DarkCompleted;
            }
            else
            {
                total = current.Count;
                completed = current.Completed;
            }

            return new
            {
                Workflow = WorkflowVersion,
                RunId = runId,
                State = running ? "Running" : (runId == null ? "Idle" : "Finished"),
                Phase = phase,
                Type = inDarks ? "darks" : "flats",
                Mode = mode,
                StopRequested = stopRequested,
                WaitingForCover = phase == "waiting-for-cover",
                Error = runError,
                TotalIterations = total,
                CompletedIterations = completed,
                TotalFilters = phaseFilters.Count,
                CompletedFilters = current == null ? -1 : Math.Max(0, phaseFilters.IndexOf(current)),
                CurrentFilterId = current?.FilterId,
                CurrentFilterName = current?.FilterName,
                CurrentADU = adu,
                // Bit depth of the image CurrentADU was measured on (full scale 2^BitDepth - 1). For RAW
                // frames it can be below the camera's profile bit depth, e.g. 14 for a NEF.
                BitDepth = adu.HasValue && lastBitDepth > 0 ? lastBitDepth : (int?)null,
                DarkCount = darkCount,
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                Filters = filterStates.Select(f => new
                {
                    f.FilterId,
                    f.FilterName,
                    f.State,
                    // Frames NINA actually wrote to disk, not loop iterations.
                    f.Completed,
                    f.SaveFailed,
                    f.Count,
                    f.ExposureTime,
                    f.Brightness,
                    f.LastADU,
                    f.Error,
                    f.DarkState,
                    f.DarkCompleted,
                    f.DarkSaveFailed,
                    f.DarkCount,
                }).ToList(),
            };
        }
    }

    // ── Run ─────────────────────────────────────────────────────────────────────

    private static async Task RunAsync(bool keepClosed, CancellationToken token)
    {
        var progress = new FlatStatusProgress(TouchNStars.Mediators.ApplicationStatus);

        List<FilterRunState> states;
        string runMode;
        string currentRunId;
        int darks;
        lock (gate)
        {
            states = filterStates;
            runMode = mode;
            currentRunId = runId;
            darks = darkCount;
        }

        var usesPanel = runMode != "SkyFlat";

        try
        {
            AttachImageHooks();

            for (int i = 0; i < states.Count; i++)
            {
                if (token.IsCancellationRequested) break;

                var state = states[i];
                // Between filters the panel stays closed; after the last one it only opens when
                // neither the user nor the upcoming dark flats need it closed.
                var isLast = i == states.Count - 1;
                var keepPanelClosed = !isLast || keepClosed || darks > 0;

                lock (gate)
                {
                    currentIndex = i;
                    state.State = "running";
                }

                await RunFlatsForFilter(state, runMode, keepPanelClosed, progress, token);
            }

            var darkTargets = states.Where(s => s.State == "success" && s.DarkCount > 0).ToList();
            lock (gate)
            {
                foreach (var s in states.Where(s => s.DarkState == "pending" && !darkTargets.Contains(s)))
                {
                    s.DarkState = "skipped";
                }
            }

            if (darkTargets.Count > 0 && !token.IsCancellationRequested)
            {
                await RunDarkFlats(darkTargets, usesPanel, progress, token);
            }

            if (usesPanel && !keepClosed && !token.IsCancellationRequested)
            {
                // A failed filter can leave the light on; never open the cover with it burning.
                await SwitchPanelLightOff();
                await OpenCoverIfSupported(progress, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stop requested - the per-filter states already say where it stopped. NINA's flat
            // items only switch the panel light off when they fail, not when they are cancelled.
            if (usesPanel) await SwitchPanelLightOff();
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            lock (gate) runError = ex.Message;
            if (usesPanel) await SwitchPanelLightOff();
        }
        finally
        {
            // NINA saves through a background queue, so the run's last frames can still be waiting
            // to be written when the run ends. Keep the hooks a little longer for them.
            _ = DetachImageHooksLater(currentRunId);

            lock (gate)
            {
                foreach (var s in filterStates)
                {
                    if (s.State == "pending" || s.State == "running") s.State = "stopped";
                    // Dark flats only ever run for filters whose flats succeeded.
                    if (s.DarkState == "pending" || s.DarkState == "running")
                        s.DarkState = stopRequested && s.State == "success" ? "stopped" : "skipped";
                }
                currentIndex = -1;
                liveADU = null;
                coverConfirmation = null;
                phase = "done";
                running = false;
                finishedAt = DateTime.UtcNow;
            }

            progress.Clear();
        }
    }

    private static async Task RunFlatsForFilter(FilterRunState state, string runMode, bool keepPanelClosed,
        IProgress<ApplicationStatus> progress, CancellationToken token)
    {
        var fc = state.Config;
        SequentialContainer flat;
        TakeExposure exposureItem;
        Func<double> determinedADU;
        Func<double?> brightness;

        try
        {
            (flat, exposureItem, determinedADU, brightness) = BuildFlat(runMode, fc, state.Filter, keepPanelClosed);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            lock (gate)
            {
                state.State = "failed";
                state.Error = ex.Message;
            }
            return;
        }

        if (!flat.Validate())
        {
            var issues = string.Join(", ", flat.Issues ?? Enumerable.Empty<string>());
            Logger.Warning($"Flat validation failed for filter {state.FilterName}: {issues}");
            lock (gate)
            {
                state.State = "failed";
                state.Error = issues;
            }
            return;
        }

        var loop = GetIterations(flat);
        lock (gate)
        {
            liveADU = () => determinedADU() > 0 ? determinedADU() : (double?)null;
        }

        string outcome;
        string error = null;
        try
        {
            await flat.Execute(progress, token);
            outcome = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            outcome = "stopped";
        }
        catch (SequenceEntityFailedException ex)
        {
            // NINA's flat items throw this when the light is too dim or too bright.
            Logger.Warning($"Flats failed for filter {state.FilterName}: {ex.Message}");
            error = ex.Message;
            outcome = ClassifySkyFlatLimit(ex.Message) ?? ClassifyFailure(determinedADU(), fc.HistogramMean / 100.0);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Flats failed for filter {state.FilterName}: {ex.Message}");
            error = ex.Message;
            outcome = token.IsCancellationRequested ? "stopped" : "failed";
        }

        if (outcome != "stopped")
        {
            // The flat items only switch the light off after a "too dim / too bright" failure, not
            // after a camera error.
            if (outcome != null && runMode != "SkyFlat") await SwitchPanelLightOff();

            // A loop iteration also counts when the exposure inside it failed (the flat items run
            // with ContinueOnError) or when the file could not be written, so only frames NINA
            // reports as saved count. Wait for the save queue before the next filter takes over the
            // counting. A successful iteration queues exactly one frame.
            var captured = loop.CompletedIterations;
            await WaitForSaves(() => state.Completed + state.SaveFailed >= captured,
                () => state.SavesStarted <= state.Completed + state.SaveFailed, token);
            if (outcome == null)
            {
                lock (gate)
                {
                    outcome = state.Completed >= state.Count ? "success" : "failed";
                    if (outcome == "failed") error = SaveSummary(state.Completed, state.Count, "flats", state.SaveError);
                }
            }
        }

        lock (gate)
        {
            var adu = determinedADU();
            state.LastADU = adu > 0 ? adu : null;
            state.ExposureTime = exposureItem.ExposureTime;
            state.Binning = exposureItem.Binning;
            state.Gain = exposureItem.Gain;
            state.Offset = exposureItem.Offset;
            state.Brightness = brightness();
            state.State = outcome;
            state.Error = error;
            lastADU = state.LastADU ?? lastADU;
            liveADU = null;
        }

        if (outcome == "stopped") token.ThrowIfCancellationRequested();
    }

    private static (SequentialContainer, TakeExposure, Func<double>, Func<double?>) BuildFlat(
        string runMode, FlatRunFilterConfig fc, FilterInfo filter, bool keepPanelClosed)
    {
        var m = TouchNStars.Mediators;

        switch (runMode)
        {
            case "AutoBrightness":
                {
                    var flat = new AutoBrightnessFlat(m.Profile, m.Camera, m.Imaging, m.ImageSaveMediator,
                        m.ImageHistory, m.FilterWheel, m.FlatDevice);
                    flat.GetIterations().Iterations = fc.Count;
                    flat.GetExposureItem().ExposureTime = fc.ExposureTime;

                    // The app sends the profile value (default 32767), which is above any real panel;
                    // NINA's own wizard clamps to the panel's range as well. Validate() only accepts
                    // values inside it. Without a connected panel validation fails, as it should.
                    var deviceInfo = m.FlatDevice?.GetInfo();
                    var maxBrightness = fc.MaxBrightness;
                    var minBrightness = fc.MinBrightness;
                    if (deviceInfo?.Connected == true && deviceInfo.MaxBrightness > deviceInfo.MinBrightness)
                    {
                        maxBrightness = Math.Clamp(maxBrightness, deviceInfo.MinBrightness, deviceInfo.MaxBrightness);
                        minBrightness = Math.Clamp(minBrightness, deviceInfo.MinBrightness, maxBrightness);
                    }
                    flat.MaxBrightness = maxBrightness;
                    flat.MinBrightness = minBrightness;

                    flat.HistogramTargetPercentage = fc.HistogramMean / 100.0;
                    flat.HistogramTolerancePercentage = fc.MeanTolerance / 100.0;
                    flat.KeepPanelClosed = keepPanelClosed;
                    ApplyExposureSettings(flat.GetExposureItem(), fc);
                    ApplyFilter(flat.GetSwitchFilterItem(), filter);
                    return (flat, flat.GetExposureItem(), () => flat.DeterminedHistogramADU,
                        () => flat.GetSetBrightnessItem().Brightness);
                }
            case "SkyFlat":
                {
#if PINS
                    var flat = new SkyFlat(m.Profile, m.Camera, m.Telescope, m.Imaging, m.ImageSaveMediator,
                        m.ImageHistory, m.FilterWheel, m.TwilightCalculator, m.SymbolBroker);
#else
                    var flat = new SkyFlat(m.Profile, m.Camera, m.Telescope, m.Imaging, m.ImageSaveMediator,
                        m.ImageHistory, m.FilterWheel, m.TwilightCalculator);
#endif
                    flat.GetIterations().Iterations = fc.Count;
                    flat.MinExposure = fc.MinExposure;
                    flat.MaxExposure = fc.MaxExposure;
                    flat.HistogramTargetPercentage = fc.HistogramMean / 100.0;
                    flat.HistogramTolerancePercentage = fc.MeanTolerance / 100.0;
                    ApplyExposureSettings(flat.GetExposureItem(), fc);
                    ApplyFilter(flat.GetSwitchFilterItem(), filter);
                    return (flat, flat.GetExposureItem(), () => flat.DeterminedHistogramADU, () => null);
                }
            default:
                {
                    var flat = new AutoExposureFlat(m.Profile, m.Camera, m.Imaging, m.ImageSaveMediator,
                        m.ImageHistory, m.FilterWheel, m.FlatDevice);
                    flat.GetIterations().Iterations = fc.Count;
                    flat.MinExposure = fc.MinExposure;
                    flat.MaxExposure = fc.MaxExposure;
                    flat.GetSetBrightnessItem().Brightness = fc.Brightness;
                    flat.HistogramTargetPercentage = fc.HistogramMean / 100.0;
                    flat.HistogramTolerancePercentage = fc.MeanTolerance / 100.0;
                    flat.KeepPanelClosed = keepPanelClosed;
                    ApplyExposureSettings(flat.GetExposureItem(), fc);
                    ApplyFilter(flat.GetSwitchFilterItem(), filter);
                    return (flat, flat.GetExposureItem(), () => flat.DeterminedHistogramADU,
                        () => flat.GetSetBrightnessItem().Brightness);
                }
        }
    }

    private static void ApplyFilter(NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter switchItem, FilterInfo filter)
    {
        if (filter != null)
        {
            switchItem.Filter = filter;
            return;
        }
#if PINS
        // PINS' SwitchFilter only keeps the current filter for exactly this marker; a null
        // ComboBoxText resolves to position 0 when it executes.
        switchItem.ComboBoxText = NullFilter.Instance.Name;
#endif
    }

    private static NINA.Sequencer.Conditions.LoopCondition GetIterations(SequentialContainer flat) => flat switch
    {
        AutoExposureFlat aef => aef.GetIterations(),
        AutoBrightnessFlat abf => abf.GetIterations(),
        SkyFlat sf => sf.GetIterations(),
        _ => throw new InvalidOperationException("Unknown flat item"),
    };

    private static void ApplyExposureSettings(TakeExposure exposure, FlatRunFilterConfig fc)
    {
        exposure.Gain = fc.Gain;
        exposure.Offset = fc.Offset;
        var binning = ParseBinning(fc.Binning);
        if (binning != null) exposure.Binning = binning;
    }

    private static BinningMode ParseBinning(string binningName)
    {
        if (string.IsNullOrEmpty(binningName)) return null;
        var parts = binningName.Trim().Split('x', 'X');
        if (parts.Length == 2 && short.TryParse(parts[0], out short x) && short.TryParse(parts[1], out short y) && x > 0 && y > 0)
            return new BinningMode(x, y);
        return null;
    }

    /// <summary>
    /// NINA only says "too dim" / "too bright" in a localized exception message, so the
    /// direction is recovered from the last measured mean instead.
    /// </summary>
    private static string ClassifyFailure(double adu, double targetFraction)
    {
        if (adu <= 0) return "failed";
        int bitDepth;
        lock (gate) bitDepth = lastBitDepth;
        if (bitDepth <= 0) bitDepth = TouchNStars.Mediators.Camera?.GetInfo()?.BitDepth ?? 16;
        var maxADU = Math.Pow(2, bitDepth) - 1;
        var fraction = adu / maxADU;
        if (fraction < targetFraction) return "dim";
        if (fraction > targetFraction) return "bright";
        return "failed";
    }

    /// <summary>
    /// PINS' SkyFlat stops when the predicted exposure leaves the min/max range; DeterminedHistogramADU
    /// is from the last determination by then, so the direction comes from which limit was hit:
    /// over max means the sky got too dim, under min too bright. Null for any other failure.
    /// </summary>
    private static string ClassifySkyFlatLimit(string message)
    {
#if PINS
        if (MatchesLocalized(message, "LblExposureOverMax")) return "dim";
        if (MatchesLocalized(message, "LblExposureUnderMin")) return "bright";
#endif
        return null;
    }

#if PINS
    // The message is the localized format with the exposure time filled in for {0}.
    private static bool MatchesLocalized(string message, string key)
    {
        var format = Loc.Instance[key];
        if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(format) || !format.Contains("{0}")) return false;
        var pattern = "^" + Regex.Escape(format).Replace(@"\{0}", ".*") + "$";
        return Regex.IsMatch(message, pattern, RegexOptions.Singleline);
    }
#endif

    // ── Dark flats ─────────────────────────────────────────────────────────────

    private static async Task RunDarkFlats(List<FilterRunState> targets, bool usesPanel,
        IProgress<ApplicationStatus> progress, CancellationToken token)
    {
        var m = TouchNStars.Mediators;
        var flatInfo = m.FlatDevice?.GetInfo();
        var panelConnected = usesPanel && flatInfo?.Connected == true;

        // A flip-flat panel shuts out the light by itself. The mediator reports neither a stuck cover
        // nor a light that stayed on, so the panel's state is read back before relying on it.
        var coveredByPanel = false;
        if (panelConnected)
        {
            await m.FlatDevice.ToggleLight(false, progress, token);
            if (flatInfo.SupportsOpenClose)
            {
                await m.FlatDevice.CloseCover(progress, token);
                var after = m.FlatDevice.GetInfo();
                coveredByPanel = after?.Connected == true && after.CoverState == CoverState.Closed && !after.LightOn;
                if (!coveredByPanel)
                {
                    Logger.Warning($"Flat panel did not report closed and dark (cover {after?.CoverState}, light {after?.LightOn}); asking the user to cover the telescope");
                }
            }
        }

        // Everything else needs the user to cover the telescope. The run waits for that answer while
        // the app is closed, but not forever.
        if (!coveredByPanel)
        {
            TaskCompletionSource<bool> confirmation;
            lock (gate)
            {
                confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                coverConfirmation = confirmation;
                phase = "waiting-for-cover";
                currentIndex = filterStates.IndexOf(targets[0]);
            }

            Notification.ShowWarning("Touch 'N' Stars flat run: cover the telescope to start the dark flats");

            bool confirmed;
            var timedOut = false;
            using (token.Register(() => confirmation.TrySetResult(false)))
            using (var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var delay = Task.Delay(CoverConfirmationTimeout, delayCts.Token);
                if (await Task.WhenAny(confirmation.Task, delay) == delay && !token.IsCancellationRequested)
                {
                    timedOut = confirmation.TrySetResult(false);
                }
                delayCts.Cancel();
                confirmed = await confirmation.Task;
            }

            lock (gate) coverConfirmation = null;
            token.ThrowIfCancellationRequested();

            if (!confirmed)
            {
                if (timedOut) Logger.Warning("Flat run: nobody confirmed the telescope was covered, dark flats skipped");
                lock (gate)
                {
                    foreach (var t in targets)
                    {
                        t.DarkState = "skipped";
                        if (timedOut) t.Error = $"Nobody confirmed the telescope was covered within {CoverConfirmationTimeout.TotalMinutes:0} minutes";
                    }
                }
                return;
            }
        }

        lock (gate) phase = "darks";

        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();

            lock (gate)
            {
                currentIndex = filterStates.IndexOf(target);
                target.DarkState = "running";
            }

            var sequence = new CaptureSequence(target.ExposureTime ?? 0, CaptureSequence.ImageTypes.DARK, target.Filter,
                target.Binning ?? new BinningMode(1, 1), target.DarkCount)
            {
                Gain = target.Gain ?? -1,
                Offset = target.Offset ?? -1,
            };

            string failure = null;
            try
            {
                while (sequence.ProgressExposureCount < sequence.TotalExposureCount)
                {
                    token.ThrowIfCancellationRequested();

                    // CaptureImage switches the filter wheel to sequence.FilterType before exposing.
                    var exposure = await m.Imaging.CaptureImage(sequence, token, progress);
                    var imageData = await exposure.ToImageData(progress, token);
                    StampDarkTargetName(imageData.MetaData);
                    var prepareTask = m.Imaging.PrepareImage(imageData, new PrepareImageParameters(true, false), token);
                    await m.ImageSaveMediator.Enqueue(imageData, prepareTask, progress, token);

                    sequence.ProgressExposureCount++;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                lock (gate) target.DarkState = "stopped";
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                failure = ex.Message;
            }

            // DarkCompleted counts frames NINA reports as written, as for the flats.
            var captured = sequence.ProgressExposureCount;
            await WaitForSaves(() => target.DarkCompleted + target.DarkSaveFailed >= captured,
                () => target.DarkSavesStarted <= target.DarkCompleted + target.DarkSaveFailed, token);
            lock (gate)
            {
                if (failure == null && target.DarkCompleted >= target.DarkCount)
                {
                    target.DarkState = "success";
                }
                else
                {
                    target.DarkState = "failed";
                    target.Error = failure ?? SaveSummary(target.DarkCompleted, target.DarkCount, "dark flats", target.DarkSaveError);
                }
            }
        }
    }

    // 2 s at the 250 ms poll interval.
    private const int SaveQueueIdlePolls = 8;

    /// <summary>
    /// NINA writes frames on a background queue and reports each one only once it is on disk (or
    /// failed). Waits until `allReported` holds, nothing has been in flight for a moment, the run is
    /// stopped, or the hook grace period is over. Both conditions are evaluated under `gate`.
    /// </summary>
    private static async Task WaitForSaves(Func<bool> allReported, Func<bool> nothingInFlight, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + HookGracePeriod;
        var idlePolls = 0;
        while (true)
        {
            lock (gate)
            {
                if (allReported()) return;
                idlePolls = nothingInFlight() ? idlePolls + 1 : 0;
            }
            // The save queue picks the next frame up as soon as the previous one is done, so a moment
            // without a frame in flight means none is left: the missing ones were never queued
            // because their exposure failed.
            if (idlePolls >= SaveQueueIdlePolls) return;
            if (token.IsCancellationRequested) return;
            if (DateTime.UtcNow >= deadline)
            {
                Logger.Warning("Flat run: NINA did not report every frame as saved in time, deciding on the frames reported so far");
                return;
            }
            try
            {
                await Task.Delay(250, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static string SaveSummary(int saved, int count, string what, string saveError) =>
        saveError == null ? $"{saved}/{count} {what} saved" : $"{saved}/{count} {what} saved ({saveError})";

    // Set before the frame enters NINA's save queue, so it does not depend on the save hook's timing.
    private static void StampDarkTargetName(NINA.Image.ImageData.ImageMetaData metaData)
    {
        if (metaData?.Target == null || !string.IsNullOrWhiteSpace(metaData.Target.Name)) return;
        lock (gate)
        {
            var name = flatTargetName ?? targetName;
            if (!string.IsNullOrEmpty(name)) metaData.Target.Name = name;
        }
    }

    private static async Task OpenCoverIfSupported(IProgress<ApplicationStatus> progress, CancellationToken token)
    {
        var m = TouchNStars.Mediators;
        var info = m.FlatDevice?.GetInfo();
        if (info?.Connected != true || !info.SupportsOpenClose) return;
        try
        {
            await m.FlatDevice.OpenCover(progress, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning($"Could not open the flat panel cover after the flat run: {ex.Message}");
        }
    }

    // Has its own timeout instead of the run's token, so it also works after a stop.
    private static async Task SwitchPanelLightOff()
    {
        var m = TouchNStars.Mediators;
        var info = m.FlatDevice?.GetInfo();
        if (info?.Connected != true || !info.SupportsOnOff) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await m.FlatDevice.ToggleLight(false, null, timeout.Token);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Could not switch the flat panel light off during the flat run: {ex.Message}");
        }
    }

    // ── Image hooks (attached while a run is active and shortly after) ─────────

    private static readonly TimeSpan HookGracePeriod = TimeSpan.FromSeconds(60);
    private static bool hooksAttached;

    private static void AttachImageHooks()
    {
        var m = TouchNStars.Mediators;
        lock (gate)
        {
            if (hooksAttached) return;
            if (m.Imaging != null) m.Imaging.ImagePrepared += OnImagePrepared;
            if (m.ImageSaveMediator != null)
            {
                m.ImageSaveMediator.BeforeImageSaved += OnBeforeImageSaved;
                m.ImageSaveMediator.ImageSaved += OnImageSaved;
#if PINS
                m.ImageSaveMediator.ImageSaveFailed += OnImageSaveFailed;
#endif
            }
            hooksAttached = true;
        }
    }

    private static async Task DetachImageHooksLater(string forRunId)
    {
        await Task.Delay(HookGracePeriod);
        var m = TouchNStars.Mediators;
        lock (gate)
        {
            // A newer run started meanwhile and owns the hooks (and their grace period) now.
            if (!hooksAttached || running || runId != forRunId) return;
            if (m.Imaging != null) m.Imaging.ImagePrepared -= OnImagePrepared;
            if (m.ImageSaveMediator != null)
            {
                m.ImageSaveMediator.BeforeImageSaved -= OnBeforeImageSaved;
                m.ImageSaveMediator.ImageSaved -= OnImageSaved;
#if PINS
                m.ImageSaveMediator.ImageSaveFailed -= OnImageSaveFailed;
#endif
            }
            hooksAttached = false;
        }
    }

    private static void OnImageSaved(object sender, ImageSavedEventArgs e)
    {
        CountSaveResult(e?.MetaData?.Image?.ImageType, null);
    }

#if PINS
    private static Task OnImageSaveFailed(object sender, ImageSaveFailedEventArgs e)
    {
        var reason = e?.IsDiskFull == true ? "disk full" : e?.Exception?.Message ?? "save failed";
        CountSaveResult(e?.MetaData?.Image?.ImageType, reason);
        return Task.CompletedTask;
    }
#endif

    // Attributes a written (or failed) frame to the filter the run is on. A filter only hands over to
    // the next one after all its frames were reported, so a late frame does not land on the wrong one.
    private static void CountSaveResult(string imageType, string saveError)
    {
        var isFlat = string.Equals(imageType, CaptureSequence.ImageTypes.FLAT, StringComparison.OrdinalIgnoreCase);
        var isDark = string.Equals(imageType, CaptureSequence.ImageTypes.DARK, StringComparison.OrdinalIgnoreCase);
        if (!isFlat && !isDark) return;

        lock (gate)
        {
            if (!running || currentIndex < 0 || currentIndex >= filterStates.Count) return;
            var s = filterStates[currentIndex];
            if (phase == "flats" && isFlat)
            {
                if (saveError == null) s.Completed++;
                else
                {
                    s.SaveFailed++;
                    s.SaveError = saveError;
                }
            }
            else if (phase == "darks" && isDark)
            {
                if (saveError == null) s.DarkCompleted++;
                else
                {
                    s.DarkSaveFailed++;
                    s.DarkSaveError = saveError;
                }
            }
        }
    }

    // The flat items prepare every measured frame before reading its statistics, so the image
    // prepared last during a run is the one DeterminedHistogramADU came from.
    private static void OnImagePrepared(object sender, ImagePreparedEventArgs e)
    {
        var bitDepth = e?.RenderedImage?.RawImageData?.Properties?.BitDepth;
        if (bitDepth > 0)
        {
            lock (gate) lastBitDepth = bitDepth.Value;
        }
    }

    // Flats from standalone sequence items carry no target name, so $$TARGETNAME$$ would resolve to
    // nothing. Frames that already have a name are never touched. The name a flat was saved with is
    // remembered for the run's dark flats, which StampDarkTargetName names before they are queued.
    // Dark frames are only counted here, not named: while the hook is attached it would also name a
    // dark library.
    private static Task OnBeforeImageSaved(object sender, BeforeImageSavedEventArgs e)
    {
        try
        {
            var metaData = e?.Image?.MetaData;
            var imageType = metaData?.Image?.ImageType;
            var isFlat = string.Equals(imageType, CaptureSequence.ImageTypes.FLAT, StringComparison.OrdinalIgnoreCase);
            var isDark = string.Equals(imageType, CaptureSequence.ImageTypes.DARK, StringComparison.OrdinalIgnoreCase);
            if (!isFlat && !isDark) return Task.CompletedTask;

            lock (gate)
            {
                // The save queue has started on this frame; ImageSaved or ImageSaveFailed follows.
                if (running && currentIndex >= 0 && currentIndex < filterStates.Count)
                {
                    var s = filterStates[currentIndex];
                    if (phase == "flats" && isFlat) s.SavesStarted++;
                    else if (phase == "darks" && isDark) s.DarkSavesStarted++;
                }

                if (isFlat && metaData.Target != null)
                {
                    if (string.IsNullOrWhiteSpace(metaData.Target.Name) && !string.IsNullOrEmpty(targetName))
                        metaData.Target.Name = targetName;
                    if (!string.IsNullOrWhiteSpace(metaData.Target.Name))
                        flatTargetName = metaData.Target.Name;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"FlatWizardService: failed to stamp target name: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reports the run's progress under its own source. Synchronous, unlike Progress&lt;T&gt;, which
    /// without a SynchronizationContext posts each report to the thread pool where they can overtake
    /// each other. The empty "done" reports of the sub-steps are dropped; only Clear() removes the entry.
    /// </summary>
    private sealed class FlatStatusProgress(IApplicationStatusMediator mediator) : IProgress<ApplicationStatus>
    {
        private readonly object sync = new();

        public void Report(ApplicationStatus status)
        {
            if (status == null || string.IsNullOrEmpty(status.Status)) return;
            lock (sync)
            {
                status.Source = StatusSource;
                mediator?.StatusUpdate(status);
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                mediator?.StatusUpdate(new ApplicationStatus { Source = StatusSource });
            }
        }
    }

    private class FilterRunState
    {
        public FlatRunFilterConfig Config;
        public int? FilterId;
        public FilterInfo Filter;
        public string FilterName;
        // pending | running | success | failed | dim | bright | stopped
        public string State;
        // Frames written to disk; frames NINA failed to write and the last reason; frames the save
        // queue has started on.
        public int Completed;
        public int SaveFailed;
        public string SaveError;
        public int SavesStarted;
        public int Count;
        public double? ExposureTime;
        public double? Brightness;
        public double? LastADU;
        public BinningMode Binning;
        public int? Gain;
        public int? Offset;
        public string Error;
        // none | pending | running | success | failed | skipped | stopped
        public string DarkState;
        public int DarkCompleted;
        public int DarkSaveFailed;
        public string DarkSaveError;
        public int DarkSavesStarted;
        public int DarkCount;
    }
}

// ── Request models ────────────────────────────────────────────────────────────

public class FlatRunRequest
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; }

    [JsonPropertyName("keepClosed")]
    public bool KeepClosed { get; set; }

    /// <summary>Dark flats per filter, taken after all flats. 0 = none.</summary>
    [JsonPropertyName("darkCount")]
    public int DarkCount { get; set; }

    /// <summary>Stamped on flat and dark flat frames that carry no target name. Empty = off.</summary>
    [JsonPropertyName("targetName")]
    public string TargetName { get; set; }

    [JsonPropertyName("filters")]
    public List<FlatRunFilterConfig> Filters { get; set; }
}

public class FlatRunFilterConfig
{
    /// <summary>Profile filter index; null keeps the current filter (no filter wheel).</summary>
    [JsonPropertyName("filterId")]
    public int? FilterId { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; } = 20;

    [JsonPropertyName("gain")]
    public int Gain { get; set; } = -1;

    [JsonPropertyName("offset")]
    public int Offset { get; set; } = -1;

    [JsonPropertyName("binning")]
    public string Binning { get; set; } = "1x1";

    /// <summary>0–100 % — converted to a 0–1 fraction before passing to NINA.</summary>
    [JsonPropertyName("histogramMean")]
    public double HistogramMean { get; set; } = 50;

    /// <summary>0–100 % — converted to a 0–1 fraction before passing to NINA.</summary>
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
