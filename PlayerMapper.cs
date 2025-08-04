using static System.Enum;

namespace FPL;

public static class PlayerMapper
{
    public static Player MapRawDataToPlayer(Root rawData, int startGw, int numberOfGws)
    {
        var predictions = MapPredictions(rawData.data.predictions);
        var player = new Player
        {
            Name = rawData.webName,
            Position = (PositionEnum)rawData.data.positionId,
            CurrentPrice = rawData.data.priceInfo?.Value,
            Predictions = predictions,
            TotalPredicted = predictions.Skip(startGw - 1).Take(numberOfGws).Sum()
        };
        CalculatePlayerValue(player);
        TryParse(rawData.team.codeName, out ClubEnum a);
        player.Club = a;
        return player;
    }

    private static List<double> MapPredictions(List<Prediction> rawPredictions)
    {
        return rawPredictions?.Select(p => p.predicted_pts).ToList() ?? new List<double>();
    }


    private static void CalculatePlayerValue(Player player)
    {
        if (player.CurrentPrice is not null)
        {
            player.Value = player.TotalPredicted / (double)player.CurrentPrice;
        }
    }

    public static double CalculateTeamPoints(List<Player> players)
    {
        double sumPoints = 0;
        foreach (var player in players)
        {
            sumPoints += player.Predictions.First();
        }

        return sumPoints;
    }

    public static double? CalculateTeamPrice(List<Player> players)
    {
        double? teamPrice = 0;
        foreach (var player in players)
        {
            teamPrice += player.CurrentPrice;
        }

        return teamPrice;
    }
    public static bool IsTeamValid(List<Combination> combinations, int maxPlayersByTeam = 3)
    {
        // Optimized: Use Any with early termination and avoid creating intermediate collections
        return !combinations
            .SelectMany(p => p.Players)
            .GroupBy(p => p.Club)
            .Any(c => c.Count() > maxPlayersByTeam);
    }
    private static List<Player> SelectOptimalTeam(List<Player> players, int gw)
    {
        // Position constraints
        const int minGk = 1, maxGk = 1;
        const int minDef = 3, maxDef = 5;
        const int minMid = 2, maxMid = 5;
        const int minFwd = 1, maxFwd = 3;

        // Pre-allocate lists with estimated capacity to reduce resizing
        var gks = new List<Player>(players.Count / 4);
        var defs = new List<Player>(players.Count / 4);
        var mids = new List<Player>(players.Count / 4);
        var fwds = new List<Player>(players.Count / 4);

        // Single pass through players to group by position and sort by predicted points
        foreach (var player in players)
        {
            var points = player.Predictions[gw];
            switch (player.Position)
            {
                case PositionEnum.GK:
                    gks.Add(player);
                    break;
                case PositionEnum.DEF:
                    defs.Add(player);
                    break;
                case PositionEnum.MID:
                    mids.Add(player);
                    break;
                case PositionEnum.FWD:
                    fwds.Add(player);
                    break;
            }
        }

        // Sort each position group by predicted points (descending)
        gks.Sort((a, b) => b.Predictions[gw].CompareTo(a.Predictions[gw]));
        defs.Sort((a, b) => b.Predictions[gw].CompareTo(a.Predictions[gw]));
        mids.Sort((a, b) => b.Predictions[gw].CompareTo(a.Predictions[gw]));
        fwds.Sort((a, b) => b.Predictions[gw].CompareTo(a.Predictions[gw]));

        var bestTeam = new List<Player>(11); // Pre-allocate for 11 players
        var bestTotalPoints = 0.0;

        // Iterate through possible combinations
        for (int defCount = minDef; defCount <= maxDef; defCount++)
        {
            for (int midCount = minMid; midCount <= maxMid; midCount++)
            {
                for (int fwdCount = minFwd; fwdCount <= maxFwd; fwdCount++)
                {
                    // Check if this combination gives us exactly 11 players
                    if (1 + defCount + midCount + fwdCount == 11)
                    {
                        var currentTeam = new List<Player>(11);
                        var totalPoints = 0.0;
                        
                        // Add GK
                        if (gks.Count > 0)
                        {
                            currentTeam.Add(gks[0]);
                            totalPoints += gks[0].Predictions[gw];
                        }

                        // Add DEFs
                        for (int i = 0; i < Math.Min(defCount, defs.Count); i++)
                        {
                            currentTeam.Add(defs[i]);
                            totalPoints += defs[i].Predictions[gw];
                        }

                        // Add MIDs
                        for (int i = 0; i < Math.Min(midCount, mids.Count); i++)
                        {
                            currentTeam.Add(mids[i]);
                            totalPoints += mids[i].Predictions[gw];
                        }

                        // Add FWDs
                        for (int i = 0; i < Math.Min(fwdCount, fwds.Count); i++)
                        {
                            currentTeam.Add(fwds[i]);
                            totalPoints += fwds[i].Predictions[gw];
                        }

                        // Update best team if this combination is better
                        if (totalPoints > bestTotalPoints)
                        {
                            bestTotalPoints = totalPoints;
                            bestTeam.Clear();
                            bestTeam.AddRange(currentTeam);
                        }
                    }
                }
            }
        }

        // Fallback: if no valid combination found, return top 11 players by predicted points
        if (bestTeam.Count == 0)
        {
            var allPlayers = new List<Player>(players);
            allPlayers.Sort((a, b) => b.Predictions[gw].CompareTo(a.Predictions[gw]));
            return allPlayers.Take(11).ToList();
        }

        return bestTeam;
    }

    public static (double PredictedPoints, List<string> CaptainsByWeek, List<List<Player>> OptimalTeamsByWeek) CalculatePredictedPoints(List<Player> players, int startGw, int endGw, int? benchBoostGW = null)
    {
        int from = startGw - 1;
        int to = endGw - 1;

        double totalPoints = 0;
        double captainPoints = 0;
        var captainsByWeek = new List<string>(to - from + 1); // Pre-allocate capacity
        var optimalTeamsByWeek = new List<List<Player>>(to - from + 1); // Pre-allocate capacity
        
        // Pre-calculate optimal teams for each gameweek to avoid repeated calculations
        var optimalTeamsByGw = new Dictionary<int, List<Player>>();
        
        for (var gw = from; gw <= to; gw++)
        {
            // Determine how many players to include for this GW
            var playersToInclude = benchBoostGW.HasValue && benchBoostGW.Value == gw + 1 
                ? players // Include all 15 players for Bench Boost GW
                : GetOrCreateOptimalTeam(optimalTeamsByGw, players, gw); // Use cached or create optimal team
            
            // Single pass through players to calculate total and find captain
            var gwTotal = 0.0;
            var maxGw = double.MinValue;
            var captain = "";
            
            foreach (var player in playersToInclude)
            {
                var points = player.Predictions[gw];
                gwTotal += points;

                if (points > maxGw)
                {
                    maxGw = points;
                    captain = player.Name;
                }
            }
            
            captainsByWeek.Add(captain);
            optimalTeamsByWeek.Add(playersToInclude);
            totalPoints += gwTotal;
            captainPoints += maxGw;
        }

        return (totalPoints + captainPoints, captainsByWeek, optimalTeamsByWeek);
    }

    public static (double PredictedPoints, List<string> CaptainsByWeek, List<List<Player>> OptimalTeamsByWeek, int BestBenchBoostWeek, double BenchBoostDifference) CalculatePredictedPointsWithBestBenchBoost(List<Player> players, int startGw, int endGw)
    {
        var bestPoints = 0.0;
        var bestCaptainsByWeek = new List<string>();
        var bestOptimalTeamsByWeek = new List<List<Player>>();
        var bestBenchBoostWeek = 0; // 0 means no bench boost
        var bestBenchBoostDifference = 0.0;

        // Calculate base points without bench boost
        var noBenchBoostCalculation = CalculatePredictedPoints(players, startGw, endGw, null);
        bestPoints = noBenchBoostCalculation.PredictedPoints;
        bestCaptainsByWeek = noBenchBoostCalculation.CaptainsByWeek;
        bestOptimalTeamsByWeek = noBenchBoostCalculation.OptimalTeamsByWeek;

        // Try bench boost in each week and find the one with highest difference
        for (int bbWeek = startGw; bbWeek <= endGw; bbWeek++)
        {
            var calculation = CalculatePredictedPoints(players, startGw, endGw, bbWeek);
            var benchBoostDifference = calculation.PredictedPoints - noBenchBoostCalculation.PredictedPoints;

            if (benchBoostDifference > bestBenchBoostDifference)
            {
                bestBenchBoostDifference = benchBoostDifference;
                bestPoints = calculation.PredictedPoints;
                bestCaptainsByWeek = calculation.CaptainsByWeek;
                bestOptimalTeamsByWeek = calculation.OptimalTeamsByWeek;
                bestBenchBoostWeek = bbWeek;
            }
        }

        return (bestPoints, bestCaptainsByWeek, bestOptimalTeamsByWeek, bestBenchBoostWeek, bestBenchBoostDifference);
    }

    public static string FormatTeamByPosition(List<Player> team, int gameweek = 0)
    {
        // Group players by position and sort by predicted points within each position
        var gks = team.Where(p => p.Position == PositionEnum.GK).OrderByDescending(p => p.Predictions[gameweek]).ToList();
        var defs = team.Where(p => p.Position == PositionEnum.DEF).OrderByDescending(p => p.Predictions[gameweek]).ToList();
        var mids = team.Where(p => p.Position == PositionEnum.MID).OrderByDescending(p => p.Predictions[gameweek]).ToList();
        var fwds = team.Where(p => p.Position == PositionEnum.FWD).OrderByDescending(p => p.Predictions[gameweek]).ToList();

        var result = new List<string>();
        
        if (gks.Any()) result.Add($"GK: {string.Join(", ", gks.Select(p => p.Name))}");
        if (defs.Any()) result.Add($"DEF: {string.Join(", ", defs.Select(p => p.Name))}");
        if (mids.Any()) result.Add($"MID: {string.Join(", ", mids.Select(p => p.Name))}");
        if (fwds.Any()) result.Add($"FWD: {string.Join(", ", fwds.Select(p => p.Name))}");
        
        return string.Join(" | ", result);
    }

    private static List<Player> GetOrCreateOptimalTeam(Dictionary<int, List<Player>> cache, List<Player> players, int gw)
    {
        if (cache.TryGetValue(gw, out var cachedTeam))
        {
            return cachedTeam;
        }
        
        var optimalTeam = SelectOptimalTeam(players, gw);
        cache[gw] = optimalTeam;
        return optimalTeam;
    }
}