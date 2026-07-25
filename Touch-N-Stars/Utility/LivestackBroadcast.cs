using System;
using System.Collections.Generic;
using NINA.Plugin.Interfaces;

namespace TouchNStars.Utility;

/// <summary>
/// A stack update announcement on the Livestack message broker topic.
///
/// Livestack publishes this only after stacking a frame, so a color combination that was created
/// or changed through the API would stay invisible to every consumer - most importantly to the
/// Advanced API, whose /livestack/image/available list is fed exclusively by this topic - until
/// the next frame arrives. Publishing it ourselves after re-rendering the combined image keeps
/// that list in sync.
/// </summary>
public class LivestackStackUpdateMessage(object content) : IMessage
{
    public const string StackUpdateTopic = "Livestack_LivestackDockable_StackUpdateBroadcast";

    public Guid SenderId => Guid.Parse(TouchNStars.PluginId);

    public string Sender => nameof(TouchNStars);

    public DateTimeOffset SentAt => DateTime.UtcNow;

    public Guid MessageId => Guid.NewGuid();

    public DateTimeOffset? Expiration => null;

    public Guid? CorrelationId => Guid.NewGuid();

    public int Version => 1;

    public IDictionary<string, object> CustomHeaders => new Dictionary<string, object>();

    public string Topic => StackUpdateTopic;

    public object Content => content;
}

/// <summary>
/// Mirrors Livestack's own broadcast payload. Consumers read it by property name via reflection,
/// so the shape - not the type - is what matters. Image is typed object so that this file stays
/// free of WPF types; at runtime it carries the BitmapSource of the combined image.
/// </summary>
public class LivestackStackUpdateContent
{
    public bool IsMonochrome { get; set; }
    public int? StackCount { get; set; }
    public int? RedStackCount { get; set; }
    public int? GreenStackCount { get; set; }
    public int? BlueStackCount { get; set; }
    public string Filter { get; set; }
    public string Target { get; set; }
    public object Image { get; set; }
}
