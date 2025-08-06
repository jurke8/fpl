namespace FPL.API;

/// <summary>
/// Request model for team points calculation
/// </summary>
public class TeamPointsRequest
{
    /// <summary>
    /// List of player names to calculate team points for
    /// </summary>
    public required List<string> PlayerNames { get; set; }
    
    /// <summary>
    /// Starting gameweek (inclusive)
    /// </summary>
    public int StartGameweek { get; set; }
    
    /// <summary>
    /// Ending gameweek (inclusive)
    /// </summary>
    public int EndGameweek { get; set; }
} 