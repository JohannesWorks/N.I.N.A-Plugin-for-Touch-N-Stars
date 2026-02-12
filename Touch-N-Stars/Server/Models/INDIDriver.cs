using System.Collections.Generic;

namespace TouchNStars.Server.Models;

public class INDIDriver
{
    public string Name { get; set; }
    public string Label { get; set; }
    public string Type { get; set; }
}

public static class INDIFocusDrivers
{
    public static readonly List<INDIDriver> Drivers = new()
    {
        new INDIDriver { Name = "indi_celestron_sct_focus", Label = "Celestron SCT Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_deepskydad_af1_focus", Label = "DeepSkyDad AF1 Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_deepskydad_af2_focus", Label = "DeepSkyDad AF2 Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_deepskydad_af3_focus", Label = "DeepSkyDad AF3 Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_dmfc_focus", Label = "DMFC Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_esattoarco_focus", Label = "Esatto Arco Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_esatto_focus", Label = "Esatto Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_fcusb_focus", Label = "FocusLynx USB Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_gemini_focus", Label = "Gemini Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_hitecastrodc_focus", Label = "HitecAstro DC Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_lacerta_mfoc_fmc_focus", Label = "Lacerta MFoc FMC Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_lacerta_mfoc_focus", Label = "Lacerta MFoc Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_microtouch_focus", Label = "Microtouch Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_moonlite_focus", Label = "Moonlite Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_moonlitedro_focus", Label = "Moonlite DRO Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_myfocuserpro2_focus", Label = "MyFocuserPro2 Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_pegasus_focuscube", Label = "Pegasus FocusCube", Type = "focuser" },
        new INDIDriver { Name = "indi_pegasus_focuscube3", Label = "Pegasus FocusCube 3", Type = "focuser" },
        new INDIDriver { Name = "indi_qhy_focuser", Label = "QHY Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_robo_focus", Label = "RoboFocus Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_sestosenso_focus", Label = "Sesto Senso Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_sestosenso2_focus", Label = "Sesto Senso 2 Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_simulator_focus", Label = "Simulator Focuser", Type = "focuser" },
        new INDIDriver { Name = "indi_teenastro_focus", Label = "TeenAstro Focuser", Type = "focuser" },
    };
}

public static class INDIRotatorDrivers
{
    public static readonly List<INDIDriver> Drivers = new()
    {
        new INDIDriver { Name = "indi_asi_rotator", Label = "ZWO ASI Rotator", Type = "rotator" },
        new INDIDriver { Name = "indi_falcon_rotator", Label = "Falcon Rotator", Type = "rotator" },
        new INDIDriver { Name = "indi_falconv2_rotator", Label = "Falcon v2 Rotator", Type = "rotator" },
        new INDIDriver { Name = "indi_simulator_rotator", Label = "Simulator Rotator", Type = "rotator" },
    };
}

public static class INDIFilterWheelDrivers
{
    public static readonly List<INDIDriver> Drivers = new()
    {
        new INDIDriver { Name = "indi_pegasusindigo_wheel", Label = "Pegasus Indigo Filter Wheel", Type = "filterwheel" },
        new INDIDriver { Name = "indi_simulator_wheel", Label = "Simulator Filter Wheel", Type = "filterwheel" },
    };
}

public static class INDIMountDrivers
{
    public static readonly List<INDIDriver> Drivers = new()
    {
        new INDIDriver { Name = "indi_azgti_telescope", Label = "AZ-GTi Telescope", Type = "telescope" },
        new INDIDriver { Name = "indi_eq500x_telescope", Label = "EQ500x Telescope", Type = "telescope" },
        new INDIDriver { Name = "indi_eqmod_telescope", Label = "EQMod Telescope", Type = "telescope" },
        new INDIDriver { Name = "indi_ioptronv3_telescope", Label = "iOptron v3 Telescope", Type = "telescope" },
        new INDIDriver { Name = "indi_paramount_telescope", Label = "Paramount Telescope", Type = "telescope" },
        new INDIDriver { Name = "indi_simulator_telescope", Label = "Simulator Telescope", Type = "telescope" },
        new INDIDriver { Name = "indi_staradventurer2i_telescope", Label = "Star Adventurer 2i Telescope", Type = "telescope" },
        new INDIDriver { Name = "indi_staradventurergti_telescope", Label = "Star Adventurer GTi Telescope", Type = "telescope" },
        new INDIDriver { Name = "indi_synscanlegacy_telescope", Label = "SynScan Legacy Telescope", Type = "telescope" },
        new INDIDriver { Name = "indi_synscan_telescope", Label = "SynScan Telescope", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200_10micron", Label = "LX200 10Micron", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200am5", Label = "LX200 AM5", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200basic", Label = "LX200 Basic", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200classic", Label = "LX200 Classic", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200fs2", Label = "LX200 FS2", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200gemini", Label = "LX200 Gemini", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200generic", Label = "LX200 Generic", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200_OnStep", Label = "LX200 OnStep", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200_OpenAstroTech", Label = "LX200 OpenAstroTech", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200_pegasus_nyx101", Label = "LX200 Pegasus NYX101", Type = "telescope" },
        new INDIDriver { Name = "indi_lx200_TeenAstro", Label = "LX200 TeenAstro", Type = "telescope" },
        new INDIDriver { Name = "indi_skywatcherAltAzMount", Label = "Skywatcher AltAz", Type = "telescope" },
    };
}

public static class INDIWeatherDrivers
{
    public static readonly List<INDIDriver> Drivers = new()
    {
        new INDIDriver { Name = "indi_mbox_weather", Label = "MBox Weather", Type = "weather" },
        new INDIDriver { Name = "indi_simulator_weather", Label = "Simulator Weather", Type = "weather" },
        new INDIDriver { Name = "indi_sqm_weather", Label = "SQM Weather", Type = "weather" },
        new INDIDriver { Name = "indi_uranus_weather", Label = "Uranus Weather", Type = "weather" },
    };
}

public static class INDISwitchDrivers
{
    public static readonly List<INDIDriver> Drivers = new()
    {
        new INDIDriver { Name = "indi_celestron_dewpower", Label = "Celestron Dew Power", Type = "switches" },
        new INDIDriver { Name = "indi_pegasus_ppb", Label = "Pegasus PPB", Type = "switches" },
        new INDIDriver { Name = "indi_pegasus_ppba", Label = "Pegasus PPB Advanced", Type = "switches" },
        new INDIDriver { Name = "indi_pegasus_spb", Label = "Pegasus SPB", Type = "switches" },
        new INDIDriver { Name = "indi_pegasus_upb", Label = "Pegasus UPB", Type = "switches" },
        new INDIDriver { Name = "indi_wanderer_dew_terminator", Label = "Wanderer Dew Terminator", Type = "switches" },
    };
}

public static class INDIFlatPanelDrivers
{
    public static readonly List<INDIDriver> Drivers = new()
    {
        new INDIDriver { Name = "indi_deepskydata_fp", Label = "DeepSkyDad Flat Panel", Type = "flatpanel" },
        new INDIDriver { Name = "indi_gemini_flatpanel", Label = "Gemini Flat Panel", Type = "flatpanel" },
        new INDIDriver { Name = "indi_simulator_lightpanel", Label = "Simulator Light Panel", Type = "flatpanel" },
        new INDIDriver { Name = "indi_wanderer_cover", Label = "Wanderer Cover", Type = "flatpanel" },
    };
}
