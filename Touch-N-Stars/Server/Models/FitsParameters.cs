namespace TouchNStars.Server.Models;

public class FitsParameters {
    public bool Success { get; set; }
    public bool HasWcs { get; set; }
    public bool HasCoordinates { get; set; }
    public double FocalLength { get; set; }
    public double PixelSize { get; set; }
    public int Binning { get; set; }
    public double? Ra { get; set; }
    public double? Dec { get; set; }
    public string RaString { get; set; }
    public string DecString { get; set; }
    public string Error { get; set; }
}
