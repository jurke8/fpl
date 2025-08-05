using System.Diagnostics;
using System.Text.Json;
using FPL;

//Configuration
const double minTeamPrice = 98;
Console.WriteLine("Importing and mapping players..");
const int startGw = 1;
const int endGw = 7;
const int maxTeamPrice = 100;
const int complexity = 50;
const int maxPlayersByTeam = 3;
const bool calculateBenchBoost = true;
// Data source configuration
bool useLocalJson = false; // Set to false for production mode (download from URL)
const string localJsonPath = "test.json";
const string remoteJsonUrl = "https://www.fantasyfootballhub.co.uk/player-data/player-data.json";

//Variables
const int numberOfGws = endGw - startGw + 1;
var combinations = new List<Combination>();
var players = new List<Player>();
int numberOfPlayers = 0;

//Data import and mapping
Stopwatch stopWatch = new Stopwatch();
stopWatch.Start();

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

//Map raw data to players
players.AddRange(rawPlayers.Select(rawPlayer => PlayerMapper.MapRawDataToPlayer(rawPlayer, startGw, numberOfGws)));


//Data cleaning
players.RemoveAll(p => p.CurrentPrice is null || p.Value == 0);

//Selecting top points and value (point/price) players
var topPointsPlayer = players.OrderByDescending(p => p.TotalPredicted).Take(200);
var topValuePlayers = players.OrderByDescending(o => o.Value).Take(200);
var concatedPlayers = topPointsPlayer.Concat(topValuePlayers).Distinct().OrderByDescending(p => p.Value).ToList();

foreach (var player in IncludedPlayers.LockedPlayers.Keys.Union(IncludedPlayers.IncludeList).Distinct())
{
    if (!concatedPlayers.Select(s => s.Name).Contains(player))
    {
        concatedPlayers.AddRange(players.Where(p => p.Name == player));
    }
}

//Removing unwanted players
concatedPlayers.RemoveAll(r => IncludedPlayers.BanList.Contains(r.Name));
//Selecting best players from position, price and club
var groupedPlayers = concatedPlayers.GroupBy(p => new { p.Position, p.CurrentPrice, p.Club })
    .Select(x => x.OrderByDescending(b => b.TotalPredicted).First());
//Grouping players
var enumerable = groupedPlayers.ToList();
var playersByPosition = enumerable.GroupBy(o => o.Position);
//Creating combinations of players per position
foreach (var position in playersByPosition)
{
    var combinationsByPosition = new List<List<Player>>();

    numberOfPlayers = position.Key.ToString() switch
    {
        "GK" => 2,
        "DEF" => 5,
        "MID" => 5,
        "FWD" => 3,
        _ => numberOfPlayers
    };

    combinationsByPosition.AddRange(Combinations<Player>.GetCombinations(
        enumerable.Where(p => p.Position.ToString().Equals(position.Key.ToString()))
            .ToList(), numberOfPlayers));

    combinations.AddRange(combinationsByPosition.Select(combination => new Combination(combination, startGw, endGw)));
}

//Filtering teams with locked players:
Console.WriteLine("Filtering teams with locked players:");
var validGks = new List<Combination>();
var validDefs = new List<Combination>();
var validMids = new List<Combination>();
var validFwds = new List<Combination>();

// Group locked players by position
var lockedPlayersByPosition = IncludedPlayers.LockedPlayers
    .GroupBy(p => p.Value)
    .ToDictionary(g => g.Key, g => g.Select(p => p.Key).ToList());

// Add combinations that contain ALL locked players for each position
if (lockedPlayersByPosition.TryGetValue(nameof(PositionEnum.GK), out var value1))
{
    validGks.AddRange(combinations.Where(c => c.Position == PositionEnum.GK).Where(gk =>
        value1.All(lockedPlayer => gk.Players.Select(p => p.Name).Contains(lockedPlayer))));
}

if (lockedPlayersByPosition.TryGetValue(nameof(PositionEnum.DEF), out var value2))
{
    validDefs.AddRange(combinations.Where(c => c.Position == PositionEnum.DEF).Where(def =>
        value2.All(lockedPlayer => def.Players.Select(p => p.Name).Contains(lockedPlayer))));
}

if (lockedPlayersByPosition.TryGetValue(nameof(PositionEnum.MID), out var value3))
{
    validMids.AddRange(combinations.Where(c => c.Position == PositionEnum.MID).Where(mid =>
        value3.All(lockedPlayer => mid.Players.Select(p => p.Name).Contains(lockedPlayer))));
}

if (lockedPlayersByPosition.TryGetValue(nameof(PositionEnum.FWD), out var value4))
{
    validFwds.AddRange(combinations.Where(c => c.Position == PositionEnum.FWD).Where(fwd =>
        value4.All(lockedPlayer => fwd.Players.Select(p => p.Name).Contains(lockedPlayer))));
}

// If no locked players for a position, use all combinations
if (validGks.Count == 0)
    validGks = combinations.Where(c => c.Position == PositionEnum.GK).ToList();
if (validDefs.Count == 0)
    validDefs = combinations.Where(c => c.Position == PositionEnum.DEF).ToList();
if (validMids.Count == 0)
    validMids = combinations.Where(c => c.Position == PositionEnum.MID).ToList();
if (validFwds.Count == 0)
    validFwds = combinations.Where(c => c.Position == PositionEnum.FWD).ToList();


//Selecting top combinations by price and value with complexity parameter
var gks = validGks.OrderByDescending(c => c.Value).Take(complexity / 5).Union(
        validGks.OrderByDescending(c => c.PredictedPoints / c.NumberOfPlayers)
            .Take(complexity))
    .Distinct().ToList();
var defs = validDefs.OrderByDescending(c => c.Value).Take(complexity)
    .Union(validDefs
        .OrderByDescending(c => c.PredictedPoints / c.NumberOfPlayers)
        .Take(complexity))
    .Distinct().ToList();
var mids = validMids.OrderByDescending(c => c.Value).Take(complexity)
    .Union(validMids
        .OrderByDescending(c => c.PredictedPoints / c.NumberOfPlayers)
        .Take(complexity))
    .Distinct().ToList();
var fwds = validFwds.OrderByDescending(c => c.Value).Take(complexity)
    .Union(validFwds
        .OrderByDescending(c => c.PredictedPoints / c.NumberOfPlayers)
        .Take(complexity))
    .Distinct().ToList();
Console.WriteLine("Creating valid teams..");

var validTeamCombinations = new List<List<Combination>>();
stopWatch.Stop();
// Get the elapsed time as a TimeSpan value.
TimeSpan ts = stopWatch.Elapsed;
// Format and display the TimeSpan value.
var elapsedTime = $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
Console.WriteLine("Player import, map and filtering completed in:" + elapsedTime);
stopWatch.Restart();


stopWatch.Stop();
// Get the elapsed time as a TimeSpan value.
ts = stopWatch.Elapsed;
// Format and display the TimeSpan value.
elapsedTime = $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
Console.WriteLine("Filtering completed in:" + elapsedTime);
stopWatch.Restart();
Console.WriteLine("Calculating possible teams...");
var processedGk = 0;
// Optimized nested loops with early filtering and reduced memory allocations
foreach (var gk in gks)
{
    foreach (var def in defs)
    {
        // Early price check - if GK + DEF already exceeds max, skip remaining combinations
        if (!PlayerMapper.IsTeamValid([gk, def], maxPlayersByTeam))
            continue;

        foreach (var mid in mids)
        {
            var gkDefMidPrice = gk.Price + def.Price + mid.Price;

            // Early price check - if GK + DEF + MID already exceeds max, skip remaining combinations
            if (gkDefMidPrice > maxTeamPrice || !PlayerMapper.IsTeamValid([gk, def, mid], maxPlayersByTeam))
                continue;

            foreach (var fwd in fwds)
            {
                var totalPrice = gkDefMidPrice + fwd.Price;

                // Early price validation
                if (totalPrice is > maxTeamPrice or < minTeamPrice)
                    continue;

                // Create combination only if price is valid and validate team composition
                if (PlayerMapper.IsTeamValid([gk, def, mid, fwd], maxPlayersByTeam))
                {
                    validTeamCombinations.Add([gk, def, mid, fwd]);
                }
            }
        }
    }

    // Progress tracking
    var progress = Math.Round((double)++processedGk / gks.Count * 100, 2);
    Console.WriteLine($"{progress}% completed");
}

stopWatch.Stop();
// Get the elapsed time as a TimeSpan value.
ts = stopWatch.Elapsed;
// Format and display the TimeSpan value.
elapsedTime = $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
Console.WriteLine("Calculating completed in:" + elapsedTime);
stopWatch.Restart();

// Filter teams to ensure they contain ALL locked players
if (IncludedPlayers.LockedPlayers.Count > 0)
{
    Console.WriteLine("Filtering teams to ensure they contain all locked players...");
    var filteredTeamCombinations = new List<List<Combination>>();

    // Pre-build set of locked player names for O(1) lookup
    var lockedPlayerNames = new HashSet<string>(IncludedPlayers.LockedPlayers.Select(p => p.Key));

    foreach (var team in validTeamCombinations)
    {
        // Build team player names set for efficient lookup
        var teamPlayerNames = new HashSet<string>();
        foreach (var combination in team)
        {
            foreach (var player in combination.Players)
            {
                teamPlayerNames.Add(player.Name);
            }
        }

        // Check if team contains all locked players using set operations
        if (lockedPlayerNames.IsSubsetOf(teamPlayerNames))
        {
            filteredTeamCombinations.Add(team);
        }
    }

    validTeamCombinations = filteredTeamCombinations;
    Console.WriteLine($"Filtered to {validTeamCombinations.Count} teams containing all locked players.");
}

// Optimized team selection: Parallel processing and reduced memory allocations
Console.WriteLine("Calculating predicted points and selecting top teams...");
var totalTeams = validTeamCombinations.Count;
var processedTeams = 0;
var progressLock = new object();
var lastProgressUpdate = DateTime.Now;
var progressUpdateInterval = TimeSpan.FromSeconds(2); // Update every 2 seconds

// Process teams in parallel for better performance
var teamsWithPoints = validTeamCombinations
    .Select(team =>
    {
        // Pre-allocate list with estimated capacity and build player list efficiently
        var totalPlayers = team.Sum(c => c.Players.Count);
        var teamPlayers = new List<Player>(totalPlayers);

        foreach (var combination in team)
        {
            teamPlayers.AddRange(combination.Players);
        }

        // Calculate predicted points with best bench boost week
        var calculation = PlayerMapper.CalculatePredictedPoints(
            teamPlayers,
            startGw,
            endGw);

        // Update progress (thread-safe, minimal overhead)
        lock (progressLock)
        {
            processedTeams++;
            var now = DateTime.Now;
            if (now - lastProgressUpdate >= progressUpdateInterval)
            {
                var percentage = (double)processedTeams / totalTeams * 100;
                Console.WriteLine($"Progress: {processedTeams}/{totalTeams} teams processed ({percentage:F1}%)");
                lastProgressUpdate = now;
            }
        }

        return new
        {
            Team = team,
            ResultTeam = calculation
        };
    })
    .OrderByDescending(t => t.ResultTeam.PredictedPoints)
    .Take(5)
    .ToList();

//For bench boost
if(calculateBenchBoost)
{
        foreach (var team in teamsWithPoints)
        {
            team.ResultTeam.PredictedPoints += team.ResultTeam.BenchPoints.Max();
        }
}
stopWatch.Stop();
// Get the elapsed time as a TimeSpan value.
ts = stopWatch.Elapsed;
// Format and display the TimeSpan value.
elapsedTime = $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
Console.WriteLine($"Calculating predicted points for {totalTeams} teams completed in:" +
                  elapsedTime); // Progress tracking variables

//Printing
foreach (var teamData in teamsWithPoints)
{
    var team = teamData.Team;

    // Build player names string efficiently
    var names = new List<string>();
    foreach (var combination in team)
    {
        foreach (var player in combination.Players)
        {
            names.Add(player.Name);
        }
    }

    // Use string.Join for more efficient concatenation
    Console.Write(string.Join(",", names));
    Console.WriteLine(" Points: " +
                      Math.Round(teamData.ResultTeam.PredictedPoints, 2) +
                      "; Price:" + team.Sum(x => x.Price));

    // Print optimal teams by week with captain marked
    for (int i = 0; i < teamData.ResultTeam.OptimalTeamsByWeek.Count; i++)
    {
        var gw = startGw + i;
        var optimalTeam = teamData.ResultTeam.OptimalTeamsByWeek[i];
        var captain = teamData.ResultTeam.CaptainsByWeek[i];

        // Group players by position and sort by predicted points within each position
        var gkPlayers = optimalTeam.Where(p => p.Position == PositionEnum.GK).OrderByDescending(p => p.Predictions[i])
            .ToList();
        var defPlayers = optimalTeam.Where(p => p.Position == PositionEnum.DEF).OrderByDescending(p => p.Predictions[i])
            .ToList();
        var midPlayers = optimalTeam.Where(p => p.Position == PositionEnum.MID).OrderByDescending(p => p.Predictions[i])
            .ToList();
        var fwdPlayers = optimalTeam.Where(p => p.Position == PositionEnum.FWD).OrderByDescending(p => p.Predictions[i])
            .ToList();

        var result = new List<string>();

        var gkNames = gkPlayers.Select(p => p.Name == captain ? $"{p.Name}(C)" : p.Name);
        result.Add($"{string.Join(", ", gkNames)}");

        var defNames = defPlayers.Select(p => p.Name == captain ? $"{p.Name}(C)" : p.Name);
        result.Add($"{string.Join(", ", defNames)}");

        var midNames = midPlayers.Select(p => p.Name == captain ? $"{p.Name}(C)" : p.Name);
        result.Add($"{string.Join(", ", midNames)}");

        var fwdNames = fwdPlayers.Select(p => p.Name == captain ? $"{p.Name}(C)" : p.Name);
        result.Add($"{string.Join(", ", fwdNames)}");


        var formattedTeam = string.Join(" | ", result);
        Console.WriteLine($"  GW{gw}: {formattedTeam}");
    }

    Console.WriteLine();
}

stopWatch.Stop();
// Get the elapsed time as a TimeSpan value.
ts = stopWatch.Elapsed;
// Format and display the TimeSpan value.
elapsedTime = $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
Console.Write("Selecting top teams completed in:" + elapsedTime);

Console.ReadLine();