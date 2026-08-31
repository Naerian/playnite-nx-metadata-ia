using System;
using System.Reflection;
using System.Threading;
using MetaDataIAPlugin;
using Playnite.SDK.Models;

internal static class SteamSysReqLiveRunner
{
    private static int failures;

    private static int Main()
    {
        TestGame("Palworld", "1623730", true);
        TestGame("ARC Raiders", "1808500", true);
        TestGame("Palworld", null, true);
        TestGame("Arc Raiders", null, true);
        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static void TestGame(string name, string gameId, bool expectSteamSource)
    {
        var settings = new MetaDataIASettings { Language = "es-ES" };
        var service = new OfficialStoreDataService(settings);
        var game = new Game
        {
            Name = name,
            GameId = gameId ?? string.Empty
        };
        if (expectSteamSource && !string.IsNullOrWhiteSpace(gameId))
        {
            // Playnite Steam library plugin id. Source is read-only on Game.
            game.PluginId = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        }

        OfficialStoreMetadata meta = null;
        try
        {
            meta = service.TryGetSteamContextAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Fail(name + " threw " + ex.GetType().Name + ": " + ex.Message);
            return;
        }

        if (meta == null)
        {
            Fail(name + " (id=" + (gameId ?? "search") + ") returned null Steam metadata");
            return;
        }

        if (string.IsNullOrWhiteSpace(meta.MinimumSystemRequirements))
        {
            Fail(name + " missing minimum requirements");
            return;
        }

        if (string.IsNullOrWhiteSpace(meta.RecommendedSystemRequirements))
        {
            Fail(name + " missing recommended requirements");
            return;
        }

        Console.WriteLine("[PASS] " + name + " id=" + (gameId ?? "search") +
                          " minChars=" + meta.MinimumSystemRequirements.Length +
                          " recChars=" + meta.RecommendedSystemRequirements.Length);
        Console.WriteLine("       min: " + FirstLine(meta.MinimumSystemRequirements));
    }

    private static string FirstLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var line = value.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
        return line.Length > 90 ? line.Substring(0, 90) + "..." : line;
    }

    private static void Fail(string message)
    {
        failures++;
        Console.WriteLine("[FAIL] " + message);
    }
}
