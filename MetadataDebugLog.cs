using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Append-only organize/apply debug log under the plugin user-data folder.
    /// Always on: batch-readable summaries by default; full dumps on warn/error.
    /// </summary>
    public static class MetadataDebugLog
    {
        public enum Level
        {
            Info,
            Warn,
            Error
        }

        private static readonly object Gate = new object();
        private static readonly ConcurrentDictionary<Guid, GameScope> OpenGames =
            new ConcurrentDictionary<Guid, GameScope>();

        private static string directory;
        private static string filePath;
        private static string lastUserDataPath;
        private static Func<string> userDataPathResolver;
        private static string currentBatchId;
        private static bool verbose;

        private const long MaxBytes = 8L * 1024L * 1024L;
        private const int MaxDetailChars = 48000;
        private const int LeanDescriptionPreviewChars = 600;

        private sealed class GameScope
        {
            public Guid Id;
            public string Title;
            public string BatchId;
            public Stopwatch Watch;
        }

        public static string FilePath
        {
            get { return filePath; }
        }

        public static string CurrentBatchId
        {
            get { return currentBatchId; }
        }

        public static bool Verbose
        {
            get { return verbose; }
            set { verbose = value; }
        }

        public static void SetUserDataPathResolver(Func<string> resolver)
        {
            userDataPathResolver = resolver;
        }

        public static void Initialize(string userDataPath)
        {
            if (string.IsNullOrWhiteSpace(userDataPath))
            {
                return;
            }

            lock (Gate)
            {
                lastUserDataPath = userDataPath;
                directory = Path.Combine(userDataPath, "logs");
                filePath = Path.Combine(directory, "organize-debug.log");
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch
                {
                }

                try
                {
                    if (!File.Exists(filePath))
                    {
                        WriteUnlocked(BuildHeader());
                    }
                }
                catch
                {
                }
            }
        }

        public static void Clear()
        {
            EnsureReady();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(filePath, BuildHeader(), Encoding.UTF8);
                }
                catch
                {
                }
            }
        }

        public static void EnsureInitialized()
        {
            EnsureReady();
        }

        private static string BuildHeader()
        {
            return "==== Metadata AI organize debug log ====\r\n" +
                   "File: " + filePath + "\r\n" +
                   "Levels: INFO WARN ERROR  |  Default: steps + summaries. Verbose: full prompts/responses.\r\n" +
                   "Delete this file anytime; it is recreated on the next organize/apply.\r\n\r\n";
        }

        public static string NewBatchId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public static string ShortId(Guid id)
        {
            return id.ToString("N").Substring(0, 8);
        }

        public static void BatchBegin(
            string batchId,
            int gameCount,
            string provider,
            string model,
            string language)
        {
            currentBatchId = batchId;
            var details =
                "games=" + gameCount +
                " provider=" + (provider ?? string.Empty) +
                " model=" + (model ?? string.Empty) +
                " language=" + (language ?? string.Empty);
            WriteBanner("BATCH begin | id=" + (batchId ?? string.Empty), details);
        }

        public static void BatchEnd(
            string batchId,
            int processed,
            int failed,
            int cancelled,
            bool stopped,
            TimeSpan elapsed)
        {
            CloseOpenGamesForBatch(batchId);
            var details =
                "ok=" + processed +
                " failed=" + failed +
                " cancelled=" + cancelled +
                " stopped=" + stopped +
                " | " + FormatElapsed(elapsed);
            WriteBanner("BATCH end | id=" + (batchId ?? string.Empty) + " | " + details, null);
            if (string.Equals(currentBatchId, batchId, StringComparison.Ordinal))
            {
                currentBatchId = null;
            }
        }

        private static void CloseOpenGamesForBatch(string batchId)
        {
            if (string.IsNullOrEmpty(batchId))
            {
                return;
            }

            foreach (var pair in OpenGames.ToArray())
            {
                var scope = pair.Value;
                if (scope == null || !string.Equals(scope.BatchId, batchId, StringComparison.Ordinal))
                {
                    continue;
                }

                GameScope removed;
                if (!OpenGames.TryRemove(pair.Key, out removed) || removed == null)
                {
                    continue;
                }

                var elapsed = removed.Watch == null ? string.Empty : FormatElapsed(removed.Watch.Elapsed);
                WriteBanner(
                    "GAME end | " + removed.Title +
                    " | id=" + ShortId(removed.Id) +
                    " | batch=" + batchId +
                    " | incomplete" +
                    (string.IsNullOrEmpty(elapsed) ? string.Empty : " | " + elapsed),
                    "reason=batch-ended-while-open");
            }
        }

        public static void GameBegin(Game game, string batchId = null)
        {
            if (game == null)
            {
                return;
            }

            var scope = new GameScope
            {
                Id = game.Id,
                Title = game.Name ?? string.Empty,
                BatchId = batchId ?? currentBatchId,
                Watch = Stopwatch.StartNew()
            };
            OpenGames[game.Id] = scope;

            WriteBanner(
                "GAME begin | " + scope.Title + " | id=" + ShortId(scope.Id) +
                (string.IsNullOrEmpty(scope.BatchId) ? string.Empty : " | batch=" + scope.BatchId),
                null);
        }

        public static void GameEnd(Game game, bool ok, string reason = null)
        {
            if (game == null)
            {
                return;
            }

            GameScope scope;
            OpenGames.TryRemove(game.Id, out scope);
            var title = game.Name ?? (scope == null ? string.Empty : scope.Title);
            var id = ShortId(game.Id);
            var batch = scope == null ? currentBatchId : scope.BatchId;
            var elapsed = scope == null || scope.Watch == null
                ? string.Empty
                : FormatElapsed(scope.Watch.Elapsed);
            var status = ok ? "ok" : "failed";
            var line = "GAME end | " + title + " | id=" + id +
                       (string.IsNullOrEmpty(batch) ? string.Empty : " | batch=" + batch) +
                       " | " + status +
                       (string.IsNullOrEmpty(elapsed) ? string.Empty : " | " + elapsed);
            if (!ok && !string.IsNullOrWhiteSpace(reason))
            {
                WriteBanner(line, "reason=" + reason.Trim());
            }
            else
            {
                WriteBanner(line, null);
            }
        }

        public static void Info(Game game, string topic, string details, string detailDump = null)
        {
            WriteLine(Level.Info, game, topic, details, detailDump);
        }

        public static void Info(string topic, string details, string detailDump = null)
        {
            WriteLine(Level.Info, null, topic, details, detailDump);
        }

        public static void Warn(Game game, string topic, string details, string detailDump = null)
        {
            WriteLine(Level.Warn, game, topic, details, detailDump);
        }

        public static void Warn(string topic, string details, string detailDump = null)
        {
            WriteLine(Level.Warn, null, topic, details, detailDump);
        }

        public static void Error(
            Game game,
            string topic,
            string details,
            Exception ex = null,
            string detailDump = null)
        {
            var dump = CombineDump(detailDump, FormatException(ex));
            WriteLine(Level.Error, game, topic, details, dump);
        }

        public static void Error(string topic, string details, Exception ex = null, string detailDump = null)
        {
            var dump = CombineDump(detailDump, FormatException(ex));
            WriteLine(Level.Error, null, topic, details, dump);
        }

        public static string FormatTermList(IEnumerable<string> terms)
        {
            return "[" + string.Join(", ", terms ?? Enumerable.Empty<string>()) + "]";
        }

        public static string PrettyJsonOrRaw(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var trimmed = text.Trim();
            try
            {
                var token = JToken.Parse(trimmed);
                return token.ToString(Formatting.Indented);
            }
            catch
            {
                return trimmed;
            }
        }

        public static string Truncate(string text, int maxChars = MaxDetailChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, maxChars) +
                   "\r\n…(truncated, " + text.Length.ToString(CultureInfo.InvariantCulture) + " chars total)";
        }

        public static string DescriptionForLog(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return "(empty)";
            }

            if (verbose)
            {
                return Truncate(description);
            }

            return Truncate(description, LeanDescriptionPreviewChars);
        }

        /// <summary>
        /// INFO payloads follow Verbose. WARN/ERROR always keep the dump.
        /// </summary>
        public static string DetailIfVerbose(string detailDump)
        {
            return verbose ? detailDump : null;
        }

        private static void EnsureReady()
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            string path = null;
            if (userDataPathResolver != null)
            {
                try
                {
                    path = userDataPathResolver();
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                path = lastUserDataPath;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                Initialize(path);
            }
        }

        private static void WriteBanner(string title, string details)
        {
            EnsureReady();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var block = new StringBuilder();
            block.Append("===== ").Append(stamp).Append(" | ").Append(title ?? "log").Append(" =====\r\n");
            if (!string.IsNullOrEmpty(details))
            {
                block.Append(details.TrimEnd());
                block.Append("\r\n");
            }

            block.Append("\r\n");
            Append(block.ToString());
        }

        private static void WriteLine(
            Level level,
            Game game,
            string topic,
            string details,
            string detailDump)
        {
            EnsureReady();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            // Keep WARN/ERROR dumps always; INFO dumps only in verbose mode.
            if (level == Level.Info && !verbose)
            {
                detailDump = null;
            }

            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var levelLabel = LevelLabel(level);
            var block = new StringBuilder();
            block.Append(levelLabel)
                .Append("  ")
                .Append(stamp)
                .Append(" | ")
                .Append(topic ?? "log");

            if (game != null)
            {
                block.Append(" | ")
                    .Append(game.Name ?? string.Empty)
                    .Append(" | id=")
                    .Append(ShortId(game.Id));
            }

            block.Append("\r\n");
            if (!string.IsNullOrEmpty(details))
            {
                block.Append(details.TrimEnd());
                block.Append("\r\n");
            }

            if (!string.IsNullOrEmpty(detailDump))
            {
                block.Append("--- detail ---\r\n");
                block.Append(Truncate(detailDump).TrimEnd());
                block.Append("\r\n--- end detail ---\r\n");
            }

            block.Append("\r\n");
            Append(block.ToString());
        }

        private static void Append(string text)
        {
            lock (Gate)
            {
                try
                {
                    RotateIfNeededUnlocked();
                    WriteUnlocked(text);
                }
                catch
                {
                }
            }
        }

        private static void WriteUnlocked(string text)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrEmpty(text))
            {
                return;
            }

            File.AppendAllText(filePath, text, Encoding.UTF8);
        }

        private static void RotateIfNeededUnlocked()
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            var info = new FileInfo(filePath);
            if (info.Length < MaxBytes)
            {
                return;
            }

            var backup = filePath + ".old";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }

            File.Move(filePath, backup);
        }

        private static string LevelLabel(Level level)
        {
            switch (level)
            {
                case Level.Warn:
                    return "WARN ";
                case Level.Error:
                    return "ERROR";
                default:
                    return "INFO ";
            }
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
            {
                return elapsed.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture);
            }

            return elapsed.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
        }

        private static string FormatException(Exception ex)
        {
            return ex == null ? null : ex.ToString();
        }

        private static string CombineDump(string detailDump, string exceptionText)
        {
            if (string.IsNullOrEmpty(detailDump))
            {
                return exceptionText;
            }

            if (string.IsNullOrEmpty(exceptionText))
            {
                return detailDump;
            }

            return detailDump.TrimEnd() + "\r\n\r\nexception:\r\n" + exceptionText;
        }
    }
}
