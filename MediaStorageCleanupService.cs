using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MetaDataIAPlugin
{
    public class MediaCleanupItem
    {
        public string DatabasePath { get; set; }
        public string FullPath { get; set; }
        public long Size { get; set; }
    }

    public class MediaCleanupReport
    {
        public List<MediaCleanupItem> Items { get; set; }
        public int FailedCount { get; set; }

        public int FileCount { get { return Items == null ? 0 : Items.Count; } }
        public long TotalBytes { get { return Items == null ? 0 : Items.Sum(x => x.Size); } }

        public MediaCleanupReport()
        {
            Items = new List<MediaCleanupItem>();
        }
    }

    public static class MediaStorageCleanupService
    {
        public static MediaCleanupReport Scan(IPlayniteAPI api)
        {
            var report = new MediaCleanupReport();
            if (api == null || api.Database == null)
            {
                return report;
            }

            var storageRoot = GetStorageRoot(api);
            if (string.IsNullOrWhiteSpace(storageRoot) || !Directory.Exists(storageRoot))
            {
                return report;
            }

            var referenced = GetReferencedMediaPaths(api, storageRoot);
            foreach (var directory in Directory.GetDirectories(storageRoot, "*", SearchOption.TopDirectoryOnly))
            {
                Guid gameId;
                if (!Guid.TryParse(Path.GetFileName(directory), out gameId))
                {
                    continue;
                }

                foreach (var file in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    var fullPath = NormalizeFullPath(file);
                    if (referenced.Contains(fullPath) || !IsImageFile(fullPath))
                    {
                        continue;
                    }

                    var databasePath = ToDatabasePath(storageRoot, fullPath);
                    if (string.IsNullOrWhiteSpace(databasePath))
                    {
                        continue;
                    }

                    report.Items.Add(new MediaCleanupItem
                    {
                        DatabasePath = databasePath,
                        FullPath = fullPath,
                        Size = new FileInfo(fullPath).Length
                    });
                }
            }

            return report;
        }

        public static MediaCleanupReport Delete(IPlayniteAPI api, MediaCleanupReport scan)
        {
            var removed = new MediaCleanupReport();
            if (api == null || scan == null || scan.Items == null || scan.Items.Count == 0)
            {
                return removed;
            }

            var storageRoot = GetStorageRoot(api);
            var referenced = GetReferencedMediaPaths(api, storageRoot);
            foreach (var item in scan.Items)
            {
                try
                {
                    var fullPath = NormalizeFullPath(item.FullPath);
                    if (!IsInsideStorage(storageRoot, fullPath) || referenced.Contains(fullPath) || !File.Exists(fullPath) || !IsImageFile(fullPath))
                    {
                        continue;
                    }

                    api.Database.RemoveFile(item.DatabasePath);
                    if (!File.Exists(fullPath))
                    {
                        removed.Items.Add(item);
                    }
                    else
                    {
                        removed.FailedCount++;
                    }
                }
                catch
                {
                    removed.FailedCount++;
                }
            }

            return removed;
        }

        public static bool TryRemoveUnreferencedMedia(IPlayniteAPI api, string databasePath)
        {
            if (api == null || string.IsNullOrWhiteSpace(databasePath))
            {
                return false;
            }

            try
            {
                var storageRoot = GetStorageRoot(api);
                var fullPath = NormalizeFullPath(api.Database.GetFullFilePath(databasePath));
                if (!IsInsideStorage(storageRoot, fullPath) || !File.Exists(fullPath))
                {
                    return false;
                }

                var referenced = GetReferencedMediaPaths(api, storageRoot);
                if (referenced.Contains(fullPath))
                {
                    return false;
                }

                api.Database.RemoveFile(databasePath);
                return !File.Exists(fullPath);
            }
            catch
            {
                return false;
            }
        }

        private static HashSet<string> GetReferencedMediaPaths(IPlayniteAPI api, string storageRoot)
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var game in api.Database.Games)
            {
                AddReferencedPath(api, storageRoot, referenced, game.CoverImage);
                AddReferencedPath(api, storageRoot, referenced, game.Icon);
                AddReferencedPath(api, storageRoot, referenced, game.BackgroundImage);
            }

            return referenced;
        }

        private static void AddReferencedPath(IPlayniteAPI api, string storageRoot, HashSet<string> referenced, string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                return;
            }

            try
            {
                var fullPath = NormalizeFullPath(api.Database.GetFullFilePath(databasePath));
                if (IsInsideStorage(storageRoot, fullPath))
                {
                    referenced.Add(fullPath);
                }
            }
            catch
            {
            }
        }

        private static string GetStorageRoot(IPlayniteAPI api)
        {
            return NormalizeFullPath(Path.Combine(api.Database.DatabasePath, "files"));
        }

        private static string NormalizeFullPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsInsideStorage(string storageRoot, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(storageRoot) || string.IsNullOrWhiteSpace(fullPath))
            {
                return false;
            }

            var prefix = storageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToDatabasePath(string storageRoot, string fullPath)
        {
            if (!IsInsideStorage(storageRoot, fullPath))
            {
                return string.Empty;
            }

            var prefix = storageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.Substring(prefix.Length);
        }

        private static bool IsImageFile(string path)
        {
            try
            {
                var header = new byte[12];
                int read;
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    read = stream.Read(header, 0, header.Length);
                }

                return read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF ||
                       read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A ||
                       read >= 6 && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38 ||
                       read >= 2 && header[0] == 0x42 && header[1] == 0x4D ||
                       read >= 4 && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x01 && header[3] == 0x00 ||
                       read >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;
            }
            catch
            {
                return false;
            }
        }
    }
}
