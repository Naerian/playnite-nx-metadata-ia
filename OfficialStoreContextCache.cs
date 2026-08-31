using Playnite.SDK.Models;
using System;
using System.Collections.Generic;

namespace MetaDataIAPlugin
{
    internal static class OfficialStoreContextCache
    {
        private const int MaxEntries = 256;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, List<OfficialStoreMetadata>> Official =
            new Dictionary<string, List<OfficialStoreMetadata>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, OfficialStoreMetadata> Steam =
            new Dictionary<string, OfficialStoreMetadata>(StringComparer.Ordinal);

        public static bool TryGetOfficial(Game game, string language, out List<OfficialStoreMetadata> value)
        {
            return TryGet(Official, Key("official", language, game), out value);
        }

        public static void SetOfficial(Game game, string language, List<OfficialStoreMetadata> value)
        {
            if (value == null || value.Count == 0)
            {
                return;
            }

            Set(Official, Key("official", language, game), value);
        }

        public static bool TryGetSteam(Game game, string language, out OfficialStoreMetadata value)
        {
            return TryGet(Steam, Key("steam", language, game), out value);
        }

        public static void SetSteam(Game game, string language, OfficialStoreMetadata value)
        {
            if (value == null)
            {
                return;
            }

            Set(Steam, Key("steam", language, game), value);
        }

        private static bool TryGet<T>(Dictionary<string, T> store, string key, out T value)
        {
            lock (Gate)
            {
                return store.TryGetValue(key, out value);
            }
        }

        private static void Set<T>(Dictionary<string, T> store, string key, T value)
        {
            lock (Gate)
            {
                if (store.Count >= MaxEntries)
                {
                    store.Clear();
                }

                store[key] = value;
            }
        }

        private static string Key(string kind, string language, Game game)
        {
            if (game == null)
            {
                return kind + "|" + (language ?? string.Empty) + "|";
            }

            return kind + "|" +
                   (language ?? string.Empty) + "|" +
                   game.PluginId + "|" +
                   (game.GameId ?? string.Empty) + "|" +
                   (game.Name ?? string.Empty);
        }
    }
}
