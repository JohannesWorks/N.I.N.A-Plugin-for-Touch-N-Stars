namespace TouchNStars.Server.Models;

public class FitsSolveResult {
    public bool Success { get; set; }
    public double Ra { get; set; }
    public double Dec { get; set; }
    public string RaString { get; set; }
    public string DecString { get; set; }
    public double Rotation { get; set; }
    public double PixelScale { get; set; }
    public bool SolvedFromWcs { get; set; }
    public string Error { get; set; }
}
