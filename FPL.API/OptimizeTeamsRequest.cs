namespace FPL.API;

public class OptimizeTeamsRequest
{
    public int StartGw { get; set; } = 1;
    public int EndGw { get; set; } = 3;

    public int Complexity { get; set; } = 80;
    public int MaxPlayersByTeam { get; set; } = 3;

    public double MinTeamPrice { get; set; } = 99;
    public int MaxTeamPrice { get; set; } = 100;

    public bool CalculateBenchBoost { get; set; } = false;

    public int TopTeams { get; set; } = 5;

    public List<string>? IncludeList { get; set; }
    public List<string>? BanList { get; set; }
    // name -> position (GK, DEF, MID, FWD)
    public Dictionary<string, string>? LockedPlayers { get; set; }
}


