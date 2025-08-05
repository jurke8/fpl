namespace FPL.API;

/// <summary>
/// Response model for player data
/// </summary>
public class PlayerResponse
{
    public string Name { get; set; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string Club { get; set; } = string.Empty;
    public double? CurrentPrice {get;set;}
    public double TotalPredictionPoints { get; set; }
    public double Value { get; init; }
}