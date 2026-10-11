using EmbedIO.WebSockets;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.INDI;
using NINA.INDI.Devices;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using TouchNStars.Server.Models;

namespace TouchNStars.Server;

/// <summary>
/// WebSocket for manual (press-and-hold) mount slewing, served INDI-direct from the
/// Touch-N-Stars plugin so it can drive the mount by direction only. The slew rate is set
/// separately through POST /api/indi/mount/slew-rate (the capability model); this socket never
/// touches the rate, which keeps a chosen discrete step (e.g. "Guide") from being overwritten on
/// every keepalive message. pins' native OnStepX mount driver is moved the same way, at its
/// selected move rate.
///
/// Protocol: the client sends { "direction": "north|south|east|west|stop" } on press and repeats
/// it as a keepalive (~every 800 ms); it sends "stop" on release. A server-side dead-man watchdog
/// stops all motion if no message arrives within the timeout, so a dropped connection or dead tab
/// cannot leave the mount slewing.
/// </summary>
public class MountControlSocket : WebSocketModule
{
    private static readonly TimeSpan DeadManTimeout = TimeSpan.FromSeconds(1.8);

    private static readonly object _lock = new object();
    private static DateTime _lastMoveAt;
    private static string _lastDirection = string.Empty;

    public MountControlSocket(string urlPath) : base(urlPath, true)
    {
    }

    protected override async Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
    {
        ApiResponse response;
        try
        {
            var message = Encoding.GetString(buffer);
            var json = JsonSerializer.Deserialize<Dictionary<string, object>>(message);
            string direction = json != null && json.TryGetValue("direction", out var d) ? d?.ToString()?.ToLowerInvariant() : null;

            var move = MountMover();
            if (move == null)
            {
                response = Error("No mount is currently connected");
            }
            else
            {
                response = HandleDirection(move, direction);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"MountControlSocket error: {ex}");
            response = Error(ex.Message);
        }

        await context.WebSocket.SendAsync(Encoding.GetBytes(JsonSerializer.Serialize(response)), true);
    }

    /// <summary>
    /// Direction-only motion of the connected mount: pins' native OnStepX driver when NINA uses it,
    /// otherwise the INDI mount; null when neither is connected.
    /// </summary>
    private static Action<TelescopeAxes, int> MountMover()
    {
        if (TouchNStars.Mediators?.Telescope?.GetDevice() is OnStepXTelescope { Connected: true } onStepX)
        {
            return onStepX.MoveAxisDirection;
        }
        var mount = INDIClient.Instance.GetRegisteredDevice<INDITelescope>();
        return mount == null ? null : mount.MoveAxisDirection;
    }

    private ApiResponse HandleDirection(Action<TelescopeAxes, int> move, string direction)
    {
        // the profile's "primary/secondary reversed", which NINA applies in TelescopeVM.MoveAxis; this socket bypasses it
        var settings = TouchNStars.Mediators?.Profile?.ActiveProfile?.TelescopeSettings;
        int primary = settings?.PrimaryReversed == true ? -1 : 1;
        int secondary = settings?.SecondaryReversed == true ? -1 : 1;
        switch (direction)
        {
            case "north":
                move(TelescopeAxes.Secondary, secondary);
                break;
            case "south":
                move(TelescopeAxes.Secondary, -secondary);
                break;
            case "east":
                move(TelescopeAxes.Primary, primary);
                break;
            case "west":
                move(TelescopeAxes.Primary, -primary);
                break;
            case "stop":
                lock (_lock)
                {
                    // a pending dead-man stop has nothing left to do
                    _lastMoveAt = DateTime.UtcNow;
                    _lastDirection = string.Empty;
                }
                StopAll(move);
                return new ApiResponse { Success = true, Response = "Stopped Move", StatusCode = 200, Type = "MountControl" };
            default:
                return Error("Invalid direction");
        }

        DateTime scheduledAt;
        lock (_lock)
        {
            _lastMoveAt = DateTime.UtcNow;
            _lastDirection = direction;
            scheduledAt = _lastMoveAt;
        }
        ScheduleDeadManStop(move, scheduledAt);

        return new ApiResponse { Success = true, Response = "Moving", StatusCode = 200, Type = "MountControl" };
    }

    /// <summary>
    /// Stops all motion if no newer move message arrived after <paramref name="scheduledAt"/>.
    /// The timestamp acts as a token: each move records a fresh <see cref="_lastMoveAt"/>, so a
    /// later message invalidates this scheduled stop.
    /// </summary>
    private static void ScheduleDeadManStop(Action<TelescopeAxes, int> move, DateTime scheduledAt)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(DeadManTimeout + TimeSpan.FromMilliseconds(200));
            try
            {
                lock (_lock)
                {
                    if (_lastMoveAt != scheduledAt)
                    {
                        return; // a newer move superseded this one
                    }
                    if (DateTime.UtcNow - _lastMoveAt >= DeadManTimeout)
                    {
                        StopAll(move);
                        _lastDirection = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"MountControlSocket dead-man stop failed: {ex}");
            }
        });
    }

    private static void StopAll(Action<TelescopeAxes, int> move)
    {
        move(TelescopeAxes.Primary, 0);
        move(TelescopeAxes.Secondary, 0);
    }

    private static ApiResponse Error(string error)
        => new ApiResponse { Success = false, Error = error, StatusCode = 400, Type = "Error" };
}
