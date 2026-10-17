using NINA.Plugin.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchNStars.Server.Bridge;

/// <summary>A request the app sends to a bridge plugin through <c>/api/ext/{pluginId}/{path}</c>.</summary>
public sealed record BridgeRequest(string Method, string Path, string Query, string ContentType, object Body);

/// <summary>A bridge plugin's answer, or an error produced by Touch'N'Stars itself.</summary>
public sealed class BridgeResponse {
    public BridgeResponse(int status, string contentType, object content) {
        Status = status;
        ContentType = contentType;
        Content = content;
    }

    public int Status { get; }
    public string ContentType { get; }

    /// <summary>A string, a byte[], a Stream or null.</summary>
    public object Content { get; }

    public static BridgeResponse Error(int status, string message) =>
        new(status, "application/json; charset=utf-8",
            JsonSerializer.Serialize(new { error = message, source = "touchnstars" }));

    internal static BridgeResponse FromMessage(IMessage message) {
        int status = 200;
        if (message.CustomHeaders != null && message.CustomHeaders.TryGetValue("status", out object raw) && raw != null) {
            try { status = Convert.ToInt32(raw); } catch { status = 200; }
        }
        string contentType = PluginBridgeService.Header(message, "contentType");
        if (string.IsNullOrEmpty(contentType)) {
            contentType = message.Content switch {
                null => null,
                string => "application/json; charset=utf-8",
                _ => "application/octet-stream",
            };
        }
        return new BridgeResponse(status, contentType, message.Content);
    }
}

/// <summary>An event a bridge plugin published on <c>TouchNStars.{id}.Event</c>.</summary>
public sealed record BridgeEvent(string PluginId, string Name, object Data);

/// <summary>What a bridge plugin announced about itself.</summary>
public sealed class BridgePluginInfo {
    public string Id { get; init; }
    public string Name { get; init; }
    public string Version { get; init; }
    public int ContractVersion { get; init; }
    public bool Compatible => ContractVersion == PluginBridgeService.ContractVersion;
    public IReadOnlyList<BridgeRouteInfo> Routes { get; init; } = Array.Empty<BridgeRouteInfo>();

    internal TimeSpan TimeoutFor(string method, string path, TimeSpan defaultTimeout, TimeSpan maxTimeout) {
        BridgeRouteInfo route = (Routes ?? Array.Empty<BridgeRouteInfo>()).FirstOrDefault(r =>
            string.Equals(r.Method, method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (route?.TimeoutMs is not long ms) {
            return defaultTimeout;
        }
        // Plugins may only raise the wait, never shorten it below the default.
        TimeSpan requested = TimeSpan.FromMilliseconds(ms);
        if (requested < defaultTimeout) return defaultTimeout;
        return requested > maxTimeout ? maxTimeout : requested;
    }
}

public sealed class BridgeRouteInfo {
    public string Method { get; init; }
    public string Path { get; init; }
    public long? TimeoutMs { get; init; }
}

/// <summary>
/// IMessage with values fixed at construction. MessageId in particular must stay stable: the
/// plugin echoes it back as CorrelationId.
/// </summary>
internal sealed class BridgeMessage : IMessage {
    public BridgeMessage(string topic, object content, IDictionary<string, object> headers, Guid senderId) {
        Topic = topic;
        Content = content;
        CustomHeaders = headers ?? new Dictionary<string, object>();
        SenderId = senderId;
    }

    public Guid SenderId { get; }
    public string Sender => "TouchNStars";
    public DateTimeOffset SentAt { get; } = DateTimeOffset.UtcNow;
    public Guid MessageId { get; } = Guid.NewGuid();
    public DateTimeOffset? Expiration => null;
    public Guid? CorrelationId => null;
    public int Version => PluginBridgeService.ContractVersion;
    public IDictionary<string, object> CustomHeaders { get; }
    public string Topic { get; }
    public object Content { get; }
}
