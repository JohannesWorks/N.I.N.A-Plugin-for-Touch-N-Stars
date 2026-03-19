using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using NINA.Core.Utility;

namespace TouchNStars.Server.Models;

public class INDIDriver
{
    public string Name { get; set; }
    public string Label { get; set; }
    public string Type { get; set; }
}

public static class INDIDriverRegistry
{
    // Base directory: ~/Documents/INDI/ (or My Documents\INDI\ on Windows)
    private static readonly string DriverDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "INDI");

    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();
    // Embedded resource name format: TouchNStars.Server.Models.indi_drivers.<type>.json
    private static string ResourceName(string driverType) =>
        $"TouchNStars.Server.Models.indi_drivers.{driverType}.json";

    public static List<INDIDriver> GetDrivers(string driverType)
    {
        EnsureDriverFile(driverType);

        var filePath = Path.Combine(DriverDirectory, $"{driverType}.json");
        if (!File.Exists(filePath))
        {
            Logger.Warning($"INDI driver file not found for type '{driverType}' at '{filePath}', returning empty list");
            return new List<INDIDriver>();
        }

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (IOException ex)
        {
            Logger.Error($"Failed to read INDI driver file '{filePath}': {ex.Message}");
            return new List<INDIDriver>();
        }

        try
        {
            var drivers = JsonConvert.DeserializeObject<List<INDIDriver>>(json);
            if (drivers == null)
            {
                Logger.Warning($"INDI driver file '{filePath}' deserialised to null (expected a JSON array)");
                return new List<INDIDriver>();
            }
            return drivers;
        }
        catch (JsonException ex)
        {
            Logger.Error($"Failed to parse INDI driver file '{filePath}': {ex.Message}");
            return new List<INDIDriver>();
        }
    }

    // Copy the shipped default file into the user data directory if it does not yet exist.
    private static void EnsureDriverFile(string driverType)
    {
        try
        {
            Directory.CreateDirectory(DriverDirectory);
        }
        catch (IOException ex)
        {
            Logger.Error($"Failed to create INDI driver directory '{DriverDirectory}': {ex.Message}");
            return;
        }

        var dest = Path.Combine(DriverDirectory, $"{driverType}.json");
        if (File.Exists(dest))
            return;

        var resourceName = ResourceName(driverType);
        using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Logger.Warning($"No embedded default found for INDI driver type '{driverType}' (resource '{resourceName}')");
            return;
        }

        try
        {
            using var fs = File.Create(dest);
            stream.CopyTo(fs);
        }
        catch (IOException ex)
        {
            Logger.Error($"Failed to seed INDI driver file '{dest}': {ex.Message}");
        }
    }
}
