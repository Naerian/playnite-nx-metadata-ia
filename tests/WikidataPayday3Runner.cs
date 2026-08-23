using MetaDataIAPlugin;
using Newtonsoft.Json.Linq;
using Playnite.SDK.Models;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Reproduces the Wikidata "Can not convert Object to String" failure and
/// verifies PAYDAY 3 context fetch no longer throws.
/// </summary>
internal static class WikidataPayday3Runner
{
    private static int failures;

    private static int Main()
    {
        Console.WriteLine("Wikidata Object→String / PAYDAY 3 checks");
        Console.WriteLine("======================================");

        Test_OldMatchCastThrows();
        Test_TokenTextReadsMatchObject();
        Test_LivePayday3DoesNotThrow().GetAwaiter().GetResult();

        if (failures == 0)
        {
            Console.WriteLine();
            Console.WriteLine("ALL PASSED");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("FAILED: " + failures);
        return 1;
    }

    private static void Test_OldMatchCastThrows()
    {
        // Exact shape returned by wbsearchentities for "match".
        var match = JObject.Parse("{\"type\":\"label\",\"language\":\"en\",\"text\":\"PAYDAY 3\"}");
        try
        {
            var unused = (string)match;
            Fail("legacy (string)matchObject should throw, got: " + unused);
        }
        catch (ArgumentException)
        {
            Pass("legacy (string)JObject throws (reproduces user error)");
        }
        catch (InvalidCastException)
        {
            Pass("legacy (string)JObject throws (reproduces user error)");
        }
        catch (Exception ex)
        {
            // Newtonsoft typically: InvalidCastException / ArgumentException with this message.
            if ((ex.Message ?? string.Empty).IndexOf("Object to String", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (ex.Message ?? string.Empty).IndexOf("Can not convert", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Pass("legacy cast fails with Object→String: " + ex.GetType().Name);
            }
            else
            {
                Fail("unexpected exception: " + ex.GetType().Name + " / " + ex.Message);
            }
        }
    }

    private static void Test_TokenTextReadsMatchObject()
    {
        var searchHit = JObject.Parse(
            "{\"id\":\"Q118947053\",\"label\":\"PAYDAY 3\",\"match\":{\"type\":\"label\",\"language\":\"en\",\"text\":\"PAYDAY 3\"}}");

        // Mirror the fixed extraction path used by WikidataMetadataService.
        var label = SafeTokenText(searchHit["label"]);
        var matchObj = SafeTokenText(searchHit["match"]);
        var matchText = SafeTokenText(searchHit.SelectToken("match.text"));
        var id = SafeTokenText(searchHit["id"]);

        AssertEqual("label", "PAYDAY 3", label);
        AssertEqual("match object → text", "PAYDAY 3", matchObj);
        AssertEqual("match.text", "PAYDAY 3", matchText);
        AssertEqual("id", "Q118947053", id);
    }

    private static async Task Test_LivePayday3DoesNotThrow()
    {
        try
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |=
                    (System.Net.SecurityProtocolType)3072 |
                    (System.Net.SecurityProtocolType)768 |
                    System.Net.SecurityProtocolType.Tls;
            }
            catch
            {
            }

            var asm = typeof(MetaDataIASettings).Assembly;
            var type = asm.GetType("MetaDataIAPlugin.WikidataMetadataService", throwOnError: true);
            var service = Activator.CreateInstance(type, nonPublic: true);
            var method = type.GetMethod("GetContextAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? type.GetMethod("GetContextAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                Fail("GetContextAsync/GetContextAsync not found on WikidataMetadataService. Methods: " +
                     string.Join(", ", type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(m => m.Name).Distinct()));
                return;
            }

            var game = new Game("PAYDAY 3");
            var task = (Task)method.Invoke(service, new object[] { game, CancellationToken.None });
            await task.ConfigureAwait(false);
            var resultProperty = task.GetType().GetProperty("Result");
            var result = resultProperty == null ? null : resultProperty.GetValue(task);

            if (result == null)
            {
                // No exact video-game match is acceptable; the bug was throwing.
                Pass("live PAYDAY 3 fetch completed without throw (null context)");
                return;
            }

            var title = result.GetType().GetProperty("Title").GetValue(result) as string;
            var source = result.GetType().GetProperty("SourceName").GetValue(result) as string;
            Pass("live PAYDAY 3 fetch ok — source=" + source + ", title=" + title);
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            Fail("live PAYDAY 3 threw: " + inner.GetType().Name + " — " + inner.Message);
        }
    }

    // Same rules as CommunityMetadataServices.TokenText (kept local so this runner
    // can validate extraction without InternalsVisibleTo on private methods).
    private static string SafeTokenText(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
        {
            return null;
        }

        if (token.Type == JTokenType.String || token.Type == JTokenType.Integer ||
            token.Type == JTokenType.Float || token.Type == JTokenType.Boolean ||
            token.Type == JTokenType.Guid || token.Type == JTokenType.Uri ||
            token.Type == JTokenType.Date)
        {
            return token.ToString();
        }

        if (token.Type == JTokenType.Object)
        {
            return SafeTokenText(token["text"]) ?? SafeTokenText(token["value"]) ??
                   SafeTokenText(token["name"]) ?? SafeTokenText(token["id"]) ??
                   SafeTokenText(token["time"]);
        }

        return null;
    }

    private static void AssertEqual(string name, string expected, string actual)
    {
        if (string.Equals(expected ?? string.Empty, actual ?? string.Empty, StringComparison.Ordinal))
        {
            Pass(name);
            return;
        }

        Fail(name + " expected=[" + expected + "] actual=[" + actual + "]");
    }

    private static void Pass(string name)
    {
        Console.WriteLine("[PASS] " + name);
    }

    private static void Fail(string name)
    {
        failures++;
        Console.WriteLine("[FAIL] " + name);
    }
}
