namespace FPL.API;

/// <summary>
/// Response model for team points calculation
/// </summary>
public class TeamPointsResponse
{
    /// <summary>
    /// Original list of player names requested
    /// </summary>
    public List<string> PlayerNames { get; set; } = new();
    
    /// <summary>
    /// Starting gameweek
    /// </summary>
    public int StartGameweek { get; set; }
    
    /// <summary>
    /// Ending gameweek
    /// </summary>
    public int EndGameweek { get; set; }
    
    /// <summary>
    /// List of total points for each gameweek in the range
    /// </summary>
    public List<double> GameweekPoints { get; set; } = new();
    
    /// <summary>
    /// Total points across all gameweeks
    /// </summary>
    public double TotalPoints { get; set; }
    
    /// <summary>
    /// List of player names that were not found in the database
    /// </summary>
    public List<string> NotFoundPlayers { get; set; } = new();
    
    /// <summary>
    /// Number of players successfully found and included in calculations
    /// </summary>
    public int FoundPlayersCount { get; set; }

    public List<List<string>> TeamByWeek { get; set; } = new();
    public List<string> CaptainsByWeek { get; set; } = new();
    public int BbGw { get; set; } = 0;
} 