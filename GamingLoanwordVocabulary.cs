using System;
using System.Collections.Generic;
using System.Linq;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Industry gaming loanwords and acronyms that stay untranslated in every
    /// plugin language. Defaults ship with the product; the Library setting
    /// <see cref="MetaDataIASettings.KeptLoanwords"/> is the editable source of
    /// truth used by organize prompts and by <see cref="TermFieldResolver"/>
    /// (strip / translation-retry / alias enforcement). Exact match after
    /// <see cref="LibraryNameMatching.NormalizeKey"/>, plus optional locale aliases
    /// so a keep-list entry like Action replaces Acción/Aktion/….
    /// </summary>
    public static class GamingLoanwordVocabulary
    {
        /// <summary>
        /// Canonical default keep-list. Order is display order in settings.
        /// </summary>
        public static readonly string[] DefaultTerms =
        {
            "Indie",
            "Party",
            "Roguelike",
            "Roguelite",
            "Metroidvania",
            "Soulslike",
            "Hack and slash",
            "Battle Royale",
            "MOBA",
            "Deckbuilder",
            "Sandbox",
            "Auto Battler",
            "Bullet Hell",
            "RPG",
            "JRPG",
            "ARPG",
            "MMORPG",
            "MMO",
            "RTS",
            "FPS",
            "TPS",
            "PvP",
            "PvE"
        };

        /// <summary>
        /// Known store/IGDB locale variants of the same concept. Used only when the
        /// user puts one form on the keep-list (e.g. Action) so a model/store
        /// translation (Acción) is rewritten to that keep spelling.
        /// </summary>
        private static readonly string[][] LocaleAliasGroups =
        {
            new[] { "Action", "Acción", "Aktion", "Azione", "Ação", "Akcja", "Actie", "Действие", "アクション", "액션", "动作", "動作" },
            new[] { "Adventure", "Aventura", "Abenteuer", "Avventura", "Aventura", "Przygoda", "Avontuur", "Приключение", "アドベンチャー", "어드벤처", "冒险", "冒險" },
            new[] { "Strategy", "Estrategia", "Strategie", "Strategia", "Estratégia", "Strategia", "Strategie", "Стратегия", "ストラテジー", "전략", "策略" },
            new[] { "Shooter", "Disparos", "Shooter", "Sparatutto", "Tiro", "Strzelanka", "Shooter", "Шутер", "シューター", "슈팅", "射击", "射擊" },
            new[] { "Puzzle", "Puzle", "Rompecabezas", "Puzzle", "Rompicapo", "Quebra-cabeça", "Łamigłówka", "Puzzel", "Головоломка", "パズル", "퍼즐", "益智" },
            new[] { "Racing", "Carreras", "Rennen", "Corse", "Corrida", "Wyścigi", "Racen", "Гонки", "レース", "레이싱", "竞速", "競速" },
            new[] { "Simulation", "Simulación", "Simulation", "Simulazione", "Simulação", "Symulacja", "Simulatie", "Симулятор", "シミュレーション", "시뮬레이션", "模拟", "模擬" },
            new[] { "Sports", "Deportes", "Sport", "Sport", "Esportes", "Sport", "Sport", "Спорт", "スポーツ", "스포츠", "体育", "體育" },
            new[] { "Fighting", "Lucha", "Kampf", "Picchiaduro", "Luta", "Bijatyka", "Vechtspel", "Файтинг", "対戦格闘", "격투", "格斗", "格鬥" },
            new[] { "Platform", "Plataformas", "Platformer", "Platform", "Plataforma", "Platformówka", "Platform", "Платформер", "プラットフォーマー", "플랫포머", "平台", "平臺" },
            new[] { "Horror", "Terror", "Horror", "Horror", "Terror", "Horror", "Horror", "Хоррор", "ホラー", "호러", "恐怖" },
            new[] { "Casual", "Casual", "Casual", "Casual", "Casual", "Casual", "Casual", "Казуальная", "カジュアル", "캐주얼", "休闲", "休閒" },
            new[] { "PvP", "JcJ", "JcJ en línea", "JcJ en linea", "JcJ en LAN", "Player vs Player", "Player versus Player", "Jugador contra jugador" },
            new[] { "PvE", "JcE", "JcE en línea", "JcE en linea", "Player vs Environment", "Player versus Environment", "Jugador contra el entorno", "Jugador contra entorno" }
        };

        public static string FormatDefaultList()
        {
            return string.Join(", ", DefaultTerms);
        }

        public static List<string> ParseTerms(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw
                .Replace("\r", string.Empty)
                .Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(LibraryNameMatching.NormalizeKey)
                .Where(g => g.Key.Length > 0)
                .Select(g => g.First())
                .ToList();
        }

        public static HashSet<string> ToKeySet(IEnumerable<string> terms)
        {
            return new HashSet<string>(
                (terms ?? Enumerable.Empty<string>())
                    .Select(LibraryNameMatching.NormalizeKey)
                    .Where(key => key.Length > 0),
                StringComparer.Ordinal);
        }

        public static bool IsKept(string value, IEnumerable<string> keepTerms)
        {
            return IsKept(value, ToKeySet(keepTerms));
        }

        public static bool IsKept(string value, HashSet<string> keepKeys)
        {
            if (keepKeys == null || keepKeys.Count == 0 || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return keepKeys.Contains(LibraryNameMatching.NormalizeKey(value));
        }

        public static string CanonicalSpelling(string value, IEnumerable<string> keepTerms)
        {
            if (string.IsNullOrWhiteSpace(value) || keepTerms == null)
            {
                return value;
            }

            var key = LibraryNameMatching.NormalizeKey(value);
            if (key.Length == 0)
            {
                return value;
            }

            foreach (var term in keepTerms)
            {
                if (string.Equals(LibraryNameMatching.NormalizeKey(term), key, StringComparison.Ordinal))
                {
                    return term;
                }
            }

            return value;
        }

        /// <summary>
        /// Rewrites locale aliases of keep-list terms to the keep-list spelling and
        /// reinstates keep-list Incoming labels the model translated away.
        /// Example: keep-list has Action, response has Acción → Action.
        /// </summary>
        public static List<string> EnforceKeepListSpelling(
            IEnumerable<string> values,
            TermFieldRequest field,
            IEnumerable<string> keepTerms)
        {
            var cleaned = (values ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();
            var keepList = (keepTerms ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();
            if (keepList.Count == 0)
            {
                return cleaned;
            }

            var incoming = field == null ? new List<string>() : (field.Existing ?? new List<string>())
                .Concat(field.Incoming ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            foreach (var keepTerm in keepList)
            {
                var preferred = keepTerm.Trim();
                var aliasKeys = AliasKeysFor(preferred);
                var presentInInput = incoming.Any(t => aliasKeys.Contains(LibraryNameMatching.NormalizeKey(t)));
                var presentInOutput = cleaned.Any(t => aliasKeys.Contains(LibraryNameMatching.NormalizeKey(t)));
                if (!presentInInput && !presentInOutput)
                {
                    continue;
                }

                cleaned = cleaned
                    .Where(t => !aliasKeys.Contains(LibraryNameMatching.NormalizeKey(t)))
                    .ToList();
                cleaned.Add(preferred);
            }

            // Preserve relative order: preferred keep terms that replaced aliases stay,
            // distinct case-insensitive.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            foreach (var term in cleaned)
            {
                if (seen.Add(term))
                {
                    ordered.Add(term);
                }
            }

            return ordered;
        }

        public static HashSet<string> AliasKeysFor(string keepTerm)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var preferredKey = LibraryNameMatching.NormalizeKey(keepTerm);
            if (preferredKey.Length == 0)
            {
                return keys;
            }

            keys.Add(preferredKey);
            foreach (var group in LocaleAliasGroups)
            {
                var groupKeys = group.Select(LibraryNameMatching.NormalizeKey).Where(k => k.Length > 0).ToList();
                if (!groupKeys.Contains(preferredKey))
                {
                    continue;
                }

                foreach (var key in groupKeys)
                {
                    keys.Add(key);
                }
            }

            return keys;
        }

        public static bool MatchesKeepTermIncludingAliases(string value, string keepTerm)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(keepTerm))
            {
                return false;
            }

            return AliasKeysFor(keepTerm).Contains(LibraryNameMatching.NormalizeKey(value));
        }
    }
}
