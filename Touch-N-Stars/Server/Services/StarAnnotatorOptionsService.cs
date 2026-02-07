using NINA.Core.Utility;
using System;
using System.Drawing;
using System.Reflection;
using TouchNStars.Server.Models;

namespace TouchNStars.Server.Services;

/// <summary>
/// Service to manage StarAnnotatorOptions and provide API access
/// </summary>
public class StarAnnotatorOptionsService
{

    private static string ColorToHex(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static Color HexToColor(string hex)
    {
        try
        {
            hex = hex.Replace("#", "");
            if (hex.Length == 8)
            {
                byte a = (byte)Convert.ToUInt32(hex.Substring(0, 2), 16);
                byte r = (byte)Convert.ToUInt32(hex.Substring(2, 2), 16);
                byte g = (byte)Convert.ToUInt32(hex.Substring(4, 2), 16);
                byte b = (byte)Convert.ToUInt32(hex.Substring(6, 2), 16);
                return Color.FromArgb(a, r, g, b);
            }
            else
            {
                return ColorTranslator.FromHtml(hex);
            }
        }
        catch
        {
            return Color.White;
        }
    }

    /// <summary>
    /// Get current StarAnnotatorOptions as a DTO
    /// </summary>
    public static StarAnnotatorOptionsDto GetOptions()
    {
        try
        {
            var options = GetHocusFocusStarAnnotatorOptions();
            if (options == null)
            {
                Logger.Warning("StarAnnotatorOptions is null");
                return GetDefaultOptions();
            }

            return new StarAnnotatorOptionsDto
            {
                ShowAnnotations = (bool)GetPropertyValue(options, "ShowAnnotations", true),
                ShowAllStars = (bool)GetPropertyValue(options, "ShowAllStars", true),
                MaxStars = (int)GetPropertyValue(options, "MaxStars", 200),
                ShowStarBounds = (bool)GetPropertyValue(options, "ShowStarBounds", true),
                StarBoundsType = GetPropertyValue(options, "StarBoundsType", "Ellipse").ToString(),
                StarBoundsColor = ColorToHex(GetColorPropertyValue(options, "StarBoundsColor")),
                ShowAnnotationType = GetPropertyValue(options, "ShowAnnotationType", "HFR").ToString(),
                AnnotationColor = ColorToHex(GetColorPropertyValue(options, "AnnotationColor")),
                AnnotationFontFamily = GetFontFamilyName(GetPropertyValue(options, "AnnotationFontFamily", null)),
                AnnotationFontSizePoints = (float)GetPropertyValue(options, "AnnotationFontSizePoints", 18f),
                ShowROI = (bool)GetPropertyValue(options, "ShowROI", false),
                ROIColor = ColorToHex(GetColorPropertyValue(options, "ROIColor")),
                ShowStarCenter = (bool)GetPropertyValue(options, "ShowStarCenter", true),
                StarCenterColor = ColorToHex(GetColorPropertyValue(options, "StarCenterColor")),
                ShowStructureMap = GetPropertyValue(options, "ShowStructureMap", "None").ToString(),
                StructureMapColor = ColorToHex(GetColorPropertyValue(options, "StructureMapColor")),
                TooFlatColor = ColorToHex(GetColorPropertyValue(options, "TooFlatColor")),
                SaturatedColor = ColorToHex(GetColorPropertyValue(options, "SaturatedColor")),
                LowSensitivityColor = ColorToHex(GetColorPropertyValue(options, "LowSensitivityColor")),
                NotCenteredColor = ColorToHex(GetColorPropertyValue(options, "NotCenteredColor")),
                DegenerateColor = ColorToHex(GetColorPropertyValue(options, "DegenerateColor")),
                TooDistortedColor = ColorToHex(GetColorPropertyValue(options, "TooDistortedColor")),
                ShowTooDistorted = (bool)GetPropertyValue(options, "ShowTooDistorted", false),
                ShowDegenerate = (bool)GetPropertyValue(options, "ShowDegenerate", false),
                ShowSaturated = (bool)GetPropertyValue(options, "ShowSaturated", false),
                ShowLowSensitivity = (bool)GetPropertyValue(options, "ShowLowSensitivity", false),
                ShowNotCentered = (bool)GetPropertyValue(options, "ShowNotCentered", false),
                ShowTooFlat = (bool)GetPropertyValue(options, "ShowTooFlat", false),
            };
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting StarAnnotatorOptions", ex);
            return GetDefaultOptions();
        }
    }

    /// <summary>
    /// Set StarAnnotatorOptions from a DTO
    /// </summary>
    public static bool SetOptions(StarAnnotatorOptionsDto dto)
    {
        try
        {
            var options = GetHocusFocusStarAnnotatorOptions();
            if (options == null)
            {
                Logger.Warning("StarAnnotatorOptions is null");
                return false;
            }

            SetPropertyValue(options, "ShowAnnotations", dto.ShowAnnotations);
            SetPropertyValue(options, "ShowAllStars", dto.ShowAllStars);
            SetPropertyValue(options, "MaxStars", dto.MaxStars);
            SetPropertyValue(options, "ShowStarBounds", dto.ShowStarBounds);
            SetPropertyValue(options, "StarBoundsType", ConvertStringToEnum(options, "StarBoundsType", dto.StarBoundsType));
            SetColorPropertyValue(options, "StarBoundsColor", HexToColor(dto.StarBoundsColor));
            SetPropertyValue(options, "ShowAnnotationType", ConvertStringToEnum(options, "ShowAnnotationType", dto.ShowAnnotationType));
            SetColorPropertyValue(options, "AnnotationColor", HexToColor(dto.AnnotationColor));
            SetPropertyValue(options, "AnnotationFontFamily", CreateFontFamily(dto.AnnotationFontFamily));
            SetPropertyValue(options, "AnnotationFontSizePoints", dto.AnnotationFontSizePoints);
            SetPropertyValue(options, "ShowROI", dto.ShowROI);
            SetColorPropertyValue(options, "ROIColor", HexToColor(dto.ROIColor));
            SetPropertyValue(options, "ShowStarCenter", dto.ShowStarCenter);
            SetColorPropertyValue(options, "StarCenterColor", HexToColor(dto.StarCenterColor));
            SetPropertyValue(options, "ShowStructureMap", ConvertStringToEnum(options, "ShowStructureMap", dto.ShowStructureMap));
            SetColorPropertyValue(options, "StructureMapColor", HexToColor(dto.StructureMapColor));
            SetColorPropertyValue(options, "TooFlatColor", HexToColor(dto.TooFlatColor));
            SetColorPropertyValue(options, "SaturatedColor", HexToColor(dto.SaturatedColor));
            SetColorPropertyValue(options, "LowSensitivityColor", HexToColor(dto.LowSensitivityColor));
            SetColorPropertyValue(options, "NotCenteredColor", HexToColor(dto.NotCenteredColor));
            SetColorPropertyValue(options, "DegenerateColor", HexToColor(dto.DegenerateColor));
            SetColorPropertyValue(options, "TooDistortedColor", HexToColor(dto.TooDistortedColor));
            SetPropertyValue(options, "ShowTooDistorted", dto.ShowTooDistorted);
            SetPropertyValue(options, "ShowDegenerate", dto.ShowDegenerate);
            SetPropertyValue(options, "ShowSaturated", dto.ShowSaturated);
            SetPropertyValue(options, "ShowLowSensitivity", dto.ShowLowSensitivity);
            SetPropertyValue(options, "ShowNotCentered", dto.ShowNotCentered);
            SetPropertyValue(options, "ShowTooFlat", dto.ShowTooFlat);

            Logger.Info("StarAnnotatorOptions updated successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Error setting StarAnnotatorOptions", ex);
            return false;
        }
    }

    /// <summary>
    /// Reset options to defaults
    /// </summary>
    public static bool ResetToDefaults()
    {
        try
        {
            var options = GetHocusFocusStarAnnotatorOptions();
            var resetMethod = options?.GetType().GetMethod("ResetDefaults", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (resetMethod != null)
            {
                resetMethod.Invoke(options, null);
                Logger.Info("StarAnnotatorOptions reset to defaults");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("Error resetting StarAnnotatorOptions to defaults", ex);
            return false;
        }
    }

    private static StarAnnotatorOptionsDto GetDefaultOptions()
    {
        return new StarAnnotatorOptionsDto
        {
            ShowAnnotations = true,
            ShowAllStars = true,
            MaxStars = 200,
            ShowStarBounds = true,
            StarBoundsType = "Ellipse",
            StarBoundsColor = "#80FF0000",
            ShowAnnotationType = "HFR",
            AnnotationColor = "#FFFF0000",
            AnnotationFontFamily = "Arial",
            AnnotationFontSizePoints = 18,
            ShowROI = false,
            ROIColor = "#FF00FF00",
            ShowStarCenter = true,
            StarCenterColor = "#FFFF00FF",
            ShowStructureMap = "None",
            StructureMapColor = "#FF0000FF",
            TooFlatColor = "#FFFF6600",
            SaturatedColor = "#FFFF0000",
            LowSensitivityColor = "#FF0066FF",
            NotCenteredColor = "#FF00FF99",
            DegenerateColor = "#FF9900FF",
            TooDistortedColor = "#FFCCCCCC",
            ShowTooDistorted = false,
            ShowDegenerate = false,
            ShowSaturated = false,
            ShowLowSensitivity = false,
            ShowNotCentered = false,
            ShowTooFlat = false,
        };
    }

    /// <summary>
    /// Get HocusFocus StarAnnotatorOptions via reflection
    /// </summary>
    private static object GetHocusFocusStarAnnotatorOptions()
    {
        try
        {
            var hocusFocusPluginType = Type.GetType("NINA.Joko.Plugins.HocusFocus.HocusFocusPlugin, NINA.Joko.Plugins.HocusFocus");
            if (hocusFocusPluginType == null)
            {
                Logger.Warning("HocusFocusPlugin type not found");
                return null;
            }

            var optionsProperty = hocusFocusPluginType.GetProperty("StarAnnotatorOptions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (optionsProperty == null)
            {
                Logger.Warning("StarAnnotatorOptions property not found");
                return null;
            }

            return optionsProperty.GetValue(null);
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting HocusFocusPlugin.StarAnnotatorOptions via reflection", ex);
            return null;
        }
    }

    /// <summary>
    /// Get a property value from an object via reflection
    /// </summary>
    private static object GetPropertyValue(object obj, string propertyName, object defaultValue)
    {
        try
        {
            var property = obj?.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property != null && property.CanRead)
            {
                return property.GetValue(obj) ?? defaultValue;
            }
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Get a Color property value via reflection
    /// </summary>
    private static Color GetColorPropertyValue(object obj, string propertyName)
    {
        try
        {
            var property = obj?.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property != null && property.CanRead)
            {
                var value = property.GetValue(obj);
                if (value is Color color)
                {
                    return color;
                }
            }
            return Color.White;
        }
        catch
        {
            return Color.White;
        }
    }

    /// <summary>
    /// Set a property value on an object via reflection
    /// </summary>
    private static void SetPropertyValue(object obj, string propertyName, object value)
    {
        try
        {
            var property = obj?.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(obj, value);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Could not set property {propertyName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Set a Color property value via reflection
    /// </summary>
    private static void SetColorPropertyValue(object obj, string propertyName, Color value)
    {
        try
        {
            var property = obj?.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(obj, value);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Could not set color property {propertyName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Convert a string to an enum value via reflection
    /// </summary>
    private static object ConvertStringToEnum(object optionsObj, string propertyName, string enumValue)
    {
        try
        {
            var property = optionsObj?.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property != null && property.PropertyType.IsEnum)
            {
                return Enum.Parse(property.PropertyType, enumValue, ignoreCase: true);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create a FontFamily object via reflection
    /// </summary>
    private static object CreateFontFamily(string familyName)
    {
        try
        {
            var fontFamilyType = Type.GetType("System.Windows.Media.FontFamily, PresentationCore");
            if (fontFamilyType != null)
            {
                var constructor = fontFamilyType.GetConstructor(new[] { typeof(string) });
                if (constructor != null)
                {
                    return constructor.Invoke(new[] { familyName ?? "Arial" });
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get the font family name via reflection
    /// </summary>
    private static string GetFontFamilyName(object fontFamily)
    {
        try
        {
            if (fontFamily == null)
            {
                return "Arial";
            }
            var sourceProperty = fontFamily.GetType().GetProperty("Source", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (sourceProperty != null)
            {
                return sourceProperty.GetValue(fontFamily) as string ?? "Arial";
            }
            return "Arial";
        }
        catch
        {
            return "Arial";
        }
    }
}