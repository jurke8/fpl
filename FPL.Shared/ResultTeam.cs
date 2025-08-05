namespace FPL.Shared;

public class ResultTeam
{
    public List<List<Player>> OptimalTeamsByWeek { get; set; }
    public List<string> CaptainsByWeek { get; set; }
    public double PredictedPoints { get; set; }
    public List<double> BenchPoints { get; set; }
    public int BbGw { get; set; }
}