using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces;
using System;
using System.Collections.Generic;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// API Controller for observing-site location queries.
/// Exposes the coordinates stored in the NINA profile (AstrometrySettings)
/// and the coordinates currently reported by the connected mount.
/// </summary>
public class LocationController : WebApiController
{
    /// <summary>
    /// GET /api/location
    /// Returns both the profile location and (when a mount is connected) the
    /// mount's reported site location in a single response.
    /// </summary>
    [Route(HttpVerbs.Get, "/location")]
    public object GetLocation()
    {
        try
        {
            var profileLocation = GetProfileLocation();
            var mountLocation = GetMountLocation();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "profile", profileLocation },
                { "mount", mountLocation }
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

    /// <summary>
    /// GET /api/location/profile
    /// Returns the Lat/Long/Elevation stored in the active NINA profile
    /// (Options → Equipment → Astrometry Settings).
    /// </summary>
    [Route(HttpVerbs.Get, "/location/profile")]
    public object GetProfileLocation()
    {
        try
        {
            var astrometry = TouchNStars.Mediators.Profile.ActiveProfile.AstrometrySettings;

            return new Dictionary<string, object>
            {
                { "latitude",  astrometry.Latitude },
                { "longitude", astrometry.Longitude },
                { "elevation", astrometry.Elevation }
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

    /// <summary>
    /// GET /api/location/mount
    /// Returns the Lat/Long/Elevation read live from the mount driver on every request.
    /// Uses IDeviceMediator.GetDevice() → ITelescope to bypass NINA's cached TelescopeInfo
    /// snapshot, which only records site coordinates at connect-time and never refreshes
    /// them during the polling loop.  This means GPS-set or externally-updated coordinates
    /// are always current.
    /// Returns connected=false (with null coordinates) when no mount is connected.
    /// Note: ASCOM drivers that do not implement site-location properties return -1 for
    /// latitude/longitude and 0 for elevation as a fallback sentinel.
    /// </summary>
    [Route(HttpVerbs.Get, "/location/mount")]
    public object GetMountLocation()
    {
        try
        {
            var info = TouchNStars.Mediators.Telescope.GetInfo();

            if (info == null || !info.Connected)
            {
                return new Dictionary<string, object>
                {
                    { "connected", false },
                    { "latitude",  (object)null },
                    { "longitude", (object)null },
                    { "elevation", (object)null }
                };
            }

            // Read directly from the live driver via GetDevice() so that
            // GPS-locked or externally-updated coordinates are always current.
            // NINA's TelescopeInfo snapshot only captures site coords at connect-time.
            double latitude, longitude, elevation;
            var device = TouchNStars.Mediators.Telescope.GetDevice() as ITelescope;
            if (device != null && device.Connected)
            {
                latitude  = device.SiteLatitude;
                longitude = device.SiteLongitude;
                elevation = device.SiteElevation;
            }
            else
            {
                // Fall back to cached snapshot if the driver instance is unexpectedly unavailable
                latitude  = info.SiteLatitude;
                longitude = info.SiteLongitude;
                elevation = info.SiteElevation;
            }

            // ASCOM returns -1 for lat/long when SiteLatitude/SiteLongitude are not
            // implemented by the driver; flag that explicitly so clients can tell apart
            // "driver doesn't support it" from a real coordinate near 0°.
            bool latLongSupported = latitude != -1d || longitude != -1d;

            return new Dictionary<string, object>
            {
                { "connected", true },
                { "siteLocationSupported", latLongSupported },
                { "latitude",  latitude },
                { "longitude", longitude },
                { "elevation", elevation }
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
}
