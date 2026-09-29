using System;
using System.Linq;
using System.Threading;
using MetaDataIAPlugin;
using Playnite.SDK.Models;

internal static class PsnStoreLiveRunner
{
    private static int failures;

    private static int Main()
    {
        TestAstroBot();
        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static void TestAstroBot()
    {
        var settings = new MetaDataIASettings
        {
            Language = "es-ES",
            UsePsnStoreMetadata = true,
            MediaUsePsnStore = true
        };
        var service = new OfficialStoreDataService(settings);
        var game = new Game { Name = "Astro Bot" };

        OfficialStoreMetadata meta = null;
        try
        {
            var contexts = service.GetOfficialContextsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            meta = contexts.FirstOrDefault(x =>
                string.Equals(x.SourceName, OfficialStoreDataService.SourcePsnStore, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Fail("metadata threw " + ex.GetType().Name + ": " + ex.Message);
            return;
        }

        if (meta == null)
        {
            Fail("Astro Bot returned no PlayStation Store metadata context");
            return;
        }

        if (string.IsNullOrWhiteSpace(meta.StoreUrl) ||
            meta.StoreUrl.IndexOf("store.playstation.com", StringComparison.OrdinalIgnoreCase) < 0)
        {
            Fail("missing store URL: " + (meta.StoreUrl ?? "(null)"));
            return;
        }

        if (string.IsNullOrWhiteSpace(meta.Description))
        {
            Fail("missing description");
            return;
        }

        if (meta.Publishers == null || meta.Publishers.Count == 0)
        {
            Fail("missing publisher");
            return;
        }

        if (string.IsNullOrWhiteSpace(meta.ReleaseDate))
        {
            Fail("missing release date");
            return;
        }

        Console.WriteLine("[PASS] metadata title=" + meta.Title);
        Console.WriteLine("       url=" + meta.StoreUrl);
        Console.WriteLine("       genres=" + string.Join(", ", meta.Genres ?? Enumerable.Empty<string>()));
        Console.WriteLine("       publishers=" + string.Join(", ", meta.Publishers));
        Console.WriteLine("       release=" + meta.ReleaseDate);
        Console.WriteLine("       desc=" + Truncate(meta.Description, 90));

        try
        {
            var covers = service.GetMediaCandidatesAsync(game, MediaKind.Cover, OfficialStoreDataService.SourcePsnStore, CancellationToken.None)
                .GetAwaiter().GetResult();
            var backgrounds = service.GetMediaCandidatesAsync(game, MediaKind.Background, OfficialStoreDataService.SourcePsnStore, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (covers == null || covers.Count == 0)
            {
                Fail("no cover candidates");
                return;
            }

            if (backgrounds == null || backgrounds.Count == 0)
            {
                Fail("no background candidates");
                return;
            }

            Console.WriteLine("[PASS] media covers=" + covers.Count + " backgrounds=" + backgrounds.Count);
            Console.WriteLine("       cover=" + Truncate(covers[0].Url, 100));
            Console.WriteLine("       bg=" + Truncate(backgrounds[0].Url, 100));
        }
        catch (Exception ex)
        {
            Fail("media threw " + ex.GetType().Name + ": " + ex.Message);
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
