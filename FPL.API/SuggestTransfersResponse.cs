namespace FPL.API;

public class SuggestTransfersResponse
{
    public required List<string> OriginalTeam { get; set; } // input team
    public required List<TransferSuggestion> Suggestions { get; set; } // ordered list of suggested transfers to apply (<= FreeTransfers)
    public required List<string> FinalTeam { get; set; } // team after applying suggestions
    public double OriginalProjectedPoints { get; set; } // across lookahead
    public double FinalProjectedPoints { get; set; }
    public double ProjectedGain => Math.Round(FinalProjectedPoints - OriginalProjectedPoints, 2);
}

public class TransferSuggestion
{
    public required string Out { get; set; }
    public required string In { get; set; }
    public double DeltaPoints { get; set; } // gain across the lookahead horizon
}


