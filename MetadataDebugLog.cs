using System;
using System.IO;
using System.Text;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Append-only organize/apply debug log under the plugin user-data folder.
    /// Always on so batch genre issues can be diagnosed without a settings hunt.
    /// </summary>
    public static class MetadataDebugLog
    {
        private static readonly object Gate = new object();
        private static string directory;
        private static string filePath;
        private static string lastUserDataPath;
        private static Func<string> userDataPathResolver;
        private const long MaxBytes = 8L * 1024L * 1024L;

        public static string FilePath
        {
            get { return filePath; }
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
                    WriteUnlocked(
                        "==== Metadata AI organize debug log ready ====\r\n" +
                        "File: " + filePath + "\r\n" +
                        "Delete this file anytime; it is recreated on the next organize/apply.\r\n");
                }
                catch
                {
                }
            }
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

        public static void Write(string section, string details)
        {
            EnsureReady();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var block = new StringBuilder();
            block.Append("----- ").Append(stamp).Append(" | ").Append(section ?? "log").Append(" -----\r\n");
            if (!string.IsNullOrEmpty(details))
            {
                block.Append(details.TrimEnd());
                block.Append("\r\n");
            }

            block.Append("\r\n");

            lock (Gate)
            {
                try
                {
                    RotateIfNeededUnlocked();
                    WriteUnlocked(block.ToString());
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
    }
}
