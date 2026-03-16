using NINA.Core.Utility;
using NINA.Plugins.TouchNStars.Tilter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Settings = TouchNStars.Properties.Settings;

namespace TouchNStars.Server.Services;

/// <summary>
/// Service for managing ETA Tilter devices via Wanderer SDK
/// Implemented as a singleton to maintain device connection state across requests
/// </summary>
public class TilterService
{
    // Singleton instance
    private static TilterService _instance;
    private static readonly object _instanceLock = new object();

    private Dictionary<int, TilterDeviceInfo> ConnectedDevices = new();
    private const int MaxDevices = 32;

    /// <summary>
    /// Gets the singleton instance of TilterService
    /// </summary>
    public static TilterService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    if (_instance == null)
                    {
                        _instance = new TilterService();
                    }
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Private constructor to enforce singleton pattern
    /// </summary>
    private TilterService()
    {
    }

    public class TilterDeviceInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsConnected { get; set; }
        public WandererSDK.WTVersion Version { get; set; }
    }

    public class TilterDeviceDTO
    {
        public string Id { get; set; }  // Unique identifier (e.g., "ETA_1_FW001")
        public int DeviceId { get; set; }  // Raw device ID from SDK
        public string Name { get; set; }
        public string SerialInfo { get; set; }  // Serial or identifying info
    }

    public class TilterStatusDTO
    {
        public bool IsMoving { get; set; }
        public double CurrentPosition1 { get; set; }
        public double CurrentPosition2 { get; set; }
        public double CurrentPosition3 { get; set; }

        // Image plane corner offsets from center (calculated server-side)
        public double ImagePlaneTopLeftOffset { get; set; }
        public double ImagePlaneTopRightOffset { get; set; }
        public double ImagePlaneBottomLeftOffset { get; set; }
        public double ImagePlaneBottomRightOffset { get; set; }

        // Absolute Z values for each corner
        public double ImagePlaneTopLeftAbsolute { get; set; }
        public double ImagePlaneTopRightAbsolute { get; set; }
        public double ImagePlaneBottomLeftAbsolute { get; set; }
        public double ImagePlaneBottomRightAbsolute { get; set; }
    }

    public class SensorConfigurationDTO
    {
        public double SensorWidth { get; set; }   // in mm
        public double SensorHeight { get; set; }  // in mm
        public double SensorRotation { get; set; } // in degrees
    }

    public class ApplyTiltPlaneDTO
    {
        public double ImagePlaneTopLeftZ { get; set; }      // Desired Z at top-left corner
        public double ImagePlaneTopRightZ { get; set; }     // Desired Z at top-right corner
        public double ImagePlaneBottomLeftZ { get; set; }   // Desired Z at bottom-left corner
        public double ImagePlaneBottomRightZ { get; set; }  // Desired Z at bottom-right corner
        public double OuterRadius { get; set; }             // Outer radius of tilter screws (default 78mm for Wanderer ETA)
        public bool DontOffsetToZero { get; set; }          // For manual tilters: don't shift positions to avoid negatives
    }

    public class ApplyTiltPlaneResultDTO
    {
        public bool Success { get; set; }
        public float Position1 { get; set; }
        public float Position2 { get; set; }
        public float Position3 { get; set; }
        public float? RawPosition1 { get; set; }  // Raw calculated value before offsetting (for manual tilters)
        public float? RawPosition2 { get; set; }  // Raw calculated value before offsetting (for manual tilters)
        public float? RawPosition3 { get; set; }  // Raw calculated value before offsetting (for manual tilters)
        public string Message { get; set; }
    }

    /// <summary>
    /// Scans for available ETA Tilter devices
    /// </summary>
    public List<TilterDeviceDTO> ScanDevices()
    {
        var devices = new List<TilterDeviceDTO>();

        try
        {
            // Prepare array for device IDs (max 32 devices)
            var deviceIds = new int[MaxDevices];
            int deviceCount = MaxDevices;

            // Call the SDK scan function
            var result = WandererSDK.WTETAScan(ref deviceCount, deviceIds);

            if (result != WandererSDK.WTErrorType.Success)
            {
                Logger.Error($"TilterService: WTETAScan failed with error: {result}");
                return devices;
            }

            // Add found devices to the list
            for (int i = 0; i < deviceCount; i++)
            {
                int deviceId = deviceIds[i];
                string serialInfo = "Unknown";

                // Try to get device version info (which includes model)
                try
                {
                    var version = new WandererSDK.WTVersion();
                    var versionResult = WandererSDK.WTETAGetVersion(deviceId, ref version);

                    if (versionResult == WandererSDK.WTErrorType.Success)
                    {
                        // Extract model string from byte array
                        string modelStr = System.Text.Encoding.ASCII.GetString(version.Model).TrimEnd('\0');
                        serialInfo = $"Model: {modelStr}, FW: {version.Firmware}";
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"TilterService: Failed to get version for device {deviceId}: {ex.Message}");
                    serialInfo = "Unable to retrieve version";
                }

                // Create unique identifier combining device ID and model
                string uniqueId = $"ETA_{deviceId}_{serialInfo.Replace(" ", "").Replace(".", "")}";

                devices.Add(new TilterDeviceDTO
                {
                    Id = uniqueId,
                    DeviceId = deviceId,
                    Name = $"ETA Tilter {deviceId}",
                    SerialInfo = serialInfo
                });
            }

            Logger.Info($"TilterService: Found {deviceCount} ETA Tilter devices");
        }
        catch (Exception ex)
        {
            Logger.Error($"TilterService: Error scanning devices: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Gets list of currently available devices
    /// </summary>
    public List<TilterDeviceDTO> GetAvailableDevices()
    {
        return ScanDevices();
    }

    /// <summary>
    /// Connects to an ETA Tilter device
    /// </summary>
    public bool ConnectDevice(int deviceId)
    {
        try
        {
            // Manual tilter (device ID -1) doesn't need connection
            if (deviceId == -1)
            {
                Logger.Info($"TilterService: Manual tilter (device {deviceId}) - no connection needed");
                return true;
            }

            // Check if already connected
            if (ConnectedDevices.ContainsKey(deviceId))
            {
                Logger.Info($"TilterService: Device {deviceId} already connected");
                return true;
            }

            // Open the device
            var result = WandererSDK.WTETAOpen(deviceId);

            if (result != WandererSDK.WTErrorType.Success)
            {
                Logger.Error($"TilterService: Failed to connect to device {deviceId}: {result}");
                return false;
            }

            // Get device version info
            var version = new WandererSDK.WTVersion();
            result = WandererSDK.WTETAGetVersion(deviceId, ref version);

            if (result != WandererSDK.WTErrorType.Success)
            {
                Logger.Error($"TilterService: Failed to get version for device {deviceId}: {result}");
                WandererSDK.WTETAClose(deviceId);
                return false;
            }

            // Store device info
            ConnectedDevices[deviceId] = new TilterDeviceInfo
            {
                Id = deviceId,
                Name = $"ETA Tilter {deviceId}",
                IsConnected = true,
                Version = version
            };

            Logger.Info($"TilterService: Successfully connected to device {deviceId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"TilterService: Error connecting to device {deviceId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Disconnects from an ETA Tilter device
    /// </summary>
    public bool DisconnectDevice(int deviceId)
    {
        try
        {
            // Manual tilter (device ID -1) doesn't need disconnection
            if (deviceId == -1)
            {
                Logger.Info($"TilterService: Manual tilter (device {deviceId}) - no disconnection needed");
                return true;
            }

            // Check if device is connected
            if (!ConnectedDevices.ContainsKey(deviceId))
            {
                Logger.Info($"TilterService: Device {deviceId} not currently connected");
                return true;
            }

            // Close the device
            var result = WandererSDK.WTETAClose(deviceId);

            if (result != WandererSDK.WTErrorType.Success)
            {
                Logger.Error($"TilterService: Failed to disconnect device {deviceId}: {result}");
                return false;
            }

            // Remove from connected devices
            ConnectedDevices.Remove(deviceId);

            Logger.Info($"TilterService: Successfully disconnected device {deviceId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"TilterService: Error disconnecting device {deviceId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the current status of a connected device
    /// </summary>
    public TilterStatusDTO GetDeviceStatus(int deviceId, double? outerRadius = null)
    {
        var status = new TilterStatusDTO();

        try
        {
            // Manual tilter (device ID -1) is virtual - return default status
            if (deviceId == -1)
            {
                status.IsMoving = false;
                status.CurrentPosition1 = 0;
                status.CurrentPosition2 = 0;
                status.CurrentPosition3 = 0;
                Logger.Info($"TilterService: Manual tilter (device {deviceId}) - returning default status");
                return status;
            }

            // Get status from SDK
            var etaStatus = new WandererSDK.WTEtaStatus();
            var result = WandererSDK.WTETAGetStatus(deviceId, ref etaStatus);

            if (result != WandererSDK.WTErrorType.Success)
            {
                Logger.Error($"TilterService: Failed to get status for device {deviceId}: {result}");
                return status;
            }

            status.IsMoving = etaStatus.IsMoving != 0;
            status.CurrentPosition1 = etaStatus.CurrentPosition1;
            status.CurrentPosition2 = etaStatus.CurrentPosition2;
            status.CurrentPosition3 = etaStatus.CurrentPosition3;

            // Use outer radius from device if available, otherwise use provided value
            double radius = outerRadius ?? etaStatus.Radius;

            // Calculate image plane corner values
            CalculateImagePlaneCorners(status, radius);
        }
        catch (Exception ex)
        {
            Logger.Error($"TilterService: Error getting status for device {deviceId}: {ex.Message}");
        }

        return status;
    }

    /// <summary>
    /// Calculates the tilt (Z value) at any given point on the image plane
    /// </summary>
    public double GetTiltValueAtPoint(int deviceId, double cornerX, double cornerY, double? outerRadius = null)
    {
        try
        {
            // Manual tilter (device ID -1) is virtual - return 0
            if (deviceId == -1)
            {
                Logger.Debug($"TilterService: Manual tilter (device {deviceId}) - returning tilt value 0");
                return 0.0;
            }

            // Check if device is connected
            if (!ConnectedDevices.ContainsKey(deviceId))
            {
                Logger.Error($"TilterService: Device {deviceId} is not connected");
                return 0.0;
            }

            // Get current device status
            var status = GetDeviceStatus(deviceId, outerRadius);

            if (status == null)
            {
                Logger.Error($"TilterService: Failed to get status for device {deviceId}");
                return 0.0;
            }

            // For this calculation we need the radius - use provided value or fetch from device
            // If neither is available, we cannot calculate
            double radius = outerRadius ?? 0.0;
            if (radius <= 0)
            {
                Logger.Error($"TilterService: Outer radius not available for device {deviceId}");
                return 0.0;
            }

            // Calculate tilt value at the specified point
            return CalculateCornerValue(cornerX, cornerY, status.CurrentPosition1, status.CurrentPosition2, status.CurrentPosition3, radius);
        }
        catch (Exception ex)
        {
            Logger.Error($"TilterService: Error calculating tilt at point ({cornerX}, {cornerY}) for device {deviceId}: {ex.Message}");
            return 0.0;
        }
    }

    /// <summary>
    /// Sets the positions for a connected device
    /// Only the provided positions will be set; others retain their current values
    /// </summary>
    public bool SetDevicePositions(int deviceId, float? position1 = null, float? position2 = null, float? position3 = null)
    {
        try
        {
            // Manual tilter (device ID -1) is always valid - no hardware to set
            if (deviceId == -1)
            {
                Logger.Info($"TilterService: Manual tilter (device {deviceId}) - positions request succeeded (no hardware)");
                return true;
            }

            // Check if device is connected
            if (!ConnectedDevices.ContainsKey(deviceId))
            {
                Logger.Error($"TilterService: Device {deviceId} is not connected");
                return false;
            }

            // Get current status to use as fallback for unspecified positions
            var status = GetDeviceStatus(deviceId);

            // Use provided values or fall back to current positions
            float pos1 = position1 ?? (float)status.CurrentPosition1;
            float pos2 = position2 ?? (float)status.CurrentPosition2;
            float pos3 = position3 ?? (float)status.CurrentPosition3;

            // Build mask only for positions that are being set
            uint mask = 0;
            if (position1.HasValue) mask |= WandererSDK.MASK_ETA_POINT_1;
            if (position2.HasValue) mask |= WandererSDK.MASK_ETA_POINT_2;
            if (position3.HasValue) mask |= WandererSDK.MASK_ETA_POINT_3;

            // If no positions specified, nothing to do
            if (mask == 0)
            {
                Logger.Info($"TilterService: No positions specified for device {deviceId}");
                return true;
            }

            // Create config with only the specified positions
            var config = new WandererSDK.WTEtaConfig
            {
                Mask = mask,
                Position1 = pos1,
                Position2 = pos2,
                Position3 = pos3
            };

            Logger.Info($"TilterService: Setting positions for device {deviceId} - Mask: 0x{mask:X}, P1: {(position1.HasValue ? position1.Value.ToString("F6") : "unchanged")}, P2: {(position2.HasValue ? position2.Value.ToString("F6") : "unchanged")}, P3: {(position3.HasValue ? position3.Value.ToString("F6") : "unchanged")}");

            var result = WandererSDK.WTETASetConfig(deviceId, ref config);

            if (result != WandererSDK.WTErrorType.Success)
            {
                Logger.Error($"TilterService: Failed to set positions for device {deviceId}: {result}");
                return false;
            }

            Logger.Info($"TilterService: Successfully set positions for device {deviceId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"TilterService: Error setting positions for device {deviceId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets SDK version
    /// </summary>
    public string GetSDKVersion()
    {
        try
        {
            var versionBuilder = new StringBuilder(WandererSDK.WT_VERSION_LEN);
            var result = WandererSDK.WTGetSDKVersion(versionBuilder);

            if (result != WandererSDK.WTErrorType.Success)
            {
                Logger.Error($"TilterService: Failed to get SDK version: {result}");
                return "Unknown";
            }

            return versionBuilder.ToString();
        }
        catch (Exception ex)
        {
            Logger.Error($"TilterService: Error getting SDK version: {ex.Message}");
            return "Unknown";
        }
    }

    /// <summary>
    /// Gets list of currently connected devices
    /// </summary>
    public List<TilterDeviceInfo> GetConnectedDevices()
    {
        return ConnectedDevices.Values.ToList();
    }

    /// <summary>
    /// Checks if a device is currently connected
    /// </summary>
    public bool IsDeviceConnected(int deviceId)
    {
        // Manual tilter (device ID -1) is always "connected" (it's a virtual device)
        if (deviceId == -1)
            return true;

        return ConnectedDevices.ContainsKey(deviceId);
    }

    /// <summary>
    /// Represents a plane equation in 3D space: Nx*x + Ny*y + Nz*z + D = 0
    /// Or equivalently: z = -(Nx*x + Ny*y + D) / Nz
    /// </summary>
    private struct PlaneEquation
    {
        public double Nx, Ny, Nz; // Normal vector components
        public double D;           // Plane constant

        /// <summary>
        /// Evaluates Z coordinate at a given (x, y) point on the plane
        /// </summary>
        public double EvaluateZ(double x, double y)
        {
            if (Nz == 0)
                return 0; // Avoid division by zero

            return -(Nx * x + Ny * y + D) / Nz;
        }
    }

    /// <summary>
    /// Calculates plane equation from three 3D points
    /// Points must not be collinear
    /// </summary>
    private PlaneEquation CalculatePlaneEquation(
        double x1, double y1, double z1,
        double x2, double y2, double z2,
        double x3, double y3, double z3)
    {
        // Vectors in the plane
        double vx1 = x2 - x1, vy1 = y2 - y1, vz1 = z2 - z1;
        double vx2 = x3 - x1, vy2 = y3 - y1, vz2 = z3 - z1;

        // Normal vector = v1 × v2
        double nx = vy1 * vz2 - vz1 * vy2;
        double ny = vz1 * vx2 - vx1 * vz2;
        double nz = vx1 * vy2 - vy1 * vx2;

        // Plane equation: Nx*(x-x1) + Ny*(y-y1) + Nz*(z-z1) = 0
        // Expanded: Nx*x + Ny*y + Nz*z + D = 0
        // where D = -(Nx*x1 + Ny*y1 + Nz*z1)
        double d = -(nx * x1 + ny * y1 + nz * z1);

        return new PlaneEquation { Nx = nx, Ny = ny, Nz = nz, D = d };
    }


    /// <summary>
    /// Calculates the tilt value at a specific image plane corner
    /// </summary>
    private double CalculateCornerValue(
        double cornerX, double cornerY,
        double pos1, double pos2, double pos3,
        double outerRadius)
    {
        double innerRadius = outerRadius / 2.0;  // For equilateral triangle, inner_radius ≈ outer_radius/2
        double a = Math.Sqrt(outerRadius * outerRadius - innerRadius * innerRadius);

        // Actuator positions:
        // P1 = (-a, inner_radius, position1)
        // P2 = (0, -outer_radius, position2)
        // P3 = (a, inner_radius, position3)
        double p1X = -a, p1Y = innerRadius, p1Z = pos1;
        double p2X = 0.0, p2Y = -outerRadius, p2Z = pos2;
        double p3X = a, p3Y = innerRadius, p3Z = pos3;

        Logger.Trace($"[CalculateCornerValue] Computing Z for corner ({cornerX:F3}, {cornerY:F3})");
        Logger.Trace($"[CalculateCornerValue] Actuators - P1: ({p1X:F3}, {p1Y:F3}, {p1Z:F6}), P2: ({p2X:F3}, {p2Y:F3}, {p2Z:F6}), P3: ({p3X:F3}, {p3Y:F3}, {p3Z:F6})");

        // Calculate plane equation from the three actuator points
        PlaneEquation plane = CalculatePlaneEquation(
            p1X, p1Y, p1Z,
            p2X, p2Y, p2Z,
            p3X, p3Y, p3Z);

        Logger.Trace($"[CalculateCornerValue] Plane Equation - Normal: ({plane.Nx:F6}, {plane.Ny:F6}, {plane.Nz:F6}), D: {plane.D:F6}");

        // Evaluate Z at the corner point
        double result = plane.EvaluateZ(cornerX, cornerY);
        Logger.Trace($"[CalculateCornerValue] Corner ({cornerX:F3}, {cornerY:F3}) -> Z: {result:F6}");
        return result;
    }

    /// <summary>
    /// Calculates image plane corner offsets and absolute values for the status
    /// </summary>
    private void CalculateImagePlaneCorners(TilterStatusDTO status, double outerRadius)
    {
        // Get sensor configuration from settings
        double sensorWidth = Settings.Default.SensorWidth;
        double sensorHeight = Settings.Default.SensorHeight;
        double sensorRotationDegrees = Settings.Default.SensorRotation;
        double sensorRotation = -sensorRotationDegrees * Math.PI / 180.0;

        // Half-dimensions of the sensor
        double halfWidth = sensorWidth / 2.0;
        double halfHeight = sensorHeight / 2.0;

        // Calculate corner positions in sensor coordinates
        // TL, TR, BL, BR before rotation
        double[][] corners = new[]
        {
            new[] { halfWidth, halfHeight },    // Top-left
            new[] { -halfWidth, halfHeight },   // Top-right
            new[] { halfWidth, -halfHeight },   // Bottom-left
            new[] { -halfWidth, -halfHeight }   // Bottom-right
        };

        // Apply rotation to each corner if rotation is non-zero
        for (int i = 0; i < corners.Length; i++)
        {
            if (sensorRotation != 0)
            {
                double x = corners[i][0];
                double y = corners[i][1];
                corners[i][0] = x * Math.Cos(sensorRotation) - y * Math.Sin(sensorRotation);
                corners[i][1] = x * Math.Sin(sensorRotation) + y * Math.Cos(sensorRotation);
            }
        }

        // Center height (average of three actuators)
        double centerHeight = (status.CurrentPosition1 + status.CurrentPosition2 + status.CurrentPosition3) / 3.0;

        // Calculate absolute values at each corner
        status.ImagePlaneTopLeftAbsolute = CalculateCornerValue(corners[0][0], corners[0][1], status.CurrentPosition1, status.CurrentPosition2, status.CurrentPosition3, outerRadius);
        status.ImagePlaneTopRightAbsolute = CalculateCornerValue(corners[1][0], corners[1][1], status.CurrentPosition1, status.CurrentPosition2, status.CurrentPosition3, outerRadius);
        status.ImagePlaneBottomLeftAbsolute = CalculateCornerValue(corners[2][0], corners[2][1], status.CurrentPosition1, status.CurrentPosition2, status.CurrentPosition3, outerRadius);
        status.ImagePlaneBottomRightAbsolute = CalculateCornerValue(corners[3][0], corners[3][1], status.CurrentPosition1, status.CurrentPosition2, status.CurrentPosition3, outerRadius);

        // Calculate offsets from center
        status.ImagePlaneTopLeftOffset = status.ImagePlaneTopLeftAbsolute - centerHeight;
        status.ImagePlaneTopRightOffset = status.ImagePlaneTopRightAbsolute - centerHeight;
        status.ImagePlaneBottomLeftOffset = status.ImagePlaneBottomLeftAbsolute - centerHeight;
        status.ImagePlaneBottomRightOffset = status.ImagePlaneBottomRightAbsolute - centerHeight;
    }

    /// <summary>
    /// Gets the current sensor configuration (size and orientation)
    /// </summary>
    public SensorConfigurationDTO GetSensorConfiguration()
    {
        return new SensorConfigurationDTO
        {
            SensorWidth = Settings.Default.SensorWidth,
            SensorHeight = Settings.Default.SensorHeight,
            SensorRotation = Settings.Default.SensorRotation
        };
    }

    /// <summary>
    /// Sets the sensor configuration (size and orientation)
    /// </summary>
    public void SetSensorConfiguration(SensorConfigurationDTO config)
    {
        try
        {
            Settings.Default.SensorWidth = config.SensorWidth;
            Settings.Default.SensorHeight = config.SensorHeight;
            // Clamp rotation to 0-359 degrees
            Settings.Default.SensorRotation = Math.Clamp(config.SensorRotation, 0, 359.9);
            CoreUtil.SaveSettings(Settings.Default);
            Logger.Info($"TilterService: Sensor configuration updated - Width: {config.SensorWidth}mm, Height: {config.SensorHeight}mm, Rotation: {Settings.Default.SensorRotation}°");
        }
        catch (Exception ex)
        {
            Logger.Error($"TilterService: Error setting sensor configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleanup - disconnects all devices
    /// </summary>
    public void Disconnect()
    {
        try
        {
            // Disconnect all connected devices
            var deviceIds = ConnectedDevices.Keys.ToList();
            foreach (var deviceId in deviceIds)
            {
                DisconnectDevice(deviceId);
            }

            Logger.Info("TilterService: All devices disconnected");
        }
        catch (Exception ex)
        {
            Logger.Error($"TilterService: Error during cleanup: {ex.Message}");
        }
    }

    /// <summary>
    /// Computes required actuator positions (P1, P2, P3) to achieve a specific tilt plane
    /// defined by Z values at the four image sensor corners (inverse calculation)
    /// </summary>
    public ApplyTiltPlaneResultDTO CalculateActuatorPositions(ApplyTiltPlaneDTO desiredPlane, double currentP1 = 0, double currentP2 = 0, double currentP3 = 0, bool dontOffsetToZero = false)
    {
        try
        {
            Logger.Info($"[CalculateActuatorPositions] Current actuator positions (mm) - P1: {currentP1:F6}, P2: {currentP2:F6}, P3: {currentP3:F6}");
            double sensorWidth = Settings.Default.SensorWidth;
            double sensorHeight = Settings.Default.SensorHeight;
            double sensorRotationDegrees = Settings.Default.SensorRotation;
            double sensorRotation = -sensorRotationDegrees * Math.PI / 180.0;

            // Half-dimensions
            double halfWidth = sensorWidth / 2.0;
            double halfHeight = sensorHeight / 2.0;

            // Calculate corner positions in sensor coordinates (before rotation)
            double[][] corners = new[]
            {
                new[] { halfWidth, halfHeight },    // Top-left
                new[] { -halfWidth, halfHeight },   // Top-right
                new[] { halfWidth, -halfHeight },   // Bottom-left
                new[] { -halfWidth, -halfHeight }   // Bottom-right
            };

            // Apply rotation
            for (int i = 0; i < corners.Length; i++)
            {
                if (sensorRotation != 0)
                {
                    double x = corners[i][0];
                    double y = corners[i][1];
                    corners[i][0] = x * Math.Cos(sensorRotation) - y * Math.Sin(sensorRotation);
                    corners[i][1] = x * Math.Sin(sensorRotation) + y * Math.Cos(sensorRotation);
                }
            }

            // Fit a plane through the 4 corners using least squares
            // Plane equation: z = A*x + B*y + C
            // We'll use the first 3 points to define the plane exactly,
            // and the 4th point will be fitted as best as possible
            double cornerTLx = corners[0][0], cornerTLy = corners[0][1], cornerTLz = desiredPlane.ImagePlaneTopLeftZ;
            double cornerTRx = corners[1][0], cornerTRy = corners[1][1], cornerTRz = desiredPlane.ImagePlaneTopRightZ;
            double cornerBLx = corners[2][0], cornerBLy = corners[2][1], cornerBLz = desiredPlane.ImagePlaneBottomLeftZ;
            double cornerBRx = corners[3][0], cornerBRy = corners[3][1], cornerBRz = desiredPlane.ImagePlaneBottomRightZ;

            // Use all 4 points for least squares fit
            // Build normal equations: [sum(x²)  sum(xy)  sum(x) ] [A]   [sum(xz)]
            //                         [sum(xy) sum(y²)  sum(y) ] [B] = [sum(yz)]
            //                         [sum(x)  sum(y)   4      ] [C]   [sum(z) ]
            double sumX2 = cornerTLx * cornerTLx + cornerTRx * cornerTRx + cornerBLx * cornerBLx + cornerBRx * cornerBRx;
            double sumY2 = cornerTLy * cornerTLy + cornerTRy * cornerTRy + cornerBLy * cornerBLy + cornerBRy * cornerBRy;
            double sumXY = cornerTLx * cornerTLy + cornerTRx * cornerTRy + cornerBLx * cornerBLy + cornerBRx * cornerBRy;
            double sumX = cornerTLx + cornerTRx + cornerBLx + cornerBRx;
            double sumY = cornerTLy + cornerTRy + cornerBLy + cornerBRy;
            double sumXZ = cornerTLx * cornerTLz + cornerTRx * cornerTRz + cornerBLx * cornerBLz + cornerBRx * cornerBRz;
            double sumYZ = cornerTLy * cornerTLz + cornerTRy * cornerTRz + cornerBLy * cornerBLz + cornerBRy * cornerBRz;
            double sumZ = cornerTLz + cornerTRz + cornerBLz + cornerBRz;

            // Solve using Cramer's rule (3x3 system)
            // Matrix form: M * [A B C]^T = [sumXZ sumYZ sumZ]^T
            double det = sumX2 * (sumY2 * 4 - sumY * sumY) -
                         sumXY * (sumXY * 4 - sumY * sumX) +
                         sumX * (sumXY * sumY - sumY2 * sumX);

            if (Math.Abs(det) < 1e-10)
            {
                return new ApplyTiltPlaneResultDTO
                {
                    Success = false,
                    Message = "Unable to solve for actuator positions: singular matrix"
                };
            }

            double detA = sumXZ * (sumY2 * 4 - sumY * sumY) -
                          sumYZ * (sumXY * 4 - sumY * sumX) +
                          sumZ * (sumXY * sumY - sumY2 * sumX);

            double detB = sumX2 * (sumYZ * 4 - sumY * sumZ) -
                          sumXY * (sumXZ * 4 - sumX * sumZ) +
                          sumX * (sumXZ * sumY - sumYZ * sumX);

            double detC = sumX2 * (sumY2 * sumZ - sumYZ * sumY) -
                          sumXY * (sumXY * sumZ - sumYZ * sumX) +
                          sumXZ * (sumXY * sumY - sumY2 * sumX);

            double A = detA / det;
            double B = detB / det;
            double C = detC / det;

            Logger.Info($"[CalculateActuatorPositions] Fitted plane: Z = {A:F6}*X + {B:F6}*Y + {C:F6}");

            // WandererETA geometry constants
            double outerRadius = desiredPlane.OuterRadius;  // Use outer radius from DTO
            double innerRadius = outerRadius / 2.0;  // For equilateral triangle, inner_radius ≈ outer_radius/2
            double a = Math.Sqrt(outerRadius * outerRadius - innerRadius * innerRadius);

            // Calculate the correction deltas needed at the three actuator locations.
            // The plane equation evaluated at each actuator's XY position gives the Z-correction
            // (in mm) that needs to be applied to achieve the desired image plane tilt.
            // P1 = (-a, innerRadius), P2 = (0, -outerRadius), P3 = (a, innerRadius)
            double delta1 = A * (-a) + B * innerRadius + C;
            double delta2 = A * 0 + B * (-outerRadius) + C;
            double delta3 = A * a + B * innerRadius + C;

            Logger.Info($"[CalculateActuatorPositions] Correction deltas (mm) - P1: {delta1:F6}, P2: {delta2:F6}, P3: {delta3:F6}");

            // New target = current position + correction delta
            double p1Final = currentP1 + delta1;
            double p2Final = currentP2 + delta2;
            double p3Final = currentP3 + delta3;

            // Save raw values before offsetting (for manual tilters, to show what was calculated)
            double p1Raw = p1Final;
            double p2Raw = p2Final;
            double p3Raw = p3Final;

            // If any position would go below 0, shift all up by the same amount
            // (preserves the relative tilt while satisfying the hardware minimum)
            double minValue = Math.Min(Math.Min(p1Final, p2Final), p3Final);
            if (minValue < 0)
            {
                double shift = Math.Abs(minValue);
                p1Final += shift;
                p2Final += shift;
                p3Final += shift;
                Logger.Debug($"[CalculateActuatorPositions] Shifted up by {shift:F6} mm to satisfy hardware minimum");
            }

            // Clamp to valid hardware range [0, 1.2] mm only if offsetting to zero is enabled
            // For manual tilters with dontOffsetToZero=true, allow negative values to indicate direction
            if (dontOffsetToZero)
            {
                p1Final = Math.Round(Math.Max(0, p1Final), 6);
                p2Final = Math.Round(Math.Max(0, p2Final), 6);
                p3Final = Math.Round(Math.Max(0, p3Final), 6);
            }
            else
            {
                p1Final = Math.Round(Math.Max(0, Math.Min(1.2, p1Final)), 6);
                p2Final = Math.Round(Math.Max(0, Math.Min(1.2, p2Final)), 6);
                p3Final = Math.Round(Math.Max(0, Math.Min(1.2, p3Final)), 6);
            }

            Logger.Info($"[CalculateActuatorPositions] Final target positions (mm) - P1: {p1Final:F6}, P2: {p2Final:F6}, P3: {p3Final:F6}");

            return new ApplyTiltPlaneResultDTO
            {
                Success = true,
                Position1 = (float)p1Final,
                Position2 = (float)p2Final,
                Position3 = (float)p3Final,
                RawPosition1 = dontOffsetToZero ? (float?)Math.Round(p1Raw, 6) : null,
                RawPosition2 = dontOffsetToZero ? (float?)Math.Round(p2Raw, 6) : null,
                RawPosition3 = dontOffsetToZero ? (float?)Math.Round(p3Raw, 6) : null,
                Message = "Actuator positions calculated successfully"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"TilterService: Error calculating actuator positions: {ex.Message}");
            return new ApplyTiltPlaneResultDTO
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
}
