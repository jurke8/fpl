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