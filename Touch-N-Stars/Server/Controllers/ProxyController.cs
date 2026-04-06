using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// Generic proxy controller. Fetches any URL server-side and streams the response back.
/// This bypasses browser CORS restrictions and credential-in-URL blocking (Chrome/Edge).
/// Usage: GET /api/proxy?url=http://user:pass@host/path
/// </summary>
public class ProxyController : WebApiController
{
    private static readonly HttpClient client = new HttpClient();

    [Route(HttpVerbs.Get, "/proxy")]
    public async Task GetProxy()
    {
        string targetUrl = HttpContext.Request.QueryString.Get("url");

        if (string.IsNullOrEmpty(targetUrl))
        {
            HttpContext.Response.StatusCode = 400;
            await HttpContext.SendStringAsync("Missing 'url' query parameter", "text/plain", Encoding.UTF8);
            return;
        }

        try
        {
            Uri uri = new Uri(targetUrl);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            // Extract credentials from URL and send as Basic Auth header
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                string credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(Uri.UnescapeDataString(uri.UserInfo)));
                request.Headers.Add("Authorization", $"Basic {credentials}");

                // Rebuild URI without credentials
                UriBuilder builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
                request.RequestUri = builder.Uri;
            }

            HttpResponseMessage response = await client.SendAsync(request);
            HttpContext.Response.StatusCode = (int)response.StatusCode;

            if (response.Content != null)
            {
                string contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                HttpContext.Response.ContentType = contentType;

                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                await Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Proxy error for '{targetUrl}': {ex}");
            HttpContext.Response.StatusCode = 500;
            await HttpContext.SendStringAsync(ex.Message, "text/plain", Encoding.UTF8);
        }
    }
}
