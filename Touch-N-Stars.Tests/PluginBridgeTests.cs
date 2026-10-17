using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NINA.Plugin.Messaging;
using TouchNStars.Server.Bridge;
using TouchNStarsBridge;
using Xunit;

namespace TouchNStars.Tests;

/// <summary>
/// End-to-end over a real N.I.N.A. MessageBroker: the Touch'N'Stars side (PluginBridgeService)
/// against the exact TnsBridge.cs that plugin authors copy from docs/plugin-bridge/.
/// </summary>
public class PluginBridgeTests : IDisposable
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(500);

    private readonly MessageBroker broker = new();
    private readonly List<IDisposable> disposables = new();

    public void Dispose()
    {
        foreach (var d in disposables) d.Dispose();
    }

    private PluginBridgeService CreateService(TimeSpan? timeout = null)
    {
        var service = new PluginBridgeService(broker, Guid.NewGuid(), timeout ?? ShortTimeout);
        disposables.Add(service);
        return service;
    }

    private TnsBridge CreatePlugin(string id = "demo")
    {
        var bridge = new TnsBridge(broker, id, "Demo", "1.2.3");
        disposables.Add(bridge);
        return bridge;
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition not met in time");
            await Task.Delay(10);
        }
    }

    private static Task WaitForPlugin(PluginBridgeService service, string id = "demo") =>
        WaitUntil(() => service.ListPlugins().Any(p => p.Id == id));

    private static BridgeRequest Get(string path, string query = "") => new("GET", path, query, "", null);

    private async Task<PluginBridgeService> StartPair(Action<TnsBridge> routes, TimeSpan? timeout = null)
    {
        var service = CreateService(timeout);
        service.Start();
        var plugin = CreatePlugin();
        routes(plugin);
        plugin.Start();
        await WaitForPlugin(service);
        return service;
    }

    [Fact]
    public async Task JsonRequest_RoundTripsWithQuery()
    {
        var service = await StartPair(p => p.Get("status", r => TnsResponse.Json(new { state = "idle", x = r.Query["x"] })));

        BridgeResponse response = await service.SendAsync("demo", Get("/status/", "x=1%202"));

        Assert.Equal(200, response.Status);
        Assert.StartsWith("application/json", response.ContentType);
        using var doc = JsonDocument.Parse((string)response.Content);
        Assert.Equal("idle", doc.RootElement.GetProperty("state").GetString());
        Assert.Equal("1 2", doc.RootElement.GetProperty("x").GetString());
    }

    [Fact]
    public async Task PostBody_IsDeliveredToHandler()
    {
        var service = await StartPair(p => p.Post("echo", r => TnsResponse.Json(r.ReadJson<Dictionary<string, int>>())));

        BridgeResponse response = await service.SendAsync("demo",
            new BridgeRequest("POST", "echo", "", "application/json", "{\"steps\":42}"));

        Assert.Equal(200, response.Status);
        Assert.Equal("{\"steps\":42}", response.Content);
    }

    [Fact]
    public async Task BinaryResponse_IsByteIdentical()
    {
        byte[] image = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var service = await StartPair(p => p.Get("thumb", _ => TnsResponse.Bytes(image, "image/jpeg")));

        BridgeResponse response = await service.SendAsync("demo", Get("thumb"));

        Assert.Equal(200, response.Status);
        Assert.Equal("image/jpeg", response.ContentType);
        Assert.Equal(image, Assert.IsType<byte[]>(response.Content));
    }

    [Fact]
    public async Task Discovery_PluginStartedBeforeTouchNStars()
    {
        var plugin = CreatePlugin();
        plugin.Get("status", _ => TnsResponse.Json(new { ok = true }));
        plugin.Start();
        await Task.Delay(50);

        var service = CreateService();
        service.Start();
        await WaitForPlugin(service);

        BridgePluginInfo info = service.ListPlugins().Single();
        Assert.Equal("Demo", info.Name);
        Assert.Equal("1.2.3", info.Version);
        Assert.Equal(PluginBridgeService.ContractVersion, info.ContractVersion);
        Assert.True(info.Compatible);
    }

    [Fact]
    public async Task Discovery_PluginStartedAfterTouchNStars()
    {
        var service = await StartPair(p => p.Get("status", _ => TnsResponse.Json(new { ok = true })));

        Assert.Single(service.ListPlugins());
    }

    [Fact]
    public async Task UnknownPlugin_Returns404Immediately()
    {
        var service = CreateService(TimeSpan.FromSeconds(10));
        service.Start();

        var sw = Stopwatch.StartNew();
        BridgeResponse response = await service.SendAsync("missing", Get("status"));

        Assert.Equal(404, response.Status);
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public async Task UnknownRouteAndWrongMethod_AreAnsweredByThePlugin()
    {
        var service = await StartPair(p => p.Get("status", _ => TnsResponse.Json(new { ok = true })));

        Assert.Equal(404, (await service.SendAsync("demo", Get("nope"))).Status);
        Assert.Equal(405, (await service.SendAsync("demo", new BridgeRequest("POST", "status", "", "", null))).Status);
    }

    [Fact]
    public async Task SilentPlugin_Returns504AfterTimeout()
    {
        var service = await StartPair(p => p.Get("slow", async _ =>
        {
            await Task.Delay(5000);
            return TnsResponse.Json(new { late = true });
        }));

        var sw = Stopwatch.StartNew();
        BridgeResponse response = await service.SendAsync("demo", Get("slow"));

        Assert.Equal(504, response.Status);
        Assert.InRange(sw.ElapsedMilliseconds, 400, 2000);
    }

    [Fact]
    public async Task RouteTimeout_CanRaiseTheDefault()
    {
        var service = await StartPair(p => p.Post("run", async _ =>
        {
            await Task.Delay(1000);
            return TnsResponse.Json(new { done = true });
        }, TimeSpan.FromSeconds(5)));

        BridgeResponse response = await service.SendAsync("demo", new BridgeRequest("POST", "run", "", "", null));

        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task RouteTimeout_CannotGoBelowTheDefault()
    {
        var service = await StartPair(p => p.Get("slowish", async _ =>
        {
            await Task.Delay(300);
            return TnsResponse.Json(new { ok = true });
        }, TimeSpan.FromMilliseconds(50)));

        Assert.Equal(200, (await service.SendAsync("demo", Get("slowish"))).Status);
    }

    [Fact]
    public async Task HandlerException_Returns500WithMessage()
    {
        var service = await StartPair(p => p.Get("boom", (Func<TnsRequest, TnsResponse>)(_ => throw new InvalidOperationException("focuser not connected"))));

        BridgeResponse response = await service.SendAsync("demo", Get("boom"));

        Assert.Equal(500, response.Status);
        Assert.Contains("focuser not connected", (string)response.Content);
    }

    [Fact]
    public async Task SlowHandler_DoesNotBlockOtherRequests()
    {
        var service = await StartPair(p =>
        {
            p.Get("slow", async _ =>
            {
                await Task.Delay(3000);
                return TnsResponse.Json(new { ok = true });
            }, TimeSpan.FromSeconds(10));
            p.Get("fast", _ => TnsResponse.Json(new { ok = true }));
        });

        Task<BridgeResponse> slow = service.SendAsync("demo", Get("slow"));
        var sw = Stopwatch.StartNew();
        BridgeResponse fast = await service.SendAsync("demo", Get("fast"));

        Assert.Equal(200, fast.Status);
        Assert.True(sw.ElapsedMilliseconds < 1000);
        Assert.False(slow.IsCompleted);
    }

    [Fact]
    public async Task IncompatibleContract_IsListedAndRejected()
    {
        var service = CreateService();
        service.Start();
        await broker.Publish(new TnsBridgeMessage(PluginBridgeService.AnnounceTopic,
            "{\"id\":\"future\",\"name\":\"Future\",\"version\":\"9\",\"contractVersion\":2}",
            null, Guid.Empty, null));
        await WaitForPlugin(service, "future");

        Assert.False(service.ListPlugins().Single().Compatible);
        BridgeResponse response = await service.SendAsync("future", Get("status"));
        Assert.Equal(409, response.Status);
        Assert.Contains("contract version 2", (string)response.Content);
    }

    [Fact]
    public async Task Events_AreForwardedOnceEvenAfterReannounce()
    {
        // Started first, the plugin announces twice (own start + answer to Discover).
        var plugin = CreatePlugin();
        plugin.Start();
        var service = CreateService();
        var received = new ConcurrentQueue<BridgeEvent>();
        service.EventReceived += e => received.Enqueue(e);
        service.Start();
        await WaitForPlugin(service);
        await Task.Delay(100);

        await plugin.PublishEventAsync("progress", new { percent = 40 });
        await WaitUntil(() => !received.IsEmpty);
        await Task.Delay(100);

        BridgeEvent ev = Assert.Single(received);
        Assert.Equal("demo", ev.PluginId);
        Assert.Equal("progress", ev.Name);
        Assert.Equal("{\"percent\":40}", ev.Data);
    }

    [Fact]
    public void InvalidPluginId_IsRejectedByTheBridge()
    {
        Assert.Throws<ArgumentException>(() => new TnsBridge(broker, "My Plugin", "x", "1"));
    }
}
