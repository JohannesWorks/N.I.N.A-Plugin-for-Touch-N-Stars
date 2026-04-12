using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// Controller for browsing and managing the local filesystem.
/// Routes:
///   GET    /api/filesystem/browse?path=...         — list directories and files
///   POST   /api/filesystem/directory               — create a directory (body: { "path": "..." })
///   DELETE /api/filesystem/directory?path=...      — delete a directory (recursive)
///   DELETE /api/filesystem/file?path=...           — delete a file
/// </summary>
public class FilesystemController : WebApiController
{
    private Task SendJson(object data, int statusCode = 200)
    {
        HttpContext.Response.StatusCode = statusCode;
        string json = JsonConvert.SerializeObject(data);
        return HttpContext.SendStringAsync(json, "application/json", Encoding.UTF8);
    }

    // -------------------------------------------------------------------------
    // GET /api/filesystem/browse
    // -------------------------------------------------------------------------
    [Route(HttpVerbs.Get, "/filesystem/browse")]
    public async Task Browse()
    {
        try
        {
            string pathParam = HttpContext.Request.QueryString["path"];
            string path = string.IsNullOrWhiteSpace(pathParam)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Uri.UnescapeDataString(pathParam);

            string fullPath = Path.GetFullPath(path);

            if (!Directory.Exists(fullPath))
            {
                await SendJson(new { success = false, error = "Path does not exist" }, 404);
                return;
            }

            var directories = new List<object>();
            var files = new List<object>();

            try
            {
                foreach (var dir in Directory.GetDirectories(fullPath).OrderBy(d => d))
                {
                    try
                    {
                        var info = new DirectoryInfo(dir);
                        directories.Add(new
                        {
                            name = info.Name,
                            path = info.FullName,
                            lastModified = info.LastWriteTimeUtc.ToString("o")
                        });
                    }
                    catch { /* skip inaccessible entries */ }
                }

                foreach (var file in Directory.GetFiles(fullPath).OrderBy(f => f))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        files.Add(new
                        {
                            name = info.Name,
                            path = info.FullName,
                            size = info.Length,
                            lastModified = info.LastWriteTimeUtc.ToString("o")
                        });
                    }
                    catch { /* skip inaccessible entries */ }
                }
            }
            catch (UnauthorizedAccessException)
            {
                await SendJson(new { success = false, error = "Access denied" }, 403);
                return;
            }

            string parentPath = Directory.GetParent(fullPath)?.FullName;

            await SendJson(new
            {
                success = true,
                currentPath = fullPath,
                parentPath,
                directories,
                files
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"[FilesystemController.Browse] {ex.Message}", ex);
            await SendJson(new { success = false, error = ex.Message }, 500);
        }
    }

    // -------------------------------------------------------------------------
    // POST /api/filesystem/directory  body: { "path": "C:\\some\\new\\dir" }
    // -------------------------------------------------------------------------
    [Route(HttpVerbs.Post, "/filesystem/directory")]
    public async Task CreateDirectory()
    {
        try
        {
            var body = await HttpContext.GetRequestDataAsync<Dictionary<string, string>>();
            if (body == null || !body.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
            {
                await SendJson(new { success = false, error = "Missing 'path' in request body" }, 400);
                return;
            }

            string fullPath = Path.GetFullPath(path);

            if (Directory.Exists(fullPath))
            {
                await SendJson(new { success = false, error = "Directory already exists" }, 409);
                return;
            }

            Directory.CreateDirectory(fullPath);
            Logger.Info($"[FilesystemController] Created directory: {fullPath}");

            await SendJson(new { success = true, path = fullPath }, 201);
        }
        catch (UnauthorizedAccessException)
        {
            await SendJson(new { success = false, error = "Access denied" }, 403);
        }
        catch (Exception ex)
        {
            Logger.Error($"[FilesystemController.CreateDirectory] {ex.Message}", ex);
            await SendJson(new { success = false, error = ex.Message }, 500);
        }
    }

    // -------------------------------------------------------------------------
    // DELETE /api/filesystem/directory?path=...
    // -------------------------------------------------------------------------
    [Route(HttpVerbs.Delete, "/filesystem/directory")]
    public async Task DeleteDirectory()
    {
        try
        {
            string pathParam = HttpContext.Request.QueryString["path"];
            if (string.IsNullOrWhiteSpace(pathParam))
            {
                await SendJson(new { success = false, error = "Missing 'path' query parameter" }, 400);
                return;
            }

            string fullPath = Path.GetFullPath(Uri.UnescapeDataString(pathParam));

            if (!Directory.Exists(fullPath))
            {
                await SendJson(new { success = false, error = "Directory does not exist" }, 404);
                return;
            }

            Directory.Delete(fullPath, recursive: true);
            Logger.Info($"[FilesystemController] Deleted directory: {fullPath}");

            await SendJson(new { success = true, path = fullPath });
        }
        catch (UnauthorizedAccessException)
        {
            await SendJson(new { success = false, error = "Access denied" }, 403);
        }
        catch (Exception ex)
        {
            Logger.Error($"[FilesystemController.DeleteDirectory] {ex.Message}", ex);
            await SendJson(new { success = false, error = ex.Message }, 500);
        }
    }

    // -------------------------------------------------------------------------
    // DELETE /api/filesystem/file?path=...
    // -------------------------------------------------------------------------
    [Route(HttpVerbs.Delete, "/filesystem/file")]
    public async Task DeleteFile()
    {
        try
        {
            string pathParam = HttpContext.Request.QueryString["path"];
            if (string.IsNullOrWhiteSpace(pathParam))
            {
                await SendJson(new { success = false, error = "Missing 'path' query parameter" }, 400);
                return;
            }

            string fullPath = Path.GetFullPath(Uri.UnescapeDataString(pathParam));

            if (!File.Exists(fullPath))
            {
                await SendJson(new { success = false, error = "File does not exist" }, 404);
                return;
            }

            File.Delete(fullPath);
            Logger.Info($"[FilesystemController] Deleted file: {fullPath}");

            await SendJson(new { success = true, path = fullPath });
        }
        catch (UnauthorizedAccessException)
        {
            await SendJson(new { success = false, error = "Access denied" }, 403);
        }
        catch (Exception ex)
        {
            Logger.Error($"[FilesystemController.DeleteFile] {ex.Message}", ex);
            await SendJson(new { success = false, error = ex.Message }, 500);
        }
    }
}
