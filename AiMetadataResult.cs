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
        public string Description { get; set; }

        public AiMetadataResult()
        {
            Provenance = new List<MetadataFieldProvenance>();
            Features = new List<string>();
            Genres = new List<string>();
            Tags = new List<string>();
            Developers = new List<string>();
            Publishers = new List<string>();
            AgeRatings = new List<string>();
            Regions = new List<string>();
            Categories = new List<string>();
            Links = new List<AiMetadataLink>();
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
            Notes = Clean(Notes);
            RecommendedFor = Clean(RecommendedFor);
            Features = CleanList(Features, settings.MaxFeatures, blacklist, string.Empty)
                .Select(CleanFeature)
                .Select(x => Canonicalize(x, CanonicalFeatureAliases))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxFeatures)
                .ToList();
            Genres = CleanList(Genres, settings.MaxGenres, blacklist, string.Empty)
                .Select(x => Canonicalize(x, CanonicalGenreAliases))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxGenres)
                .ToList();
            Tags = CleanList(Tags, settings.MaxTags, blacklist, settings.TagPrefix)
                .Select(x => CanonicalizePrefixed(x, settings.TagPrefix, CanonicalTagAliases))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxTags)
                .ToList();
            Developers = CleanList(Developers, settings.MaxDevelopers, blacklist, string.Empty);
            Publishers = CleanList(Publishers, settings.MaxPublishers, blacklist, string.Empty);
            AgeRatings = CleanList(AgeRatings, settings.MaxAgeRatings, blacklist, string.Empty);
            Regions = CleanList(Regions, settings.MaxRegions, blacklist, string.Empty);
            Categories = CleanList(Categories, settings.MaxCategories, blacklist, settings.CategoryPrefix)
                .Select(x => CanonicalizePrefixed(x, settings.CategoryPrefix, CanonicalCategoryAliases))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxCategories)
                .ToList();
            Links = CleanLinks(Links, settings.MaxLinks);
            AddReliableSourceLinks(game, settings);
            EnsureFeatureFallback(settings);
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

        private void EnsureFeatureFallback(MetaDataIASettings settings)
        {
            if (!settings.GenerateFeatures || Features.Count > 0)
            {
                return;
            }

            var fallback = new List<string>();
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
            var featureText = Features.Count == 0
                ? string.Empty
                : string.Join("\n", Features.Select(x => "- " + x));

            if (IsHtmlTemplate(template))
            {
                featureText = Features.Count == 0
                    ? string.Empty
                    : "<ul>\n" + string.Join("\n", Features.Select(x => "<li>" + EscapeHtml(x) + "</li>")) + "\n</ul>";
            }

            var html = IsHtmlTemplate(template);
            var description = template;

            if (html)
            {
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
            }
            else
            {
                description = description
                    .Replace("{short}", Short ?? string.Empty)
                    .Replace("{synopsis}", Synopsis ?? string.Empty)
                    .Replace("{premise}", Premise ?? string.Empty)
                    .Replace("{gameplay}", Gameplay ?? string.Empty)
                    .Replace("{tone}", Tone ?? string.Empty)
                    .Replace("{setting}", Setting ?? string.Empty)
                    .Replace("{perspective}", Perspective ?? string.Empty)
                    .Replace("{playModes}", PlayModes ?? string.Empty)
                    .Replace("{estimatedLength}", EstimatedLength ?? string.Empty)
                    .Replace("{similarGames}", SimilarGames ?? string.Empty)
                    .Replace("{notes}", Notes ?? string.Empty)
                    .Replace("{recommendedFor}", RecommendedFor ?? string.Empty);
            }

            return description
                .Replace("{genres}", FormatList(Genres))
                .Replace("{tags}", FormatList(Tags))
                .Replace("{developers}", FormatList(Developers))
                .Replace("{publishers}", FormatList(Publishers))
                .Replace("{ageRatings}", FormatList(AgeRatings))
                .Replace("{regions}", FormatList(Regions))
                .Replace("{categories}", FormatList(Categories))
                .Replace("{features}", featureText)
                .Trim();
        }

        private static bool IsHtmlTemplate(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return false;
            }

            return template.IndexOf("<p", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   template.IndexOf("<h", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   template.IndexOf("<ul", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   template.IndexOf("<br", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatList(IEnumerable<string> values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            return string.Join("\n", values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => "- " + x));
        }

        private static string ReplaceHtmlTextToken(string template, string token, string value)
        {
            var tokenPattern = Regex.Escape("{" + token + "}");
            var paragraphs = FormatHtmlParagraphs(value);
            var inline = FormatHtmlInline(value);

            var result = Regex.Replace(
                template,
                @"<p\b[^>]*>\s*" + tokenPattern + @"\s*</p>",
                match => paragraphs,
                RegexOptions.IgnoreCase);

            result = Regex.Replace(
                result,
                @"(?m)^\s*" + tokenPattern + @"\s*$",
                match => paragraphs,
                RegexOptions.IgnoreCase);

            return result.Replace("{" + token + "}", inline);
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

            var key = NormalizeKey(value);
            string canonical;
            return aliases.TryGetValue(key, out canonical) ? canonical : value.Trim();
        }

        private static string NormalizeKey(string value)
        {
            var normalized = RemoveDiacritics(value ?? string.Empty).ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"[^a-z0-9]+", " ").Trim();
            return Regex.Replace(normalized, @"\s+", " ");
        }

        private static string RemoveDiacritics(string value)
        {
            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static readonly Dictionary<string, string> CanonicalGenreAliases = BuildAliases(new Dictionary<string, string[]>
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
            { "Ritmo", new[] { "ritmo", "rhythm", "musical" } }
        });

        private static readonly Dictionary<string, string> CanonicalTagAliases = BuildAliases(new Dictionary<string, string[]>
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
            { "Procedural", new[] { "procedural", "generacion procedural", "generación procedural" } }
        });

        private static readonly Dictionary<string, string> CanonicalFeatureAliases = BuildAliases(new Dictionary<string, string[]>
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

        private static readonly Dictionary<string, string> CanonicalCategoryAliases = BuildAliases(new Dictionary<string, string[]>
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

        private static Dictionary<string, string> BuildAliases(Dictionary<string, string[]> source)
        {
            var result = new Dictionary<string, string>();
            foreach (var pair in source)
            {
                result[NormalizeKey(pair.Key)] = pair.Key;
                foreach (var alias in pair.Value)
                {
                    result[NormalizeKey(alias)] = pair.Key;
                }
            }

            return result;
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
