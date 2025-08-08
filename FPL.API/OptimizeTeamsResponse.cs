namespace FPL.API;

public class OptimizeTeamsResponse
{
    public List<OptimizedTeam> Teams { get; set; } = new();
    public string? ElapsedImportFilter { get; set; }
    public string? ElapsedCombining { get; set; }
    public string? ElapsedScoring { get; set; }
}

public class OptimizedTeam
{
    public List<string> PlayerNames { get; set; } = new();
    public double PredictedPoints { get; set; }
    public double Price { get; set; }
    public List<List<string>> OptimalTeamsByWeek { get; set; } = new();
    public List<string> CaptainsByWeek { get; set; } = new();
    public int? BenchBoostGw { get; set; }
}


