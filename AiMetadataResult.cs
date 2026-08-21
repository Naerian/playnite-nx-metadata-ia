using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace MetaDataIAPlugin
{
    public class AiMetadataLink
    {
        public string Name { get; set; }
        public string Url { get; set; }

        public AiMetadataLink()
        {
        }

        public AiMetadataLink(string name, string url)
        {
            Name = name;
            Url = url;
        }
    }

    public class AiMetadataResult
    {
        public List<MetadataFieldProvenance> Provenance { get; set; }
        public List<MetadataFieldConflict> Conflicts { get; set; }
        public string Short { get; set; }
        public string Synopsis { get; set; }
        public string Premise { get; set; }
        public string Gameplay { get; set; }
        public string Tone { get; set; }
        public string Setting { get; set; }
        public string Perspective { get; set; }
        public string PlayModes { get; set; }
        public string EstimatedLength { get; set; }
        public string SimilarGames { get; set; }
        public List<string> SimilarGamesList { get; set; }
        public string Notes { get; set; }
        public List<string> Features { get; set; }
        public string RecommendedFor { get; set; }
        public List<string> Genres { get; set; }
        public List<string> Tags { get; set; }
        public List<string> Developers { get; set; }
        public List<string> Publishers { get; set; }
        public List<string> AgeRatings { get; set; }
        public List<string> Regions { get; set; }
        public List<string> Categories { get; set; }
        public List<AiMetadataLink> Links { get; set; }
        public string ReleaseDate { get; set; }
        public List<string> Series { get; set; }
        public string SortingName { get; set; }
        public string Description { get; set; }

        public AiMetadataResult()
        {
            Provenance = new List<MetadataFieldProvenance>();
            Conflicts = new List<MetadataFieldConflict>();
            Features = new List<string>();
            Genres = new List<string>();
            Tags = new List<string>();
            Developers = new List<string>();
            Publishers = new List<string>();
            AgeRatings = new List<string>();
            Regions = new List<string>();
            Categories = new List<string>();
            Links = new List<AiMetadataLink>();
            Series = new List<string>();
            SimilarGamesList = new List<string>();
        }

        public void Normalize(MetaDataIASettings settings, Playnite.SDK.Models.Game game)
        {
            var blacklist = settings.GetBlacklistTerms();
            Short = Clean(Short);
            Synopsis = Clean(Synopsis);
            Synopsis = EnsureParagraphCount(Synopsis, settings.SynopsisLength);
            Premise = Clean(Premise);
            Gameplay = Clean(Gameplay);
            Tone = Clean(Tone);
            Setting = Clean(Setting);
            Perspective = Clean(Perspective);
            PlayModes = Clean(PlayModes);
            EstimatedLength = Clean(EstimatedLength);
            SimilarGames = Clean(SimilarGames);
            SimilarGamesList = (SimilarGamesList ?? new List<string>())
                .Select(x => Clean(x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (SimilarGamesList.Count == 0)
            {
                SimilarGamesList = ParseSimilarGamesText(SimilarGames);
            }
            Notes = Clean(Notes);
            RecommendedFor = Clean(RecommendedFor);
            Features = CleanList(Features, settings.MaxFeatures, blacklist, string.Empty)
                .Select(CleanFeature)
                .Select(x => Canonicalize(x, FeatureAliases))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxFeatures)
                .ToList();
            Genres = CleanList(Genres, settings.MaxGenres, blacklist, string.Empty)
                .Select(x => Canonicalize(x, GenreAliases))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxGenres)
                .ToList();
            Tags = CleanList(Tags, settings.MaxTags, blacklist, settings.TagPrefix)
                .Select(x => CanonicalizePrefixed(x, settings.TagPrefix, TagAliases))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxTags)
                .ToList();
            Developers = CleanList(Developers, settings.MaxDevelopers, blacklist, string.Empty);
            Publishers = CleanList(Publishers, settings.MaxPublishers, blacklist, string.Empty);
            AgeRatings = CleanList(AgeRatings, settings.MaxAgeRatings, blacklist, string.Empty)
                .Select(x => Canonicalize(x, AgeRatingAliases))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxAgeRatings)
                .ToList();
            Regions = CleanList(Regions, settings.MaxRegions, blacklist, string.Empty);
            Categories = CleanList(Categories, settings.MaxCategories, blacklist, settings.CategoryPrefix)
                .Select(x => CanonicalizePrefixed(x, settings.CategoryPrefix, CategoryAliases))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxCategories)
                .ToList();
            Links = CleanLinks(Links, settings.MaxLinks);
            ReleaseDate = Clean(ReleaseDate);
            Series = CleanList(Series, settings.MaxSeries, blacklist, string.Empty);
            AddReliableSourceLinks(game, settings);
            EnsureFeatureFallback(settings, game);
            Description = BuildDescription(settings, game);
        }

        private void AddReliableSourceLinks(Playnite.SDK.Models.Game game, MetaDataIASettings settings)
        {
            if (!settings.GenerateLinks || game == null || game.Source == null || string.IsNullOrWhiteSpace(game.GameId))
            {
                return;
            }

            var source = game.Source.Name ?? string.Empty;
            if (source.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0 && Regex.IsMatch(game.GameId, @"^\d+$"))
            {
                AddLinkIfMissing("Steam", "https://store.steampowered.com/app/" + game.GameId + "/");
            }
        }

        private void AddLinkIfMissing(string name, string url)
        {
            if (string.IsNullOrWhiteSpace(url) ||
                Links.Any(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Links.Add(new AiMetadataLink(name, url));
        }

        private void EnsureFeatureFallback(MetaDataIASettings settings, Playnite.SDK.Models.Game game)
        {
            if (Features.Count > 0)
            {
                return;
            }

            var template = settings.ResolveTemplate(game) ?? string.Empty;
            var templateNeedsFeatures = Regex.IsMatch(template, @"\{features\}|\{feature_\d+\}", RegexOptions.IgnoreCase);
            if (!settings.GenerateFeatures && !templateNeedsFeatures)
            {
                return;
            }

            var fallback = new List<string>();
            if (game != null && game.Features != null)
            {
                foreach (var feature in game.Features)
                {
                    if (feature != null && !string.IsNullOrWhiteSpace(feature.Name))
                    {
                        fallback.Add(feature.Name);
                    }
                }
            }

            AddFallback(fallback, Gameplay);
            AddFallback(fallback, PlayModes);
            AddFallback(fallback, Perspective);
            AddFallback(fallback, Setting);
            fallback.AddRange(Genres);
            fallback.AddRange(Tags);

            Features = fallback
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, settings.MaxFeatures))
                .ToList();
        }

        private static void AddFallback(List<string> values, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        private string BuildDescription(MetaDataIASettings settings, Playnite.SDK.Models.Game game)
        {
            var template = settings.ResolveTemplate(game) ?? string.Empty;
            var description = Regex.Replace(template, @"\{feature_N\}", "{feature_1}", RegexOptions.IgnoreCase);
            description = Regex.Replace(description, @"\{similar_game_N\}", "{similar_game_1}", RegexOptions.IgnoreCase);

            description = ReplaceHtmlTextToken(description, "short", Short);
            description = ReplaceHtmlTextToken(description, "synopsis", Synopsis);
            description = ReplaceHtmlTextToken(description, "premise", Premise);
            description = ReplaceHtmlTextToken(description, "gameplay", Gameplay);
            description = ReplaceHtmlTextToken(description, "tone", Tone);
            description = ReplaceHtmlTextToken(description, "setting", Setting);
            description = ReplaceHtmlTextToken(description, "perspective", Perspective);
            description = ReplaceHtmlTextToken(description, "playModes", PlayModes);
            description = ReplaceHtmlTextToken(description, "estimatedLength", EstimatedLength);
            description = ReplaceHtmlTextToken(description, "similarGames", SimilarGames);
            description = ReplaceHtmlTextToken(description, "notes", Notes);
            description = ReplaceHtmlTextToken(description, "recommendedFor", RecommendedFor);

            description = ReplaceHtmlListToken(description, "genres", Genres);
            description = ReplaceHtmlListToken(description, "tags", Tags);
            description = ReplaceHtmlListToken(description, "developers", Developers);
            description = ReplaceHtmlListToken(description, "publishers", Publishers);
            description = ReplaceHtmlListToken(description, "ageRatings", AgeRatings);
            description = ReplaceHtmlListToken(description, "regions", Regions);
            description = ReplaceHtmlListToken(description, "categories", Categories);
            description = ReplaceHtmlListToken(description, "features", Features);

            for (int i = 0; i < Features.Count; i++)
            {
                description = ReplaceHtmlTextToken(description, "feature_" + (i + 1), Features[i]);
            }
            description = Regex.Replace(description, @"<li\b[^>]*>\s*\{feature_\d+\}\s*</li>\s*", string.Empty, RegexOptions.IgnoreCase);
            description = Regex.Replace(description, @"\{feature_\d+\}", string.Empty, RegexOptions.IgnoreCase);

            var similarList = SimilarGamesList != null && SimilarGamesList.Count > 0
                ? SimilarGamesList
                : ParseSimilarGamesText(SimilarGames);
            for (int i = 0; i < similarList.Count; i++)
            {
                description = ReplaceHtmlTextToken(description, "similar_game_" + (i + 1), similarList[i]);
            }
            description = Regex.Replace(description, @"<li\b[^>]*>\s*\{similar_game_\d+\}\s*</li>\s*", string.Empty, RegexOptions.IgnoreCase);
            description = Regex.Replace(description, @"\{similar_game_\d+\}", string.Empty, RegexOptions.IgnoreCase);

            return description.Trim();
        }

        private static string ReplaceHtmlTextToken(string template, string token, string value)
        {
            var tokenPattern = Regex.Escape("{" + token + "}");
            var paragraphs = FormatHtmlParagraphs(value);
            var inline = FormatHtmlInline(value);
            var listItem = string.IsNullOrWhiteSpace(value) ? string.Empty : "<li>" + inline + "</li>";

            var result = Regex.Replace(
                template,
                @"<p\b[^>]*>\s*" + tokenPattern + @"\s*</p>",
                match => paragraphs,
                RegexOptions.IgnoreCase);

            result = Regex.Replace(
                result,
                @"<li\b[^>]*>\s*" + tokenPattern + @"\s*</li>",
                match => listItem,
                RegexOptions.IgnoreCase);

            result = Regex.Replace(
                result,
                @"(?m)^\s*" + tokenPattern + @"\s*$",
                match => paragraphs,
                RegexOptions.IgnoreCase);

            return Regex.Replace(result, tokenPattern, match => inline, RegexOptions.IgnoreCase);
        }

        private static string ReplaceHtmlListToken(string template, string token, IEnumerable<string> values)
        {
            var tokenPattern = Regex.Escape("{" + token + "}");
            var list = FormatHtmlList(values);
            var inline = FormatHtmlListInline(values);

            var result = Regex.Replace(
                template,
                @"<(?<tag>p|ul)\b[^>]*>\s*" + tokenPattern + @"\s*</\k<tag>>",
                match => list,
                RegexOptions.IgnoreCase);

            result = Regex.Replace(
                result,
                @"(?m)^\s*" + tokenPattern + @"\s*$",
                match => list,
                RegexOptions.IgnoreCase);

            return result.Replace("{" + token + "}", inline);
        }

        private static string FormatHtmlList(IEnumerable<string> values)
        {
            var items = (values ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => "<li>" + EscapeHtml(x.Trim()) + "</li>")
                .ToList();

            return items.Count == 0
                ? string.Empty
                : "<ul>\n" + string.Join("\n", items) + "\n</ul>";
        }

        private static string FormatHtmlListInline(IEnumerable<string> values)
        {
            return string.Join(", ", (values ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => EscapeHtml(x.Trim())));
        }

        private static string FormatHtmlParagraphs(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Replace("\r", string.Empty).Trim();
            var paragraphs = Regex.Split(normalized, @"\n\s*\n+")
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (paragraphs.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("\n", paragraphs.Select(x => "<p>" + EscapeHtml(x).Replace("\n", "<br/>") + "</p>"));
        }

        private static string FormatHtmlInline(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return EscapeHtml(value.Replace("\r", string.Empty).Trim())
                .Replace("\n\n", "<br/><br/>")
                .Replace("\n", "<br/>");
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value.Trim();
            text = Regex.Replace(text, @"^\s*#{1,6}\s*", string.Empty, RegexOptions.Multiline);
            text = Regex.Replace(
                text,
                @"^\s*(descripcion|descripci\u00f3n|descripcion breve|descripci\u00f3n breve|resumen|sinopsis|premisa|jugabilidad|gameplay|tono|ambientacion|ambientaci\u00f3n|contexto|perspectiva|modos de juego|duracion estimada|duraci\u00f3n estimada|juegos similares|notas|recomendado para|ideal para|caracteristicas principales|caracter\u00edsticas principales|caracteristicas|caracter\u00edsticas)\s*[:\uFF1A\-\u2013\u2014]\s*",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(
                text,
                @"^\s*(descripcion|descripci\u00f3n|descripcion breve|descripci\u00f3n breve|resumen|sinopsis|premisa|jugabilidad|gameplay|tono|ambientacion|ambientaci\u00f3n|contexto|perspectiva|modos de juego|duracion estimada|duraci\u00f3n estimada|juegos similares|notas|recomendado para|ideal para|caracteristicas principales|caracter\u00edsticas principales|caracteristicas|caracter\u00edsticas)\s*$\r?\n?",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            return text.Trim();
        }

        private static string EnsureParagraphCount(string value, string length)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var target = TargetParagraphCount(length);
            if (target <= 1)
            {
                return value.Trim();
            }

            var normalized = value.Replace("\r", string.Empty).Trim();
            var existingParagraphs = Regex.Split(normalized, @"\n\s*\n+")
                .Where(x => !string.IsNullOrWhiteSpace(x.Trim()))
                .ToList();

            if (existingParagraphs.Count >= target)
            {
                return normalized;
            }

            var sentences = Regex.Split(normalized.Replace("\n", " "), @"(?<=[.!?])\s+")
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (sentences.Count < target)
            {
                return normalized;
            }

            if (sentences.Count < target * 3)
            {
                return normalized;
            }

            var paragraphs = new List<string>();
            for (var i = 0; i < target; i++)
            {
                var start = i * sentences.Count / target;
                var end = (i + 1) * sentences.Count / target;
                paragraphs.Add(string.Join(" ", sentences.Skip(start).Take(end - start)));
            }

            return string.Join("\n\n", paragraphs.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static int TargetParagraphCount(string length)
        {
            if (string.Equals(length, "Media", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(length, "Larga", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (string.Equals(length, "Extra larga", StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            return 1;
        }

        private static List<string> CleanList(IEnumerable<string> values, int maxItems, IEnumerable<string> blacklist, string prefix)
        {
            if (values == null)
            {
                return new List<string>();
            }

            var blocked = blacklist == null ? new List<string>() : blacklist.ToList();
            return values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Clean)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => !blocked.Any(blockedTerm => x.IndexOf(blockedTerm, StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(x => AddPrefix(x, prefix))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxItems)
                .ToList();
        }

        private static string AddPrefix(string value, string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return value;
            }

            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value : prefix + value;
        }

        private static List<AiMetadataLink> CleanLinks(IEnumerable<AiMetadataLink> links, int maxItems)
        {
            if (links == null)
            {
                return new List<AiMetadataLink>();
            }

            var result = new List<AiMetadataLink>();
            foreach (var link in links)
            {
                if (link == null || string.IsNullOrWhiteSpace(link.Url))
                {
                    continue;
                }

                Uri uri;
                if (!Uri.TryCreate(link.Url.Trim(), UriKind.Absolute, out uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    continue;
                }

                var name = Clean(link.Name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = uri.Host.Replace("www.", string.Empty);
                }

                if (result.Any(x => string.Equals(x.Url, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Add(new AiMetadataLink(name, uri.AbsoluteUri));
                if (result.Count >= Math.Max(1, maxItems))
                {
                    break;
                }
            }

            return result;
        }

        private static string CleanFeature(string value)
        {
            var text = Clean(value).Trim(' ', '.', ';', ':', '-', '\u2013', '\u2014');
            if (text.Length <= 60)
            {
                return text;
            }

            var separators = new[] { ',', ';', ':', '.', '-', '\u2013', '\u2014' };
            var separatorIndex = separators
                .Select(x => text.IndexOf(x))
                .Where(x => x > 12 && x <= 60)
                .DefaultIfEmpty(-1)
                .First();

            if (separatorIndex > 0)
            {
                return text.Substring(0, separatorIndex).Trim();
            }

            var lastSpace = text.LastIndexOf(' ', 60);
            return lastSpace > 20 ? text.Substring(0, lastSpace).Trim() : text.Substring(0, 60).Trim();
        }

        private static string EscapeHtml(string value)
        {
            return SecurityElement.Escape(value) ?? string.Empty;
        }

        private static string CanonicalizePrefixed(string value, string prefix, Dictionary<string, string> aliases)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(prefix) || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Canonicalize(value, aliases);
            }

            var withoutPrefix = value.Substring(prefix.Length);
            return prefix + Canonicalize(withoutPrefix, aliases);
        }

        private static string Canonicalize(string value, Dictionary<string, string> aliases)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var key = LibraryNameMatching.NormalizeKey(value);
            string canonical;
            return aliases.TryGetValue(key, out canonical) ? canonical : value.Trim();
        }

        internal static readonly Dictionary<string, string> GenreAliases = BuildAliases(new Dictionary<string, string[]>
        {
            { "Accion", new[] { "accion", "action" } },
            { "Aventura", new[] { "aventura", "adventure" } },
            { "RPG", new[] { "rpg", "rol", "juego de rol", "role playing", "role playing game" } },
            { "Estrategia", new[] { "estrategia", "strategy", "tactica", "tactico", "tacticos" } },
            { "Simulacion", new[] { "simulacion", "simulador", "simulation", "simulator" } },
            { "Deportes", new[] { "deportes", "sports", "sport" } },
            { "Carreras", new[] { "carreras", "conduccion", "racing", "driving" } },
            { "Lucha", new[] { "lucha", "fighting", "peleas", "brawler" } },
            { "Plataformas", new[] { "plataformas", "platformer", "platform", "saltos" } },
            { "Puzzle", new[] { "puzzle", "puzle", "puzzles", "rompecabezas" } },
            { "Disparos", new[] { "shooter", "shooters", "disparos", "tiros", "fps", "tps", "disparos en primera persona", "disparos enemigos" } },
            { "Terror", new[] { "terror", "horror", "miedo" } },
            { "Supervivencia", new[] { "supervivencia", "survival" } },
            { "Sigilo", new[] { "sigilo", "stealth" } },
            { "Roguelike", new[] { "roguelike", "roguelite", "rogue like", "rogue lite" } },
            { "Mundo abierto", new[] { "mundo abierto", "open world" } },
            { "Metroidvania", new[] { "metroidvania" } },
            { "Novela visual", new[] { "novela visual", "visual novel" } },
            { "Ritmo", new[] { "ritmo", "rhythm", "musical" } },
            { "Indie", new[] { "indie", "independiente" } }
        });

        internal static readonly Dictionary<string, string> TagAliases = BuildAliases(new Dictionary<string, string[]>
        {
            { "Un jugador", new[] { "un jugador", "single player", "singleplayer", "solo", "para un jugador" } },
            { "Multijugador", new[] { "multijugador", "multiplayer" } },
            { "Cooperativo", new[] { "cooperativo", "co op", "coop", "cooperative" } },
            { "Cooperativo online", new[] { "cooperativo online", "online co op", "online coop", "co op online" } },
            { "Cooperativo local", new[] { "cooperativo local", "local co op", "local coop", "co op local" } },
            { "Competitivo", new[] { "competitivo", "competitive" } },
            { "Deduccion social", new[] { "deduccion social", "social deduction", "deducción social" } },
            { "Exploracion", new[] { "exploracion", "exploración", "exploration" } },
            { "Construccion", new[] { "construccion", "construcción", "building", "construction" } },
            { "Gestion", new[] { "gestion", "gestión", "management" } },
            { "Crafteo", new[] { "crafteo", "crafting", "fabricacion", "fabricación" } },
            { "Historia profunda", new[] { "historia profunda", "story rich", "narrativa profunda" } },
            { "Narrativo", new[] { "narrativo", "narrative" } },
            { "Dificil", new[] { "dificil", "difícil", "hard", "challenging", "desafiante" } },
            { "Casual", new[] { "casual" } },
            { "Retro", new[] { "retro" } },
            { "Pixel art", new[] { "pixel art", "pixelart" } },
            { "Ciencia ficcion", new[] { "ciencia ficcion", "ciencia ficción", "sci fi", "science fiction" } },
            { "Fantasia", new[] { "fantasia", "fantasía", "fantasy" } },
            { "Sandbox", new[] { "sandbox" } },
            { "Procedural", new[] { "procedural", "generacion procedural", "generación procedural" } },
            { "Action", new[] { "action", "accion", "acción" } },
            { "Combat", new[] { "combat", "combate" } },
            { "Racing", new[] { "racing", "carreras" } },
            { "Post-apocalyptic", new[] { "post apocalyptic", "postapocalyptic", "post apocalipsis", "postapocalipsis" } }
        });

        internal static readonly Dictionary<string, string> FeatureAliases = BuildAliases(new Dictionary<string, string[]>
        {
            { "Un jugador", new[] { "un jugador", "single player", "singleplayer", "para un jugador" } },
            { "Multijugador online", new[] { "multijugador online", "online multiplayer", "multijugador en linea", "multijugador en línea" } },
            { "Multijugador local", new[] { "multijugador local", "local multiplayer" } },
            { "Cooperativo online", new[] { "cooperativo online", "online co op", "online coop" } },
            { "Cooperativo local", new[] { "cooperativo local", "local co op", "local coop" } },
            { "Pantalla dividida", new[] { "pantalla dividida", "split screen", "splitscreen" } },
            { "Soporte mando", new[] { "soporte mando", "mando", "controller support", "compatible con mando", "soporte de mando" } },
            { "Logros", new[] { "logros", "achievements", "steam achievements" } },
            { "Guardado en la nube", new[] { "guardado en la nube", "cloud saves", "steam cloud", "guardado cloud" } },
            { "Cromos de Steam", new[] { "cromos de steam", "steam trading cards", "trading cards" } },
            { "Compatibilidad Steam Deck", new[] { "steam deck", "steam deck verified", "compatibilidad steam deck" } },
            { "Juego cruzado", new[] { "juego cruzado", "cross play", "crossplay" } },
            { "Editor de niveles", new[] { "editor de niveles", "level editor" } },
            { "Modos PvP", new[] { "pvp", "modos pvp" } },
            { "Modos PvE", new[] { "pve", "modos pve" } },
            { "Compras integradas", new[] { "compras integradas", "in app purchases", "microtransacciones", "microtransactions" } }
        });

        internal static readonly Dictionary<string, string> CategoryAliases = BuildAliases(new Dictionary<string, string[]>
        {
            { "Pendientes", new[] { "pendiente", "pendientes", "por jugar", "backlog" } },
            { "Completados", new[] { "completado", "completados", "terminado", "terminados", "finished", "completed" } },
            { "Abandonados", new[] { "abandonado", "abandonados", "dropped" } },
            { "Para jugar en cooperativo", new[] { "cooperativo", "para cooperativo", "para jugar en coop", "para jugar en cooperativo" } },
            { "Para jugar rapido", new[] { "partidas rapidas", "para jugar rapido", "sesiones cortas", "quick play" } },
            { "Para sesiones largas", new[] { "sesiones largas", "para sesiones largas" } },
            { "Relax", new[] { "relax", "relajante", "chill" } },
            { "Retos", new[] { "reto", "retos", "desafio", "desafios", "desafío", "desafíos" } },
            { "Narrativos", new[] { "narrativo", "narrativos", "historia", "story" } },
            { "Multijugador", new[] { "multijugador", "multiplayer" } },
            { "Indie", new[] { "indie", "independiente" } },
            { "Retro", new[] { "retro" } },
            { "Emulacion", new[] { "emulacion", "emulación", "emulation" } }
        });

        internal static readonly Dictionary<string, string> AgeRatingAliases = BuildAliases(new Dictionary<string, string[]>
        {
            { "ESRB E", new[] { "esrb e", "esrb everyone", "everyone", "rated e", "e for everyone" } },
            { "ESRB E10+", new[] { "esrb e10", "esrb e10+", "e10", "everyone 10", "esrb everyone 10" } },
            { "ESRB T", new[] { "esrb t", "teen", "esrb teen" } },
            { "ESRB M", new[] { "esrb m", "mature", "esrb mature" } },
            { "ESRB AO", new[] { "esrb ao", "adults only", "esrb adults only" } },
            { "ESRB RP", new[] { "esrb rp", "rating pending" } },
            { "PEGI 3", new[] { "pegi 3", "pegi3" } },
            { "PEGI 7", new[] { "pegi 7", "pegi7" } },
            { "PEGI 12", new[] { "pegi 12", "pegi12" } },
            { "PEGI 16", new[] { "pegi 16", "pegi16" } },
            { "PEGI 18", new[] { "pegi 18", "pegi18" } }
        });

        private static Dictionary<string, string> BuildAliases(Dictionary<string, string[]> source)
        {
            var result = new Dictionary<string, string>();
            foreach (var pair in source)
            {
                result[LibraryNameMatching.NormalizeKey(pair.Key)] = pair.Key;
                foreach (var alias in pair.Value)
                {
                    result[LibraryNameMatching.NormalizeKey(alias)] = pair.Key;
                }
            }

            return result;
        }

        private static List<string> ParseSimilarGamesText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            return Regex.Split(text, @"\s*(?:,|;|/|\n|·|\u2022|(?:\s+y\s+)|(?:\s+and\s+))\s*")
                .Select(x => x.Trim().Trim('.', '-', '\u2013', '\u2014'))
                .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length > 1)
                .ToList();
        }
    }

    public class MetadataFieldProvenance
    {
        public string Field { get; set; }
        public string Source { get; set; }
        public string Method { get; set; }
        public string Confidence { get; set; }
        public string Detail { get; set; }
    }
}
