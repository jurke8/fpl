using System.Text.Json;
using FPL;
using FPL.API;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger/OpenAPI
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FPL API",
        Version = "v1",
        Description = "Fantasy Premier League API for player predictions and analysis"
    });
});

// Data source configuration
bool useLocalJson = false; // Set to false for production mode (download from URL)
const string localJsonPath = "player-data.json";
const string remoteJsonUrl = "https://www.fantasyfootballhub.co.uk/player-data/player-data.json";

// Load data once on startup
List<Root>? rawPlayers;
if (useLocalJson)
{
    Console.WriteLine($"Development mode: Loading data from local file '{localJsonPath}'");
    if (!File.Exists(AppContext.BaseDirectory + localJsonPath))
    {
        Console.WriteLine($"Error: Local JSON file '{localJsonPath}' not found.");
        Console.WriteLine("Please ensure the file exists or set useLocalJson to false for production mode.");
        return;
    }

    var jsonContent = File.ReadAllText(localJsonPath);
    rawPlayers = JsonSerializer.Deserialize<List<Root>>(jsonContent);
    if (rawPlayers == null)
    {
        Console.WriteLine("Error: Failed to deserialize local JSON file.");
        return;
    }

    Console.WriteLine($"Successfully loaded {rawPlayers.Count} players from local file.");
}
else
{
    Console.WriteLine($"Production mode: Downloading data from '{remoteJsonUrl}'");
    using HttpClient client = new HttpClient();
    try
    {
        HttpResponseMessage response = await client.GetAsync(remoteJsonUrl);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();
        rawPlayers = JsonSerializer.Deserialize<List<Root>>(responseBody);
        if (rawPlayers == null)
        {
            Console.WriteLine("Error: Failed to deserialize downloaded JSON data.");
            return;
        }

        Console.WriteLine($"Successfully downloaded and loaded {rawPlayers.Count} players.");
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"Error downloading data: {ex.Message}");
        Console.WriteLine("Please check your internet connection or set useLocalJson to true for development mode.");
        return;
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"Error parsing JSON data: {ex.Message}");
        return;
    }
}

// Register optimization service as singleton with loaded data
builder.Services.AddSingleton<IOptimizationService>(sp => new OptimizationService(rawPlayers!));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "FPL API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at the app's root
    });
}

app.UseHttpsRedirection();

// Minimal API endpoint for top players
app.MapGet("/api/top-players",
        (int startGameweek = 1, int endGameweek = 38, string? position = null, string sortBy = "points") =>
        {
            try
            {
                // Validate input parameters
                if (startGameweek < 1 || endGameweek < 1)
                {
                    return Results.BadRequest("Gameweek numbers must be positive integers.");
                }

                if (startGameweek > endGameweek)
                {
                    return Results.BadRequest("Start gameweek must be less than or equal to end gameweek.");
                }

                if (endGameweek > 38)
                {
                    return Results.BadRequest("End gameweek cannot exceed 38 (full Premier League season).");
                }

                // Validate position parameter if provided
                if (!string.IsNullOrEmpty(position))
                {
                    var validPositions = new[] { "GK", "DEF", "MID", "FWD" };
                    if (!validPositions.Contains(position.ToUpper()))
                    {
                        return Results.BadRequest("Position must be one of: GK, DEF, MID, FWD");
                    }
                }

                // Validate sortBy parameter
                var validSortOptions = new[] { "points", "value" };
                if (!validSortOptions.Contains(sortBy.ToLower()))
                {
                    return Results.BadRequest("SortBy must be one of: points, value");
                }

                var players = new List<Player>();

                players.AddRange(rawPlayers.Select(rawPlayer =>
                    PlayerMapper.MapRawDataToPlayer(rawPlayer, startGameweek, endGameweek - startGameweek + 1)));
                players.RemoveAll(p => p.CurrentPrice is null || p.Value == 0);

                // Calculate total prediction points for each player in the specified range
                var playersWithTotalPoints = players.Select(player => new PlayerResponse
                    {
                        Name = player.Name,
                        Position = player.Position.ToString(),
                        Club = player.Club.ToString(),
                        CurrentPrice = player.CurrentPrice,
                        TotalPredictionPoints = Math.Round(player.TotalPredicted, 2),
                        Value = Math.Round(player.Value, 2)
                    })
                    .Where(p => p.TotalPredictionPoints > 0) // Filter out players with no predictions
                    .Where(p => string.IsNullOrEmpty(position) ||
                                p.Position.Equals(position,
                                    StringComparison.OrdinalIgnoreCase)) // Filter by position if provided
                    .OrderByDescending(p => sortBy.ToLower() == "value" ? p.Value : p.TotalPredictionPoints)
                    .Take(20)
                    .ToList();

                return Results.Ok(playersWithTotalPoints);
            }
            catch (Exception ex)
            {
                return Results.Problem($"An error occurred while processing the request: {ex.Message}");
            }
        })
    .WithName("GetTopPlayers")
    .WithOpenApi(operation =>
    {
        operation.Summary = "Gets the top 20 players by prediction points or value for a specified gameweek range";
        operation.Description =
            "Returns a list of the top 20 players sorted by total prediction points or value across the specified gameweek range. Optionally filter by position (GK, DEF, MID, FWD) and sort by points or value.";
        return operation;
    });

// Endpoint for comparing two players across gameweeks
app.MapPost("/api/compare-players", (ComparePlayersRequest request) =>
    {
        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(request.Player1Name) || string.IsNullOrWhiteSpace(request.Player2Name))
            {
                return Results.BadRequest("Both player names must be provided.");
            }

            if (request.StartGameweek < 1 || request.EndGameweek < 1)
            {
                return Results.BadRequest("Gameweek numbers must be positive integers.");
            }

            if (request.StartGameweek > request.EndGameweek)
            {
                return Results.BadRequest("Start gameweek must be less than or equal to end gameweek.");
            }

            if (request.EndGameweek > 38)
            {
                return Results.BadRequest("End gameweek cannot exceed 38 (full Premier League season).");
            }

            var p1Raw = rawPlayers.FirstOrDefault(p => string.Equals(p.webName, request.Player1Name, StringComparison.OrdinalIgnoreCase));
            var p2Raw = rawPlayers.FirstOrDefault(p => string.Equals(p.webName, request.Player2Name, StringComparison.OrdinalIgnoreCase));

            var notFound = new List<string>();
            if (p1Raw is null) notFound.Add(request.Player1Name);
            if (p2Raw is null) notFound.Add(request.Player2Name);
            if (notFound.Count > 0)
            {
                return Results.NotFound($"Player(s) not found: {string.Join(", ", notFound)}");
            }

            var numberOfGws = request.EndGameweek - request.StartGameweek + 1;
            var p1 = PlayerMapper.MapRawDataToPlayer(p1Raw!, request.StartGameweek, numberOfGws);
            var p2 = PlayerMapper.MapRawDataToPlayer(p2Raw!, request.StartGameweek, numberOfGws);

            // Per-GW points are the player's prediction for that gw index
            var gwPoints1 = new List<double>(capacity: numberOfGws);
            var gwPoints2 = new List<double>(capacity: numberOfGws);
            var deltas = new List<double>(capacity: numberOfGws);

            for (int gw = request.StartGameweek - 1; gw < request.EndGameweek; gw++)
            {
                var p1Pts = Math.Round(p1.Predictions[gw], 2);
                var p2Pts = Math.Round(p2.Predictions[gw], 2);
                gwPoints1.Add(p1Pts);
                gwPoints2.Add(p2Pts);
                deltas.Add(Math.Round(p1Pts - p2Pts, 2));
            }

            var player1 = new PlayerComparison
            {
                Name = p1.Name,
                Position = p1.Position.ToString(),
                Club = p1.Club.ToString(),
                CurrentPrice = p1.CurrentPrice,
                Value = Math.Round(p1.Value, 2),
                GameweekPoints = gwPoints1,
                TotalPoints = Math.Round(gwPoints1.Sum(), 2)
            };

            var player2 = new PlayerComparison
            {
                Name = p2.Name,
                Position = p2.Position.ToString(),
                Club = p2.Club.ToString(),
                CurrentPrice = p2.CurrentPrice,
                Value = Math.Round(p2.Value, 2),
                GameweekPoints = gwPoints2,
                TotalPoints = Math.Round(gwPoints2.Sum(), 2)
            };

            var response = new ComparePlayersResponse
            {
                Player1 = player1,
                Player2 = player2,
                GameweekDelta = deltas,
                TotalDelta = Math.Round(deltas.Sum(), 2)
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem($"An error occurred while processing the request: {ex.Message}");
        }
    })
    .WithName("ComparePlayers")
    .WithOpenApi(op =>
    {
        op.Summary = "Compares two players across a gameweek range";
        op.Description = "Returns per-gameweek predicted points, per-week delta, prices, values, totals, and total delta for the two specified players.";
        return op;
    });

// New endpoint for calculating team points across gameweeks (supports comparing two teams)
app.MapPost("/api/team-points",
        (TeamPointsRequest request) =>
        {
            try
            {
                // Validate input parameters
                if (request.PlayerNames.Count == 0)
                {
                    return Results.BadRequest("Player names list cannot be null or empty.");
                }

                if (request.StartGameweek < 1 || request.EndGameweek < 1)
                {
                    return Results.BadRequest("Gameweek numbers must be positive integers.");
                }

                if (request.StartGameweek > request.EndGameweek)
                {
                    return Results.BadRequest("Start gameweek must be less than or equal to end gameweek.");
                }

                if (request.EndGameweek > 38)
                {
                    return Results.BadRequest("End gameweek cannot exceed 38 (full Premier League season).");
                }

                // Helper to build a team response from a list of names
                TeamPointsResponse BuildResponse(List<string> names)
                {
                    // Find players by names (case-insensitive)
                    var teamPlayersLocal = new List<Player>();
                    var notFoundPlayersLocal = new List<string>();

                    foreach (var playerName in names)
                    {
                        var player = rawPlayers.FirstOrDefault(p =>
                            string.Equals(p.webName, playerName, StringComparison.OrdinalIgnoreCase));

                        if (player != null)
                        {
                            var mappedPlayer = PlayerMapper.MapRawDataToPlayer(player, request.StartGameweek,
                                request.EndGameweek - request.StartGameweek + 1);
                            teamPlayersLocal.Add(mappedPlayer);
                        }
                        else
                        {
                            notFoundPlayersLocal.Add(playerName);
                        }
                    }

                    if (!teamPlayersLocal.Any())
                    {
                        // This will be handled by the caller to return a BadRequest
                        return new TeamPointsResponse
                        {
                            PlayerNames = names,
                            StartGameweek = request.StartGameweek,
                            EndGameweek = request.EndGameweek,
                            NotFoundPlayers = notFoundPlayersLocal
                        };
                    }

                    var teamLocal = PlayerMapper.CalculatePredictedPoints(teamPlayersLocal, request.StartGameweek,
                        request.EndGameweek);

                    // Calculate points for each gameweek (maintaining existing indexing approach)
                    var gameWeekPointsLocal = new List<double>();
                    for (var i = request.StartGameweek - 1; i < request.EndGameweek; i++)
                    {
                        gameWeekPointsLocal.Add(Math.Round(
                            teamLocal.OptimalTeamsByWeek[i].Select(s => s.Predictions[i]).Sum() +
                            teamLocal.OptimalTeamsByWeek[i].Select(p => p.Predictions[i]).Max(), 2));
                    }

                    var teamByWeekLocal = new List<List<string>>();
                    foreach (var listPlayer in teamLocal.OptimalTeamsByWeek)
                    {
                        var list = new List<string>();
                        foreach (var player in listPlayer)
                        {
                            list.Add(player.Name);
                        }

                        teamByWeekLocal.Add(list);
                    }

                    teamLocal.BbGw = teamLocal.BenchPoints.IndexOf(teamLocal.BenchPoints.Max()) + 1;

                    // Calculate team value
                    var teamValueLocal = PlayerMapper.CalculateTeamPrice(teamPlayersLocal);

                    return new TeamPointsResponse
                    {
                        PlayerNames = names,
                        StartGameweek = request.StartGameweek,
                        EndGameweek = request.EndGameweek,
                        GameweekPoints = gameWeekPointsLocal,
                        TotalPoints = Math.Round(teamLocal.PredictedPoints, 2),
                        NotFoundPlayers = notFoundPlayersLocal.Count > 0 ? notFoundPlayersLocal : null,
                        TeamValue = teamValueLocal,
                        TeamByWeek = teamByWeekLocal,
                        CaptainsByWeek = teamLocal.CaptainsByWeek,
                        BbGw = teamLocal.BbGw
                    };
                }

                // Build primary team response
                var primary = BuildResponse(request.PlayerNames);
                if (primary.GameweekPoints.Count == 0)
                {
                    return Results.BadRequest("No valid players found in the provided list.");
                }

                // If a second team is provided, build opponent and compute differences
                if (request.PlayerNames2 is not null && request.PlayerNames2.Count > 0)
                {
                    var opponent = BuildResponse(request.PlayerNames2);
                    if (opponent.GameweekPoints.Count == 0)
                    {
                        return Results.BadRequest("No valid players found in the second list (PlayerNames2).");
                    }

                    var count = Math.Min(primary.GameweekPoints.Count, opponent.GameweekPoints.Count);
                    var deltas = new List<double>(capacity: count);
                    for (int i = 0; i < count; i++)
                    {
                        deltas.Add(Math.Round(primary.GameweekPoints[i] - opponent.GameweekPoints[i], 2));
                    }

                    primary.Opponent = new TeamPointsResponse
                    {
                        PlayerNames = opponent.PlayerNames,
                        StartGameweek = opponent.StartGameweek,
                        EndGameweek = opponent.EndGameweek,
                        GameweekPoints = opponent.GameweekPoints,
                        TotalPoints = opponent.TotalPoints,
                        NotFoundPlayers = opponent.NotFoundPlayers,
                        TeamValue = opponent.TeamValue,
                        TeamByWeek = opponent.TeamByWeek,
                        CaptainsByWeek = opponent.CaptainsByWeek,
                        BbGw = opponent.BbGw,
                        Opponent = null,
                        Difference = null
                    };

                    primary.Difference = new TeamPointsDifference
                    {
                        GameweekPointsDelta = deltas,
                        TotalPointsDelta = Math.Round(primary.TotalPoints - opponent.TotalPoints, 2),
                        TeamValueDelta = (primary.TeamValue ?? 0) - (opponent.TeamValue ?? 0)
                    };
                }

                return Results.Ok(primary);
            }
            catch (Exception ex)
            {
                return Results.Problem($"An error occurred while processing the request: {ex.Message}");
            }
        })
    .WithName("GetTeamPoints")
    .WithOpenApi(operation =>
    {
        operation.Summary = "Calculates total points for one or two teams across specified gameweeks (with comparison)";
        operation.Description =
            "Takes one or two lists of player names and calculates the total predicted points for the team(s) across the specified gameweek range. If two teams are provided, returns both results and a difference (primary - opponent).";
        return operation;
    });

// Endpoint for suggesting best transfers over a lookahead horizon
app.MapPost("/api/suggest-transfers", (SuggestTransfersRequest request) =>
    {
        try
        {
            // Basic validations
            if (request.PlayerNames is null || request.PlayerNames.Count != 15)
            {
                return Results.BadRequest("Provide exactly 15 player names for the team.");
            }
            if (request.FreeTransfers < 0)
            {
                return Results.BadRequest("FreeTransfers must be >= 0.");
            }
            if (request.CurrentGameweek < 1 || request.CurrentGameweek > 38)
            {
                return Results.BadRequest("CurrentGameweek must be between 1 and 38.");
            }
            if (request.LookaheadGameweeks < 1)
            {
                return Results.BadRequest("LookaheadGameweeks must be >= 1.");
            }

            var startGw = request.CurrentGameweek;
            var endGw = Math.Min(38, request.CurrentGameweek + request.LookaheadGameweeks - 1);
            var numberOfGws = endGw - startGw + 1;

            // Prepare ban list (case-insensitive)
            var bannedNames = new HashSet<string>(request.BanList ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            // Prepare locked players (case-insensitive) - these should not be suggested as transfer OUT
            var lockedNames = new HashSet<string>(request.LockedPlayers ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            // Find original team players
            var notFound = new List<string>();
            var originalTeamPlayers = new List<Player>(capacity: 11);
            foreach (var name in request.PlayerNames)
            {
                var raw = rawPlayers.FirstOrDefault(p => string.Equals(p.webName, name, StringComparison.OrdinalIgnoreCase));
                if (raw is null)
                {
                    notFound.Add(name);
                    continue;
                }
                var mapped = PlayerMapper.MapRawDataToPlayer(raw, startGw, numberOfGws);
                if (mapped.CurrentPrice is null || mapped.Value == 0)
                {
                    // Treat unpriceable players as not found/invalid for prediction purposes
                    notFound.Add(name);
                    continue;
                }
                originalTeamPlayers.Add(mapped);
            }

            if (notFound.Count > 0)
            {
                return Results.NotFound($"Player(s) not found or invalid: {string.Join(", ", notFound)}");
            }

            if (originalTeamPlayers.Count != 15)
            {
                return Results.BadRequest("Exactly 15 valid players are required.");
            }

            // Helper: compute projected points across horizon
            double ProjectedPoints(List<Player> team) => Math.Round(
                PlayerMapper.CalculatePredictedPoints(team, startGw, endGw).PredictedPoints, 2);

            // Club limit (FPL standard) and team budget constraint
            const int MAX_PER_CLUB = 3;
            const double MAX_TEAM_BUDGET = 100.0; // budget for 15-man squad
            double GetTeamPrice(List<Player> team) => PlayerMapper.CalculateTeamPrice(team) ?? double.MaxValue;
            var workingTeamPrice = GetTeamPrice(originalTeamPlayers);
            bool ExceedsClubLimitAfterSwap(List<Player> current, Player outPlayer, Player inPlayer)
            {
                if (outPlayer.Club == inPlayer.Club)
                    return false; // no change in count for that club
                var counts = current.GroupBy(p => p.Club).ToDictionary(g => g.Key, g => g.Count());
                counts[outPlayer.Club] = counts.GetValueOrDefault(outPlayer.Club, 0) - 1;
                counts[inPlayer.Club] = counts.GetValueOrDefault(inPlayer.Club, 0) + 1;
                return counts[inPlayer.Club] > MAX_PER_CLUB || counts.GetValueOrDefault(outPlayer.Club, 0) < 0;
            }

            // Build candidate pool once
            var allMappedPlayers = rawPlayers
                .Select(r => PlayerMapper.MapRawDataToPlayer(r, startGw, numberOfGws))
                .Where(p => p.CurrentPrice is not null && p.Value > 0)
                .ToList();

            var nameInCurrent = new HashSet<string>(originalTeamPlayers.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            // Start with current team
            var workingTeam = new List<Player>(originalTeamPlayers);
            var suggestions = new List<TransferSuggestion>();
            var currentPoints = ProjectedPoints(workingTeam);

            for (int i = 0; i < request.FreeTransfers; i++)
            {
                TransferSuggestion? best = null;
                List<Player>? bestTeam = null;

                foreach (var outPlayer in workingTeam)
                {
                    // Skip if out player is locked
                    if (lockedNames.Contains(outPlayer.Name))
                        continue;

                    // Only consider replacements of the same position to keep roster coherent
                    var candidates = allMappedPlayers.Where(p => p.Position == outPlayer.Position && !nameInCurrent.Contains(p.Name) && !bannedNames.Contains(p.Name));

                    foreach (var candidate in candidates)
                    {
                        if (ExceedsClubLimitAfterSwap(workingTeam, outPlayer, candidate))
                            continue;

                        // Budget check for the swap
                        var newTeamPrice = workingTeamPrice - (outPlayer.CurrentPrice ?? 0) + (candidate.CurrentPrice ?? 0);
                        if (newTeamPrice > MAX_TEAM_BUDGET)
                            continue;

                        // Build new team
                        var newTeam = new List<Player>(workingTeam);
                        var idx = newTeam.FindIndex(p => p.Name.Equals(outPlayer.Name, StringComparison.OrdinalIgnoreCase));
                        if (idx < 0) continue;
                        newTeam[idx] = candidate;

                        var newPoints = ProjectedPoints(newTeam);
                        var delta = Math.Round(newPoints - currentPoints, 2);
                        if (delta > 0 && (best is null || delta > best.DeltaPoints))
                        {
                            best = new TransferSuggestion { Out = outPlayer.Name, In = candidate.Name, DeltaPoints = delta };
                            bestTeam = newTeam;
                        }
                    }
                }

                if (best is null || bestTeam is null)
                {
                    // No positive improvement available; stop early
                    break;
                }

                // Apply best transfer and iterate
                suggestions.Add(best);
                nameInCurrent.Remove(best.Out);
                nameInCurrent.Add(best.In);
                // update team price for next iteration
                var outP = workingTeam.First(p => p.Name.Equals(best.Out, StringComparison.OrdinalIgnoreCase));
                var inP = allMappedPlayers.First(p => p.Name.Equals(best.In, StringComparison.OrdinalIgnoreCase));
                workingTeamPrice = workingTeamPrice - (outP.CurrentPrice ?? 0) + (inP.CurrentPrice ?? 0);
                workingTeam = bestTeam;
                currentPoints = ProjectedPoints(workingTeam);
            }

            var response = new SuggestTransfersResponse
            {
                OriginalTeam = originalTeamPlayers.Select(p => p.Name).ToList(),
                Suggestions = suggestions,
                FinalTeam = workingTeam.Select(p => p.Name).ToList(),
                OriginalProjectedPoints = ProjectedPoints(originalTeamPlayers),
                FinalProjectedPoints = ProjectedPoints(workingTeam)
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem($"An error occurred while processing the request: {ex.Message}");
        }
    })
    .WithName("SuggestTransfers")
    .WithOpenApi(operation =>
    {
        operation.Summary = "Suggests best transfers to maximize predicted points over a lookahead horizon";
        operation.Description = "Given a 15-man squad, number of free transfers, current gameweek, and lookahead length, returns up to N transfers (same-position swaps, max 3 per club) that maximize predicted points across the horizon. Optionally provide a ban list so specific players are never suggested as incoming transfers, and a locked players list so those players are never suggested as transfers OUT.";
        return operation;
    });

// Endpoint to start optimization job
app.MapPost("/api/optimize/start", async (OptimizeTeamsRequest request, IOptimizationService service, HttpContext ctx) =>
{
    var jobId = await service.StartOptimizationAsync(request, ctx.RequestAborted);
    return Results.Ok(new { JobId = jobId });
}).WithName("StartOptimization").WithOpenApi(op =>
{
    op.Summary = "Starts team optimization based on console logic";
    op.Description = "Starts a background job to compute best teams; use the progress and result endpoints to track and fetch results.";
    return op;
});

// Server-Sent Events (SSE) progress stream
app.MapGet("/api/optimize/progress/{jobId}", async (string jobId, IOptimizationService service, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Append("Content-Type", "text/event-stream");

    await foreach (var update in service.StreamProgressAsync(jobId, context.RequestAborted))
    {
        var json = JsonSerializer.Serialize(update);
        await context.Response.WriteAsync($"data: {json}\n\n");
        await context.Response.Body.FlushAsync();
    }
});

// Fetch result
app.MapGet("/api/optimize/result/{jobId}", async (string jobId, IOptimizationService service) =>
{
    var result = await service.GetResultAsync(jobId);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).WithName("GetOptimizationResult").WithOpenApi(op =>
{
    op.Summary = "Gets optimization result by job id";
    op.Description = "Returns final top teams and timings once the optimization job completes.";
    return op;
});

app.Run();