using NINA.Plugin;
using NINA.Plugin.Interfaces;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using TouchNStarsBridge;

namespace ExampleBridgePlugin {

    /// <summary>
    /// Minimal N.I.N.A. plugin on the Touch'N'Stars plugin bridge. The app reaches it as
    /// /api/ext/bridge-example/... on the Touch'N'Stars port - this plugin opens no server.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class ExampleBridgePlugin : PluginBase {
        // A tiny 1x1 PNG, standing in for an image the plugin produces.
        private static readonly byte[] SamplePng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        private readonly TnsBridge bridge;
        private int counter;

        [ImportingConstructor]
        public ExampleBridgePlugin(IMessageBroker broker) {
            bridge = new TnsBridge(broker, "bridge-example", "Bridge Example", Version.ToString(), Guid.Parse(Identifier));

            // GET /api/ext/bridge-example/status
            bridge.Get("status", _ => TnsResponse.Json(new { counter, time = DateTimeOffset.UtcNow }));

            // POST /api/ext/bridge-example/counter  body: { "step": 2 }
            bridge.Post("counter", async request => {
                var body = request.ReadJson<CounterRequest>();
                int value = Interlocked.Add(ref counter, body?.Step ?? 1);
                // Events are additive: the same value is also readable via GET status.
                await bridge.PublishEventAsync("counter-changed", new { counter = value });
                return TnsResponse.Json(new { counter = value });
            });

            // GET /api/ext/bridge-example/image  -> binary response
            bridge.Get("image", _ => TnsResponse.Bytes(SamplePng, "image/png"));

            // POST /api/ext/bridge-example/slow?ms=15000  -> long-running route with a raised timeout
            bridge.Post("slow", async request => {
                int ms = int.TryParse(request.Query.TryGetValue("ms", out var raw) ? raw : null, out var parsed) ? parsed : 1000;
                await Task.Delay(ms);
                return TnsResponse.Json(new { waitedMs = ms });
            }, timeout: TimeSpan.FromSeconds(30));

            bridge.Start();
        }

        public override Task Teardown() {
            bridge.Dispose();
            return base.Teardown();
        }

        private sealed class CounterRequest {
            public int? Step { get; set; }
        }
    }
}
