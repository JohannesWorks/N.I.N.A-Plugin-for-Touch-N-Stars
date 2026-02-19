namespace TouchNStars.Server.Models;

/// <summary>
/// PowerBox device information
/// </summary>
public class PowerBoxInfo
{
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Id { get; set; }
    public string UniqueId { get; set; }
    public string Firmware { get; set; }
    public string DriverVersion { get; set; }
    public string UpTimeFormatted { get; set; }
    public bool Connected { get; set; }
    public double CoreTemp { get; set; }
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public double DewPoint { get; set; }
    public double Supply5A { get; set; }
    public double Supply5W { get; set; }
    public double AverageAmps { get; set; }
    public int UpdateRate { get; set; }
    public int EnvUpdateRate { get; set; }
    public double TemperatureOffset { get; set; }
    public double HumidityOffset { get; set; }
    public bool ExtSensor { get; set; }
    public bool HasWifi { get; set; }
    public PowerSupplyInfo PowerSupply { get; set; }
    public PowerPortsInfo PowerPorts { get; set; }
    public PowerPortsInfo USBPorts { get; set; }
    public DewPortsInfo DewPorts { get; set; }
    public BuckPortsInfo BuckPorts { get; set; }
    public PWMPortsInfo PWMPorts { get; set; }
    public WiFiInfo WiFi { get; set; }
}

/// <summary>
/// PowerBox power supply information
/// </summary>
public class PowerSupplyInfo
{
    public double VoltageIn { get; set; }
    public double VoltageOut { get; set; }
    public double Current { get; set; }
    public double Power { get; set; }
    public int Efficiency { get; set; }
    public double Temperature { get; set; }
    public bool IsStabilized { get; set; }
}

/// <summary>
/// PowerBox power ports information
/// </summary>
public class PowerPortsInfo
{
    public int MaxPorts { get; set; }
    public PortInfo[] Ports { get; set; }
}

/// <summary>
/// Single power port information
/// </summary>
public class PortInfo
{
    public int Index { get; set; }
    public string Name { get; set; }
    public double Voltage { get; set; }
    public double Current { get; set; }
    public bool Enabled { get; set; }
    public bool BootState { get; set; }
    public bool Overcurrent { get; set; }
    public bool ReadOnly { get; set; }
}

/// <summary>
/// PowerBox dew ports information
/// </summary>
public class DewPortsInfo
{
    public int MaxPorts { get; set; }
    public DewPortInfo[] Ports { get; set; }
}

/// <summary>
/// Single dew port information
/// </summary>
public class DewPortInfo
{
    public int Index { get; set; }
    public string Name { get; set; }
    public double Current { get; set; }
    public bool Enabled { get; set; }
    public int Resolution { get; set; }
    public int PowerLevel { get; set; }
    public int SetPower { get; set; }
    public bool AutoMode { get; set; }
    public double AutoThreshold { get; set; }
    public double Probe { get; set; }
    public bool Overcurrent { get; set; }
}

/// <summary>
/// PowerBox buck converter ports information
/// </summary>
public class BuckPortsInfo
{
    public int MaxPorts { get; set; }
    public BuckPortInfo[] Ports { get; set; }
}

/// <summary>
/// Single buck converter port information
/// </summary>
public class BuckPortInfo
{
    public int Index { get; set; }
    public string Name { get; set; }
    public double Voltage { get; set; }
    public double SetVoltage { get; set; }
    public double Current { get; set; }
    public bool Enabled { get; set; }
    public double MaxVoltage { get; set; }
    public double MinVoltage { get; set; }
    public bool Overcurrent { get; set; }
}

/// <summary>
/// PowerBox PWM ports information
/// </summary>
public class PWMPortsInfo
{
    public int MaxPorts { get; set; }
    public PWMPortInfo[] Ports { get; set; }
}

/// <summary>
/// Single PWM port information
/// </summary>
public class PWMPortInfo
{
    public int Index { get; set; }
    public string Name { get; set; }
    public int Power { get; set; }
    public int SetPower { get; set; }
    public double Current { get; set; }
    public bool Enabled { get; set; }
    public int Resolution { get; set; }
    public bool Overcurrent { get; set; }
}

/// <summary>
/// PowerBox WiFi information
/// </summary>
public class WiFiInfo
{
    public string SSID { get; set; }
    public string IP { get; set; }
    public string Hostname { get; set; }
    public string Mode { get; set; }
}
