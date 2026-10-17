using EmbedIO.WebSockets;
using NINA.Core.Utility;
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TouchNStars.Server.Bridge;

/// <summary>
/// /ws/ext: one socket shared by all bridge plugins. Clients send
/// <c>{"type":"subscribe","pluginId":"x"}</c> / <c>{"type":"unsubscribe","pluginId":"x"}</c> and
/// receive <c>{"type":"event","pluginId","event","data"}</c> only for ids they subscribed to,
/// plus a heartbeat every 10 s so the app can detect half-open sockets.
/// </summary>
internal sealed class PluginBridgeSocketModule : WebSocketModule {
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string HeartbeatJson = "{\"type\":\"heartbeat\"}";

    // Context id -> subscribed plugin ids (value unused).
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> subscriptions = new();
    private PluginBridgeService service;
    private Timer heartbeat;

    public PluginBridgeSocketModule(string urlPath) : base(urlPath, true) {
    }

    protected override void OnStart(CancellationToken cancellationToken) {
        base.OnStart(cancellationToken);
        service = PluginBridgeService.Instance;
        if (service != null) {
            service.EventReceived += OnBridgeEvent;
        }
        heartbeat = new Timer(_ => {
            if (ActiveContexts.Count > 0) {
                _ = BroadcastAsync(HeartbeatJson);
            }
        }, null, HeartbeatInterval, HeartbeatInterval);
    }

    protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result) {
        try {
            using JsonDocument doc = JsonDocument.Parse(buffer);
            JsonElement root = doc.RootElement;
            string type = root.TryGetProperty("type", out JsonElement t) ? t.GetString() : null;
            string pluginId = root.TryGetProperty("pluginId", out JsonElement p) ? p.GetString() : null;
            if (string.IsNullOrEmpty(pluginId)) return Task.CompletedTask;

            var ids = subscriptions.GetOrAdd(context.Id, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            if (type == "subscribe") {
                ids[pluginId] = 0;
            } else if (type == "unsubscribe") {
                ids.TryRemove(pluginId, out _);
            }
        } catch (JsonException) {
            // Not a control message; ignore.
        }
        return Task.CompletedTask;
    }

    protected override Task OnClientDisconnectedAsync(IWebSocketContext context) {
        subscriptions.TryRemove(context.Id, out _);
        return Task.CompletedTask;
    }

    private void OnBridgeEvent(BridgeEvent bridgeEvent) {
        try {
            string json = JsonSerializer.Serialize(new {
                type = "event",
                pluginId = bridgeEvent.PluginId,
                @event = bridgeEvent.Name,
                data = ToJsonData(bridgeEvent.Data),
            }, JsonOptions);
            _ = BroadcastAsync(json, context =>
                subscriptions.TryGetValue(context.Id, out var ids) && ids.ContainsKey(bridgeEvent.PluginId));
        } catch (Exception ex) {
            Logger.Warning($"Plugin bridge: could not forward event from '{bridgeEvent.PluginId}': {ex.Message}");
        }
    }

    private static object ToJsonData(object data) {
        switch (data) {
            case null:
                return null;
            case string text:
                // TnsBridge sends JSON; embed it as JSON rather than as an escaped string.
                try {
                    using JsonDocument doc = JsonDocument.Parse(text);
                    return doc.RootElement.Clone();
                } catch (JsonException) {
                    return text;
                }
            case byte[] bytes:
                return Convert.ToBase64String(bytes);
            default:
                return data;
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            heartbeat?.Dispose();
            heartbeat = null;
            if (service != null) {
                service.EventReceived -= OnBridgeEvent;
                service = null;
            }
        }
        base.Dispose(disposing);
    }
}
