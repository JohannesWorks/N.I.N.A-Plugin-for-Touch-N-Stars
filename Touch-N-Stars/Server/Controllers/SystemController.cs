using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// API Controller for system control (shutdown/restart)
/// </summary>
public class SystemController : WebApiController
{
    private enum SystemAction
    {
        Shutdown,
        Restart
    }

    /// <summary>
    /// GET /api/system/shutdown - Shutdown the system
    /// </summary>
    [Route(HttpVerbs.Get, "/system/shutdown")]
    public object SystemShutdown()
    {
        var remote = HttpContext?.Request?.RemoteEndPoint?.ToString() ?? "unknown";
        Logger.Info($"Shutdown request received from {remote}");
        return ExecuteSystemAction(SystemAction.Shutdown, "System shutdown initiated");
    }

    /// <summary>
    /// GET /api/system/restart - Restart the system
    /// </summary>
    [Route(HttpVerbs.Get, "/system/restart")]
    public object SystemRestart()
    {
        var remote = HttpContext?.Request?.RemoteEndPoint?.ToString() ?? "unknown";
        Logger.Info($"Restart request received from {remote}");
        return ExecuteSystemAction(SystemAction.Restart, "System restart initiated");
    }

    private Dictionary<string, object> ExecuteSystemAction(SystemAction action, string successMessage)
    {
        try
        {
            var startInfo = BuildProcessStartInfo(action);
            var commandLine = $"{startInfo.FileName} {startInfo.Arguments}".Trim();

            Logger.Info($"System action {action}: Detected OS {Environment.OSVersion}; Windows={OperatingSystem.IsWindows()}, Linux={OperatingSystem.IsLinux()}");
            Logger.Info($"System action {action}: executing '{commandLine}'");

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                Logger.Info($"System action {action}: stdout => {standardOutput.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(standardError))
            {
                Logger.Warning($"System action {action}: stderr => {standardError.Trim()}");
            }

            Logger.Info($"System action {action}: exit code {process.ExitCode}");

            var success = process.ExitCode == 0;
            if (!success)
            {
                var errorMessage = string.IsNullOrWhiteSpace(standardError)
                    ? $"Command '{commandLine}' exited with code {process.ExitCode}"
                    : standardError.Trim();
                Logger.Error($"System action {action} failed: {errorMessage}");

                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "error", errorMessage }
                };
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "message", successMessage }
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            HttpContext.Response.StatusCode = 500;
            return new Dictionary<string, object>
            {
                { "success", false },
                { "error", ex.Message }
            };
        }
    }


    private static ProcessStartInfo BuildProcessStartInfo(SystemAction action)
    {
        if (OperatingSystem.IsWindows())
        {
            var windowsCommand = action == SystemAction.Shutdown
                ? "Stop-Computer -Force"
                : "Restart-Computer -Force";

            return new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-Command \"{windowsCommand}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }

        if (OperatingSystem.IsLinux())
        {
            var linuxCommand = action == SystemAction.Shutdown
                ? "sudo shutdown -h now"
                : "sudo shutdown -r now";

            return new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{linuxCommand}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }

        throw new PlatformNotSupportedException("System commands are only supported on Windows and Linux.");
    }
}
