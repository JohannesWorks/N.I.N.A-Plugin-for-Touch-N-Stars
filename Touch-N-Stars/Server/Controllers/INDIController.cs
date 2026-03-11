using TouchNStars.Server.Models;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// API Controller for INDI driver management
/// </summary>
public class INDIController : WebApiController
{
    /// <summary>
    /// GET /api/indi/focuser - Get available INDI focuser drivers
    /// </summary>
    [Route(HttpVerbs.Get, "/indi/focuser")]
    public ApiResponse GetFocuserDrivers()
    {
        return GetDriversByType("focuser");
    }

    /// <summary>
    /// GET /api/indi/filterwheel - Get available INDI filterwheel drivers
    /// </summary>
    [Route(HttpVerbs.Get, "/indi/filterwheel")]
    public ApiResponse GetFilterwheelDrivers()
    {
        return GetDriversByType("filterwheel");
    }

    /// <summary>
    /// GET /api/indi/rotator - Get available INDI rotator drivers
    /// </summary>
    [Route(HttpVerbs.Get, "/indi/rotator")]
    public ApiResponse GetRotatorDrivers()
    {
        return GetDriversByType("rotator");
    }

    /// <summary>
    /// GET /api/indi/telescope - Get available INDI telescope mount drivers
    /// </summary>
    [Route(HttpVerbs.Get, "/indi/telescope")]
    public ApiResponse GetTelescopeDrivers()
    {
        return GetDriversByType("telescope");
    }

    /// <summary>
    /// GET /api/indi/weather - Get available INDI weather device drivers
    /// </summary>
    [Route(HttpVerbs.Get, "/indi/weather")]
    public ApiResponse GetWeatherDrivers()
    {
        return GetDriversByType("weather");
    }

    /// <summary>
    /// GET /api/indi/switches - Get available INDI switch/power device drivers
    /// </summary>
    [Route(HttpVerbs.Get, "/indi/switches")]
    public ApiResponse GetSwitchDrivers()
    {
        return GetDriversByType("switches");
    }

    /// <summary>
    /// GET /api/indi/flatpanel - Get available INDI flat panel drivers
    /// </summary>
    [Route(HttpVerbs.Get, "/indi/flatpanel")]
    public ApiResponse GetFlatpanelDrivers()
    {
        return GetDriversByType("flatpanel");
    }

    /// <summary>
    /// GET /api/indi/serialports - Get available serial ports for INDI connections
    /// </summary>
    [Route(HttpVerbs.Get, "/indi/serialports")]
    public ApiResponse GetAvailableSerialPorts()
    {
        try
        {
            var portNames = System.IO.Ports.SerialPort.GetPortNames();
            var portInfoList = new List<SerialPortInfo>();

            foreach (var portName in portNames)
            {
                var description = GetPortDescription(portName);
                var isAvailable = IsPortAvailable(portName);

                portInfoList.Add(new SerialPortInfo
                {
                    PortName = portName,
                    Description = description,
                    IsAvailable = isAvailable
                });
            }

            HttpContext.Response.StatusCode = 200;
            return new ApiResponse
            {
                Success = true,
                Response = portInfoList,
                StatusCode = 200,
                Type = "SerialPorts"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"Unexpected error while retrieving available serial ports: {ex}");
            HttpContext.Response.StatusCode = 500;
            return new ApiResponse
            {
                Success = false,
                Error = "An unexpected error occurred while retrieving serial ports",
                StatusCode = 500,
                Type = "Error"
            };
        }
    }

    private bool IsPortAvailable(string portName)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return IsLinuxPortAvailable(portName);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return IsWindowsPortAvailable(portName);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error checking port availability for {portName}: {ex}");
        }

        // Default to assuming it's available if we can't determine
        return true;
    }

    private bool IsLinuxPortAvailable(string portName)
    {
        try
        {
            // Use lsof to check if any process has the port open
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "lsof",
                Arguments = portName,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(processInfo))
            {
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    // If lsof returns output (other than header), the port is in use
                    var lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
                    return lines.Length <= 1; // Only header means port is available
                }
            }
        }
        catch
        {
            // If lsof is not available, fall back to trying to open the port
            try
            {
                using (var port = new System.IO.Ports.SerialPort(portName))
                {
                    port.Open();
                    port.Close();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private bool IsWindowsPortAvailable(string portName)
    {
        try
        {
            using (var port = new System.IO.Ports.SerialPort(portName))
            {
                port.Open();
                port.Close();
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private string GetPortDescription(string portName)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return GetLinuxPortDescription(portName);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetWindowsPortDescription(portName);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error getting port description for {portName}: {ex}");
        }
        return string.Empty;
    }

    private string GetLinuxPortDescription(string portName)
    {
        try
        {
            var serialByIdPath = "/dev/serial/by-id/";

            if (System.IO.Directory.Exists(serialByIdPath))
            {
                // Look for symlinks in /dev/serial/by-id/ that point to our port
                foreach (var linkPath in System.IO.Directory.GetFiles(serialByIdPath))
                {
                    try
                    {
                        // Resolve the symlink target
                        var linkTarget = System.IO.File.ResolveLinkTarget(linkPath, returnFinalTarget: true)?.FullName;

                        if (linkTarget != null && System.IO.Path.GetFullPath(linkTarget) == System.IO.Path.GetFullPath(portName))
                        {
                            // Extract the descriptive name from the symlink (e.g., "usb-1a86_CH340_serial_converter-if00")
                            var linkName = System.IO.Path.GetFileName(linkPath);
                            // Clean up the name by removing common suffixes
                            linkName = System.Text.RegularExpressions.Regex.Replace(linkName, @"(-if\d+)?$", "");
                            return linkName;
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error reading Linux port description for {portName}: {ex}");
        }

        return string.Empty;
    }

    private string GetWindowsPortDescription(string portName)
    {
        try
        {
            // For Windows, try to read from registry or WMI
            // This is a simplified approach - you might want to use WMI for better results
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"HARDWARE\DEVICEMAP\SERIALCOMM\{portName.Replace("COM", "")}"))
            {
                if (key != null)
                {
                    var description = key.GetValue("Description");
                    if (description != null)
                    {
                        return description.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error reading Windows port description: {ex}");
        }

        return string.Empty;
    }

    /// <summary>
    /// Helper method to get drivers by type using hardcoded driver lists
    /// </summary>
    private ApiResponse GetDriversByType(string driverType)
    {
        try
        {
            var drivers = driverType switch
            {
                "focuser" => INDIFocusDrivers.Drivers,
                "filterwheel" => INDIFilterWheelDrivers.Drivers,
                "rotator" => INDIRotatorDrivers.Drivers,
                "telescope" => INDIMountDrivers.Drivers,
                "weather" => INDIWeatherDrivers.Drivers,
                "switches" => INDISwitchDrivers.Drivers,
                "flatpanel" => INDIFlatPanelDrivers.Drivers,
                _ => new List<INDIDriver>()
            };

            HttpContext.Response.StatusCode = 200;
            return new ApiResponse
            {
                Success = true,
                Response = drivers,
                StatusCode = 200,
                Type = "INDIDrivers"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"Unexpected error while retrieving INDI drivers: {ex}");
            HttpContext.Response.StatusCode = 500;
            return new ApiResponse
            {
                Success = false,
                Error = "An unexpected error occurred while retrieving INDI drivers",
                StatusCode = 500,
                Type = "Error"
            };
        }
    }
}
