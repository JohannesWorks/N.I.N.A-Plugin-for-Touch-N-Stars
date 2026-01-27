using TouchNStars.Server.Models;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// API Controller for INDI driver management
/// </summary>
public class INDIController : WebApiController
{
    private static readonly string INDIDriversPath = "/usr/share/indi/drivers.xml";

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
    /// Helper method to get drivers by type
    /// </summary>
    private ApiResponse GetDriversByType(string driverType)
    {
        try
        {
            if (!File.Exists(INDIDriversPath))
            {
                // Return empty list on Windows or systems without INDI
                HttpContext.Response.StatusCode = 200;
                return new ApiResponse
                {
                    Success = true,
                    Response = new List<INDIDriver>(),
                    StatusCode = 200,
                    Type = "INDIDrivers"
                };
            }

            var doc = XDocument.Load(INDIDriversPath);
            var root = doc.Root;

            var drivers = new List<INDIDriver>();

            if (root != null)
            {
                // Special handling for flatpanel - filter from Auxiliary group
                if (driverType == "flatpanel")
                {
                    var auxiliaryGroup = root.Elements("devGroup")
                        .FirstOrDefault(g => g.Attribute("group")?.Value == "Auxiliary");

                    if (auxiliaryGroup != null)
                    {
                        foreach (var deviceElement in auxiliaryGroup.Elements("device"))
                        {
                            var label = deviceElement.Attribute("label")?.Value ?? "";

                            // Include devices with "flat", "panel", "cover", "FP", or "GIOTTO" in the label (case-insensitive)
                            if (label.Contains("flat", StringComparison.OrdinalIgnoreCase) ||
                                label.Contains("panel", StringComparison.OrdinalIgnoreCase) ||
                                label.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
                                label.Contains("FP", StringComparison.OrdinalIgnoreCase) ||
                                label.Contains("GIOTTO", StringComparison.OrdinalIgnoreCase))
                            {
                                var driverElement = deviceElement.Element("driver");
                                if (driverElement != null)
                                {
                                    var name = driverElement.Attribute("name")?.Value;
                                    var executableName = driverElement.Value;

                                    if (!string.IsNullOrWhiteSpace(name))
                                    {
                                        var indiDriver = new INDIDriver
                                        {
                                            Name = executableName ?? name,
                                            Label = label,
                                            Type = driverType
                                        };
                                        drivers.Add(indiDriver);
                                    }
                                }
                            }
                        }
                    }

                    HttpContext.Response.StatusCode = 200;
                    return new ApiResponse
                    {
                        Success = true,
                        Response = drivers,
                        StatusCode = 200,
                        Type = "INDIDrivers"
                    };
                }

                // Map driver types to devGroup names in INDI's drivers.xml
                string devGroupName = driverType switch
                {
                    "focuser" => "Focusers",
                    "filterwheel" => "Filter Wheels",
                    "rotator" => "Rotators",
                    "telescope" => "Telescopes",
                    "weather" => "Weather",
                    "switches" => "Power",
                    _ => ""
                };

                if (string.IsNullOrEmpty(devGroupName))
                {
                    HttpContext.Response.StatusCode = 200;
                    return new ApiResponse
                    {
                        Success = true,
                        Response = drivers,
                        StatusCode = 200,
                        Type = "INDIDrivers"
                    };
                }

                var devGroup = root.Elements("devGroup")
                    .FirstOrDefault(g => g.Attribute("group")?.Value == devGroupName);

                if (devGroup != null)
                {
                    foreach (var deviceElement in devGroup.Elements("device"))
                    {
                        var label = deviceElement.Attribute("label")?.Value;
                        var driverElement = deviceElement.Element("driver");

                        if (driverElement != null)
                        {
                            var name = driverElement.Attribute("name")?.Value;
                            var executableName = driverElement.Value; // The driver executable name is the text content

                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                var indiDriver = new INDIDriver
                                {
                                    Name = executableName ?? name,
                                    Label = label ?? name,
                                    Type = driverType
                                };
                                drivers.Add(indiDriver);
                            }
                        }
                    }
                }
            }

            HttpContext.Response.StatusCode = 200;
            return new ApiResponse
            {
                Success = true,
                Response = drivers,
                StatusCode = 200,
                Type = "INDIDrivers"
            };
        }
        catch (XmlException ex)
        {
            Logger.Warning($"Failed to parse INDI drivers file: {ex.Message}");
            HttpContext.Response.StatusCode = 400;
            return new ApiResponse
            {
                Success = false,
                Error = $"Failed to parse INDI drivers file: {ex.Message}",
                StatusCode = 400,
                Type = "Error"
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
