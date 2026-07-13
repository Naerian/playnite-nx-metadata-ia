using System;
using System.Security.Cryptography;
using System.Text;

namespace MetaDataIAPlugin
{
    internal static class SecretProtectionService
    {
        private const string Prefix = "metadata-ai-dpapi-v1:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(
            "MetaDataIAPlugin_2f42c46c-9e3f-48cb-99b6-7f41f12d9b83");

        public static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value) || IsProtected(value))
            {
                return value ?? string.Empty;
            }

            var plainBytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protectedBytes);
        }

        public static bool TryUnprotect(string value, out string plainText)
        {
            plainText = value ?? string.Empty;
            if (string.IsNullOrEmpty(value) || !IsProtected(value))
            {
                return true;
            }

            try
            {
                var protectedBytes = Convert.FromBase64String(value.Substring(Prefix.Length));
                var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                plainText = Encoding.UTF8.GetString(plainBytes);
                return true;
            }
            catch (CryptographicException)
            {
                plainText = string.Empty;
                return false;
            }
            catch (FormatException)
            {
                plainText = string.Empty;
                return false;
            }
        }

        private static bool IsProtected(string value)
        {
            return !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);
        }
    }
}
