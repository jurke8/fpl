namespace FPL.API;

public class OptimizationProgress
{
    public string Stage { get; set; } = string.Empty; // e.g., Import, Filtering, Combining, Scoring
    public string Message { get; set; } = string.Empty;
    public double? Percent { get; set; }
}


