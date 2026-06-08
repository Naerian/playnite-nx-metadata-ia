using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using Playnite.SDK;

namespace MetaDataIAPlugin
{
    public static class PluginLocalization
    {
        private static IPlayniteAPI playniteApi;
        private static readonly ILogger logger = LogManager.GetLogger();
        private static ResourceDictionary englishFallbackResources;

        public static void Initialize(IPlayniteAPI api)
        {
            playniteApi = api;
            EnsureEnglishFallbackResources();
        }

        public static string GetString(string key, string fallback = null)
        {
            var value = playniteApi == null || playniteApi.Resources == null
                ? null
                : playniteApi.Resources.GetString(key);

            return string.IsNullOrWhiteSpace(value) || value == key
                ? (fallback ?? GetEnglishFallbackString(key) ?? key)
                : value;
        }

        private static void EnsureEnglishFallbackResources()
        {
            try
            {
                englishFallbackResources = LoadEnglishFallbackResources();
                if (englishFallbackResources == null || Application.Current == null || Application.Current.Resources == null)
                {
                    return;
                }

                var alreadyLoaded = Application.Current.Resources.MergedDictionaries
                    .OfType<ResourceDictionary>()
                    .Any(a => ReferenceEquals(a, englishFallbackResources) ||
                        a.Contains("MTDA_PluginName") && Equals(a["MTDA_PluginName"], "Metadata IA"));

                if (!alreadyLoaded)
                {
                    Application.Current.Resources.MergedDictionaries.Insert(0, englishFallbackResources);
                }
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.Warn(ex, "Failed to load Metadata IA English fallback resources.");
                }
            }
        }

        private static ResourceDictionary LoadEnglishFallbackResources()
        {
            var assemblyDirectory = Path.GetDirectoryName(typeof(PluginLocalization).Assembly.Location);
            if (string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                return null;
            }

            var path = Path.Combine(assemblyDirectory, "Localization", "en_US.xaml");
            if (!File.Exists(path))
            {
                return null;
            }

            using (var stream = File.OpenRead(path))
            {
                return XamlReader.Load(stream) as ResourceDictionary;
            }
        }

        private static string GetEnglishFallbackString(string key)
        {
            if (englishFallbackResources == null)
            {
                englishFallbackResources = LoadEnglishFallbackResources();
            }

            if (englishFallbackResources != null && englishFallbackResources.Contains(key))
            {
                var value = englishFallbackResources[key];
                return value == null ? null : value.ToString();
            }

            return null;
        }
    }
}
