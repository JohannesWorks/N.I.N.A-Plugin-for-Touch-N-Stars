using NINA.Core.Utility;
using NINA.Plugin.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TouchNStars.Server.Bridge;

/// <summary>
/// The single path for third-party N.I.N.A. plugins to talk to the app: HTTP requests on
/// <c>/api/ext/{pluginId}/…</c> become message-broker requests, and plugin events are forwarded to
/// <c>/ws/ext</c>. The plugin side of the contract is docs/plugin-bridge/TnsBridge.cs.
/// <para>
/// Only <see cref="IMessageBroker.Publish(IMessage)"/> may be used: the cancelable overload exists
/// in the PINS fork only and would throw MissingMethodException on upstream N.I.N.A. Because the
/// upstream broker awaits every subscriber, publishes run detached and the wait is bounded by the
/// route timeout instead.
/// </para>
/// </summary>
public sealed class PluginBridgeService : ISubscriber, IDisposable {
    public const int ContractVersion = 1;
    public const string DiscoverTopic = "TouchNStars.Discover";
    public const string AnnounceTopic = "TouchNStars.Announce";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(10);

    private const string TopicPrefix = "TouchNStars.";
    private const string ResponseSuffix = ".Response";
    private const string EventSuffix = ".Event";
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageBroker broker;
    private readonly Guid senderId;
    private readonly TimeSpan defaultTimeout;
    private readonly ConcurrentDictionary<string, BridgePluginInfo> plugins = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<BridgeResponse>> pending = new();
    private readonly HashSet<string> subscribedTopics = new(StringComparer.Ordinal);
    private readonly object topicLock = new();
    private bool disposed;

    public static PluginBridgeService Instance { get; private set; }

    public event Action<BridgeEvent> EventReceived;

    public PluginBridgeService(IMessageBroker broker, Guid senderId, TimeSpan? defaultTimeout = null) {
        this.broker = broker ?? throw new ArgumentNullException(nameof(broker));
        this.senderId = senderId;
        this.defaultTimeout = defaultTimeout ?? DefaultTimeout;
    }

    /// <summary>Creates and starts the process-wide instance used by the web server.</summary>
    public static void Initialize(IMessageBroker broker, Guid senderId) {
        Instance?.Dispose();
        Instance = new PluginBridgeService(broker, senderId);
        Instance.Start();
    }

    public static void Shutdown() {
        Instance?.Dispose();
        Instance = null;
    }

    public void Start() {
        SubscribeOnce(AnnounceTopic);
        // Plugins constructed before us missed nothing: they answer this with an Announce.
        PublishDetached(new BridgeMessage(DiscoverTopic, null,
            new Dictionary<string, object> { ["contractVersion"] = ContractVersion }, senderId));
    }

    public IReadOnlyList<BridgePluginInfo> ListPlugins() =>
        plugins.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

    public async Task<BridgeResponse> SendAsync(string pluginId, BridgeRequest request, CancellationToken token = default) {
        if (pluginId == null || !plugins.TryGetValue(pluginId, out BridgePluginInfo info)) {
            return BridgeResponse.Error(404, $"No bridge plugin '{pluginId}' is installed.");
        }
        if (!info.Compatible) {
            return BridgeResponse.Error(409,
                $"Plugin '{pluginId}' uses bridge contract version {info.ContractVersion}; this Touch'N'Stars supports version {ContractVersion}. Update Touch'N'Stars or the plugin.");
        }

        string method = (request.Method ?? "GET").ToUpperInvariant();
        string path = (request.Path ?? string.Empty).Trim('/');
        TimeSpan timeout = info.TimeoutFor(method, path, defaultTimeout, MaxTimeout);

        var headers = new Dictionary<string, object> {
            ["method"] = method,
            ["path"] = path,
            ["query"] = request.Query ?? string.Empty,
            ["contentType"] = request.ContentType ?? string.Empty,
            ["contractVersion"] = ContractVersion,
        };
        var message = new BridgeMessage(TopicPrefix + pluginId + ".Request", request.Body, headers, senderId);
        var completion = new TaskCompletionSource<BridgeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[message.MessageId] = completion;
        try {
            PublishDetached(message, ex => completion.TrySetResult(
                BridgeResponse.Error(500, $"Publishing to '{pluginId}' failed: {ex.Message}")));

            using var delayCancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task finished = await Task.WhenAny(completion.Task, Task.Delay(timeout, delayCancel.Token)).ConfigureAwait(false);
            if (finished == completion.Task) {
                delayCancel.Cancel();
                return await completion.Task.ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            // Deliberately no retry: a plugin command may move hardware.
            return BridgeResponse.Error(504, $"Plugin '{pluginId}' did not answer within {timeout.TotalSeconds:0} s.");
        } finally {
            pending.TryRemove(message.MessageId, out _);
        }
    }

    public Task OnMessageReceived(IMessage message) {
        try {
            string topic = message?.Topic;
            if (topic == null) return Task.CompletedTask;
            if (topic == AnnounceTopic) {
                HandleAnnounce(message);
            } else if (topic.EndsWith(ResponseSuffix, StringComparison.Ordinal)) {
                if (message.CorrelationId is Guid id && pending.TryGetValue(id, out var completion)) {
                    completion.TrySetResult(BridgeResponse.FromMessage(message));
                }
            } else if (topic.EndsWith(EventSuffix, StringComparison.Ordinal)) {
                string pluginId = topic.Substring(TopicPrefix.Length, topic.Length - TopicPrefix.Length - EventSuffix.Length);
                EventReceived?.Invoke(new BridgeEvent(pluginId, Header(message, "event") ?? string.Empty, message.Content));
            }
        } catch (Exception ex) {
            Logger.Error($"Plugin bridge: failed to handle message on '{message?.Topic}': {ex}");
        }
        return Task.CompletedTask;
    }

    public void Dispose() {
        string[] topics;
        lock (topicLock) {
            if (disposed) return;
            disposed = true;
            topics = subscribedTopics.ToArray();
            subscribedTopics.Clear();
        }
        foreach (string topic in topics) {
            try { broker.Unsubscribe(topic, this); } catch { }
        }
        foreach (var completion in pending.Values) {
            completion.TrySetResult(BridgeResponse.Error(503, "Touch'N'Stars is shutting down."));
        }
        pending.Clear();
    }

    internal static string Header(IMessage message, string key) {
        if (message?.CustomHeaders == null) return null;
        return message.CustomHeaders.TryGetValue(key, out object value) ? value?.ToString() : null;
    }

    private void HandleAnnounce(IMessage message) {
        string json = message.Content as string;
        if (string.IsNullOrWhiteSpace(json)) {
            Logger.Warning("Plugin bridge: ignoring announce without content");
            return;
        }
        BridgePluginInfo info = JsonSerializer.Deserialize<BridgePluginInfo>(json, JsonOptions);
        if (info?.Id == null || !IdPattern.IsMatch(info.Id)) {
            Logger.Warning($"Plugin bridge: ignoring announce with invalid id '{info?.Id}'");
            return;
        }

        bool isNew = !plugins.ContainsKey(info.Id);
        plugins[info.Id] = info;
        if (info.Compatible) {
            SubscribeOnce(TopicPrefix + info.Id + ResponseSuffix);
            SubscribeOnce(TopicPrefix + info.Id + EventSuffix);
        }
        if (isNew) {
            Logger.Info($"Plugin bridge: '{info.Id}' {info.Version} registered (contract v{info.ContractVersion}{(info.Compatible ? "" : ", incompatible")})");
        }
    }

    private void SubscribeOnce(string topic) {
        lock (topicLock) {
            // The broker does not deduplicate: a second Subscribe would deliver every message twice.
            if (disposed || !subscribedTopics.Add(topic)) return;
        }
        broker.Subscribe(topic, this);
    }

    private void PublishDetached(IMessage message, Action<Exception> onError = null) {
        _ = Task.Run(async () => {
            try {
                await broker.Publish(message).ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Warning($"Plugin bridge: publish on '{message.Topic}' failed: {ex.Message}");
                onError?.Invoke(ex);
            }
        });
    }
}
