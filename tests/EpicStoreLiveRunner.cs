using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using MetaDataIAPlugin;
using Playnite.SDK.Models;

internal static class EpicStoreLiveRunner
{
    private static int failures;

    private static int Main()
    {
        Test("Fortnite", "https://store.epicgames.com/p/fortnite", true);
        Test("Cyberpunk 2077", null, true);
        Test("Alan Wake 2", null, true);
        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static void Test(string name, string epicUrl, bool expectMedia)
    {
        var settings = new MetaDataIASettings
        {
            Language = "es-ES",
            UseEpicStoreMetadata = true,
            MediaUseEpicStore = true
        };
        var service = new OfficialStoreDataService(settings);
        var game = new Game { Name = name };
        if (!string.IsNullOrWhiteSpace(epicUrl))
        {
            game.Links = new ObservableCollection<Link>
            {
                new Link("Epic Store", epicUrl)
            };
        }

        OfficialStoreMetadata meta = null;
        try
        {
            var contexts = service.GetOfficialContextsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            meta = contexts.FirstOrDefault(x =>
                string.Equals(x.SourceName, OfficialStoreDataService.SourceEpicStore, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Fail(name + " metadata threw " + ex.GetType().Name + ": " + ex.Message);
            return;
        }

        if (meta == null || string.IsNullOrWhiteSpace(meta.Description))
        {
            Fail(name + " missing Epic metadata/description");
            return;
        }

        Console.WriteLine("[PASS] metadata " + name + " title=" + meta.Title);
        Console.WriteLine("       url=" + meta.StoreUrl);
        Console.WriteLine("       publishers=" + string.Join(", ", meta.Publishers ?? Enumerable.Empty<string>()));
        Console.WriteLine("       desc=" + Truncate(meta.Description, 90));

        if (!expectMedia)
        {
            return;
        }

        try
        {
            var covers = service.GetMediaCandidatesAsync(game, MediaKind.Cover, OfficialStoreDataService.SourceEpicStore, CancellationToken.None)
                .GetAwaiter().GetResult();
            var backgrounds = service.GetMediaCandidatesAsync(game, MediaKind.Background, OfficialStoreDataService.SourceEpicStore, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (covers == null || covers.Count == 0 || backgrounds == null || backgrounds.Count == 0)
            {
                Fail(name + " missing media covers=" + (covers == null ? 0 : covers.Count) +
                     " backgrounds=" + (backgrounds == null ? 0 : backgrounds.Count));
                return;
            }

            Console.WriteLine("[PASS] media " + name + " covers=" + covers.Count + " backgrounds=" + backgrounds.Count);
            Console.WriteLine("       cover=" + Truncate(covers[0].Url, 100));
            Console.WriteLine("       bg=" + Truncate(backgrounds[0].Url, 100));
        }
        catch (Exception ex)
        {
            Fail(name + " media threw " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value.Substring(0, max) + "...";
    }

    private static void Fail(string message)
    {
        failures++;
        Console.WriteLine("[FAIL] " + message);
    }
}
