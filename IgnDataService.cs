using Newtonsoft.Json.Linq;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    // IGN exposes its own GraphQL endpoint for its public game catalogue. This
    // adapter intentionally treats it as a best-effort source: a changed query
    // or endpoint simply skips IGN and leaves the remaining sources available.
    // Query structure informed by Jeshibu's MIT-licensed IgnMetadata extension.
    internal sealed class IgnDataService
    {
        private const string GraphQlEndpoint = "https://mollusk.apis.ign.com/graphql";
        private const string SearchHash = "e1c2e012a21b4a98aaa618ef1b43eb0cafe9136303274a34f5d9ea4f2446e884";
        private const string GameHash = "b9c48f45a7390ecd157229419dc9a2acb48de90c0f255b667076befb38338de6";
        private const string ImagesHash = "06204b0f0871f8382e3adab7d1c59399e6c17ac94bff575c20a12ebf9d880b86";
        static IgnDataService()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
        }

        public async Task<OfficialStoreMetadata> GetContextAsync(Game game, CancellationToken cancellationToken)
        {
            var match = await FindExactGameAsync(game, cancellationToken).ConfigureAwait(false);
            if (match == null) return null;

            var slug = (string)match["slug"];
            var region = FirstString(match.SelectTokens("objectRegions[*].region"));
            if (!string.IsNullOrWhiteSpace(region)) region = region.ToLowerInvariant();
            var details = await CallAsync("ObjectSelectByTypeAndSlug", new { slug = slug, objectType = "Game", region = region, state = "Published" }, GameHash, cancellationToken).ConfigureAwait(false);
            var item = details == null ? null : details.SelectToken("data.objectSelectByTypeAndSlug") as JObject;
            if (item == null) return null;

            var names = ReadNames(item.SelectToken("metadata.names"));
            var url = string.IsNullOrWhiteSpace(slug) ? string.Empty : "https://www.ign.com/games/" + slug;
            return new OfficialStoreMetadata
            {
                SourceName = MetaDataIASettings.SourceIgn,
                StoreUrl = url,
                Title = names.FirstOrDefault(),
                Description = FirstString(item.SelectTokens("metadata.descriptions.long")) ?? FirstString(item.SelectTokens("metadata.descriptions.short")),
                Genres = ReadAttributeNames(item["genres"]),
                Features = ReadAttributeNames(item["features"]),
                Developers = ReadAttributeNames(item["producers"]),
                Publishers = ReadAttributeNames(item["publishers"]),
                AgeRating = ReadAgeRating(item["objectRegions"]),
                ReleaseDate = ReadReleaseDate(item["objectRegions"]),
                Series = ReadAttributeNames(item["franchises"]),
                Links = string.IsNullOrWhiteSpace(url) ? new List<Link>() : new List<Link> { new Link("IGN", url) },
                IsExactMatch = true
            };
        }

        public async Task<List<OfficialMediaCandidate>> GetMediaCandidatesAsync(Game game, MediaKind kind, CancellationToken cancellationToken)
        {
            if (kind != MediaKind.Cover && kind != MediaKind.Background) return new List<OfficialMediaCandidate>();
            var match = await FindExactGameAsync(game, cancellationToken).ConfigureAwait(false);
            if (match == null) return new List<OfficialMediaCandidate>();

            var slug = (string)match["slug"];
            if (string.IsNullOrWhiteSpace(slug)) return new List<OfficialMediaCandidate>();
            var candidates = new List<OfficialMediaCandidate>();
            if (kind == MediaKind.Cover)
            {
                var primary = (string)match.SelectToken("primaryImage.url");
                AddCandidate(candidates, primary, "official cover", 70);
            }

            // IGN places box art before screenshots for many games. Fetch a wider
            // page and classify the asset URLs so covers and backgrounds do not
            // compete with the wrong type of artwork. For example, Thief has its
            // cover art first and its usable screenshots later in the gallery.
            var images = await CallAsync("ObjectImageGallery", new { slug = slug, objectType = "Game", count = 80 }, ImagesHash, cancellationToken).ConfigureAwait(false);
            var gallery = images == null
                ? Enumerable.Empty<JObject>()
                : images.SelectTokens("data.objectSelectByTypeAndSlug.imageGallery.images[*]").OfType<JObject>();
            var assets = gallery
                .Select(image => new
                {
                    Url = (string)image["url"],
                    Caption = (string)image["caption"]
                })
                .Where(image => !string.IsNullOrWhiteSpace(image.Url))
                .GroupBy(image => image.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var nonCoverAssets = assets.Where(image => !IsLikelyCoverAsset(image.Url, image.Caption)).ToList();

            if (kind == MediaKind.Cover)
            {
                foreach (var image in assets)
                {
                    var isCover = IsLikelyCoverAsset(image.Url, image.Caption);
                    AddCandidate(candidates, image.Url, isCover ? "official cover" : "official gallery artwork", isCover ? 72 : 52);
                }
            }
            else
            {
                // A gallery without screenshots should still be usable, but its
                // cover-like assets are deliberately last-resort candidates.
                var backgroundAssets = nonCoverAssets.Count > 0 ? nonCoverAssets : assets;
                foreach (var image in backgroundAssets)
                {
                    var isFallback = nonCoverAssets.Count == 0;
                    AddCandidate(candidates, image.Url, isFallback ? "official gallery artwork" : "official screenshot", isFallback ? 40 : 70);
                }
            }
            return candidates.GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
        }

        private async Task<JObject> FindExactGameAsync(Game game, CancellationToken cancellationToken)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name)) return null;
            var response = await CallAsync("SearchObjectsByName", new { term = game.Name.Trim(), count = 20, objectType = "Game" }, SearchHash, cancellationToken).ConfigureAwait(false);
            var matches = response == null
                ? new List<JObject>()
                : response.SelectTokens("data.searchObjectsByName.objects[*]").OfType<JObject>().ToList();

            // Prefer an exact match across all names returned by IGN, then accept
            // only a conservative edition-name variant. This makes entries such
            // as "Game of the Year Edition" resolve to their base game without
            // turning a broad title into an unrelated result.
            var exact = matches.FirstOrDefault(match => ReadNames(match.SelectToken("metadata.names"))
                .Any(name => IsExactTitleMatch(game.Name, name)));
            if (exact != null) return exact;

            return matches.FirstOrDefault(match => ReadNames(match.SelectToken("metadata.names"))
                .Any(name => IsSafeEditionVariant(game.Name, name)));
        }

        private static async Task<JObject> CallAsync(string operationName, object variables, string hash, CancellationToken cancellationToken)
        {
            var extensions = new { persistedQuery = new { version = 1, sha256Hash = hash } };
            var url = GraphQlEndpoint + "?operationName=" + Uri.EscapeDataString(operationName) +
                      "&variables=" + Uri.EscapeDataString(Newtonsoft.Json.JsonConvert.SerializeObject(variables)) +
                      "&extensions=" + Uri.EscapeDataString(Newtonsoft.Json.JsonConvert.SerializeObject(extensions));
            // Do not use HttpClient here. Playnite loads extensions in an isolated
            // context and some installations do not resolve System.Net.Http for a
            // plugin even though it is present on the machine.
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.Referer] = "https://www.ign.com/reviews/games";
                client.Headers[HttpRequestHeader.Accept] = "application/json";
                client.Headers["apollographql-client-name"] = "kraken";
                client.Headers["apollographql-client-version"] = "v0.67.0";
                client.Headers["apollo-require-preflight"] = "true";
                cancellationToken.ThrowIfCancellationRequested();
                using (cancellationToken.Register(client.CancelAsync))
                {
                    var root = JObject.Parse(await client.DownloadStringTaskAsync(url).ConfigureAwait(false));
                    var errors = root["errors"] as JArray;
                    return errors != null && errors.Count > 0 ? null : root;
                }
            }
        }

        private static void AddCandidate(List<OfficialMediaCandidate> target, string url, string style, int score)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
            target.Add(new OfficialMediaCandidate
            {
                Url = uri.AbsoluteUri,
                Style = style,
                Score = score,
                SourceName = MetaDataIASettings.SourceIgn,
                IsOfficial = true,
                Extension = System.IO.Path.GetExtension(uri.AbsolutePath)
            });
        }

        private static List<string> ReadAttributeNames(JToken token)
        {
            return (token as JArray ?? new JArray()).OfType<JObject>()
                .Select(x => ((string)x["name"] ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> ReadNames(JToken token)
        {
            var name = token == null ? null : (string)token.SelectToken("name");
            var shortName = token == null ? null : (string)token.SelectToken("short");
            var alternateNames = token == null ? null : token.SelectToken("alt") as JArray;
            var values = new[] { name, shortName }
                .Concat((alternateNames ?? new JArray()).Select(item => (string)item))
                .Select(value => (value ?? string.Empty).Trim()).Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return values;
        }

        private static string ReadAgeRating(JToken regions)
        {
            return (regions as JArray ?? new JArray()).OfType<JObject>()
                .Select(region => string.Join(" ", new[] { (string)region.SelectToken("ageRating.ageRatingType"), (string)region.SelectToken("ageRating.name") }.Where(value => !string.IsNullOrWhiteSpace(value))))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string ReadReleaseDate(JToken regions)
        {
            return (regions as JArray ?? new JArray()).SelectTokens("[*].releases[*].date")
                .Select(x => (string)x).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        }

        private static string FirstString(IEnumerable<JToken> tokens)
        {
            return (tokens ?? Enumerable.Empty<JToken>()).Select(x => (string)x).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        }

        private static bool IsExactTitleMatch(string left, string right)
        {
            var normalizedLeft = NormalizeTitle(left);
            var normalizedRight = NormalizeTitle(right);
            return !string.IsNullOrWhiteSpace(normalizedLeft) && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
        }

        private static string NormalizeTitle(string value)
        {
            return new string(TrimReleaseYear(value).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static bool IsSafeEditionVariant(string left, string right)
        {
            var leftBase = RemoveEditionSuffix(NormalizeTitle(left));
            var rightBase = RemoveEditionSuffix(NormalizeTitle(right));
            return leftBase.Length >= 4 && string.Equals(leftBase, rightBase, StringComparison.Ordinal);
        }

        private static string RemoveEditionSuffix(string value)
        {
            var result = value ?? string.Empty;
            // These are generic release labels, rather than game-specific rules.
            // They are removed only for the edition-variant fallback above.
            var labels = new[]
            {
                "gameoftheyearedition", "gameoftheyear", "gotyedition", "goty",
                "completeedition", "complete", "definitiveedition", "definitive",
                "ultimateedition", "ultimate", "deluxeedition", "deluxe",
                "specialedition", "special", "collectorsedition", "collectors",
                "remastered", "remake", "anniversaryedition", "anniversary",
                "maximumedition", "enhancededition", "enhanced"
            };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var label in labels)
                {
                    if (result.EndsWith(label, StringComparison.Ordinal) && result.Length > label.Length)
                    {
                        result = result.Substring(0, result.Length - label.Length);
                        changed = true;
                        break;
                    }
                }
            }
            return result;
        }

        private static string TrimReleaseYear(string value)
        {
            var result = (value ?? string.Empty).Trim();
            if (result.Length < 6) return result;
            var trailing = result.Substring(result.Length - 4);
            int year;
            if (int.TryParse(trailing, out year) && year >= 1970 && year <= 2099)
            {
                var prefix = result.Substring(0, result.Length - 4).TrimEnd();
                if (prefix.EndsWith("(", StringComparison.Ordinal))
                {
                    prefix = prefix.Substring(0, prefix.Length - 1).TrimEnd();
                }
                return prefix;
            }
            return result;
        }

        private static bool IsLikelyCoverAsset(string url, string caption)
        {
            var value = ((url ?? string.Empty) + " " + (caption ?? string.Empty)).ToLowerInvariant();
            return value.Contains("boxart") || value.Contains("box-") || value.Contains("box_") ||
                   value.Contains("boxjpg") || value.Contains("cover") || value.Contains("button") ||
                   value.Contains("poster") || value.Contains("packshot") || value.Contains("case-");
        }
    }
}
