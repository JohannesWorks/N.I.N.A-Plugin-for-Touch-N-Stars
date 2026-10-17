// TnsBridge.cs - Touch'N'Stars plugin bridge, contract version 1.
//
// Copy this file unchanged into your N.I.N.A. plugin project. It is deliberately a source file and
// not a DLL: every plugin compiles its own internal copy, so two plugins can never conflict over an
// assembly version in N.I.N.A.'s shared AppDomain. It depends only on NINA.Plugin (IMessageBroker,
// N.I.N.A. 3.1 or newer) and System.Text.Json.
//
// Documentation: https://github.com/Touch-N-Stars/N.I.N.A-Plugin-for-Touch-N-Stars/tree/master/docs/plugin-bridge

#nullable disable

using NINA.Plugin.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TouchNStarsBridge {

    /// <summary>
    /// Connects a N.I.N.A. plugin to Touch'N'Stars over the N.I.N.A. message broker. Register the
    /// routes, then call <see cref="Start"/>. Touch'N'Stars exposes every route to the app as
    /// <c>/api/ext/{id}/{path}</c> on its own web server; the plugin needs no server or port.
    /// </summary>
    internal sealed class TnsBridge : ISubscriber, IDisposable {
        public const int ContractVersion = 1;
        public const string DiscoverTopic = "TouchNStars.Discover";
        public const string AnnounceTopic = "TouchNStars.Announce";

        private static readonly Regex IdPattern = new Regex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);

        private readonly IMessageBroker broker;
        private readonly Guid senderId;
        private readonly Dictionary<string, Route> routes = new Dictionary<string, Route>(StringComparer.OrdinalIgnoreCase);
        private readonly object gate = new object();
        private bool started;
        private bool disposed;

        /// <param name="broker">The <see cref="IMessageBroker"/> injected into your plugin's constructor.</param>
        /// <param name="id">Stable, URL-safe id: lowercase letters, digits and dashes, e.g. "my-plugin".</param>
        /// <param name="name">Display name.</param>
        /// <param name="version">Your plugin version, shown in Touch'N'Stars' plugin list.</param>
        /// <param name="senderId">Optional: your plugin's identifier GUID.</param>
        public TnsBridge(IMessageBroker broker, string id, string name, string version, Guid senderId = default) {
            this.broker = broker ?? throw new ArgumentNullException(nameof(broker));
            if (id == null || !IdPattern.IsMatch(id)) {
                throw new ArgumentException("The id must match ^[a-z0-9][a-z0-9-]{0,63}$", nameof(id));
            }
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id : name;
            Version = version ?? string.Empty;
            this.senderId = senderId;
        }

        public string Id { get; }
        public string Name { get; }
        public string Version { get; }

        private string RequestTopic => $"TouchNStars.{Id}.Request";
        private string ResponseTopic => $"TouchNStars.{Id}.Response";
        private string EventTopic => $"TouchNStars.{Id}.Event";

        public TnsBridge Get(string path, Func<TnsRequest, Task<TnsResponse>> handler, TimeSpan? timeout = null) => Map("GET", path, handler, timeout);
        public TnsBridge Post(string path, Func<TnsRequest, Task<TnsResponse>> handler, TimeSpan? timeout = null) => Map("POST", path, handler, timeout);
        public TnsBridge Put(string path, Func<TnsRequest, Task<TnsResponse>> handler, TimeSpan? timeout = null) => Map("PUT", path, handler, timeout);
        public TnsBridge Delete(string path, Func<TnsRequest, Task<TnsResponse>> handler, TimeSpan? timeout = null) => Map("DELETE", path, handler, timeout);

        public TnsBridge Get(string path, Func<TnsRequest, TnsResponse> handler, TimeSpan? timeout = null) => Map("GET", path, Wrap(handler), timeout);
        public TnsBridge Post(string path, Func<TnsRequest, TnsResponse> handler, TimeSpan? timeout = null) => Map("POST", path, Wrap(handler), timeout);
        public TnsBridge Put(string path, Func<TnsRequest, TnsResponse> handler, TimeSpan? timeout = null) => Map("PUT", path, Wrap(handler), timeout);
        public TnsBridge Delete(string path, Func<TnsRequest, TnsResponse> handler, TimeSpan? timeout = null) => Map("DELETE", path, Wrap(handler), timeout);

        /// <summary>
        /// Registers a handler. <paramref name="timeout"/> raises how long Touch'N'Stars waits for this
        /// route (default and minimum 10 s, maximum 10 min). All routes must be registered before
        /// <see cref="Start"/>, because they are announced together.
        /// </summary>
        public TnsBridge Map(string method, string path, Func<TnsRequest, Task<TnsResponse>> handler, TimeSpan? timeout = null) {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (gate) {
                if (started) throw new InvalidOperationException("Register all routes before Start().");
                string normalizedMethod = (method ?? "GET").Trim().ToUpperInvariant();
                string normalizedPath = NormalizePath(path);
                routes[normalizedMethod + " " + normalizedPath] = new Route(normalizedMethod, normalizedPath, handler, timeout);
            }
            return this;
        }

        /// <summary>Subscribes to the broker and announces the plugin to Touch'N'Stars.</summary>
        public void Start() {
            lock (gate) {
                if (started || disposed) return;
                started = true;
            }
            broker.Subscribe(RequestTopic, this);
            broker.Subscribe(DiscoverTopic, this);
            // Touch'N'Stars may load before or after this plugin: announcing here covers "after",
            // answering its Discover broadcast covers "before".
            _ = Task.Run(AnnounceAsync);
        }

        /// <summary>
        /// Pushes an event to every app view subscribed to this plugin. <paramref name="data"/> is
        /// serialized as JSON. Events are additive: the same state must also be readable by request,
        /// because a phone that was asleep misses events.
        /// </summary>
        public Task PublishEventAsync(string name, object data = null) {
            var headers = new Dictionary<string, object> { ["event"] = name ?? string.Empty };
            string json = data == null ? null : JsonSerializer.Serialize(data, TnsJson.Options);
            return SafePublishAsync(new TnsBridgeMessage(EventTopic, json, headers, senderId, null));
        }

        Task ISubscriber.OnMessageReceived(IMessage message) {
            // Return immediately: the broker awaits every subscriber, so a handler doing real work
            // here would hold up Touch'N'Stars' publish call.
            if (message?.Topic == DiscoverTopic) {
                _ = Task.Run(AnnounceAsync);
            } else if (message?.Topic == RequestTopic) {
                _ = Task.Run(() => HandleRequestAsync(message));
            }
            return Task.CompletedTask;
        }

        public void Dispose() {
            lock (gate) {
                if (disposed) return;
                disposed = true;
            }
            try { broker.Unsubscribe(RequestTopic, this); } catch { }
            try { broker.Unsubscribe(DiscoverTopic, this); } catch { }
        }

        private Task AnnounceAsync() {
            List<Route> snapshot;
            lock (gate) {
                if (disposed) return Task.CompletedTask;
                snapshot = routes.Values.ToList();
            }
            var announce = new {
                id = Id,
                name = Name,
                version = Version,
                contractVersion = ContractVersion,
                routes = snapshot.Select(r => new {
                    method = r.Method,
                    path = r.Path,
                    timeoutMs = r.Timeout.HasValue ? (long?)r.Timeout.Value.TotalMilliseconds : null,
                }),
            };
            string json = JsonSerializer.Serialize(announce, TnsJson.Options);
            var headers = new Dictionary<string, object> { ["contractVersion"] = ContractVersion };
            return SafePublishAsync(new TnsBridgeMessage(AnnounceTopic, json, headers, senderId, null));
        }

        private async Task HandleRequestAsync(IMessage request) {
            TnsResponse response;
            try {
                string method = (Header(request, "method") ?? "GET").ToUpperInvariant();
                string path = NormalizePath(Header(request, "path"));
                Route route;
                bool pathKnown;
                lock (gate) {
                    routes.TryGetValue(method + " " + path, out route);
                    pathKnown = route == null && routes.Values.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
                }
                if (route != null) {
                    var tnsRequest = new TnsRequest(method, path, Header(request, "query"), Header(request, "contentType"), request.Content);
                    response = await route.Handler(tnsRequest).ConfigureAwait(false) ?? TnsResponse.NoContent();
                } else if (pathKnown) {
                    response = TnsResponse.Error(405, $"Method {method} is not allowed for '{path}'.");
                } else {
                    response = TnsResponse.Error(404, $"Route '{path}' not found.");
                }
            } catch (Exception ex) {
                response = TnsResponse.Error(500, ex.Message);
            }

            var headers = new Dictionary<string, object> {
                ["status"] = response.Status,
                ["contentType"] = response.ContentType ?? string.Empty,
            };
            await SafePublishAsync(new TnsBridgeMessage(ResponseTopic, response.Content, headers, senderId, request.MessageId)).ConfigureAwait(false);
        }

        private async Task SafePublishAsync(IMessage message) {
            try {
                await broker.Publish(message).ConfigureAwait(false);
            } catch {
                // A failing broker publish must never crash the host plugin; Touch'N'Stars answers
                // the app with a timeout instead.
            }
        }

        private static Func<TnsRequest, Task<TnsResponse>> Wrap(Func<TnsRequest, TnsResponse> handler) {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return request => Task.FromResult(handler(request));
        }

        private static string Header(IMessage message, string key) {
            if (message?.CustomHeaders == null) return null;
            return message.CustomHeaders.TryGetValue(key, out object value) ? value?.ToString() : null;
        }

        internal static string NormalizePath(string path) => (path ?? string.Empty).Trim().Trim('/');

        private sealed class Route {
            public Route(string method, string path, Func<TnsRequest, Task<TnsResponse>> handler, TimeSpan? timeout) {
                Method = method;
                Path = path;
                Handler = handler;
                Timeout = timeout;
            }

            public string Method { get; }
            public string Path { get; }
            public Func<TnsRequest, Task<TnsResponse>> Handler { get; }
            public TimeSpan? Timeout { get; }
        }
    }

    /// <summary>An HTTP request forwarded by Touch'N'Stars.</summary>
    internal sealed class TnsRequest {
        private readonly object body;

        public TnsRequest(string method, string path, string rawQuery, string contentType, object body) {
            Method = method;
            Path = path;
            RawQuery = rawQuery ?? string.Empty;
            ContentType = contentType ?? string.Empty;
            Query = ParseQuery(RawQuery);
            this.body = body;
        }

        public string Method { get; }

        /// <summary>The route path without leading or trailing slash, e.g. "status".</summary>
        public string Path { get; }

        public string RawQuery { get; }

        /// <summary>Decoded query parameters; for repeated keys the first value wins.</summary>
        public IReadOnlyDictionary<string, string> Query { get; }

        public string ContentType { get; }

        /// <summary>The body as text (null when the request had none).</summary>
        public string BodyText => body is byte[] bytes ? Encoding.UTF8.GetString(bytes) : body as string;

        /// <summary>The body as bytes (null when the request had none).</summary>
        public byte[] BodyBytes => body is string text ? Encoding.UTF8.GetBytes(text) : body as byte[];

        /// <summary>Deserializes a JSON body (camelCase or PascalCase property names both match).</summary>
        public T ReadJson<T>() {
            string text = BodyText;
            return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, TnsJson.Options);
        }

        private static IReadOnlyDictionary<string, string> ParseQuery(string rawQuery) {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in rawQuery.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)) {
                int separator = pair.IndexOf('=');
                string key = Uri.UnescapeDataString((separator < 0 ? pair : pair.Substring(0, separator)).Replace('+', ' '));
                string value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair.Substring(separator + 1).Replace('+', ' '));
                if (!result.ContainsKey(key)) result[key] = value;
            }
            return result;
        }
    }

    /// <summary>The answer to a <see cref="TnsRequest"/>.</summary>
    internal sealed class TnsResponse {
        private TnsResponse(int status, string contentType, object content) {
            Status = status;
            ContentType = contentType;
            Content = content;
        }

        public int Status { get; }
        public string ContentType { get; }

        /// <summary>A string, a byte[] or null. Passed in-process: binary data needs no encoding.</summary>
        public object Content { get; }

        /// <summary>Serializes <paramref name="value"/> as camelCase JSON.</summary>
        public static TnsResponse Json(object value, int status = 200) =>
            new TnsResponse(status, "application/json; charset=utf-8", JsonSerializer.Serialize(value, TnsJson.Options));

        public static TnsResponse Text(string text, int status = 200, string contentType = "text/plain; charset=utf-8") =>
            new TnsResponse(status, contentType, text ?? string.Empty);

        public static TnsResponse Bytes(byte[] data, string contentType, int status = 200) =>
            new TnsResponse(status, string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType, data ?? Array.Empty<byte>());

        public static TnsResponse NoContent() => new TnsResponse(204, null, null);

        /// <summary>A JSON error body <c>{ "error": message }</c>, the same shape Touch'N'Stars uses.</summary>
        public static TnsResponse Error(int status, string message) => Json(new { error = message }, status);
    }

    internal static class TnsJson {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    internal sealed class TnsBridgeMessage : IMessage {
        public TnsBridgeMessage(string topic, object content, IDictionary<string, object> headers, Guid senderId, Guid? correlationId) {
            Topic = topic;
            Content = content;
            CustomHeaders = headers ?? new Dictionary<string, object>();
            SenderId = senderId;
            CorrelationId = correlationId;
        }

        public Guid SenderId { get; }
        public string Sender => "TnsBridge";
        public DateTimeOffset SentAt { get; } = DateTimeOffset.UtcNow;
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset? Expiration => null;
        public Guid? CorrelationId { get; }
        public int Version => TnsBridge.ContractVersion;
        public IDictionary<string, object> CustomHeaders { get; }
        public string Topic { get; }
        public object Content { get; }
    }
}
