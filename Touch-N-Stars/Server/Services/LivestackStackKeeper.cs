using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin.Interfaces;
using TouchNStars.Utility;

namespace TouchNStars.Server.Services;

/// <summary>
/// Keeps the Livestack RGB combination up to date for remote viewing.
///
/// Livestack broadcasts every mono stack update, but re-renders and broadcasts the combined RGB
/// image only when its tab happens to be selected in the NINA dockable or when "Save stacked
/// lights" is on. Nobody is looking at that dockable in a remote session, so without this the
/// combined image the app shows would freeze at the state it had when it was created.
///
/// This subscriber listens for mono updates and re-renders the matching combination itself.
/// </summary>
internal class LivestackStackKeeper : ISubscriber, IDisposable
{
    /// <summary>
    /// Collapses the burst of channel updates a single frame produces (three for OSC) into one
    /// render, and gives Livestack's own refresh a chance to run first.
    /// </summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);

    /// <summary>Targets with a render queued or running, so frames never pile up renders.</summary>
    private readonly ConcurrentDictionary<string, byte> pendingTargets = new();

    private bool disposed;

    public LivestackStackKeeper()
    {
        TouchNStars.Mediators.MessageBroker.Subscribe(LivestackStackUpdateMessage.StackUpdateTopic, this);
    }

    public Task OnMessageReceived(IMessage message)
    {
        try
        {
            if (disposed || message?.Content == null)
            {
                return Task.CompletedTask;
            }

            // Our own announcements are color updates; ignoring them also rules out a feedback loop.
            if (message.SenderId == Guid.Parse(TouchNStars.PluginId))
            {
                return Task.CompletedTask;
            }

            object content = message.Content;
            if (!(content.GetType().GetProperty("IsMonochrome")?.GetValue(content) is bool isMonochrome) || !isMonochrome)
            {
                return Task.CompletedTask;
            }

            if (!(content.GetType().GetProperty("Target")?.GetValue(content) is string target)
                || string.IsNullOrEmpty(target))
            {
                return Task.CompletedTask;
            }

            if (!pendingTargets.TryAdd(target, 0))
            {
                // Already queued - the pending render will pick up this frame too.
                return Task.CompletedTask;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Debounce).ConfigureAwait(false);
                    if (!disposed)
                    {
                        await LivestackBridge.RefreshAndAnnounceAsync(target).ConfigureAwait(false);
                    }
                }
                finally
                {
                    pendingTargets.TryRemove(target, out _);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to handle a Livestack stack update: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            TouchNStars.Mediators.MessageBroker.Unsubscribe(LivestackStackUpdateMessage.StackUpdateTopic, this);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to unsubscribe the Livestack stack keeper: {ex}");
        }
    }
}
