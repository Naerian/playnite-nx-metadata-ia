using System;
using System.Reflection;
using System.Text.RegularExpressions;
using MetaDataIAPlugin;

internal static class SystemRequirementsRunner
{
    private static int failures;

    private static int Main()
    {
        Test_FormatSystemRequirements_ParsesSteamHtml();
        Test_SysReqLabels_NormalizeLanguageAndAsterisks();
        Test_SysReqTokens_RenderAsBoldList();
        Test_BareSysReqTokens_InUserTemplate();
        Test_EmptySysReqKeepsVisiblePlaceholder();
        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static void Test_FormatSystemRequirements_ParsesSteamHtml()
    {
        var html = "<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li><strong>OS:</strong> Windows 10<br></li><li><strong>Processor:</strong> Intel Core i5<br></li><li><strong>Memory:</strong> 8 GB RAM</li></ul>";
        var method = typeof(OfficialStoreDataService).GetMethod("FormatSystemRequirements", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (method == null)
        {
            Fail("FormatSystemRequirements not found");
            return;
        }

        var text = method.Invoke(null, new object[] { html }) as string ?? string.Empty;
        AssertContains("OS", text, "OS:");
        AssertContains("CPU", text, "Intel Core i5");
        AssertContains("RAM", text, "8 GB RAM");
        AssertFalse("heading stripped", Regex.IsMatch(text, @"(?im)^Minimum\b"));
        Pass("Steam HTML requirements parse");
    }

    private static void Test_SysReqLabels_NormalizeLanguageAndAsterisks()
    {
        var html = "<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li><strong>OS *:</strong> Windows 10<br></li><li><strong>Additional Notes:</strong> 64-bit required</li></ul>";
        var method = typeof(OfficialStoreDataService).GetMethod("FormatSystemRequirements", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string), typeof(string) }, null);
        if (method == null)
        {
            Fail("FormatSystemRequirements(html, language) not found");
            return;
        }

        var text = method.Invoke(null, new object[] { html, "es" }) as string ?? string.Empty;
        AssertContains("OS label kept for AI", text, "OS:");
        AssertFalse("asterisk removed", text.Contains("OS *") || text.Contains("OS*:"));
        AssertContains("additional notes kept", text, "Additional Notes:");
        AssertFalse("heading stripped", Regex.IsMatch(text, @"(?im)^Minimum\b"));
        Pass("System requirement HTML is structured and asterisks are dropped");
    }

    private static void Test_SysReqTokens_RenderAsBoldList()
    {
        var replace = typeof(AiMetadataResult).GetMethod("ReplaceSystemRequirementToken", BindingFlags.NonPublic | BindingFlags.Static);
        if (replace == null)
        {
            Fail("ReplaceSystemRequirementToken not found");
            return;
        }

        var settings = new MetaDataIASettings { Language = "en" };
        var description = replace.Invoke(null, new object[]
        {
            "<h3>Min</h3>\n<p>{min_sys_req}</p>\n<h3>Rec</h3>\n<p>{recommended_sys_req}</p>",
            "min_sys_req",
            "OS: Windows 10\nMemory: 8 GB RAM",
            settings
        }) as string;
        description = replace.Invoke(null, new object[]
        {
            description,
            "recommended_sys_req",
            "OS: Windows 11\nMemory: 16 GB RAM",
            settings
        }) as string;

        AssertContains("min list", description, "<ul>");
        AssertContains("min bold", description, "<strong>OS:</strong>");
        AssertContains("min value", description, "Windows 10");
        AssertContains("rec value", description, "16 GB RAM");
        Pass("sys req tokens render as bold HTML list");
    }

    private static void Test_BareSysReqTokens_InUserTemplate()
    {
        var replace = typeof(AiMetadataResult).GetMethod("ReplaceSystemRequirementToken", BindingFlags.NonPublic | BindingFlags.Static);
        if (replace == null)
        {
            Fail("ReplaceSystemRequirementToken not found");
            return;
        }

        var settings = new MetaDataIASettings { Language = "es" };
        var template =
            "<h3>Requisitos mínimos</h3>\n<p>{min_sys_req}</p>\n\n<h3>Requisitos recomendados</h3>\n<p>{recommended_sys_req}</p>";
        var description = replace.Invoke(null, new object[] { template, "min_sys_req", "SO: Windows 10\nMemoria: 8 GB de RAM", settings }) as string;
        description = replace.Invoke(null, new object[] { description, "recommended_sys_req", "SO: Windows 11\nMemoria: 16 GB de RAM", settings }) as string;
        AssertContains("bare min", description, "<strong>SO:</strong>");
        AssertContains("bare rec", description, "Windows 11");
        AssertContains("keeps min heading", description, "Requisitos mínimos");
        AssertFalse("token gone", description.IndexOf("{min_sys_req}", StringComparison.OrdinalIgnoreCase) >= 0);
        Pass("bare sys req tokens expand in user template");
    }

    private static void Test_EmptySysReqKeepsVisiblePlaceholder()
    {
        var replace = typeof(AiMetadataResult).GetMethod("ReplaceSystemRequirementToken", BindingFlags.NonPublic | BindingFlags.Static);
        if (replace == null)
        {
            Fail("ReplaceSystemRequirementToken not found");
            return;
        }

        var settings = new MetaDataIASettings { Language = "es" };
        var template =
            "<h3>Juegos similares</h3>\n<ul><li>A</li></ul>\n\n<h3>Requisitos mínimos</h3>\n<p>{min_sys_req}</p>\n\n<h3>Requisitos recomendados</h3>\n<p>{recommended_sys_req}</p>";
        var description = replace.Invoke(null, new object[] { template, "min_sys_req", "", settings }) as string;
        description = replace.Invoke(null, new object[] { description, "recommended_sys_req", null, settings }) as string;
        AssertContains("keeps heading", description, "Requisitos mínimos");
        AssertContains("placeholder es", description, "requisitos mínimos");
        AssertFalse("token gone", description.IndexOf("{min_sys_req}", StringComparison.OrdinalIgnoreCase) >= 0);
        Pass("empty sys req keeps visible localized placeholder");
    }

    private static void AssertContains(string name, string haystack, string needle)
    {
        if (haystack == null || haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
        {
            Fail(name + " missing '" + needle + "' in: " + haystack);
        }
    }

    private static void AssertFalse(string name, bool condition)
    {
        if (condition) Fail(name);
    }

    private static void Pass(string name)
    {
        Console.WriteLine("[PASS] " + name);
    }

    private static void Fail(string message)
    {
        failures++;
        Console.WriteLine("[FAIL] " + message);
    }
}
