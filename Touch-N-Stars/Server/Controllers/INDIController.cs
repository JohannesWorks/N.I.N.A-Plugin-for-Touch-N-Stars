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

            HttpContext.Response.StatusCode = 200;
            return new ApiResponse
            {
                Success = true,
                Response = portNames,
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
