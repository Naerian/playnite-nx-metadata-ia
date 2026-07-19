using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MetaDataIAPlugin
{
    public class MetadataHistoryDocument
    {
        public List<MetadataHistoryOperation> Operations { get; set; }

        public MetadataHistoryDocument()
        {
            Operations = new List<MetadataHistoryOperation>();
        }
    }

    public class MetadataHistoryOperation
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Kind { get; set; }
        public bool Undone { get; set; }
        public List<MetadataHistoryGameEntry> Games { get; set; }

        public MetadataHistoryOperation()
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTime.Now;
            Games = new List<MetadataHistoryGameEntry>();
        }
    }

    public class MetadataHistoryGameEntry
    {
        public Guid GameId { get; set; }
        public string GameName { get; set; }
        public GameMetadataSnapshot Before { get; set; }
        public GameMetadataSnapshot After { get; set; }
        public List<string> ChangedFields { get; set; }
        public List<MetadataFieldProvenance> Provenance { get; set; }

        public MetadataHistoryGameEntry()
        {
            ChangedFields = new List<string>();
            Provenance = new List<MetadataFieldProvenance>();
        }
    }

    public class LinkSnapshot
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    public class GameMetadataSnapshot
    {
        public string Description { get; set; }
        public string SortingName { get; set; }
        public List<Guid> GenreIds { get; set; }
        public List<Guid> TagIds { get; set; }
        public List<Guid> FeatureIds { get; set; }
        public List<Guid> DeveloperIds { get; set; }
        public List<Guid> PublisherIds { get; set; }
        public List<Guid> AgeRatingIds { get; set; }
        public List<Guid> RegionIds { get; set; }
        public List<Guid> CategoryIds { get; set; }
        public List<LinkSnapshot> Links { get; set; }
        public string CoverImage { get; set; }
        public string Icon { get; set; }
        public string BackgroundImage { get; set; }
        public string CoverBackupFile { get; set; }
        public string IconBackupFile { get; set; }
        public string BackgroundBackupFile { get; set; }

        public GameMetadataSnapshot()
        {
            GenreIds = new List<Guid>();
            TagIds = new List<Guid>();
            FeatureIds = new List<Guid>();
            DeveloperIds = new List<Guid>();
            PublisherIds = new List<Guid>();
            AgeRatingIds = new List<Guid>();
            RegionIds = new List<Guid>();
            CategoryIds = new List<Guid>();
            Links = new List<LinkSnapshot>();
        }
    }

    public sealed class MetadataHistoryService
    {
        private const int MaxOperations = 20;
        private readonly IPlayniteAPI api;
        private readonly string rootPath;
        private readonly string documentPath;
        private readonly string mediaPath;
        private readonly object syncRoot;

        public MetadataHistoryService(IPlayniteAPI api, string pluginUserDataPath)
        {
            this.api = api;
            rootPath = pluginUserDataPath;
            documentPath = Path.Combine(rootPath, "metadata-history.json");
            mediaPath = Path.Combine(rootPath, "HistoryMedia");
            syncRoot = new object();
        }

        public MetadataHistoryOperation BeginOperation(string kind)
        {
            return new MetadataHistoryOperation { Kind = kind ?? "Metadata AI" };
        }

        public GameMetadataSnapshot Capture(Game game, MetadataHistoryOperation operation, bool backupMedia)
        {
            if (game == null)
            {
                return null;
            }

            var snapshot = new GameMetadataSnapshot
            {
                Description = game.Description,
                SortingName = game.SortingName,
                GenreIds = Copy(game.GenreIds),
                TagIds = Copy(game.TagIds),
                FeatureIds = Copy(game.FeatureIds),
                DeveloperIds = Copy(game.DeveloperIds),
                PublisherIds = Copy(game.PublisherIds),
                AgeRatingIds = Copy(game.AgeRatingIds),
                RegionIds = Copy(game.RegionIds),
                CategoryIds = Copy(game.CategoryIds),
                Links = game.Links == null ? new List<LinkSnapshot>() : game.Links.Where(x => x != null).Select(x => new LinkSnapshot { Name = x.Name, Url = x.Url }).ToList(),
                CoverImage = game.CoverImage,
                Icon = game.Icon,
                BackgroundImage = game.BackgroundImage
            };

            if (backupMedia && operation != null)
            {
                snapshot.CoverBackupFile = BackupMedia(operation.Id, game.Id, "cover", game.CoverImage);
                snapshot.IconBackupFile = BackupMedia(operation.Id, game.Id, "icon", game.Icon);
                snapshot.BackgroundBackupFile = BackupMedia(operation.Id, game.Id, "background", game.BackgroundImage);
            }

            return snapshot;
        }

        public void AddGame(MetadataHistoryOperation operation, Game game, GameMetadataSnapshot before, GameMetadataSnapshot after, IEnumerable<MetadataFieldProvenance> provenance)
        {
            if (operation == null || game == null || before == null || after == null)
            {
                return;
            }

            var changed = GetChangedFields(before, after);
            if (changed.Count == 0)
            {
                return;
            }

            operation.Games.Add(new MetadataHistoryGameEntry
            {
                GameId = game.Id,
                GameName = game.Name,
                Before = before,
                After = after,
                ChangedFields = changed,
                Provenance = provenance == null ? new List<MetadataFieldProvenance>() : provenance.Select(CloneProvenance).ToList()
            });
        }

        public void SaveOperation(MetadataHistoryOperation operation)
        {
            if (operation == null || operation.Games == null || operation.Games.Count == 0)
            {
                DeleteOperationMedia(operation == null ? Guid.Empty : operation.Id);
                return;
            }

            lock (syncRoot)
            {
                Directory.CreateDirectory(rootPath);
                var document = LoadDocumentInternal();
                document.Operations.Insert(0, operation);
                while (document.Operations.Count > MaxOperations)
                {
                    var removed = document.Operations[document.Operations.Count - 1];
                    document.Operations.RemoveAt(document.Operations.Count - 1);
                    DeleteOperationMedia(removed.Id);
                }

                SaveDocumentInternal(document);
            }
        }

        public List<MetadataHistoryOperation> GetOperations()
        {
            lock (syncRoot)
            {
                return LoadDocumentInternal().Operations
                    .Where(x => x != null && !x.Undone && x.Games != null && x.Games.Count > 0)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList();
            }
        }

        public MetadataHistoryGameEntry GetLatestForGame(Guid gameId)
        {
            return GetOperations()
                .Where(x => !x.Undone)
                .SelectMany(x => x.Games ?? new List<MetadataHistoryGameEntry>())
                .FirstOrDefault(x => x.GameId == gameId);
        }

        public MetadataHistoryOperation GetLatestUndoable()
        {
            return GetOperations().FirstOrDefault(x => !x.Undone && x.Games != null && x.Games.Count > 0);
        }

        public int Undo(Guid operationId)
        {
            return UndoOperation(operationId);
        }

        public int UndoOperation(Guid operationId)
        {
            lock (syncRoot)
            {
                var document = LoadDocumentInternal();
                var operation = document.Operations.FirstOrDefault(x => x.Id == operationId);
                if (operation == null || operation.Undone)
                {
                    return 0;
                }

                var restored = 0;
                foreach (var entry in operation.Games ?? new List<MetadataHistoryGameEntry>())
                {
                    var game = api.Database.Games[entry.GameId];
                    if (game == null || entry.Before == null)
                    {
                        continue;
                    }

                    Restore(game, entry.Before, entry.ChangedFields);
                    restored++;
                }

                document.Operations.Remove(operation);
                SaveDocumentInternal(document);
                DeleteOperationMedia(operation.Id);
                return restored;
            }
        }

        public bool UndoGame(Guid operationId, Guid gameId)
        {
            lock (syncRoot)
            {
                var document = LoadDocumentInternal();
                var operation = document.Operations.FirstOrDefault(x => x.Id == operationId && !x.Undone);
                if (operation == null || operation.Games == null)
                {
                    return false;
                }

                var entry = operation.Games.FirstOrDefault(x => x.GameId == gameId);
                if (entry == null || entry.Before == null)
                {
                    return false;
                }

                var game = api.Database.Games[entry.GameId];
                if (game == null)
                {
                    return false;
                }

                Restore(game, entry.Before, entry.ChangedFields);
                operation.Games.Remove(entry);
                DeleteGameMedia(operation.Id, entry.GameId);
                if (operation.Games.Count == 0)
                {
                    document.Operations.Remove(operation);
                    DeleteOperationMedia(operation.Id);
                }

                SaveDocumentInternal(document);
                return true;
            }
        }

        public void Clear()
        {
            lock (syncRoot)
            {
                if (File.Exists(documentPath))
                {
                    File.Delete(documentPath);
                }

                if (Directory.Exists(mediaPath))
                {
                    Directory.Delete(mediaPath, true);
                }
            }
        }

        private void Restore(Game game, GameMetadataSnapshot snapshot, IEnumerable<string> changedFields)
        {
            var fields = new HashSet<string>(changedFields ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (fields.Contains("description")) game.Description = snapshot.Description;
            if (fields.Contains("sortingName")) game.SortingName = snapshot.SortingName;
            if (fields.Contains("genres")) game.GenreIds = Copy(snapshot.GenreIds);
            if (fields.Contains("tags")) game.TagIds = Copy(snapshot.TagIds);
            if (fields.Contains("features")) game.FeatureIds = Copy(snapshot.FeatureIds);
            if (fields.Contains("developers")) game.DeveloperIds = Copy(snapshot.DeveloperIds);
            if (fields.Contains("publishers")) game.PublisherIds = Copy(snapshot.PublisherIds);
            if (fields.Contains("ageRatings")) game.AgeRatingIds = Copy(snapshot.AgeRatingIds);
            if (fields.Contains("regions")) game.RegionIds = Copy(snapshot.RegionIds);
            if (fields.Contains("categories")) game.CategoryIds = Copy(snapshot.CategoryIds);
            if (fields.Contains("links")) game.Links = new ObservableCollection<Link>((snapshot.Links ?? new List<LinkSnapshot>()).Select(x => new Link(x.Name, x.Url)));

            var currentCover = game.CoverImage;
            var currentIcon = game.Icon;
            var currentBackground = game.BackgroundImage;
            if (fields.Contains("cover")) game.CoverImage = RestoreMedia(game.Id, snapshot.CoverImage, snapshot.CoverBackupFile);
            if (fields.Contains("icon")) game.Icon = RestoreMedia(game.Id, snapshot.Icon, snapshot.IconBackupFile);
            if (fields.Contains("background")) game.BackgroundImage = RestoreMedia(game.Id, snapshot.BackgroundImage, snapshot.BackgroundBackupFile);

            api.Database.Games.Update(game);
            if (fields.Contains("cover")) MediaStorageCleanupService.TryRemoveUnreferencedMedia(api, currentCover);
            if (fields.Contains("icon")) MediaStorageCleanupService.TryRemoveUnreferencedMedia(api, currentIcon);
            if (fields.Contains("background")) MediaStorageCleanupService.TryRemoveUnreferencedMedia(api, currentBackground);
        }

        private string RestoreMedia(Guid gameId, string originalReference, string backupRelativePath)
        {
            if (string.IsNullOrWhiteSpace(backupRelativePath))
            {
                return originalReference;
            }

            var fullPath = Path.Combine(rootPath, backupRelativePath);
            return File.Exists(fullPath) ? api.Database.AddFile(fullPath, gameId) : originalReference;
        }

        private string BackupMedia(Guid operationId, Guid gameId, string kind, string databasePath)
        {
            if (operationId == Guid.Empty || string.IsNullOrWhiteSpace(databasePath))
            {
                return null;
            }

            try
            {
                var source = api.Database.GetFullFilePath(databasePath);
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                {
                    return null;
                }

                var directory = Path.Combine(mediaPath, operationId.ToString("N"), gameId.ToString("N"));
                Directory.CreateDirectory(directory);
                var destination = Path.Combine(directory, kind + Path.GetExtension(source));
                File.Copy(source, destination, true);
                return MakeRelativePath(rootPath, destination);
            }
            catch
            {
                return null;
            }
        }

        private MetadataHistoryDocument LoadDocumentInternal()
        {
            try
            {
                if (!File.Exists(documentPath))
                {
                    return new MetadataHistoryDocument();
                }

                return JsonConvert.DeserializeObject<MetadataHistoryDocument>(File.ReadAllText(documentPath)) ?? new MetadataHistoryDocument();
            }
            catch
            {
                return new MetadataHistoryDocument();
            }
        }

        private void SaveDocumentInternal(MetadataHistoryDocument document)
        {
            Directory.CreateDirectory(rootPath);
            var temp = documentPath + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(document, Formatting.Indented));
            if (File.Exists(documentPath))
            {
                File.Delete(documentPath);
            }
            File.Move(temp, documentPath);
        }

        private void DeleteOperationMedia(Guid operationId)
        {
            if (operationId == Guid.Empty)
            {
                return;
            }

            try
            {
                var path = Path.Combine(mediaPath, operationId.ToString("N"));
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private void DeleteGameMedia(Guid operationId, Guid gameId)
        {
            if (operationId == Guid.Empty || gameId == Guid.Empty)
            {
                return;
            }

            try
            {
                var path = Path.Combine(mediaPath, operationId.ToString("N"), gameId.ToString("N"));
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private static List<string> GetChangedFields(GameMetadataSnapshot before, GameMetadataSnapshot after)
        {
            var changed = new List<string>();
            if (!string.Equals(before.Description, after.Description, StringComparison.Ordinal)) changed.Add("description");
            if (!string.Equals(before.SortingName, after.SortingName, StringComparison.Ordinal)) changed.Add("sortingName");
            if (!Same(before.GenreIds, after.GenreIds)) changed.Add("genres");
            if (!Same(before.TagIds, after.TagIds)) changed.Add("tags");
            if (!Same(before.FeatureIds, after.FeatureIds)) changed.Add("features");
            if (!Same(before.DeveloperIds, after.DeveloperIds)) changed.Add("developers");
            if (!Same(before.PublisherIds, after.PublisherIds)) changed.Add("publishers");
            if (!Same(before.AgeRatingIds, after.AgeRatingIds)) changed.Add("ageRatings");
            if (!Same(before.RegionIds, after.RegionIds)) changed.Add("regions");
            if (!Same(before.CategoryIds, after.CategoryIds)) changed.Add("categories");
            if (!SameLinks(before.Links, after.Links)) changed.Add("links");
            if (!string.Equals(before.CoverImage, after.CoverImage, StringComparison.OrdinalIgnoreCase)) changed.Add("cover");
            if (!string.Equals(before.Icon, after.Icon, StringComparison.OrdinalIgnoreCase)) changed.Add("icon");
            if (!string.Equals(before.BackgroundImage, after.BackgroundImage, StringComparison.OrdinalIgnoreCase)) changed.Add("background");
            return changed;
        }

        private static bool Same(IEnumerable<Guid> first, IEnumerable<Guid> second)
        {
            return (first ?? Enumerable.Empty<Guid>()).SequenceEqual(second ?? Enumerable.Empty<Guid>());
        }

        private static bool SameLinks(IEnumerable<LinkSnapshot> first, IEnumerable<LinkSnapshot> second)
        {
            var left = (first ?? Enumerable.Empty<LinkSnapshot>()).Select(x => (x.Name ?? string.Empty) + "\n" + (x.Url ?? string.Empty));
            var right = (second ?? Enumerable.Empty<LinkSnapshot>()).Select(x => (x.Name ?? string.Empty) + "\n" + (x.Url ?? string.Empty));
            return left.SequenceEqual(right);
        }

        private static List<Guid> Copy(IEnumerable<Guid> values)
        {
            return values == null ? new List<Guid>() : values.ToList();
        }

        private static MetadataFieldProvenance CloneProvenance(MetadataFieldProvenance value)
        {
            return new MetadataFieldProvenance
            {
                Field = value.Field,
                Source = value.Source,
                Method = value.Method,
                Confidence = value.Confidence,
                Detail = value.Detail
            };
        }

        private static string MakeRelativePath(string basePath, string fullPath)
        {
            var baseUri = new Uri(AppendDirectorySeparator(basePath));
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(new Uri(fullPath)).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string value)
        {
            return value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? value : value + Path.DirectorySeparatorChar;
        }
    }
}
