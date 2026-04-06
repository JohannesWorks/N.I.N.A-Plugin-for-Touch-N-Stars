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
            // Decode the URL in case it was percent-encoded by the browser
            string decodedUrl = Uri.UnescapeDataString(targetUrl);
            Uri uri = new Uri(decodedUrl);

            // Extract credentials and build clean URI
            Uri requestUri = uri;
            System.Net.ICredentials credentials = null;

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                string userInfo = Uri.UnescapeDataString(uri.UserInfo);
                int colonIndex = userInfo.IndexOf(':');
                string username = colonIndex >= 0 ? userInfo.Substring(0, colonIndex) : userInfo;
                string password = colonIndex >= 0 ? userInfo.Substring(colonIndex + 1) : string.Empty;

                // Use NetworkCredential so HttpClient handles both Basic and Digest auth automatically
                credentials = new System.Net.NetworkCredential(username, password);

                UriBuilder builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
                requestUri = builder.Uri;
            }

            // Create a per-request HttpClient with credentials
            var handler = new HttpClientHandler { Credentials = credentials, PreAuthenticate = false };
            using var httpClient = new HttpClient(handler);

            HttpResponseMessage response = await httpClient.GetAsync(requestUri);
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
