using System;

namespace MetaDataIAPlugin
{
    public static class ProviderEndpointHelper
    {
        /// <summary>
        /// Resolves an OpenAI-compatible chat completions URL.
        /// Accepts either a base URL (e.g. https://api.deepseek.com or https://api.openai.com/v1)
        /// or a full path ending in /chat/completions (or /messages).
        /// </summary>
        public static string ResolveChatCompletionsUri(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return endpoint;
            }

            Uri uri;
            if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out uri))
            {
                return endpoint.Trim();
            }

            var path = uri.AbsolutePath == null ? string.Empty : uri.AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            {
                return uri.AbsoluteUri;
            }

            var builder = new UriBuilder(uri);
            builder.Path = (string.IsNullOrEmpty(path) ? string.Empty : path) + "/chat/completions";
            return builder.Uri.AbsoluteUri;
        }
    }
}
