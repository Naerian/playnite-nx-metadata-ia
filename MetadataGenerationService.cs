using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    public class MetadataGenerationService
    {
        private readonly MetaDataIASettings settings;

        public MetadataGenerationService(MetaDataIASettings settings)
        {
            this.settings = settings;
        }

        public async Task<AiMetadataResult> GenerateAsync(Game game, CancellationToken cancellationToken = default(CancellationToken))
        {
            Exception primaryError = null;

            try
            {
                return await GenerateCurrentAsync(game, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!ShouldTryLocalFallback(ex))
                {
                    throw;
                }

                primaryError = ex;
            }

            return await TryLocalFallbacksAsync(game, primaryError, cancellationToken).ConfigureAwait(false);
        }

        private async Task<AiMetadataResult> GenerateCurrentAsync(Game game, CancellationToken cancellationToken)
        {
            if (settings.ProviderPreset == MetaDataIASettings.ProviderClaude)
            {
                return await GenerateAnthropicAsync(game, cancellationToken).ConfigureAwait(false);
            }

            return await GenerateOpenAICompatibleAsync(game, cancellationToken).ConfigureAwait(false);
        }

        private bool ShouldTryLocalFallback(Exception ex)
        {
            if (!settings.EnableLocalFallback ||
                settings.ProviderPreset == MetaDataIASettings.ProviderLmStudio ||
                settings.ProviderPreset == MetaDataIASettings.ProviderOllama)
            {
                return false;
            }

            var providerException = ex as AiProviderException;
            if (providerException != null && providerException.StopBatch)
            {
                return true;
            }

            return ex is HttpRequestException;
        }

        private async Task<AiMetadataResult> TryLocalFallbacksAsync(Game game, Exception primaryError, CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            if (settings.TryLmStudioFallback)
            {
                var result = await TryFallbackAsync(game, MetaDataIASettings.ProviderLmStudio, errors, cancellationToken).ConfigureAwait(false);
                if (result != null)
                {
                    return result;
                }
            }

            if (settings.TryOllamaFallback)
            {
                var result = await TryFallbackAsync(game, MetaDataIASettings.ProviderOllama, errors, cancellationToken).ConfigureAwait(false);
                if (result != null)
                {
                    return result;
                }
            }

            throw new AiProviderException(
                primaryError.Message +
                "\n\nMetadata IA intento usar fallback local gratuito, pero no hubo ningun proveedor local disponible.\n\n" +
                "Comprueba que LM Studio tenga el servidor local activo en http://localhost:1234 o que Ollama este arrancado en http://localhost:11434.\n\n" +
                "Errores de fallback:\n" + string.Join("\n", errors.Select(SanitizeForUser)),
                true,
                string.Join("\n", errors));
        }

        private async Task<AiMetadataResult> TryFallbackAsync(Game game, string provider, List<string> errors, CancellationToken cancellationToken)
        {
            try
            {
                var fallbackSettings = settings.CreateLocalFallbackSettings(provider);
                return await new MetadataGenerationService(fallbackSettings).GenerateCurrentAsync(game, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errors.Add(provider + ": " + ex.Message);
                return null;
            }
        }

        private async Task<AiMetadataResult> GenerateOpenAICompatibleAsync(Game game, CancellationToken cancellationToken)
        {
            var request = new
            {
                model = settings.Model,
                temperature = 0.2,
                max_tokens = 4096,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = BuildSystemPrompt()
                    },
                    new
                    {
                        role = "user",
                        content = BuildUserPrompt(game)
                    }
                }
            };

            using (var client = new HttpClient())
            using (var message = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint))
            {
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                }

                message.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw CreateConnectionException(ex);
                }

                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw CreateProviderException((int)response.StatusCode, responseText);
                }

                var content = ExtractAssistantContent(responseText);
                var result = ParseResult(content);
                result.Normalize(settings, game);
                return result;
            }
        }

        private async Task<AiMetadataResult> GenerateAnthropicAsync(Game game, CancellationToken cancellationToken)
        {
            var request = new
            {
                model = settings.Model,
                max_tokens = 4096,
                temperature = 0.2,
                system = BuildSystemPrompt(),
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = BuildUserPrompt(game)
                    }
                }
            };

            using (var client = new HttpClient())
            using (var message = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint))
            {
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    message.Headers.Add("x-api-key", settings.ApiKey);
                }

                message.Headers.Add("anthropic-version", "2023-06-01");
                message.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw CreateConnectionException(ex);
                }

                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw CreateProviderException((int)response.StatusCode, responseText);
                }

                var content = ExtractAnthropicContent(responseText);
                var result = ParseResult(content);
                result.Normalize(settings, game);
                return result;
            }
        }

        private string BuildSystemPrompt()
        {
            return "Eres un editor de metadatos de videojuegos para Playnite. " +
                   "Devuelve solo JSON valido, sin markdown, en el idioma indicado. " +
                   "Mantiene una estructura consistente. No inventes datos especificos si no hay evidencia razonable; usa listas cortas y consistentes.";
        }

        private string BuildUserPrompt(Game game)
        {
            var context = new Dictionary<string, object>();
            context["targetLanguage"] = settings.Language;
            context["gameName"] = game.Name;
            context["gameId"] = game.GameId;
            context["releaseYear"] = game.ReleaseYear;
            context["source"] = game.Source == null ? null : game.Source.Name;
            context["platforms"] = Names(game.Platforms);
            context["platformContextHint"] = "source es la tienda/biblioteca desde la que se agrego el juego; platforms son las plataformas del juego en Playnite. Usa ambos como contexto, especialmente para caracteristicas, pero no inventes funciones especificas de una tienda si no hay seguridad razonable.";
            context["tone"] = settings.Tone;
            context["length"] = settings.Length;
            context["tokenLengths"] = BuildTokenLengths();
            context["strictCompanyAgeRegion"] = settings.StrictCompanyAgeRegion;
            context["fieldsToGenerate"] = BuildFieldsToGenerate();
            context["maxDevelopers"] = settings.MaxDevelopers;
            context["maxPublishers"] = settings.MaxPublishers;
            context["companyPolicy"] = "developers debe contener solo el estudio principal acreditado del juego base; publishers debe contener solo la editora principal. No incluyas estudios de apoyo, ports, multijugador, QA, remasters, localizacion, distribucion regional ni colaboradores salvo que sean el credito principal. Si hay duda razonable, deja el campo vacio.";
            context["canonicalTerms"] = BuildCanonicalTerms();
            context["blacklist"] = settings.GetBlacklistTerms();
            context["tagPrefix"] = settings.TagPrefix;
            context["categoryPrefix"] = settings.CategoryPrefix;
            context["extraInstructions"] = settings.ExtraInstructions;

            if (!string.Equals(settings.ExistingMetadataMode, "Ignorar", StringComparison.OrdinalIgnoreCase))
            {
                context["existing"] = new
                {
                    description = game.Description,
                    genres = Names(game.Genres),
                    tags = Names(game.Tags),
                    features = Names(game.Features),
                    categories = Names(game.Categories),
                    developers = Names(game.Developers),
                    publishers = Names(game.Publishers),
                    ageRatings = Names(game.AgeRatings),
                    regions = Names(game.Regions),
                    links = game.Links == null ? new List<object>() : game.Links.Select(x => new { name = x.Name, url = x.Url }).Cast<object>().ToList()
                };
                context["existingMetadataMode"] = settings.ExistingMetadataMode;
            }

            return "Genera metadatos normalizados para este juego. " +
                   "Respeta fieldsToGenerate: si un campo esta en false, devuelve cadena vacia o lista vacia en ese campo. " +
                   "Respeta tone, length, tokenLengths, blacklist y los prefijos indicados. " +
                   "Cada token textual debe contener solo contenido, sin titulos, encabezados, etiquetas de campo, markdown ni HTML. " +
                   "No escribas frases como 'Descripcion:', 'Premisa:', 'Sinopsis:' o 'Caracteristicas principales:' dentro de ningun valor. " +
                   "Interpreta tokenLengths para short asi: Corta = 1 frase breve; Media = 2 o 3 frases; Larga = 1 parrafo; Extra larga = 2 parrafos compactos. " +
                   "Interpreta tokenLengths para synopsis asi: Corta = 1 parrafo de 4 a 6 frases; Media = 2 parrafos de 4 a 6 frases cada uno; Larga = 3 parrafos de 4 a 6 frases cada uno; Extra larga = 4 o 5 parrafos de 4 a 6 frases cada uno. " +
                   "Si tokenLengths.synopsis es Media, Larga o Extra larga, separa los parrafos dentro del string JSON usando saltos de linea dobles (\\n\\n). No devuelvas la sinopsis como un unico parrafo. " +
                   "Importante: dentro del JSON no uses saltos de linea reales en valores de texto; usa siempre escapes \\n o \\n\\n para separar lineas o parrafos. " +
                   "Cada parrafo debe ser sustancial, no una frase suelta: evita parrafos de menos de 3 frases salvo que el campo sea Corta. " +
                   "Interpreta tokenLengths para otros textos asi: Corta = 1 frase breve; Media = 1 parrafo de 3 a 5 frases; Larga = 2 parrafos de 3 a 5 frases; Extra larga = 3 parrafos de 3 a 5 frases. " +
                   "Para listas, length regula cuantos elementos utiles devuelves dentro del maximo permitido: Corta = pocos y esenciales; Media = cobertura equilibrada; Larga = cobertura amplia; Extra larga = usa el maximo cuando haya informacion suficiente. " +
                   "short y synopsis deben ser siempre diferentes: short es una descripcion editorial compacta de que es el juego y por que destaca; synopsis desarrolla premisa, contexto y propuesta sin repetir literalmente short. " +
                   "Usa canonicalTerms para mantener consistencia entre juegos. Si un concepto encaja con un termino canonico, usa exactamente ese termino en vez de sinonimos o variantes. Por ejemplo, no mezcles 'Shooter' y 'Disparos': usa el canon indicado. " +
                   "Si fieldsToGenerate.features es true, features debe contener entre 3 y " + settings.MaxFeatures + " caracteristicas concretas del juego, no frases genericas. " +
                   "Si fieldsToGenerate.links es true, links debe contener como maximo " + settings.MaxLinks + " enlaces utiles y verificables del juego. Incluye solo URLs oficiales o muy fiables: web oficial, tienda de la fuente, Discord oficial, wiki oficial o soporte oficial. No inventes URLs, no uses busquedas genericas y deja links vacio si no conoces enlaces concretos. " +
                   "Para features usa tambien source y platforms como contexto: por ejemplo controles, multijugador local/online, logros, guardado en la nube, compatibilidad con mando o funciones de plataforma solo si son razonablemente seguras. " +
                   "features debe tener estilo Steam: etiquetas muy cortas y escaneables, de 1 a 5 palabras cuando sea posible, sin frases completas, sin puntos finales y sin explicaciones. Ejemplos: 'Cooperativo online', 'Deduccion social', 'Soporte mando'. " +
                   "Si existingMetadataMode es Normalizar, conserva la intencion de los metadatos actuales pero corrige idioma, nombres duplicados, formato y coherencia. " +
                   "Para developers y publishers prioriza calidad sobre cantidad: devuelve como maximo maxDevelopers y maxPublishers, normalmente 1. Usa solo companias principales del juego base. No incluyas estudios secundarios, de apoyo, ports, multijugador, QA, localizacion o distribucion regional. " +
                   "Si strictCompanyAgeRegion es true, deja developers, publishers, ageRatings o regions vacios si no estas razonablemente seguro. " +
                   "Los campos short, synopsis, premise, gameplay, tone, setting, perspective, playModes, estimatedLength, similarGames, notes y recommendedFor deben ser texto, no arrays. " +
                   "Los campos features, genres, tags, developers, publishers, ageRatings, regions y categories deben ser arrays de texto. links debe ser un array de objetos con name y url. " +
                   "Responde con este JSON exacto: " +
                   "{\"short\":\"\",\"synopsis\":\"\",\"premise\":\"\",\"gameplay\":\"\",\"tone\":\"\",\"setting\":\"\",\"perspective\":\"\",\"playModes\":\"\",\"estimatedLength\":\"\",\"similarGames\":\"\",\"notes\":\"\",\"features\":[],\"recommendedFor\":\"\",\"genres\":[],\"tags\":[],\"developers\":[],\"publishers\":[],\"ageRatings\":[],\"regions\":[],\"categories\":[],\"links\":[]} " +
                   "Contexto: " + JsonConvert.SerializeObject(context);
        }

        private Dictionary<string, string> BuildTokenLengths()
        {
            var lengths = new Dictionary<string, string>();
            lengths["short"] = settings.ShortLength;
            lengths["synopsis"] = settings.SynopsisLength;
            lengths["premise"] = settings.PremiseLength;
            lengths["gameplay"] = settings.GameplayLength;
            lengths["tone"] = settings.ToneLength;
            lengths["setting"] = settings.SettingLength;
            lengths["perspective"] = settings.PerspectiveLength;
            lengths["playModes"] = settings.PlayModesLength;
            lengths["estimatedLength"] = settings.EstimatedLengthLength;
            lengths["similarGames"] = settings.SimilarGamesLength;
            lengths["notes"] = settings.NotesLength;
            lengths["recommendedFor"] = settings.RecommendedForLength;
            return lengths;
        }

        private Dictionary<string, bool> BuildFieldsToGenerate()
        {
            var fields = new Dictionary<string, bool>();
            fields["description"] = settings.GenerateDescription;
            fields["genres"] = settings.GenerateGenres;
            fields["tags"] = settings.GenerateTags;
            fields["features"] = settings.GenerateFeatures;
            fields["developers"] = settings.GenerateDevelopers;
            fields["publishers"] = settings.GeneratePublishers;
            fields["ageRatings"] = settings.GenerateAgeRatings;
            fields["regions"] = settings.GenerateRegions;
            fields["categories"] = settings.GenerateCategories;
            fields["links"] = settings.GenerateLinks;
            return fields;
        }

        private static Dictionary<string, List<string>> BuildCanonicalTerms()
        {
            return new Dictionary<string, List<string>>
            {
                {
                    "genres",
                    new List<string>
                    {
                        "Accion", "Aventura", "RPG", "Estrategia", "Simulacion", "Deportes", "Carreras",
                        "Lucha", "Plataformas", "Puzzle", "Disparos", "Terror", "Supervivencia",
                        "Sigilo", "Roguelike", "Mundo abierto", "Metroidvania", "Novela visual", "Ritmo"
                    }
                },
                {
                    "tags",
                    new List<string>
                    {
                        "Un jugador", "Multijugador", "Cooperativo", "Cooperativo online", "Cooperativo local",
                        "Competitivo", "PvP", "PvE", "Deduccion social", "Exploracion", "Construccion",
                        "Gestion", "Crafteo", "Historia profunda", "Narrativo", "Humor", "Dificil",
                        "Casual", "Retro", "Anime", "Pixel art", "Ciencia ficcion", "Fantasia", "Cyberpunk",
                        "Postapocaliptico", "Sandbox", "Procedural"
                    }
                },
                {
                    "features",
                    new List<string>
                    {
                        "Un jugador", "Multijugador online", "Multijugador local", "Cooperativo online",
                        "Cooperativo local", "Pantalla dividida", "Soporte mando", "Logros",
                        "Guardado en la nube", "Cromos de Steam", "Compatibilidad Steam Deck",
                        "Juego cruzado", "Editor de niveles", "Modos PvP", "Modos PvE", "Compras integradas"
                    }
                },
                {
                    "categories",
                    new List<string>
                    {
                        "Favoritos", "Pendientes", "Completados", "Abandonados", "Para jugar en cooperativo",
                        "Para jugar rapido", "Para sesiones largas", "Relax", "Retos", "Narrativos",
                        "Multijugador", "Indie", "Retro", "Emulacion"
                    }
                }
            };
        }

        private static string ExtractAssistantContent(string responseText)
        {
            var json = JObject.Parse(responseText);
            var choices = json["choices"] as JArray;
            var content = choices == null || choices.Count == 0 ? null : choices[0]["message"]["content"].ToString();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("El proveedor IA no devolvio contenido util.");
            }

            return content.Trim();
        }

        private static string ExtractAnthropicContent(string responseText)
        {
            var json = JObject.Parse(responseText);
            var blocks = json["content"] as JArray;
            if (blocks == null || blocks.Count == 0)
            {
                throw new InvalidOperationException("El proveedor IA no devolvio contenido util.");
            }

            var texts = blocks
                .Where(x => x["type"] != null && string.Equals(x["type"].ToString(), "text", StringComparison.OrdinalIgnoreCase))
                .Select(x => x["text"] == null ? string.Empty : x["text"].ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (texts.Count == 0)
            {
                throw new InvalidOperationException("El proveedor IA no devolvio texto util.");
            }

            return string.Join("\n", texts).Trim();
        }

        private static AiMetadataResult ParseResult(string content)
        {
            var cleaned = content.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                cleaned = cleaned.Trim('`').Trim();
                if (cleaned.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned.Substring(4).Trim();
                }
            }

            cleaned = ExtractJsonObject(cleaned);
            JObject json = null;
            Exception parseError = null;
            try
            {
                json = ParseJsonObject(cleaned);
            }
            catch (Exception ex)
            {
                parseError = ex;
            }

            if (json == null)
            {
                var loose = ParseLooseResult(cleaned);
                if (HasUsefulData(loose))
                {
                    return loose;
                }

                throw parseError ?? new InvalidOperationException("No se pudo interpretar la respuesta IA.");
            }

            if (json == null)
            {
                throw new InvalidOperationException("No se pudo interpretar la respuesta IA.");
            }

            return new AiMetadataResult
            {
                Short = Text(json, "short"),
                Synopsis = Text(json, "synopsis"),
                Premise = Text(json, "premise"),
                Gameplay = Text(json, "gameplay"),
                Tone = Text(json, "tone"),
                Setting = Text(json, "setting"),
                Perspective = Text(json, "perspective"),
                PlayModes = Text(json, "playModes"),
                EstimatedLength = Text(json, "estimatedLength"),
                SimilarGames = Text(json, "similarGames"),
                Notes = Text(json, "notes"),
                Features = List(json, "features"),
                RecommendedFor = Text(json, "recommendedFor"),
                Genres = List(json, "genres"),
                Tags = List(json, "tags"),
                Developers = List(json, "developers"),
                Publishers = List(json, "publishers"),
                AgeRatings = List(json, "ageRatings", "ageRating"),
                Regions = List(json, "regions", "region"),
                Categories = List(json, "categories"),
                Links = Links(json, "links")
            };
        }

        private static bool HasUsefulData(AiMetadataResult result)
        {
            if (result == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(result.Short) ||
                   !string.IsNullOrWhiteSpace(result.Synopsis) ||
                   !string.IsNullOrWhiteSpace(result.Premise) ||
                   !string.IsNullOrWhiteSpace(result.Gameplay) ||
                   result.Features.Count > 0 ||
                   result.Genres.Count > 0 ||
                   result.Tags.Count > 0 ||
                   result.Categories.Count > 0;
        }

        private static AiMetadataResult ParseLooseResult(string content)
        {
            return new AiMetadataResult
            {
                Short = LooseText(content, "short"),
                Synopsis = LooseText(content, "synopsis"),
                Premise = LooseText(content, "premise"),
                Gameplay = LooseText(content, "gameplay"),
                Tone = LooseText(content, "tone"),
                Setting = LooseText(content, "setting"),
                Perspective = LooseText(content, "perspective"),
                PlayModes = LooseText(content, "playModes"),
                EstimatedLength = LooseText(content, "estimatedLength"),
                SimilarGames = LooseText(content, "similarGames"),
                Notes = LooseText(content, "notes"),
                Features = LooseList(content, "features"),
                RecommendedFor = LooseText(content, "recommendedFor"),
                Genres = LooseList(content, "genres"),
                Tags = LooseList(content, "tags"),
                Developers = LooseList(content, "developers"),
                Publishers = LooseList(content, "publishers"),
                AgeRatings = LooseList(content, "ageRatings", "ageRating"),
                Regions = LooseList(content, "regions", "region"),
                Categories = LooseList(content, "categories"),
                Links = new List<AiMetadataLink>()
            };
        }

        private static readonly string[] KnownJsonFields = new[]
        {
            "short", "synopsis", "premise", "gameplay", "tone", "setting", "perspective", "playModes",
            "estimatedLength", "similarGames", "notes", "features", "recommendedFor", "genres", "tags",
            "developers", "publishers", "ageRatings", "ageRating", "regions", "region", "categories", "links"
        };

        private static string LooseText(string content, params string[] names)
        {
            var raw = LooseRawValue(content, names);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            raw = TrimLooseValue(raw);
            if (raw.StartsWith("[", StringComparison.Ordinal))
            {
                return string.Join(", ", LooseListFromRaw(raw));
            }

            return UnescapeLooseText(raw);
        }

        private static List<string> LooseList(string content, params string[] names)
        {
            return LooseListFromRaw(LooseRawValue(content, names));
        }

        private static string LooseRawValue(string content, params string[] names)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            Match keyMatch = null;
            foreach (var name in names)
            {
                var match = Regex.Match(content, "\"" + Regex.Escape(name) + "\"\\s*:", RegexOptions.IgnoreCase);
                if (match.Success && (keyMatch == null || match.Index < keyMatch.Index))
                {
                    keyMatch = match;
                }
            }

            if (keyMatch == null)
            {
                return string.Empty;
            }

            var start = keyMatch.Index + keyMatch.Length;
            var next = content.Length;
            foreach (Match match in Regex.Matches(content.Substring(start), ",\\s*\"(" + string.Join("|", KnownJsonFields.Select(Regex.Escape)) + ")\"\\s*:", RegexOptions.IgnoreCase))
            {
                next = start + match.Index;
                break;
            }

            var endBrace = content.LastIndexOf('}');
            if (endBrace > start && endBrace < next)
            {
                next = endBrace;
            }

            return content.Substring(start, next - start).Trim();
        }

        private static string TrimLooseValue(string raw)
        {
            var value = (raw ?? string.Empty).Trim().TrimEnd(',');
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            if (value.EndsWith("\"", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 1);
            }

            return value.Trim();
        }

        private static string UnescapeLooseText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\n", "\n")
                .Replace("\\r", string.Empty)
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\/", "/")
                .Trim();
        }

        private static List<string> LooseListFromRaw(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            var value = raw.Trim().TrimEnd(',');
            if (value.StartsWith("[", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            if (value.EndsWith("]", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 1);
            }

            var quoted = Regex.Matches(value, "\"((?:\\\\.|[^\"])*)\"")
                .Cast<Match>()
                .Select(x => UnescapeLooseText(x.Groups[1].Value))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (quoted.Count > 0)
            {
                return quoted;
            }

            return value
                .Replace("\r", string.Empty)
                .Split(new[] { '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(TrimLooseValue)
                .Select(UnescapeLooseText)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static JObject ParseJsonObject(string content)
        {
            try
            {
                return JObject.Parse(content);
            }
            catch (JsonReaderException firstError)
            {
                var repaired = EscapeRawControlCharactersInJsonStrings(content);
                if (!string.Equals(content, repaired, StringComparison.Ordinal))
                {
                    try
                    {
                        return JObject.Parse(repaired);
                    }
                    catch (JsonReaderException secondError)
                    {
                        throw CreateMalformedJsonException(secondError, firstError);
                    }
                }

                throw CreateMalformedJsonException(firstError, null);
            }
        }

        private static Exception CreateMalformedJsonException(JsonReaderException error, JsonReaderException originalError)
        {
            var detail = originalError == null ? error.Message : originalError.Message;
            return new InvalidOperationException(
                "La IA devolvio una respuesta con formato incorrecto y no se pudo interpretar.\n\n" +
                "El plugin continuara con el resto de juegos. Puedes volver a intentar este juego, reducir la longitud de los textos o cambiar a un modelo que respete mejor JSON.\n\n" +
                "Detalle breve: " + SanitizeForUser(detail));
        }

        private static string EscapeRawControlCharactersInJsonStrings(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            var builder = new StringBuilder(content.Length + 32);
            var inString = false;
            var escaped = false;
            var changed = false;

            foreach (var character in content)
            {
                if (escaped)
                {
                    builder.Append(character);
                    escaped = false;
                    continue;
                }

                if (inString && character == '\\')
                {
                    builder.Append(character);
                    escaped = true;
                    continue;
                }

                if (character == '"')
                {
                    inString = !inString;
                    builder.Append(character);
                    continue;
                }

                if (inString)
                {
                    if (character == '\r')
                    {
                        changed = true;
                        continue;
                    }

                    if (character == '\n')
                    {
                        builder.Append("\\n");
                        changed = true;
                        continue;
                    }

                    if (character == '\t')
                    {
                        builder.Append("\\t");
                        changed = true;
                        continue;
                    }

                    if (char.IsControl(character))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4"));
                        changed = true;
                        continue;
                    }
                }

                builder.Append(character);
            }

            return changed ? builder.ToString() : content;
        }

        private static string ExtractJsonObject(string content)
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }

            return content;
        }

        private static string Text(JObject json, params string[] names)
        {
            return TokenToText(Token(json, names));
        }

        private static List<string> List(JObject json, params string[] names)
        {
            return TokenToList(Token(json, names));
        }

        private static List<AiMetadataLink> Links(JObject json, params string[] names)
        {
            var token = Token(json, names);
            if (token == null || token.Type != JTokenType.Array)
            {
                return new List<AiMetadataLink>();
            }

            return token.Children()
                .OfType<JObject>()
                .Select(x => new AiMetadataLink(Text(x, "name", "title", "label"), Text(x, "url", "href")))
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .ToList();
        }

        private static JToken Token(JObject json, params string[] names)
        {
            if (json == null || names == null)
            {
                return null;
            }

            foreach (var name in names)
            {
                var property = json.Properties().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (property != null)
                {
                    return property.Value;
                }
            }

            return null;
        }

        private static string TokenToText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            if (token.Type == JTokenType.Array)
            {
                return string.Join(", ", token.Children().Select(TokenToText).Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            if (token.Type == JTokenType.Object)
            {
                return token.ToString(Formatting.None);
            }

            return token.ToString().Trim();
        }

        private static List<string> TokenToList(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return new List<string>();
            }

            if (token.Type == JTokenType.Array)
            {
                return token.Children()
                    .Select(TokenToText)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            }

            var text = TokenToText(token);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            return text
                .Replace("\r", string.Empty)
                .Split(new[] { '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static Exception CreateProviderException(int statusCode, string responseText)
        {
            var providerMessage = string.Empty;
            var providerCode = string.Empty;

            try
            {
                var json = JObject.Parse(responseText);
                providerMessage = json["error"] == null || json["error"]["message"] == null ? string.Empty : json["error"]["message"].ToString();
                providerCode = json["error"] == null || json["error"]["code"] == null ? string.Empty : json["error"]["code"].ToString();
            }
            catch
            {
                providerMessage = responseText;
            }

            if (string.Equals(providerCode, "insufficient_quota", StringComparison.OrdinalIgnoreCase))
            {
                return new AiProviderException(
                    "Tu proveedor IA ha rechazado la peticion porque la cuenta no tiene cuota disponible.\n\n" +
                    "Con OpenAI esto suele significar que no hay saldo/creditos activos o que el limite mensual esta agotado.\n\n" +
                    "Opciones sin pagar:\n" +
                    "- Usar un proveedor local compatible con OpenAI, como LM Studio u Ollama, y cambiar el endpoint en los ajustes del plugin.\n" +
                    "- Reducir el numero de juegos procesados y los campos generados, aunque si la cuota esta a cero esto no bastara.\n" +
                    "- Usar un modelo local pequeno para metadatos y dejar OpenAI solo para casos puntuales.\n\n" +
                    "Ejemplos de endpoint local si tienes esas apps instaladas:\n" +
                    "LM Studio: http://localhost:1234/v1/chat/completions\n" +
                    "Ollama: http://localhost:11434/v1/chat/completions\n\n" +
                    "En proveedores locales puedes dejar la API key vacia.",
                    true);
            }

            if (statusCode == 404 ||
                string.Equals(providerCode, "model_not_found", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(providerCode, "invalid_model", StringComparison.OrdinalIgnoreCase) ||
                providerMessage.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0 && providerMessage.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                providerMessage.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new AiProviderException(
                    "El proveedor o modelo configurado no existe, o no esta disponible para tu cuenta.\n\n" +
                    "Comprueba que el proveedor, endpoint y nombre del modelo esten bien escritos. Si has escrito el modelo a mano, copia el nombre exacto desde la documentacion o consola del proveedor.\n\n" +
                    "Ejemplos:\n" +
                    "- Gemini: gemini-2.5-flash o gemini-2.5-flash-lite\n" +
                    "- Ollama: el nombre que aparece al ejecutar 'ollama list'\n" +
                    "- LM Studio: el modelo cargado en el servidor local",
                    true,
                    responseText);
            }

            if (statusCode == 429)
            {
                return new AiProviderException(
                    "El proveedor IA ha limitado temporalmente las peticiones.\n\n" +
                    "Prueba a esperar unos minutos, procesar menos juegos de golpe o usar un modelo/local endpoint con menos restricciones.",
                    true,
                    responseText);
            }

            if (statusCode == 503 ||
                providerMessage.IndexOf("high demand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                providerMessage.IndexOf("overloaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                providerMessage.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new AiProviderException(
                    "El proveedor IA esta saturado o el modelo elegido no esta disponible temporalmente.\n\n" +
                    "Si estas usando Gemini, esto puede pasar aunque tengas Gemini Pro/Google AI Pro en la app: la API de Gemini tiene sus propios limites y disponibilidad, separados de la suscripcion de la app.\n\n" +
                    "Que puedes hacer sin pagar:\n" +
                    "- Esperar unos minutos y probar otra vez.\n" +
                    "- Cambiar el modelo a gemini-2.5-flash o gemini-2.5-flash-lite si estabas usando un modelo Pro.\n" +
                    "- Procesar menos juegos de golpe.\n" +
                    "- Usar LM Studio u Ollama en local si quieres evitar cuotas externas.",
                    true,
                    responseText);
            }

            if (statusCode == 401 || statusCode == 403)
            {
                return new AiProviderException(
                    "El proveedor IA no ha aceptado la autenticacion.\n\n" +
                    "Revisa la API key, el endpoint y el modelo configurado. Si usas LM Studio u Ollama en local, normalmente puedes dejar la API key vacia.",
                    false,
                    responseText);
            }

            return new AiProviderException(
                "El proveedor IA devolvio un error (" + statusCode + ").\n\n" +
                "Revisa el proveedor, endpoint, modelo y API key configurados. Si el problema continua, prueba otro modelo o un proveedor local.",
                false,
                responseText);
        }

        private static Exception CreateConnectionException(HttpRequestException ex)
        {
            return new AiProviderException(
                "No se ha podido conectar con el proveedor configurado.\n\n" +
                "Comprueba que el endpoint este bien escrito y que el proveedor exista. Si usas LM Studio u Ollama, asegurate de que la aplicacion esta abierta, el servidor local esta activo y el modelo esta cargado o descargado.\n\n" +
                "Detalle breve: " + SanitizeForUser(ex.Message),
                true,
                ex.ToString());
        }

        public static string SanitizeForUser(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "Error no especificado.";
            }

            var text = message.Trim();
            if (text.StartsWith("{", StringComparison.Ordinal) || text.StartsWith("[", StringComparison.Ordinal))
            {
                return "El proveedor ha devuelto un error tecnico. Revisa la configuracion o prueba otro modelo.";
            }

            var jsonStart = text.IndexOf('{');
            if (jsonStart >= 0)
            {
                text = text.Substring(0, jsonStart).Trim();
            }

            jsonStart = text.IndexOf('[');
            if (jsonStart >= 0 && text.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0)
            {
                text = text.Substring(0, jsonStart).Trim();
            }

            return text.Length > 700 ? text.Substring(0, 700).Trim() + "..." : text;
        }

        private static List<string> Names<T>(IEnumerable<T> items) where T : DatabaseObject
        {
            return items == null
                ? new List<string>()
                : items.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name).ToList();
        }
    }
}

