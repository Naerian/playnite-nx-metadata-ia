using System;
using System.Collections.Generic;
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
        private static readonly Dictionary<string, ResourceDictionary> languageResources =
            new Dictionary<string, ResourceDictionary>(StringComparer.OrdinalIgnoreCase);

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

        public static string GetStringForLanguage(string key, string language, string fallback = null)
        {
            var resources = LoadLanguageResources(language);
            if (resources != null && resources.Contains(key))
            {
                var localized = resources[key];
                if (localized != null && !string.IsNullOrWhiteSpace(localized.ToString()))
                {
                    return localized.ToString();
                }
            }

            return GetString(key, fallback);
        }

        private static void EnsureEnglishFallbackResources()
        {
            try
            {
                englishFallbackResources = LoadLocalizationFile("en_US.xaml");
                if (englishFallbackResources == null || Application.Current == null || Application.Current.Resources == null)
                {
                    return;
                }

                var alreadyLoaded = Application.Current.Resources.MergedDictionaries
                    .OfType<ResourceDictionary>()
                    .Any(a => ReferenceEquals(a, englishFallbackResources) ||
                        a.Contains("MTDA_PluginName") && Equals(a["MTDA_PluginName"], "Metadata AI"));

                if (!alreadyLoaded)
                {
                    Application.Current.Resources.MergedDictionaries.Insert(0, englishFallbackResources);
                }
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.Warn(ex, "Failed to load Metadata AI English fallback resources.");
                }
            }
        }

        private static ResourceDictionary LoadLanguageResources(string language)
        {
            var fileName = LocalizationFileName(language);
            ResourceDictionary cached;
            if (languageResources.TryGetValue(fileName, out cached))
            {
                return cached;
            }

            var loaded = LoadLocalizationFile(fileName);
            languageResources[fileName] = loaded;
            return loaded;
        }

        private static string LocalizationFileName(string language)
        {
            var code = (language ?? string.Empty).Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;
            code = code.Trim().ToLowerInvariant();
            switch (code)
            {
                case "es": return "es_ES.xaml";
                case "de": return "de_DE.xaml";
                case "fr": return "fr_FR.xaml";
                case "it": return "it_IT.xaml";
                case "pt":
                case "br": return "pt_BR.xaml";
                case "ru": return "ru_RU.xaml";
                case "pl": return "pl_PL.xaml";
                case "ja": return "ja_JP.xaml";
                case "ko": return "ko_KR.xaml";
                case "zh": return "zh_CN.xaml";
                default: return "en_US.xaml";
            }
        }

        private static ResourceDictionary LoadLocalizationFile(string fileName)
        {
            var assemblyDirectory = Path.GetDirectoryName(typeof(PluginLocalization).Assembly.Location);
            if (string.IsNullOrWhiteSpace(assemblyDirectory) || string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var path = Path.Combine(assemblyDirectory, "Localization", fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            using (var stream = File.OpenRead(path))
            {
                return XamlReader.Load(stream) as ResourceDictionary;
            }
        }

        private static ResourceDictionary LoadEnglishFallbackResources()
        {
            return LoadLocalizationFile("en_US.xaml");
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
