namespace FPL;

public class Combination(List<Player> players, int startGw, int endGw)
{
    public List<string> CaptainsByWeek { get; set; }
    public List<Player> Players { get; set; } = players;
    public int NumberOfPlayers => Players.Count();
    public double Price => Players.Sum(p => (double)p.CurrentPrice!);

    public double PredictedPoints => Players.Sum(p => p.Predictions.Skip(startGw - 1).Take(endGw - startGw + 1).Sum());

    public PositionEnum Position => Players.First().Position;
    public double Value => PredictedPoints / Price;
}