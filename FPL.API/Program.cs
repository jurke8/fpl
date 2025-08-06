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
bool useLocalJson = true; // Set to false for production mode (download from URL)
const string localJsonPath = "player-data.json";
const string remoteJsonUrl = "https://www.fantasyfootballhub.co.uk/player-data/player-data.json";

//Variables

//Data import and mapping
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

// New endpoint for calculating team points across gameweeks
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

                // Find players by names (case-insensitive)
                var teamPlayers = new List<Player>();
                var notFoundPlayers = new List<string>();

                foreach (var playerName in request.PlayerNames)
                {
                    var player = rawPlayers.FirstOrDefault(p =>
                        string.Equals(p.webName, playerName, StringComparison.OrdinalIgnoreCase));

                    if (player != null)
                    {
                        var mappedPlayer = PlayerMapper.MapRawDataToPlayer(player, request.StartGameweek,
                            request.EndGameweek - request.StartGameweek + 1);
                        teamPlayers.Add(mappedPlayer);
                    }
                    else
                    {
                        notFoundPlayers.Add(playerName);
                    }
                }

                if (!teamPlayers.Any())
                {
                    return Results.BadRequest("No valid players found in the provided list.");
                }


                var team = PlayerMapper.CalculatePredictedPoints(teamPlayers, request.StartGameweek,
                    request.EndGameweek);
                // Calculate points for each gameweek
                var gameWeekPoints = new List<double>();
                for (var i = request.StartGameweek - 1; i < request.EndGameweek; i++)
                {
                    gameWeekPoints.Add(team.OptimalTeamsByWeek[i].Select(s => s.Predictions[i]).Sum());
                }

                var teamByWeek = new List<List<string>>();
                foreach (var listPlayer in team.OptimalTeamsByWeek)
                {
                    var list = new List<string>();
                    foreach (var player in listPlayer)
                    {
                        list.Add(player.Name);
                    }
                    teamByWeek.Add(list);
                }

                team.PredictedPoints += team.BenchPoints.Max();
                team.BbGw = team.BenchPoints.IndexOf(team.BenchPoints.Max()) + 1;

                var response = new TeamPointsResponse
                {
                    PlayerNames = request.PlayerNames,
                    StartGameweek = request.StartGameweek,
                    EndGameweek = request.EndGameweek,
                    GameweekPoints = gameWeekPoints,
                    TotalPoints = team.PredictedPoints,
                    NotFoundPlayers = notFoundPlayers,
                    FoundPlayersCount = teamPlayers.Count,
                    TeamByWeek = teamByWeek,
                    CaptainsByWeek = team.CaptainsByWeek,
                    BbGw = team.BbGw
                };

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Problem($"An error occurred while processing the request: {ex.Message}");
            }
        })
    .WithName("GetTeamPoints")
    .WithOpenApi(operation =>
    {
        operation.Summary = "Calculates total points for a team across specified gameweeks";
        operation.Description =
            "Takes a list of player names and calculates the total predicted points for that team across the specified gameweek range. Returns points for each gameweek and summary statistics.";
        return operation;
    });

app.Run();