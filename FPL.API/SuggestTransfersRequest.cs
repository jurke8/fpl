namespace FPL.API;

public class SuggestTransfersRequest
{
    public required List<string> PlayerNames { get; set; } // exactly 15 names
    public int FreeTransfers { get; set; } // number of transfers available (>= 0)
    public int CurrentGameweek { get; set; } // 1..38
    public int LookaheadGameweeks { get; set; } // >= 1
    public List<string>? BanList { get; set; } // optional list of player names that must not be suggested as transfers in
    public List<string>? LockedPlayers { get; set; } // optional list of player names that are locked in and should not be transferred out
}


