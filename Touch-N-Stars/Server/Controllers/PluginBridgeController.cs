using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TouchNStars.Server.Bridge;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// Generic entry point for third-party plugins on the plugin bridge (see PluginBridgeService).
/// GET /api/ext lists them; /api/ext/{pluginId}/{path} forwards any verb to the plugin.
/// </summary>
public class PluginBridgeController : WebApiController {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Route(HttpVerbs.Get, "/ext")]
    public async Task ListPlugins() {
        PluginBridgeService service = PluginBridgeService.Instance;
        if (service == null) {
            await WriteAsync(BridgeResponse.Error(503, "The plugin bridge is not running."));
            return;
        }
        var plugins = service.ListPlugins().Select(p => new {
            p.Id,
            p.Name,
            p.Version,
            p.ContractVersion,
            p.Compatible,
            p.Routes,
        });
        string json = JsonSerializer.Serialize(new {
            contractVersion = PluginBridgeService.ContractVersion,
            plugins,
        }, JsonOptions);
        await WriteAsync(new BridgeResponse(200, "application/json; charset=utf-8", json));
    }

    [Route(HttpVerbs.Any, "/ext/{pluginId}", true)]
    public async Task Forward(string pluginId) {
        PluginBridgeService service = PluginBridgeService.Instance;
        if (service == null) {
            await WriteAsync(BridgeResponse.Error(503, "The plugin bridge is not running."));
            return;
        }

        try {
            IHttpRequest request = HttpContext.Request;
            string contentType = request.ContentType ?? string.Empty;
            object body = null;
            if (request.HasEntityBody) {
                byte[] bytes = await HttpContext.GetRequestBodyAsByteArrayAsync();
                body = IsTextual(contentType) ? Encoding.UTF8.GetString(bytes) : bytes;
            }
            var bridgeRequest = new BridgeRequest(
                request.HttpMethod,
                Route.SubPath ?? string.Empty,
                (request.Url.Query ?? string.Empty).TrimStart('?'),
                contentType,
                body);

            BridgeResponse response = await service.SendAsync(pluginId, bridgeRequest, HttpContext.CancellationToken);
            await WriteAsync(response);
        } catch (OperationCanceledException) {
            // The client went away; nothing left to answer.
        } catch (Exception ex) {
            Logger.Error($"Plugin bridge: forwarding to '{pluginId}' failed: {ex}");
            await WriteAsync(BridgeResponse.Error(500, ex.Message));
        }
    }

    private async Task WriteAsync(BridgeResponse response) {
        Response.StatusCode = response.Status;
        if (!string.IsNullOrEmpty(response.ContentType)) {
            Response.ContentType = response.ContentType;
            // EmbedIO appends "; charset=<ContentEncoding>" to every content type that names none,
            // which would turn "image/png" into "image/png; charset=utf-8". Send the plugin's
            // content type unchanged.
            if (response.ContentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase) < 0) {
                Response.ContentEncoding = null;
            }
        }
        switch (response.Content) {
            case null:
                break;
            case string text:
                await WriteBytesAsync(Encoding.UTF8.GetBytes(text));
                break;
            case byte[] bytes:
                await WriteBytesAsync(bytes);
                break;
            case Stream stream:
                await using (stream) {
                    await stream.CopyToAsync(Response.OutputStream);
                }
                break;
            default:
                // A plugin that bypasses TnsBridge may hand over a plain object.
                Response.ContentType = "application/json; charset=utf-8";
                await WriteBytesAsync(JsonSerializer.SerializeToUtf8Bytes(response.Content, JsonOptions));
                break;
        }
    }

    private async Task WriteBytesAsync(byte[] bytes) {
        Response.ContentLength64 = bytes.Length;
        await Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    private static bool IsTextual(string contentType) {
        string mime = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return mime.Length == 0
            || mime.StartsWith("text/", StringComparison.Ordinal)
            || mime == "application/json"
            || mime.EndsWith("+json", StringComparison.Ordinal)
            || mime == "application/x-www-form-urlencoded"
            || mime == "application/xml";
    }
}
