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
    public List<string>? NotFoundPlayers { get; set; }

    /// <summary>
    /// Total team value (sum of all player prices)
    /// </summary>
    public double? TeamValue { get; set; }

    public List<List<string>> TeamByWeek { get; set; } = new();
    public List<string> CaptainsByWeek { get; set; } = new();
    public int BbGw { get; set; } = 0;

    /// <summary>
    /// Optional: Comparison data when two teams are provided
    /// </summary>
    public TeamPointsResponse? Opponent { get; set; }

    /// <summary>
    /// Optional: Differences between the primary team and the opponent team (primary - opponent)
    /// </summary>
    public TeamPointsDifference? Difference { get; set; }
} 

/// <summary>
/// Model for differences between two team points results
/// </summary>
public class TeamPointsDifference
{
    /// <summary>
    /// Gameweek-by-gameweek difference (Team1 - Team2)
    /// </summary>
    public List<double> GameweekPointsDelta { get; set; } = new();

    /// <summary>
    /// Total points difference (Team1 - Team2)
    /// </summary>
    public double TotalPointsDelta { get; set; }

    /// <summary>
    /// Team value difference (Team1 - Team2)
    /// </summary>
    public double? TeamValueDelta { get; set; }
}