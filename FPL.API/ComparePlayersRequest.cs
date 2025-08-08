namespace FPL.API;

/// <summary>
/// Request model for comparing two players across a gameweek range
/// </summary>
public class ComparePlayersRequest
{
    /// <summary>
    /// First player's name (case-insensitive, use webName)
    /// </summary>
    public required string Player1Name { get; set; }

    /// <summary>
    /// Second player's name (case-insensitive, use webName)
    /// </summary>
    public required string Player2Name { get; set; }

    /// <summary>
    /// Starting gameweek (inclusive)
    /// </summary>
    public int StartGameweek { get; set; }

    /// <summary>
    /// Ending gameweek (inclusive)
    /// </summary>
    public int EndGameweek { get; set; }
}


