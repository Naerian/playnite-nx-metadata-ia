using Playnite.SDK;

namespace MetaDataIAPlugin
{
    public static class PluginLocalization
    {
        private static IPlayniteAPI playniteApi;

        public static void Initialize(IPlayniteAPI api)
        {
            playniteApi = api;
        }

        public static string GetString(string key, string fallback = null)
        {
            var value = playniteApi == null || playniteApi.Resources == null
                ? null
                : playniteApi.Resources.GetString(key);

            return string.IsNullOrWhiteSpace(value) || value == key
                ? (fallback ?? key)
                : value;
        }
    }
}
