# FPL - Fantasy Premier League Team Optimizer

A .NET console application that helps optimize Fantasy Premier League (FPL) team selections by analyzing player data and generating optimal team combinations.

## Features

- **Player Data Import**: Fetches current player data from Fantasy Football Hub
- **Team Optimization**: Generates optimal team combinations based on:
  - Predicted points
  - Value (points per price)
  - Position requirements
  - Team price constraints
  - Player availability and form
- **Flexible Configuration**: Customizable parameters for:
  - Gameweek range
  - Maximum team price
  - Complexity level
  - Maximum players per team

## Project Structure

- `Program.cs` - Main application logic and team generation
- `Player.cs` - Player data model
- `PlayerMapper.cs` - Data mapping utilities
- `Combination.cs` - Team combination logic
- `IncludedPlayers.cs` - Player inclusion/exclusion lists
- `ClubEnum.cs` - Premier League club enumerations
- `PositionEnum.cs` - Player position enumerations
- `Extensions.cs` - Utility extension methods
- `JsonProps.cs` - JSON property definitions

## Requirements

- .NET 9.0
- Internet connection for player data fetching

## Usage

1. Clone the repository
2. Navigate to the project directory
3. Run the application:
   ```bash
   dotnet run
   ```

## Configuration

The application can be configured by modifying the constants in `Program.cs`:

- `startGw` / `endGw`: Gameweek range to analyze
- `maxTeamPrice`: Maximum team price limit
- `complexity`: Analysis complexity level
- `maxPlayersByTeam`: Maximum players allowed from the same team

## License

This project is for educational and personal use in Fantasy Premier League optimization. 