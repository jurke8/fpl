namespace FPL.API;

/// <summary>
/// Response model for comparing two players
/// </summary>
public class ComparePlayersResponse
{
    public required PlayerComparison Player1 { get; init; }
    public required PlayerComparison Player2 { get; init; }
    public required List<double> GameweekDelta { get; init; } // Player1 - Player2 per GW
    public double TotalDelta { get; init; } // Sum of deltas
}

public class PlayerComparison
{
    public string Name { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string Club { get; init; } = string.Empty;
    public double? CurrentPrice { get; init; }
    public double Value { get; init; }
    public List<double> GameweekPoints { get; init; } = new();
    public double TotalPoints { get; init; }
}


