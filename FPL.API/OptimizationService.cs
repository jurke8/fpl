using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using FPL;

namespace FPL.API;

public interface IOptimizationService
{
    Task<string> StartOptimizationAsync(OptimizeTeamsRequest request, CancellationToken ct = default);
    IAsyncEnumerable<OptimizationProgress> StreamProgressAsync(string jobId, CancellationToken ct = default);
    Task<OptimizeTeamsResponse?> GetResultAsync(string jobId);
}

public class OptimizationService : IOptimizationService
{
    private readonly List<Root> rawPlayers;

    private readonly ConcurrentDictionary<string, Channel<OptimizationProgress>> progressByJob = new();
    private readonly ConcurrentDictionary<string, OptimizeTeamsResponse> resultByJob = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> ctsByJob = new();

    public OptimizationService(List<Root> rawPlayers)
    {
        this.rawPlayers = rawPlayers;
    }

    public Task<string> StartOptimizationAsync(OptimizeTeamsRequest request, CancellationToken ct = default)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var channel = Channel.CreateUnbounded<OptimizationProgress>();
        progressByJob[jobId] = channel;

        // Create a job-scoped CTS so the job is NOT tied to the HTTP request cancellation
        var jobCts = new CancellationTokenSource();
        ctsByJob[jobId] = jobCts;

        _ = Task.Run(async () =>
        {
            var writer = channel.Writer;
            try
            {
                // Initial progress write without request-scoped cancellation token
                await writer.WriteAsync(new OptimizationProgress { Stage = "Import", Message = "Importing and mapping players..." });

                var response = await RunOptimizationAsync(request, p => writer.TryWrite(p), jobCts.Token);
                resultByJob[jobId] = response;
                writer.TryWrite(new OptimizationProgress { Stage = "Done", Message = "Completed", Percent = 100 });
            }
            catch (OperationCanceledException)
            {
                writer.TryWrite(new OptimizationProgress { Stage = "Canceled", Message = "Job canceled." });
            }
            catch (Exception ex)
            {
                writer.TryWrite(new OptimizationProgress { Stage = "Error", Message = ex.Message });
            }
            finally
            {
                writer.Complete();
                if (ctsByJob.TryRemove(jobId, out var cts))
                {
                    cts.Dispose();
                }
            }
        }, CancellationToken.None);

        return Task.FromResult(jobId);
    }

    public async IAsyncEnumerable<OptimizationProgress> StreamProgressAsync(string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!progressByJob.TryGetValue(jobId, out var channel))
            yield break;

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
        {
            yield return item;
        }
    }

    public Task<OptimizeTeamsResponse?> GetResultAsync(string jobId)
    {
        resultByJob.TryGetValue(jobId, out var result);
        return Task.FromResult(result);
    }

    private Task<OptimizeTeamsResponse> RunOptimizationAsync(OptimizeTeamsRequest req, Action<OptimizationProgress> report, CancellationToken ct)
    {
        // Mirror logic from console Program.cs
        const string stageImport = "Import";
        const string stageFilter = "Filtering";
        const string stageCombine = "Combining";
        const string stageScoring = "Scoring";

        var startGw = req.StartGw;
        var endGw = req.EndGw;
        var numberOfGws = endGw - startGw + 1;

        var stopWatch = new Stopwatch();
        stopWatch.Start();

        // Map
        var players = rawPlayers.Select(r => PlayerMapper.MapRawDataToPlayer(r, startGw, numberOfGws)).ToList();

        // Data cleaning
        players.RemoveAll(p => p.CurrentPrice is null || p.Value == 0);

        // Selecting top points and value players
        var topPointsPlayer = players.OrderByDescending(p => p.TotalPredicted).Take(200);
        var topValuePlayers = players.OrderByDescending(o => o.Value).Take(200);
        var concatedPlayers = topPointsPlayer.Concat(topValuePlayers).Distinct().OrderByDescending(p => p.Value).ToList();

        // Apply include/locked/ban overrides passed in request if any
        var lockedPlayersDict = req.LockedPlayers ?? new Dictionary<string, string>();
        var includeList = req.IncludeList ?? new List<string>();
        var banList = req.BanList ?? new List<string>();

        foreach (var name in lockedPlayersDict.Keys.Union(includeList).Distinct())
        {
            if (concatedPlayers.All(s => s.Name != name))
            {
                concatedPlayers.AddRange(players.Where(p => p.Name == name));
            }
        }

        concatedPlayers.RemoveAll(r => banList.Contains(r.Name));

        var groupedPlayers = concatedPlayers
            .GroupBy(p => new { p.Position, p.CurrentPrice, p.Club })
            .Select(x => x.OrderByDescending(b => b.TotalPredicted).First())
            .ToList();

        var playersByPosition = groupedPlayers.GroupBy(o => o.Position);

        // Create combinations per position
        var combinations = new List<Combination>();
        var numberOfPlayers = 0;
        foreach (var position in playersByPosition)
        {
            var combinationsByPosition = new List<List<Player>>();
            numberOfPlayers = position.Key.ToString() switch
            {
                nameof(PositionEnum.GK) => 2,
                nameof(PositionEnum.DEF) => 5,
                nameof(PositionEnum.MID) => 5,
                nameof(PositionEnum.FWD) => 3,
                _ => numberOfPlayers
            };

            combinationsByPosition.AddRange(Combinations<Player>.GetCombinations(
                groupedPlayers.Where(p => p.Position == position.Key).ToList(), numberOfPlayers));

            combinations.AddRange(combinationsByPosition.Select(c => new Combination(c, startGw, endGw)));
        }

        var ts = stopWatch.Elapsed;
        report(new OptimizationProgress { Stage = stageImport, Message = $"Player import, map and filtering completed in: {ts:hh\\:mm\\:ss}", Percent = 20 });
        stopWatch.Restart();

        // Filtering teams with locked players
        report(new OptimizationProgress { Stage = stageFilter, Message = "Filtering teams with locked players..." });

        var validGks = new List<Combination>();
        var validDefs = new List<Combination>();
        var validMids = new List<Combination>();
        var validFwds = new List<Combination>();

        var lockedByPos = lockedPlayersDict.GroupBy(p => p.Value)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Key).ToList());

        if (lockedByPos.TryGetValue(nameof(PositionEnum.GK), out var lGk))
            validGks.AddRange(combinations.Where(c => c.Position == PositionEnum.GK).Where(gk => lGk.All(lp => gk.Players.Any(p => p.Name == lp))));
        if (lockedByPos.TryGetValue(nameof(PositionEnum.DEF), out var lDef))
            validDefs.AddRange(combinations.Where(c => c.Position == PositionEnum.DEF).Where(def => lDef.All(lp => def.Players.Any(p => p.Name == lp))));
        if (lockedByPos.TryGetValue(nameof(PositionEnum.MID), out var lMid))
            validMids.AddRange(combinations.Where(c => c.Position == PositionEnum.MID).Where(mid => lMid.All(lp => mid.Players.Any(p => p.Name == lp))));
        if (lockedByPos.TryGetValue(nameof(PositionEnum.FWD), out var lFwd))
            validFwds.AddRange(combinations.Where(c => c.Position == PositionEnum.FWD).Where(fwd => lFwd.All(lp => fwd.Players.Any(p => p.Name == lp))));

        if (validGks.Count == 0) validGks = combinations.Where(c => c.Position == PositionEnum.GK).ToList();
        if (validDefs.Count == 0) validDefs = combinations.Where(c => c.Position == PositionEnum.DEF).ToList();
        if (validMids.Count == 0) validMids = combinations.Where(c => c.Position == PositionEnum.MID).ToList();
        if (validFwds.Count == 0) validFwds = combinations.Where(c => c.Position == PositionEnum.FWD).ToList();

        // Select top combinations by price and value with complexity parameter
        var complexity = Math.Max(1, req.Complexity);
        var gks = validGks.OrderByDescending(c => c.Value).Take(complexity / 5)
            .Union(validGks.OrderByDescending(c => c.PredictedPoints / c.NumberOfPlayers).Take(complexity))
            .Distinct().ToList();
        var defs = validDefs.OrderByDescending(c => c.Value).Take(complexity)
            .Union(validDefs.OrderByDescending(c => c.PredictedPoints / c.NumberOfPlayers).Take(complexity))
            .Distinct().ToList();
        var mids = validMids.OrderByDescending(c => c.Value).Take(complexity)
            .Union(validMids.OrderByDescending(c => c.PredictedPoints / c.NumberOfPlayers).Take(complexity))
            .Distinct().ToList();
        var fwds = validFwds.OrderByDescending(c => c.Value).Take(complexity)
            .Union(validFwds.OrderByDescending(c => c.PredictedPoints / c.NumberOfPlayers).Take(complexity))
            .Distinct().ToList();

        ts = stopWatch.Elapsed;
        report(new OptimizationProgress { Stage = stageFilter, Message = $"Filtering completed in: {ts:hh\\:mm\\:ss}", Percent = 40 });
        stopWatch.Restart();

        // Create valid team combinations
        report(new OptimizationProgress { Stage = stageCombine, Message = "Calculating possible teams..." });

        var validTeamCombinations = new List<List<Combination>>();
        var processedGk = 0;
        foreach (var gk in gks)
        {
            foreach (var def in defs)
            {
                if (!PlayerMapper.IsTeamValid([gk, def], req.MaxPlayersByTeam))
                    continue;

                foreach (var mid in mids)
                {
                    var gkDefMidPrice = gk.Price + def.Price + mid.Price;
                    if (gkDefMidPrice > req.MaxTeamPrice || !PlayerMapper.IsTeamValid([gk, def, mid], req.MaxPlayersByTeam))
                        continue;

                    foreach (var fwd in fwds)
                    {
                        var totalPrice = gkDefMidPrice + fwd.Price;
                        if (totalPrice > req.MaxTeamPrice || totalPrice < req.MinTeamPrice)
                            continue;

                        if (PlayerMapper.IsTeamValid([gk, def, mid, fwd], req.MaxPlayersByTeam))
                        {
                            validTeamCombinations.Add([gk, def, mid, fwd]);
                        }
                    }
                }
            }

            processedGk++;
            var percent = gks.Count == 0 ? 50 : 40 + (double)processedGk / gks.Count * 30; // up to 70%
            report(new OptimizationProgress { Stage = stageCombine, Message = $"{Math.Round(percent, 2)}% completed", Percent = percent });
        }

        ts = stopWatch.Elapsed;
        report(new OptimizationProgress { Stage = stageCombine, Message = $"Calculating completed in: {ts:hh\\:mm\\:ss}", Percent = 70 });
        stopWatch.Restart();

        // Scoring and selecting top teams (with progress updates similar to console)
        report(new OptimizationProgress { Stage = stageScoring, Message = "Calculating predicted points and selecting top teams...", Percent = 70 });

        var totalTeams = validTeamCombinations.Count;
        var processedTeams = 0;
        var lastProgressUpdate = DateTime.UtcNow;
        var progressUpdateInterval = TimeSpan.FromSeconds(2);

        var teamsWithPoints = new List<(List<Combination> Team, FPL.Shared.ResultTeam ResultTeam)>(capacity: Math.Min(totalTeams, 100));

        foreach (var team in validTeamCombinations)
        {
            ct.ThrowIfCancellationRequested();

            var teamPlayers = new List<Player>(team.Sum(c => c.Players.Count));
            foreach (var combination in team)
                teamPlayers.AddRange(combination.Players);

            var calculation = PlayerMapper.CalculatePredictedPoints(teamPlayers, startGw, endGw);
            teamsWithPoints.Add((team, calculation));

            processedTeams++;
            var now = DateTime.UtcNow;
            if (now - lastProgressUpdate >= progressUpdateInterval)
            {
                var percent = totalTeams == 0 ? 90 : 70 + (double)processedTeams / totalTeams * 25; // up to ~95%
                report(new OptimizationProgress
                {
                    Stage = stageScoring,
                    Message = $"Progress: {processedTeams}/{totalTeams} teams processed ({percent:F1}%)",
                    Percent = percent
                });
                lastProgressUpdate = now;
            }
        }

        // Sort and take top N intermediate
        teamsWithPoints = teamsWithPoints
            .OrderByDescending(t => t.ResultTeam.PredictedPoints)
            .Take(100)
            .ToList();

        if (req.CalculateBenchBoost)
        {
            foreach (var team in teamsWithPoints)
            {
                team.ResultTeam.PredictedPoints += team.ResultTeam.BenchPoints.Max();
                team.ResultTeam.BbGw = team.ResultTeam.BenchPoints.IndexOf(team.ResultTeam.BenchPoints.Max()) + 1;
            }
        }

        var finalTeamsWithPoints = teamsWithPoints
            .OrderByDescending(t => t.ResultTeam.PredictedPoints)
            .Take(Math.Max(1, req.TopTeams))
            .ToList();

        ts = stopWatch.Elapsed;
        report(new OptimizationProgress { Stage = stageScoring, Message = $"Scoring completed in: {ts:hh\\:mm\\:ss}", Percent = 95 });

        // Build response
        var response = new OptimizeTeamsResponse
        {
            Teams = finalTeamsWithPoints.Select(t => new OptimizedTeam
            {
                PlayerNames = t.Team.SelectMany(c => c.Players).Select(p => p.Name).ToList(),
                PredictedPoints = Math.Round(t.ResultTeam.PredictedPoints, 2),
                Price = t.Team.Sum(x => x.Price),
                OptimalTeamsByWeek = t.ResultTeam.OptimalTeamsByWeek
                    .Select(week => week.Select(p => p.Name).ToList())
                    .ToList(),
                CaptainsByWeek = t.ResultTeam.CaptainsByWeek,
                BenchBoostGw = req.CalculateBenchBoost ? t.ResultTeam.BbGw : null
            }).ToList(),
            ElapsedScoring = ts.ToString("hh\\:mm\\:ss")
        };

        report(new OptimizationProgress { Stage = "Finalizing", Message = "Done", Percent = 100 });

        return Task.FromResult(response);
    }
}


