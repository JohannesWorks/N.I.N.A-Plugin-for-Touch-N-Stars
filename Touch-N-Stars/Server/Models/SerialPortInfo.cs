namespace TouchNStars.Server.Models;

/// <summary>
/// Information about an available serial port
/// </summary>
public class SerialPortInfo
{
    /// <summary>
    /// The port name (e.g., "/dev/ttyUSB0" or "COM3")
    /// </summary>
    public string PortName { get; set; }

    /// <summary>
    /// The manufacturer or description of the device (e.g., "CP2102")
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Whether the port is currently available (not in use)
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Display string combining port and description
    /// </summary>
    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrEmpty(Description) ? PortName : $"{PortName} - {Description}";
            if (!IsAvailable)
                name += " (in use)";
            return name;
        }
    }
}

