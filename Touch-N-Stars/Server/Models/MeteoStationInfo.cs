namespace TouchNStars.Server.Models;

/// <summary>
/// MeteoStation device information
/// </summary>
public class MeteoStationInfo
{
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Id { get; set; }
    public string UniqueId { get; set; }
    public string Firmware { get; set; }
    public string DriverVersion { get; set; }
    public string UpTimeFormatted { get; set; }
    public bool Connected { get; set; }
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public double DewPoint { get; set; }
    public double SkyBrightness { get; set; }
    public double SkyQuality { get; set; }
    public double SkyTemperature { get; set; }
    public double CloudCover { get; set; }
    public int UpdateRate { get; set; }
    public double TemperatureOffset { get; set; }
    public double HumidityOffset { get; set; }
    public double LuxScalingFactor { get; set; }
    public CloudModelInfo CloudModel { get; set; }
}

/// <summary>
/// MeteoStation cloud detection model configuration
/// </summary>
public class CloudModelInfo
{
    public int CloudK1 { get; set; }
    public int CloudK2 { get; set; }
    public int CloudK3 { get; set; }
    public int CloudK4 { get; set; }
    public int CloudK5 { get; set; }
    public int CloudK6 { get; set; }
    public int CloudK7 { get; set; }
    public int CloudFlagPercent { get; set; }
    public int CloudTemperatureOvercast { get; set; }
}
