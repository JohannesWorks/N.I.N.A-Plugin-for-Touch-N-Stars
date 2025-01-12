using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.WPF.Base.SkySurvey;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using TouchNStars.Utility;

namespace TouchNStars.Server;

public class GuiderData {
    public List<double> RADistanceRaw { get; set; }
    public List<double> DECDistanceRaw { get; set; }
}

public class NGCSearchResult {
    public string Name { get; set; }
    public double RA { get; set; }
    public double Dec { get; set; }
}

public class ApiResponse {
    public bool Success { get; set; }
    public string Response { get; set; }
    public string Error { get; set; }
    public int StatusCode { get; set; }
    public string Type { get; set; }
}

public class Controller : WebApiController {

    private object lockObj = new object();

    private GuiderData guiderData = new GuiderData();
    private bool afRun = false;
    private bool afError = false;
    private string afErrorText = string.Empty;
    private bool newAfGraph = false;
    private DateTime lastAfTimestamp = DateTime.MinValue;
    private bool wshvActive = false;
    private int wshvPort = 80;

    private static readonly List<string> excluded_members = new List<string>() { "GetEquipment", "RequestAll", "LoadPlugin" };


    [Route(HttpVerbs.Get, "/logs")]
    public List<Hashtable> GetRecentLogs([QueryField] int count, [QueryField] string level) {
        List<Hashtable> logs = new List<Hashtable>();

        if (level.Equals("ERROR") || level.Equals("WARNING") || level.Equals("INFO") || level.Equals("DEBUG")) {
            string currentLogFile = Directory.GetFiles(CoreUtility.LogPath).OrderByDescending(File.GetCreationTime).First();

            string[] logLines = [];

            using (var stream = new FileStream(currentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                using (var reader = new StreamReader(stream)) {
                    string content = reader.ReadToEnd();
                    logLines = content.Split('\n');
                }
            }

            List<string> filteredLogLines = new List<string>();
            foreach (string line in logLines) {
                bool valid = true;

                if (!line.Contains('|' + level + '|')) {
                    valid = false;
                }
                foreach (string excluded_member in excluded_members) {
                    if (line.Contains(excluded_member)) {
                        valid = false;
                    }
                }
                if (valid) {
                    filteredLogLines.Add(line);
                }
            }
            IEnumerable<string> lines = filteredLogLines.TakeLast(count);
            foreach (string line in lines) {
                string[] parts = line.Split('|');
                if (parts.Length >= 6) {
                    logs.Add(new Hashtable() {
                        { "timestamp", parts[0] },
                        { "level", parts[1] },
                        { "source", parts[2] },
                        { "member", parts[3] },
                        { "line", parts[4] },
                        { "message", string.Join('|', parts.Skip(5)).Trim() }
                    });
                }
            }
        }
        return logs;
    }

    [Route(HttpVerbs.Get, "/wshv")]
    public object GetWshvData() {
        lock (lockObj) {
            return new Dictionary<string, object>() { { "wshvActive", wshvActive }, { "wshvPort", wshvPort } };
        }
    }

    [Route(HttpVerbs.Get, "/autofocus/{action}")]
    public async Task<object> ControlAutofocus([QueryField] string action) {
        string targetUrl = $"{CoreUtility.BASE_API_URL}/equipment/focuser/auto-focus";
        bool info = action.Equals("info");
        bool start = action.Equals("start");
        bool stop = action.Equals("stopp");

        if (info) {
            return new Dictionary<string, object>() {
                { "Success", true },
                { "autofocus_running", afRun },
                { "newAfGraph", newAfGraph },
                { "afError", afError },
                { "afErrorText", afErrorText },
            };
        }
        if (start) {
            afRun = true;
            newAfGraph = false;
            afError = false;
            afErrorText = string.Empty;

            try {
                HttpClient client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync(targetUrl);

                if (response.IsSuccessStatusCode) {
                    ApiResponse apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse>();
                    if (apiResponse.Success) {
                        return new Dictionary<string, object>() { { "message", "Autofokus gestartet" } };
                    } else {
                        return new Dictionary<string, object>() { { "message", $"Fehler beim Starten des Autofokus: {apiResponse.Error}" } };
                    }
                } else {
                    return new Dictionary<string, object>() { { "message", $"Fehler beim Starten des Autofokus: {response.StatusCode}" } };
                }
            } catch (Exception ex) {
                Logger.Error(ex);
                HttpContext.Response.StatusCode = 500;
                return new Dictionary<string, object>() { { "error", "Interner Fehler beim Starten des Autofokus" } };
            }
        }

        if (stop) {
            afRun = false;
            newAfGraph = false;

            try {
                HttpClient client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync(targetUrl + "?cancel=true");

                if (response.IsSuccessStatusCode) {
                    ApiResponse apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse>();
                    if (apiResponse.Success) {
                        return new Dictionary<string, object>() { { "message", "Autofokus gestoppt" } };
                    } else {
                        return new Dictionary<string, object>() { { "message", $"Fehler beim Stoppen des Autofokus: {apiResponse.Error}" } };
                    }
                } else {
                    return new Dictionary<string, object>() { { "message", $"Fehler beim Stoppen des Autofokus: {response.StatusCode}" } };
                }
            } catch (Exception ex) {
                Logger.Error(ex);
                HttpContext.Response.StatusCode = 500;
                return new Dictionary<string, object>() { { "error", "Interner Fehler beim Stopoen des Autofokus" } };
            }
        }

        return new Dictionary<string, object>() { { "error", "Ungültige Anfrage" } };
    }

    [Route(HttpVerbs.Get, "/guider-data")]
    public object GetGuiderData() {
        lock (lockObj) {
            return guiderData;
        }
    }

    [Route(HttpVerbs.Get, "/ngc/search")]
    public async Task<object> SearcgNGC([QueryField(true)] string query, [QueryField] int limit) {

        TouchNStars.Mediators.DeepSkyObjectSearchVM.Limit = limit;
        TouchNStars.Mediators.DeepSkyObjectSearchVM.TargetName = query; // Setting the target name automatically starts the search

        await TouchNStars.Mediators.DeepSkyObjectSearchVM.TargetSearchResult.Task; // Wait for the search to finsish
        List<NGCSearchResult> results = new List<NGCSearchResult>();

        foreach (var result in TouchNStars.Mediators.DeepSkyObjectSearchVM.TargetSearchResult.Result) { // bring the results in a better format
            results.Add(new NGCSearchResult() {
                Name = result.Column1,
                RA = CoreUtility.HmsToDegrees(result.Column2),
                Dec = CoreUtility.DmsToDegrees(result.Column3.Replace(" ", "").Replace('°', ':').Replace('\'', ':').Replace("\"", "")) // maybe use reflection to directly get the coordinates
            });
        }

        return results;
    }

    [Route(HttpVerbs.Get, "/targetpic")]
    public async Task FetchTargetPicture([QueryField(true)] int width, [QueryField(true)] int height, [QueryField(true)] double fov, [QueryField(true)] double ra, [QueryField(true)] double dec, [QueryField] bool useCache) {
        try {
            HttpContext.Response.ContentType = "image/jpeg";
            if (useCache) {
                CacheSkySurveyImageFactory factory = new CacheSkySurveyImageFactory(width, height, new CacheSkySurvey(CoreUtility.CachePath));
                BitmapSource source = factory.Render(new Coordinates(Angle.ByDegree(ra), Angle.ByDegree(dec), Epoch.J2000), fov, 0);

                JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 100;
                using (MemoryStream stream = new MemoryStream()) {
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    encoder.Save(stream);
                    stream.Position = 0;
                    Response.OutputStream.Write(stream.ToArray(), 0, (int)stream.Length);
                }
            } else {
                HttpClient client = new HttpClient();
                byte[] image = await client.GetByteArrayAsync($"{CoreUtility.Hips2FitsUrl}?width={width}&height={height}&fov={fov}&ra={ra}&dec={dec}&hips=CDS/P/DSS2/color&projection=STG&format=jpg");
                Response.OutputStream.Write(image, 0, image.Length);

                client.Dispose();
            }

        } catch (Exception ex) {
            Logger.Error(ex);
            throw;
        }
    }
}