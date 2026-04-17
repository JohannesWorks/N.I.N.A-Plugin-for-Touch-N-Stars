using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TouchNStars.Server.Models;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// API Controller for Night Summary plugin integration.
/// Accesses the Night Summary plugin's database via reflection to avoid compile-time dependencies.
/// </summary>
public class NightSummaryController : WebApiController
{
    private static Assembly _nsAssembly;
    private static Type _sessionDbType;
    private static readonly object _initLock = new object();

    private static Assembly GetNightSummaryAssembly()
    {
        if (_nsAssembly != null) return _nsAssembly;
        lock (_initLock)
        {
            if (_nsAssembly != null) return _nsAssembly;
            _nsAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "NINA.Plugin.NightSummary");
        }
        return _nsAssembly;
    }

    private static Type GetSessionDatabaseType()
    {
        if (_sessionDbType != null) return _sessionDbType;
        lock (_initLock)
        {
            if (_sessionDbType != null) return _sessionDbType;
            var asm = GetNightSummaryAssembly();
            _sessionDbType = asm?.GetType("NINA.Plugin.NightSummary.Data.SessionDatabase");
        }
        return _sessionDbType;
    }

    private static object CreateSessionDatabase()
    {
        var dbType = GetSessionDatabaseType();
        if (dbType == null)
            throw new InvalidOperationException("Night Summary plugin not loaded");
        return Activator.CreateInstance(dbType);
    }

    /// <summary>
    /// Converts an object with public properties to a Dictionary for JSON serialization.
    /// </summary>
    private static Dictionary<string, object> MapToDict(object obj)
    {
        if (obj == null) return null;
        var dict = new Dictionary<string, object>();
        foreach (var prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            try { dict[prop.Name] = prop.GetValue(obj); }
            catch { dict[prop.Name] = null; }
        }
        return dict;
    }

    private static T GetVal<T>(Dictionary<string, object> dict, string key, T fallback = default)
    {
        if (dict == null || !dict.TryGetValue(key, out var raw) || raw == null)
            return fallback;
        try { return (T)Convert.ChangeType(raw, typeof(T)); }
        catch { return fallback; }
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>GET /api/nightsummary/status — returns whether the Night Summary plugin is loaded.</summary>
    [Route(HttpVerbs.Get, "/nightsummary/status")]
    public object GetNightSummaryStatus()
    {
        var assembly = GetNightSummaryAssembly();
        return new
        {
            Success = true,
            Response = new
            {
                Installed = assembly != null,
                Version = assembly?.GetName().Version?.ToString()
            }
        };
    }

    /// <summary>GET /api/nightsummary/sessions?limit=50 — list recent sessions (enriched with image/target counts).</summary>
    [Route(HttpVerbs.Get, "/nightsummary/sessions")]
    public async Task<object> GetSessions([QueryField] int limit = 50)
    {
        return await Task.Run(() =>
        {
            try
            {
                var db = CreateSessionDatabase();
                var method = db.GetType().GetMethod("GetRecentSessions");
                if (method == null)
                    return (object)new ApiResponse { Success = false, Error = "GetRecentSessions not found on SessionDatabase" };

                var result = method.Invoke(db, new object[] { limit });
                var sessions = ((IList)result).Cast<object>().Select(MapToDict).ToList();

                return new { Success = true, Response = sessions };
            }
            catch (Exception ex)
            {
                Logger.Error($"NightSummaryController: GetSessions failed: {ex.InnerException?.Message ?? ex.Message}");
                return (object)new ApiResponse { Success = false, Error = ex.InnerException?.Message ?? ex.Message };
            }
        });
    }

    /// <summary>GET /api/nightsummary/sessions/{sessionId} — full session detail: session record, summary stats, images, events, timing events.</summary>
    [Route(HttpVerbs.Get, "/nightsummary/sessions/{sessionId}")]
    public async Task<object> GetSession(string sessionId)
    {
        return await Task.Run(() =>
        {
            try
            {
                var db = CreateSessionDatabase();
                var dbType = db.GetType();

                var getSession = dbType.GetMethod("GetSession");
                var getImages = dbType.GetMethod("GetImagesForSession");
                var getEvents = dbType.GetMethod("GetEventsForSession");
                var getTimingEvents = dbType.GetMethod("GetTimingEventsForSession");
                var getSessionHistory = dbType.GetMethod("GetSessionHistoryForTarget");

                var session = getSession?.Invoke(db, new object[] { sessionId });
                if (session == null)
                    return (object)new ApiResponse { Success = false, Error = "Session not found" };

                var images = ((IList)(getImages?.Invoke(db, new object[] { sessionId }) ?? new object[0])).Cast<object>().Select(MapToDict).ToList();
                var events = ((IList)(getEvents?.Invoke(db, new object[] { sessionId }) ?? new object[0])).Cast<object>().Select(MapToDict).ToList();
                var timingEvents = getTimingEvents != null
                    ? ((IList)(getTimingEvents.Invoke(db, new object[] { sessionId }) ?? new object[0])).Cast<object>().Select(MapToDict).ToList()
                    : new List<Dictionary<string, object>>();

                // Compute summary stats from LIGHT images only
                var lightImages = images.Where(i => { var t = i.TryGetValue("ImageType", out var v) ? v?.ToString() : null; return string.IsNullOrEmpty(t) || t == "LIGHT"; }).ToList();
                var acceptedImages = lightImages.Where(i => i.TryGetValue("Accepted", out var v) && v is bool b && b).ToList();

                double totalExpSec = lightImages.Sum(i => GetVal<double>(i, "ExposureDuration"));
                var targets = lightImages
                    .Select(i => { i.TryGetValue("TargetName", out var v); return v?.ToString(); })
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t)
                    .ToList();

                double avgHfr = acceptedImages.Any()
                    ? acceptedImages.Select(i => GetVal<double>(i, "HFR")).Where(h => h > 0).DefaultIfEmpty(0).Average()
                    : 0;
                double avgGuiding = acceptedImages.Any()
                    ? acceptedImages.Select(i => GetVal<double>(i, "GuidingRMSTotal")).Where(g => g > 0).DefaultIfEmpty(0).Average()
                    : 0;
                double avgFwhm = acceptedImages.Any()
                    ? acceptedImages.Select(i => GetVal<double>(i, "FWHM")).Where(f => f > 0).DefaultIfEmpty(0).Average()
                    : 0;

                // Per-target + per-filter breakdown
                var byTarget = lightImages
                    .GroupBy(i => { i.TryGetValue("TargetName", out var v); return v?.ToString() ?? ""; })
                    .Select(g => new
                    {
                        Target = g.Key,
                        ImageCount = g.Count(),
                        AcceptedCount = g.Count(i => i.TryGetValue("Accepted", out var v) && v is bool b && b),
                        TotalExposureSeconds = g.Sum(i => GetVal<double>(i, "ExposureDuration")),
                        AvgHfr = Math.Round(g.Select(i => GetVal<double>(i, "HFR")).Where(h => h > 0).DefaultIfEmpty(0).Average(), 2),
                        Filters = g
                            .GroupBy(i => { i.TryGetValue("Filter", out var fv); return fv?.ToString() ?? ""; })
                            .Select(fg => new
                            {
                                Filter = fg.Key,
                                Count = fg.Count(),
                                AcceptedCount = fg.Count(i => i.TryGetValue("Accepted", out var v) && v is bool b && b),
                                TotalExposureSeconds = fg.Sum(i => GetVal<double>(i, "ExposureDuration"))
                            })
                            .OrderBy(f => f.Filter)
                            .ToList()
                    })
                    .OrderBy(t => t.Target)
                    .ToList();

                return new
                {
                    Success = true,
                    Response = new
                    {
                        Session = MapToDict(session),
                        Stats = new
                        {
                            TotalImages = lightImages.Count,
                            AcceptedImages = acceptedImages.Count,
                            TotalExposureSeconds = Math.Round(totalExpSec, 1),
                            Targets = targets,
                            AvgHfr = Math.Round(avgHfr, 2),
                            AvgGuidingRms = Math.Round(avgGuiding, 2),
                            AvgFwhm = Math.Round(avgFwhm, 2),
                            SkippedExposures = GetVal<int>(MapToDict(session), "SkippedExposures")
                        },
                        ByTarget = byTarget,
                        Images = images,
                        Events = events,
                        TimingEvents = timingEvents,
                        SessionHistory = getSessionHistory != null
                            ? targets.ToDictionary(
                                t => t,
                                t => ((IList)(getSessionHistory.Invoke(db, new object[] { t, sessionId }) ?? new object[0]))
                                     .Cast<object>().Select(MapToDict).ToList())
                            : new Dictionary<string, List<Dictionary<string, object>>>()
                    }
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"NightSummaryController: GetSession failed: {ex.InnerException?.Message ?? ex.Message}");
                return (object)new ApiResponse { Success = false, Error = ex.InnerException?.Message ?? ex.Message };
            }
        });
    }

    /// <summary>DELETE /api/nightsummary/sessions/{sessionId} — delete a session and all its records.</summary>
    [Route(HttpVerbs.Delete, "/nightsummary/sessions/{sessionId}")]
    public async Task<object> DeleteSession(string sessionId)
    {
        return await Task.Run(() =>
        {
            try
            {
                var db = CreateSessionDatabase();
                var method = db.GetType().GetMethod("DeleteSession");
                if (method == null)
                    return (object)new ApiResponse { Success = false, Error = "DeleteSession not found on SessionDatabase" };

                method.Invoke(db, new object[] { sessionId });
                return new { Success = true, Response = "Session deleted" };
            }
            catch (Exception ex)
            {
                Logger.Error($"NightSummaryController: DeleteSession failed: {ex.InnerException?.Message ?? ex.Message}");
                return (object)new ApiResponse { Success = false, Error = ex.InnerException?.Message ?? ex.Message };
            }
        });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NINA", "NightSummary", "settings.json");

    private static object GetSettingsManager()
    {
        var asm = GetNightSummaryAssembly();
        if (asm == null) return null;
        var managerType = asm.GetType("NINA.Plugin.NightSummary.Data.SettingsManager");
        if (managerType == null) return null;
        var instanceProp = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        return instanceProp?.GetValue(null);
    }

    private static object GetCurrentSettings()
    {
        var manager = GetSettingsManager();
        if (manager == null) return null;
        var currentProp = manager.GetType().GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
        return currentProp?.GetValue(manager);
    }

    private static void SaveSettings()
    {
        var manager = GetSettingsManager();
        if (manager == null) return;
        var saveMethod = manager.GetType().GetMethod("Save", BindingFlags.Public | BindingFlags.Instance);
        saveMethod?.Invoke(manager, null);
    }

    private static void SetProp(object obj, string name, object value)
    {
        var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null || !prop.CanWrite) return;
        try
        {
            var targetType = prop.PropertyType;
            if (targetType == typeof(bool) && value is JsonElement je && je.ValueKind == JsonValueKind.True || value is JsonElement je2 && je2.ValueKind == JsonValueKind.False)
                prop.SetValue(obj, value is JsonElement el ? el.GetBoolean() : Convert.ToBoolean(value));
            else if (targetType == typeof(int))
                prop.SetValue(obj, value is JsonElement ej ? ej.GetInt32() : Convert.ToInt32(value));
            else if (targetType == typeof(bool))
                prop.SetValue(obj, value is JsonElement ejb ? ejb.GetBoolean() : Convert.ToBoolean(value));
            else if (targetType == typeof(string))
                prop.SetValue(obj, value is JsonElement ejs ? ejs.GetString() : value?.ToString() ?? "");
            else
                prop.SetValue(obj, Convert.ChangeType(value, targetType));
        }
        catch (Exception ex)
        {
            Logger.Warning($"NightSummaryController: SetProp {name} failed: {ex.Message}");
        }
    }

    // ─── Settings ─────────────────────────────────────────────────────────────

    /// <summary>GET /api/nightsummary/settings — read all plugin settings.</summary>
    [Route(HttpVerbs.Get, "/nightsummary/settings")]
    public object GetSettings()
    {
        try
        {
            var settings = GetCurrentSettings();
            if (settings == null)
                return new ApiResponse { Success = false, Error = "Night Summary plugin not loaded" };

            var dict = MapToDict(settings);
            // Also build filter list from NINA profile
            var filters = GetProfileFilterNames();
            dict["_filterNames"] = filters;
            return new { Success = true, Response = dict };
        }
        catch (Exception ex)
        {
            Logger.Error($"NightSummaryController: GetSettings failed: {ex.Message}");
            return new ApiResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>PUT /api/nightsummary/settings — update one or more settings fields.</summary>
    [Route(HttpVerbs.Put, "/nightsummary/settings")]
    public async Task<object> UpdateSettings()
    {
        return await Task.Run(async () =>
        {
            try
            {
                var settings = GetCurrentSettings();
                if (settings == null)
                    return (object)new ApiResponse { Success = false, Error = "Night Summary plugin not loaded" };

                var bodyStr = await ReadBodyStringAsync();
                if (string.IsNullOrWhiteSpace(bodyStr))
                    return (object)new ApiResponse { Success = false, Error = "Invalid request body" };

                var doc = JsonDocument.Parse(bodyStr);

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var val = (object)prop.Value;
                    SetProp(settings, prop.Name, val);
                }

                SaveSettings();
                return new { Success = true, Response = "Settings saved" };
            }
            catch (Exception ex)
            {
                Logger.Error($"NightSummaryController: UpdateSettings failed: {ex.Message}");
                return (object)new ApiResponse { Success = false, Error = ex.Message };
            }
        });
    }

    private async Task<string> ReadBodyStringAsync()
    {
        using var reader = new StreamReader(HttpContext.Request.InputStream);
        return await reader.ReadToEndAsync();
    }

    private static List<string> GetProfileFilterNames()
    {
        try
        {
            var profile = TouchNStars.Mediators?.Profile?.ActiveProfile;
            if (profile == null) return new List<string>();
            var filters = profile.FilterWheelSettings?.FilterWheelFilters;
            if (filters == null) return new List<string>();
            return ((IEnumerable)filters).Cast<object>()
                .Select(f => f.GetType().GetProperty("Name")?.GetValue(f)?.ToString() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    // ─── Test notifications ───────────────────────────────────────────────────

    /// <summary>POST /api/nightsummary/test-email — send a test email.</summary>
    [Route(HttpVerbs.Post, "/nightsummary/test-email")]
    public async Task<object> TestEmail()
    {
        return await Task.Run(async () =>
        {
            var settings = GetCurrentSettings();
            if (settings == null)
                return (object)new ApiResponse { Success = false, Error = "Night Summary plugin not loaded" };

            try
            {
                var s = MapToDict(settings);
                var useGmail = GetVal<bool>(s, "UseGmailSmtp", true);
                var sender = (string)(s.GetValueOrDefault("SenderAddress") ?? "");
                var password = (string)(s.GetValueOrDefault("SmtpPassword") ?? "");
                var recipient = (string)(s.GetValueOrDefault("RecipientAddress") ?? "");
                var smtpHost = useGmail ? "smtp.gmail.com" : (string)(s.GetValueOrDefault("SmtpHost") ?? "smtp.gmail.com");
                var smtpPort = useGmail ? 587 : GetVal<int>(s, "SmtpPort", 587);
                var smtpSsl = useGmail || GetVal<bool>(s, "SmtpSsl", true);

                if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(recipient))
                    return (object)new { Success = true, Response = new { Ok = false, Message = "Fill in all email fields first" } };

                var asm = GetNightSummaryAssembly();
                var emailSenderType = asm?.GetType("NINA.Plugin.NightSummary.Reporting.EmailSender");
                if (emailSenderType == null)
                    return (object)new ApiResponse { Success = false, Error = "EmailSender type not found" };

                var emailSender = Activator.CreateInstance(emailSenderType, smtpHost, smtpPort, smtpSsl, sender, password, recipient);
                var sendMethod = emailSenderType.GetMethod("SendTestAsync");
                var task = (Task<bool>)sendMethod.Invoke(emailSender, null);
                bool ok = await task;
                return (object)new { Success = true, Response = new { Ok = ok, Message = ok ? "Test email sent" : "Failed — check NINA log" } };
            }
            catch (Exception ex)
            {
                Logger.Error($"NightSummaryController: TestEmail failed: {ex.InnerException?.Message ?? ex.Message}");
                return (object)new { Success = true, Response = new { Ok = false, Message = ex.InnerException?.Message ?? ex.Message } };
            }
        });
    }

    /// <summary>POST /api/nightsummary/test-discord — send a test Discord message.</summary>
    [Route(HttpVerbs.Post, "/nightsummary/test-discord")]
    public async Task<object> TestDiscord()
    {
        return await Task.Run(async () =>
        {
            var settings = GetCurrentSettings();
            if (settings == null)
                return (object)new ApiResponse { Success = false, Error = "Night Summary plugin not loaded" };

            try
            {
                var s = MapToDict(settings);
                var url = (string)(s.GetValueOrDefault("DiscordWebhookUrl") ?? "");
                if (string.IsNullOrWhiteSpace(url))
                    return (object)new { Success = true, Response = new { Ok = false, Message = "Webhook URL is empty" } };

                var asm = GetNightSummaryAssembly();
                var senderType = asm?.GetType("NINA.Plugin.NightSummary.Reporting.DiscordSender");
                if (senderType == null)
                    return (object)new ApiResponse { Success = false, Error = "DiscordSender type not found" };

                var discordSender = Activator.CreateInstance(senderType, url);
                var sendMethod = senderType.GetMethod("SendTestAsync");
                var task = (Task<bool>)sendMethod.Invoke(discordSender, null);
                bool ok = await task;
                return (object)new { Success = true, Response = new { Ok = ok, Message = ok ? "Test message sent" : "Failed — check NINA log" } };
            }
            catch (Exception ex)
            {
                Logger.Error($"NightSummaryController: TestDiscord failed: {ex.InnerException?.Message ?? ex.Message}");
                return (object)new { Success = true, Response = new { Ok = false, Message = ex.InnerException?.Message ?? ex.Message } };
            }
        });
    }

    /// <summary>POST /api/nightsummary/test-pushover — send a test Pushover notification.</summary>
    [Route(HttpVerbs.Post, "/nightsummary/test-pushover")]
    public async Task<object> TestPushover()
    {
        return await Task.Run(async () =>
        {
            var settings = GetCurrentSettings();
            if (settings == null)
                return (object)new ApiResponse { Success = false, Error = "Night Summary plugin not loaded" };

            try
            {
                var s = MapToDict(settings);
                var appToken = (string)(s.GetValueOrDefault("PushoverAppToken") ?? "");
                var userKey = (string)(s.GetValueOrDefault("PushoverUserKey") ?? "");

                if (string.IsNullOrWhiteSpace(appToken) || string.IsNullOrWhiteSpace(userKey))
                    return (object)new { Success = true, Response = new { Ok = false, Message = "App token or user key is empty" } };

                var asm = GetNightSummaryAssembly();
                var senderType = asm?.GetType("NINA.Plugin.NightSummary.Reporting.PushoverSender");
                if (senderType == null)
                    return (object)new ApiResponse { Success = false, Error = "PushoverSender type not found" };

                var pushoverSender = Activator.CreateInstance(senderType, appToken, userKey);
                var sendMethod = senderType.GetMethod("SendAsync");
                var task = (Task<bool>)sendMethod.Invoke(pushoverSender, new object[] { "Night Summary", "Pushover is configured correctly!" });
                bool ok = await task;
                return (object)new { Success = true, Response = new { Ok = ok, Message = ok ? "Test notification sent" : "Failed — check NINA log" } };
            }
            catch (Exception ex)
            {
                Logger.Error($"NightSummaryController: TestPushover failed: {ex.InnerException?.Message ?? ex.Message}");
                return (object)new { Success = true, Response = new { Ok = false, Message = ex.InnerException?.Message ?? ex.Message } };
            }
        });
    }

    /// <summary>POST /api/nightsummary/sessions/{sessionId}/resend — resend a session report.</summary>
    [Route(HttpVerbs.Post, "/nightsummary/sessions/{sessionId}/resend")]
    public async Task<object> ResendSession(string sessionId)
    {
        return await Task.Run(async () =>
        {
            try
            {
                var asm = GetNightSummaryAssembly();
                if (asm == null)
                    return (object)new ApiResponse { Success = false, Error = "Night Summary plugin not loaded" };

                var liveDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "NightSummary", "nightsummary.sqlite");

                if (!File.Exists(liveDbPath))
                    return (object)new ApiResponse { Success = false, Error = "Session database not found" };

                // Get SessionService from SettingsManager's assembly — find it via MEF exports
                // Pattern: SessionService is a singleton accessed via PluginBase or direct instantiation
                var sessionServiceType = asm.GetType("NINA.Plugin.NightSummary.Session.SessionService");
                if (sessionServiceType == null)
                    return (object)new ApiResponse { Success = false, Error = "SessionService type not found" };

                // SessionService needs SettingsManager — create with parameterless constructor isn't available.
                // Instead, call SendFromDatabaseAsync via the PluginLoader pattern:
                // Access the NightSummaryPlugin singleton instance via the MEF container's exported values
                // by searching loaded types for a static singleton or the plugin's active instance.
                var pluginType = asm.GetType("NINA.Plugin.NightSummary.NightSummaryPlugin");
                // Try to find an active instance via all loaded AppDomain types
                // The plugin is registered as IPluginManifest; we get the loaded assembly's exports.
                // Safest approach: invoke SessionService.SendFromDatabaseAsync with new instance
                // SessionService ctor takes SettingsManager and IProfileService.
                // We'll use the static SettingsManager.Instance (no arg ctor not available for service).
                // Instead - create a new SessionService with null profile service (it may still work for sending).
                var settingsManagerType = asm.GetType("NINA.Plugin.NightSummary.Data.SettingsManager");
                var settingsManagerInstance = settingsManagerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

                var sessionServiceCtor = sessionServiceType.GetConstructors().FirstOrDefault();
                if (sessionServiceCtor == null)
                    return (object)new ApiResponse { Success = false, Error = "SessionService constructor not found" };

                // Pass null for profileService — SessionService only uses it for filter names during live sessions
                var ctorParams = sessionServiceCtor.GetParameters();
                var args = ctorParams.Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
                // Fill settingsManager param
                for (int i = 0; i < ctorParams.Length; i++)
                {
                    if (ctorParams[i].ParameterType.Name.Contains("SettingsManager") && settingsManagerInstance != null)
                        args[i] = settingsManagerInstance;
                }

                var sessionService = sessionServiceCtor.Invoke(args);
                var sendMethod = sessionServiceType.GetMethod("SendFromDatabaseAsync",
                    new[] { typeof(string), typeof(string) });
                if (sendMethod == null)
                    sendMethod = sessionServiceType.GetMethod("SendFromDatabaseAsync",
                        new[] { typeof(string), typeof(string) });

                if (sendMethod == null)
                    return (object)new ApiResponse { Success = false, Error = "SendFromDatabaseAsync not found" };

                var task = (Task)sendMethod.Invoke(sessionService, new object[] { liveDbPath, sessionId });
                await task;
                return new { Success = true, Response = "Report resent" };
            }
            catch (Exception ex)
            {
                Logger.Error($"NightSummaryController: ResendSession failed: {ex.InnerException?.Message ?? ex.Message}");
                return (object)new ApiResponse { Success = false, Error = ex.InnerException?.Message ?? ex.Message };
            }
        });
    }
}
