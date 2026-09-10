using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Minimal client for the documented Codex app-server stdio protocol.
    /// The app-server owns ChatGPT authentication and token refresh; this
    /// client only exchanges JSON-RPC messages with the local process.
    /// </summary>
    internal sealed class CodexAppServerClient : IDisposable
    {
        private static readonly TimeSpan ReadTimeout = TimeSpan.FromMinutes(10);

        private readonly Process process;
        private readonly StreamReader output;
        private readonly StreamWriter input;
        private readonly Task<string> standardErrorTask;
        private int nextRequestId;
        private bool disposed;

        private CodexAppServerClient(Process process)
        {
            this.process = process;
            output = process.StandardOutput;
            input = process.StandardInput;
            input.AutoFlush = true;
            standardErrorTask = process.StandardError.ReadToEndAsync();
        }

        public static async Task<CodexAppServerClient> StartAsync(
            string executablePath,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(executablePath) ? "codex" : executablePath.Trim(),
                Arguments = "app-server --listen stdio://",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            Process process = null;
            try
            {
                process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                if (!process.Start())
                {
                    throw new InvalidOperationException("The Codex app-server process could not be started.");
                }

                var client = new CodexAppServerClient(process);
                await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
                return client;
            }
            catch (Exception ex)
            {
                if (process != null)
                {
                    TryStopProcess(process);
                    process.Dispose();
                }

                if (ex is Win32Exception)
                {
                    throw new InvalidOperationException(
                        "Codex was not found. Install the Codex CLI or set its full path in Metadata AI provider settings.",
                        ex);
                }

                throw;
            }
        }

        public async Task<JObject> RequestAsync(
            string method,
            JObject parameters,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            var requestId = Interlocked.Increment(ref nextRequestId);
            var request = new JObject();
            request["method"] = method;
            request["id"] = requestId;
            request["params"] = parameters ?? new JObject();
            await input.WriteLineAsync(request.ToString(Formatting.None)).ConfigureAwait(false);

            while (true)
            {
                var message = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                var messageId = (int?)message["id"];
                if (messageId != requestId)
                {
                    continue;
                }

                var error = message["error"] as JObject;
                if (error != null)
                {
                    var errorMessage = (string)error["message"];
                    throw new CodexAppServerException(
                        string.IsNullOrWhiteSpace(errorMessage)
                            ? "The Codex app-server request failed."
                            : errorMessage);
                }

                return message["result"] as JObject ?? new JObject();
            }
        }

        public async Task<JObject> ReadNotificationAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            while (true)
            {
                var message = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                if (message["id"] == null)
                {
                    return message;
                }
            }
        }

        public async Task<JObject> ReadAccountAsync(CancellationToken cancellationToken)
        {
            var parameters = new JObject();
            parameters["refreshToken"] = true;
            return await RequestAsync("account/read", parameters, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> GenerateTextAsync(
            string model,
            string prompt,
            int maxTokens,
            CancellationToken cancellationToken)
        {
            // maxTokens is retained in this method's contract so the app-server
            // provider follows the same generation budget as HTTP providers.
            // Codex app-server currently controls the final model budget itself.
            if (maxTokens < 0)
            {
                throw new ArgumentOutOfRangeException("maxTokens");
            }

            var account = await ReadAccountAsync(cancellationToken).ConfigureAwait(false);
            var accountObject = account["account"] as JObject;
            var accountType = accountObject == null ? null : (string)accountObject["type"];
            if (!string.Equals(accountType, "chatgpt", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Sign in with ChatGPT in the Metadata AI provider settings before generating metadata.");
            }

            var threadParameters = new JObject();
            threadParameters["model"] = model;
            threadParameters["serviceName"] = "metadata_ai_plugin";
            var thread = await RequestAsync("thread/start", threadParameters, cancellationToken).ConfigureAwait(false);
            var threadObject = thread["thread"] as JObject;
            var threadId = threadObject == null ? null : (string)threadObject["id"];
            if (string.IsNullOrWhiteSpace(threadId))
            {
                throw new CodexAppServerException("Codex did not return a thread id.");
            }

            var turnParameters = new JObject();
            turnParameters["threadId"] = threadId;
            var input = new JArray();
            var inputText = new JObject();
            inputText["type"] = "text";
            inputText["text"] = prompt;
            input.Add(inputText);
            turnParameters["input"] = input;
            turnParameters["model"] = model;
            turnParameters["approvalPolicy"] = "never";

            var sandboxPolicy = new JObject();
            sandboxPolicy["type"] = "readOnly";
            var access = new JObject();
            access["type"] = "fullAccess";
            sandboxPolicy["access"] = access;
            turnParameters["sandboxPolicy"] = sandboxPolicy;

            var outputSchema = new JObject();
            outputSchema["type"] = "object";
            outputSchema["additionalProperties"] = true;
            turnParameters["outputSchema"] = outputSchema;
            turnParameters["summary"] = "concise";
            await RequestAsync("turn/start", turnParameters, cancellationToken).ConfigureAwait(false);

            var responseText = new StringBuilder();
            while (true)
            {
                var message = await ReadNotificationAsync(cancellationToken).ConfigureAwait(false);
                var method = (string)message["method"];
                var parameters = message["params"] as JObject;

                if (string.Equals(method, "item/agentMessage/delta", StringComparison.OrdinalIgnoreCase))
                {
                    var delta = parameters == null ? null : (string)parameters["delta"];
                    if (!string.IsNullOrEmpty(delta))
                    {
                        responseText.Append(delta);
                    }
                }
                else if (string.Equals(method, "item/completed", StringComparison.OrdinalIgnoreCase))
                {
                    var item = parameters == null ? null : parameters["item"] as JObject;
                    if (item != null && string.Equals((string)item["type"], "agentMessage", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = (string)item["text"];
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            responseText.Clear();
                            responseText.Append(text);
                        }
                    }
                }
                else if (string.Equals(method, "turn/completed", StringComparison.OrdinalIgnoreCase))
                {
                    var turn = parameters == null ? null : parameters["turn"] as JObject;
                    var status = turn == null ? null : (string)turn["status"];
                    if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        var error = turn["error"] as JObject;
                        throw new CodexAppServerException(
                            error == null || string.IsNullOrWhiteSpace((string)error["message"])
                                ? "Codex failed to generate metadata."
                                : (string)error["message"]);
                    }

                    break;
                }
            }

            var result = responseText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(result))
            {
                throw new CodexAppServerException("Codex completed without returning metadata.");
            }

            return result;
        }

        public static async Task LoginAsync(
            string executablePath,
            Action<string> openAuthorizationUrl,
            CancellationToken cancellationToken)
        {
            using (var client = await StartAsync(executablePath, cancellationToken).ConfigureAwait(false))
            {
                var loginParameters = new JObject();
                loginParameters["type"] = "chatgpt";
                loginParameters["useHostedLoginSuccessPage"] = true;
                loginParameters["appBrand"] = "chatgpt";
                var login = await client.RequestAsync(
                    "account/login/start",
                    loginParameters,
                    cancellationToken).ConfigureAwait(false);
                var authUrl = (string)login["authUrl"];
                if (string.IsNullOrWhiteSpace(authUrl))
                {
                    throw new CodexAppServerException("Codex did not return a ChatGPT authorization URL.");
                }

                if (openAuthorizationUrl == null)
                {
                    throw new ArgumentNullException("openAuthorizationUrl");
                }

                openAuthorizationUrl(authUrl);
                var loginId = (string)login["loginId"];
                while (true)
                {
                    var notification = await client.ReadNotificationAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.Equals((string)notification["method"], "account/login/completed", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var parameters = notification["params"] as JObject;
                    var notificationLoginId = parameters == null ? null : (string)parameters["loginId"];
                    if (!string.IsNullOrWhiteSpace(loginId) && !string.Equals(notificationLoginId, loginId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var successToken = parameters == null ? null : parameters["success"];
                    var success = successToken != null && successToken.Type == JTokenType.Boolean && successToken.Value<bool>();
                    if (!success)
                    {
                        var error = parameters == null ? null : (string)parameters["error"];
                        throw new CodexAppServerException(
                            string.IsNullOrWhiteSpace(error) ? "ChatGPT sign-in was not completed." : error);
                    }

                    return;
                }
            }
        }

        public static async Task LogoutAsync(string executablePath, CancellationToken cancellationToken)
        {
            using (var client = await StartAsync(executablePath, cancellationToken).ConfigureAwait(false))
            {
                await client.RequestAsync("account/logout", new JObject(), cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var parameters = new JObject();
            var clientInfo = new JObject();
            clientInfo["name"] = "metadata_ai_playnite";
            clientInfo["title"] = "Metadata AI Playnite extension";
            clientInfo["version"] = typeof(CodexAppServerClient).Assembly.GetName().Version.ToString(3);
            parameters["clientInfo"] = clientInfo;
            await RequestAsync("initialize", parameters, cancellationToken).ConfigureAwait(false);

            var initialized = new JObject();
            initialized["method"] = "initialized";
            initialized["params"] = new JObject();
            await input.WriteLineAsync(initialized.ToString(Formatting.None)).ConfigureAwait(false);
        }

        private async Task<JObject> ReadMessageAsync(CancellationToken cancellationToken)
        {
            var readTask = output.ReadLineAsync();
            var completed = await Task.WhenAny(
                readTask,
                Task.Delay(ReadTimeout, cancellationToken)).ConfigureAwait(false);
            if (completed != readTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Codex app-server did not respond within the expected time.");
            }

            var line = await readTask.ConfigureAwait(false);
            if (line == null)
            {
                var error = standardErrorTask.IsCompleted
                    ? await standardErrorTask.ConfigureAwait(false)
                    : string.Empty;
                throw new CodexAppServerException(
                    string.IsNullOrWhiteSpace(error)
                        ? "Codex app-server closed the connection unexpectedly."
                        : "Codex app-server stopped: " + error.Trim());
            }

            try
            {
                return JObject.Parse(line);
            }
            catch (Exception ex)
            {
                throw new CodexAppServerException("Codex app-server returned invalid JSON.", ex);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("CodexAppServerClient");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            TryStopProcess(process);
            process.Dispose();
        }

        private static void TryStopProcess(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch
            {
                // Cleanup must not hide the original provider error.
            }
        }
    }

    internal sealed class CodexAppServerException : Exception
    {
        public CodexAppServerException(string message)
            : base(message)
        {
        }

        public CodexAppServerException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
