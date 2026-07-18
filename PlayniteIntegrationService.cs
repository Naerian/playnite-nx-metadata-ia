using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    public class OriginLibraryIntegrationInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    public class OriginIntegrationResult
    {
        public Guid IntegrationId { get; set; }
        public string IntegrationName { get; set; }
        public GameMetadata Metadata { get; set; }
        public string Error { get; set; }

        public bool HasMetadata
        {
            get { return Metadata != null; }
        }
    }

    public class OriginIntegrationMedia
    {
        public string Path { get; set; }
        public string Extension { get; set; }
        public string IntegrationName { get; set; }
    }

    public class PlayniteIntegrationService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CacheEntry> MetadataCache = new Dictionary<string, CacheEntry>();
        private static readonly SemaphoreSlim QueryLock = new SemaphoreSlim(1, 1);
        private static bool tempFolderCleaned;
        private readonly IPlayniteAPI playniteApi;
        private readonly MetaDataIASettings settings;

        public PlayniteIntegrationService(IPlayniteAPI playniteApi, MetaDataIASettings settings)
        {
            this.playniteApi = playniteApi;
            this.settings = settings;
        }

        public List<OriginLibraryIntegrationInfo> GetDetectedIntegrations()
        {
            if (playniteApi == null || playniteApi.Addons == null || playniteApi.Addons.Plugins == null)
            {
                return new List<OriginLibraryIntegrationInfo>();
            }

            try
            {
                return playniteApi.Addons.Plugins
                    .OfType<LibraryPlugin>()
                    .Select(x => new OriginLibraryIntegrationInfo { Id = x.Id, Name = x.Name })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not enumerate Playnite library integrations.");
                return new List<OriginLibraryIntegrationInfo>();
            }
        }

        public async Task<OriginIntegrationResult> GetOriginMetadataAsync(Game game, CancellationToken cancelToken)
        {
            if (game == null || game.PluginId == Guid.Empty || IsDisabled(game.PluginId) || playniteApi == null)
            {
                return null;
            }

            var key = game.PluginId + "|" + game.Id + "|" + (game.GameId ?? string.Empty);
            OriginIntegrationResult cached;
            if (TryGetCached(key, out cached))
            {
                return cached;
            }

            await QueryLock.WaitAsync(cancelToken).ConfigureAwait(false);
            try
            {
                if (TryGetCached(key, out cached))
                {
                    return cached;
                }

                var integration = FindIntegration(game.PluginId);
                if (integration == null)
                {
                    return Cache(key, new OriginIntegrationResult
                    {
                        IntegrationId = game.PluginId,
                        IntegrationName = game.Source == null ? string.Empty : game.Source.Name
                    });
                }

                var result = new OriginIntegrationResult
                {
                    IntegrationId = integration.Id,
                    IntegrationName = integration.Name
                };

                try
                {
                    cancelToken.ThrowIfCancellationRequested();
                    result.Metadata = await Task.Run(() =>
                    {
                        using (var provider = integration.GetMetadataDownloader())
                        {
                            return provider == null ? null : provider.GetMetadata(game);
                        }
                    }, cancelToken).ConfigureAwait(false);
                    cancelToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    Logger.Warn(ex, "Origin library integration failed to return metadata for " + game.Name + ".");
                }

                return Cache(key, result);
            }
            finally
            {
                QueryLock.Release();
            }
        }

        public OfficialStoreMetadata ToTrustedContext(OriginIntegrationResult result, Game game)
        {
            if (result == null || result.Metadata == null)
            {
                return null;
            }

            var metadata = result.Metadata;
            var links = metadata.Links ?? new List<Link>();
            var context = new OfficialStoreMetadata
            {
                SourceName = string.IsNullOrWhiteSpace(result.IntegrationName)
                    ? MetaDataIASettings.SourceOriginIntegration
                    : result.IntegrationName + " (" + MetaDataIASettings.SourceOriginIntegration + ")",
                StoreUrl = links.Select(x => x == null ? null : x.Url).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                Title = string.IsNullOrWhiteSpace(metadata.Name) ? (game == null ? string.Empty : game.Name) : metadata.Name,
                Description = metadata.Description,
                Genres = ResolveProperties(metadata.Genres, id => NameOf(playniteApi.Database.Genres.Get(id))),
                Features = ResolveProperties(metadata.Features, id => NameOf(playniteApi.Database.Features.Get(id))),
                Developers = ResolveProperties(metadata.Developers, id => NameOf(playniteApi.Database.Companies.Get(id))),
                Publishers = ResolveProperties(metadata.Publishers, id => NameOf(playniteApi.Database.Companies.Get(id))),
                Regions = ResolveProperties(metadata.Regions, id => NameOf(playniteApi.Database.Regions.Get(id))),
                Links = links.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Url)).Select(x => new Link(x.Name, x.Url)).ToList(),
                ReleaseDate = metadata.ReleaseDate.HasValue ? metadata.ReleaseDate.Value.ToString() : string.Empty,
                IsExactMatch = true
            };

            context.AgeRating = ResolveProperties(metadata.AgeRatings, id => NameOf(playniteApi.Database.AgeRatings.Get(id))).FirstOrDefault();
            return context;
        }

        public async Task<OriginIntegrationMedia> GetMediaAsync(OriginIntegrationResult result, MediaKind kind, CancellationToken cancelToken)
        {
            if (result == null || result.Metadata == null)
            {
                return null;
            }

            var file = kind == MediaKind.Cover
                ? result.Metadata.CoverImage
                : kind == MediaKind.Icon
                    ? result.Metadata.Icon
                    : result.Metadata.BackgroundImage;
            if (file == null || !file.HasContent)
            {
                return null;
            }

            cancelToken.ThrowIfCancellationRequested();
            var path = file.Path;
            if (file.Content != null && file.Content.Length > 0)
            {
                path = await MaterializeAsync(result, kind, file, cancelToken).ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(path) &&
                     !Uri.IsWellFormedUriString(path, UriKind.Absolute) &&
                     !File.Exists(path))
            {
                try
                {
                    var fullPath = playniteApi.Database.GetFullFilePath(path);
                    if (File.Exists(fullPath))
                    {
                        path = fullPath;
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return new OriginIntegrationMedia
            {
                Path = path,
                Extension = GetExtension(file.FileName, path),
                IntegrationName = result.IntegrationName
            };
        }

        private LibraryPlugin FindIntegration(Guid pluginId)
        {
            try
            {
                return playniteApi.Addons.Plugins.OfType<LibraryPlugin>().FirstOrDefault(x => x.Id == pluginId);
            }
            catch
            {
                return null;
            }
        }

        private bool IsDisabled(Guid integrationId)
        {
            return settings != null &&
                   settings.DisabledOriginIntegrationIds != null &&
                   settings.DisabledOriginIntegrationIds.Contains(integrationId);
        }

        private static bool TryGetCached(string key, out OriginIntegrationResult result)
        {
            lock (CacheLock)
            {
                CacheEntry entry;
                if (MetadataCache.TryGetValue(key, out entry) && DateTime.UtcNow - entry.Created < TimeSpan.FromMinutes(15))
                {
                    result = entry.Result;
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static OriginIntegrationResult Cache(string key, OriginIntegrationResult result)
        {
            lock (CacheLock)
            {
                if (MetadataCache.Count > 12)
                {
                    MetadataCache.Clear();
                }

                MetadataCache[key] = new CacheEntry { Created = DateTime.UtcNow, Result = result };
            }

            return result;
        }

        private static List<string> ResolveProperties(IEnumerable<MetadataProperty> properties, Func<Guid, string> idResolver)
        {
            return (properties ?? Enumerable.Empty<MetadataProperty>())
                .Select(x =>
                {
                    var named = x as MetadataNameProperty;
                    if (named != null)
                    {
                        return named.Name;
                    }

                    var identified = x as MetadataIdProperty;
                    return identified == null || idResolver == null ? null : idResolver(identified.Id);
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NameOf(DatabaseObject item)
        {
            return item == null ? null : item.Name;
        }

        private static async Task<string> MaterializeAsync(OriginIntegrationResult result, MediaKind kind, MetadataFile file, CancellationToken cancelToken)
        {
            var folder = Path.Combine(Path.GetTempPath(), "MetadataAI", "OriginIntegrationMedia");
            Directory.CreateDirectory(folder);
            CleanupTempFolder(folder);
            var extension = GetExtension(file.FileName, file.Path);
            var name = result.IntegrationId.ToString("N") + "_" + kind.ToString().ToLowerInvariant() + "_" + Math.Abs(ComputeContentHash(file.Content)) + extension;
            var path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
                await Task.Run(() => File.WriteAllBytes(path, file.Content), cancelToken).ConfigureAwait(false);
            }

            return path;
        }

        private static void CleanupTempFolder(string folder)
        {
            lock (CacheLock)
            {
                if (tempFolderCleaned)
                {
                    return;
                }

                tempFolderCleaned = true;
            }

            try
            {
                foreach (var path in Directory.GetFiles(folder))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1))
                        {
                            File.Delete(path);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static int ComputeContentHash(byte[] content)
        {
            unchecked
            {
                var hash = 17;
                if (content == null)
                {
                    return hash;
                }

                var step = Math.Max(1, content.Length / 64);
                for (var index = 0; index < content.Length; index += step)
                {
                    hash = hash * 31 + content[index];
                }

                return hash * 31 + content.Length;
            }
        }

        private static string GetExtension(string fileName, string path)
        {
            var extension = Path.GetExtension(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(extension))
            {
                try
                {
                    extension = Path.GetExtension(new Uri(path ?? string.Empty, UriKind.RelativeOrAbsolute).IsAbsoluteUri
                        ? new Uri(path).AbsolutePath
                        : path);
                }
                catch
                {
                }
            }

            return string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
        }

        private class CacheEntry
        {
            public DateTime Created { get; set; }
            public OriginIntegrationResult Result { get; set; }
        }
    }
}
