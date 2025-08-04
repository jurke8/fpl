namespace FPL;

public class Player
{
    public required string Name { get; set; }
    public PositionEnum Position { get; set; }
    public ClubEnum Club { get; set; }
    public double? CurrentPrice { get; set; }
    public required List<double> Predictions { get; set; }
    public double Value { get; set; }
    public double TotalPredicted { get; set; }
}





