using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Events;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Text.Json.Serialization;

namespace ChatLogs;

public class ChatLogsConfig : BasePluginConfig
{

    [JsonPropertyName("DatabaseHost")] public string DatabaseHost { get; set; } = "";
    [JsonPropertyName("DatabasePort")] public int DatabasePort { get; set; } = 3306;
    [JsonPropertyName("DatabaseUser")] public string DatabaseUser { get; set; } = "";
    [JsonPropertyName("DatabasePassword")] public string DatabasePassword { get; set; } = "";
    [JsonPropertyName("DatabaseName")] public string DatabaseName { get; set; } = "";
    [JsonPropertyName("IgnoreCommands")] public bool IgnoreCommands { get; set; } = true;

}
public class ChatLogs : BasePlugin, IPluginConfig<ChatLogsConfig>
{
    public override string ModuleName => "ChatLogs";
    public override string ModuleDescription => "Store chat messages to MySQL database";
    public override string ModuleAuthor => "verneri";
    public override string ModuleVersion => "1.1";

    public ChatLogsConfig Config { get; set; } = new();

    public void OnConfigParsed(ChatLogsConfig config)
    {
        Config = config;
    }
    public override void Load(bool hotReload)
    {
        Logger.LogInformation($"loaded successfully! (Version {ModuleVersion})");
        RegisterEventHandler<EventPlayerChat>(OnEventPlayerChat);
        CreateTable();
    }

    private string GetConnectionString()
    {
        if (string.IsNullOrWhiteSpace(Config.DatabaseHost) || string.IsNullOrWhiteSpace(Config.DatabaseName) || string.IsNullOrWhiteSpace(Config.DatabaseUser) || string.IsNullOrWhiteSpace(Config.DatabasePassword))
        {
            Logger.LogError("Database configuration is incomplete. Check the configuration file.");
        }

        var builder = new MySqlConnectionStringBuilder
        {
            Server = Config.DatabaseHost,
            Database = Config.DatabaseName,
            UserID = Config.DatabaseUser,
            Password = Config.DatabasePassword,
            Port = (uint)Config.DatabasePort
        };
        return builder.ConnectionString;
    }

    private void CreateTable()
    {
        try
        {
            using var connection = new MySqlConnection(GetConnectionString());
            connection.Open();

            string createTableQuery = @"
            CREATE TABLE IF NOT EXISTS chatlogs (
                timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                team VARCHAR(50) NOT NULL,
                playername VARCHAR(255) NOT NULL,
                steamid VARCHAR(255) NOT NULL,
                message TEXT NOT NULL,
                PRIMARY KEY (steamid, timestamp)
            )";

            using var command = new MySqlCommand(createTableQuery, connection);
            command.ExecuteNonQuery();

            Logger.LogInformation("Table 'chatlogs' was created or already exists.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create table 'chatlogs': {ex.Message}");
        }
    }

    public HookResult OnEventPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        var eventplayer = @event.Userid;
        var player = Utilities.GetPlayerFromUserid(eventplayer);
        if (player == null || !player.IsValid ||  @event.Text == null)
            return HookResult.Continue;

        if (Config.IgnoreCommands)
        {
            if (@event.Text.StartsWith('!') || @event.Text.StartsWith('/'))
                return HookResult.Continue;
        }

        string playerTeam = "[ALL]";
        if (@event.Teamonly)
        {
            playerTeam = player.TeamNum switch
            {
                (int)CsTeam.Terrorist => "[T]",
                (int)CsTeam.CounterTerrorist => "[CT]",
                (int)CsTeam.Spectator => "[SPEC]",
                _ => "[NONE]"
            };
        }

        try
        {
            using (var connection = new MySqlConnection(GetConnectionString()))
            {
                connection.Open();

                string insertQuery = @"
                INSERT INTO chatlogs (team, playername, steamid, message)
                VALUES (@team, @playername, @steamid, @message)";

                using var command = new MySqlCommand(insertQuery, connection);
                command.Parameters.AddWithValue("@team", playerTeam);
                command.Parameters.AddWithValue("@playername", player.PlayerName);
                command.Parameters.AddWithValue("@steamid", player.SteamID);
                command.Parameters.AddWithValue("@message", @event.Text);
                command.ExecuteNonQuery();

            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ERROR] An unexpected error occurred: {ex.Message}");
            return HookResult.Continue;
        }

        return HookResult.Continue;
    }

}