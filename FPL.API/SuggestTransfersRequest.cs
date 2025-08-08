namespace FPL.API;

public class SuggestTransfersRequest
{
    public required List<string> PlayerNames { get; set; } // exactly 15 names
    public int FreeTransfers { get; set; } // number of transfers available (>= 0)
    public int CurrentGameweek { get; set; } // 1..38
    public int LookaheadGameweeks { get; set; } // >= 1
}


