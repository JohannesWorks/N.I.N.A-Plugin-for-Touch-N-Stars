# Touch'N'Stars plugin bridge

Connect your N.I.N.A. plugin to Touch'N'Stars without running a web server.

Your plugin talks to Touch'N'Stars over the N.I.N.A. message broker (`IMessageBroker`), which every
plugin gets injected. Touch'N'Stars publishes your routes on its own web server, on the port the
app already uses:

```
App ──HTTP──▶ Touch'N'Stars  /api/ext/<your-id>/<path>
                    │  message broker (in-process)
                    ▼
              your plugin  ──▶ handler ──▶ response
```

You need **no server, no port, no CORS headers and no DLL dependency on Touch'N'Stars**. It works
the same on Windows N.I.N.A. and on PINS.

- Minimum: N.I.N.A. 3.1 (message broker) and a Touch'N'Stars plugin version with the bridge.
- Contract version: **1**.

## Quick start

1. Copy [`TnsBridge.cs`](TnsBridge.cs) into your plugin project **unchanged**. It is a source file
   on purpose: every plugin compiles its own internal copy, so two plugins can never conflict over
   an assembly version. It needs only `NINA.Plugin` and `System.Text.Json`.
2. Create the bridge in your plugin's constructor, register routes and call `Start()`:

```csharp
[Export(typeof(IPluginManifest))]
public class MyPlugin : PluginBase {
    private readonly TnsBridge bridge;

    [ImportingConstructor]
    public MyPlugin(IMessageBroker broker, IFocuserMediator focuser) {
        bridge = new TnsBridge(broker, "my-plugin", "My Plugin", Version.ToString(), Guid.Parse(Identifier));

        bridge.Get("status", _ => TnsResponse.Json(new { position = focuser.GetInfo().Position }));

        bridge.Post("run", async request => {
            var options = request.ReadJson<RunOptions>();
            await RunAsync(options);
            return TnsResponse.Json(new { started = true });
        }, timeout: TimeSpan.FromMinutes(2));

        bridge.Start();
    }

    public override Task Teardown() {
        bridge.Dispose();
        return base.Teardown();
    }
}
```

3. Check it: `GET http://<nina-host>:<tns-port>/api/ext` lists your plugin, and
   `GET http://<nina-host>:<tns-port>/api/ext/my-plugin/status` calls your handler.

A complete, buildable plugin is in [`example/`](example/).

## Plugin id

The id is part of the URL and of every broker topic. It must match `^[a-z0-9][a-z0-9-]{0,63}$`
(lowercase letters, digits, dashes) and must never change once released. Pick something
unique, usually your plugin's name.

## Routes

| Method | Example |
| --- | --- |
| `bridge.Get(path, handler)` | `GET /api/ext/my-plugin/status` |
| `bridge.Post(path, handler)` | `POST /api/ext/my-plugin/run` |
| `bridge.Put(path, handler)` | `PUT /api/ext/my-plugin/settings` |
| `bridge.Delete(path, handler)` | `DELETE /api/ext/my-plugin/cache` |

- Handlers may be synchronous (`Func<TnsRequest, TnsResponse>`) or asynchronous
  (`Func<TnsRequest, Task<TnsResponse>>`). They run on the thread pool, never on the broker thread.
- Paths match exactly, case-insensitive, without leading or trailing slash. Use query parameters
  for ids (`targets?id=42`) instead of path segments.
- Register all routes **before** `Start()`: they are announced to Touch'N'Stars in one go.
- An unknown path answers `404`, a known path with the wrong method `405`, and an exception in a
  handler `500` with the exception message. You do not need to catch everything yourself.

### Request

| Member | Meaning |
| --- | --- |
| `Method`, `Path` | e.g. `POST`, `run` |
| `Query` | decoded query parameters (`request.Query["id"]`) |
| `RawQuery` | the query string as sent |
| `ContentType` | the request's content type |
| `BodyText` / `BodyBytes` | the body as text or as bytes |
| `ReadJson<T>()` | deserializes a JSON body; camelCase and PascalCase both match |

### Response

| Factory | Result |
| --- | --- |
| `TnsResponse.Json(value, status = 200)` | `value` serialized as camelCase JSON |
| `TnsResponse.Text(text, status, contentType)` | plain text |
| `TnsResponse.Bytes(data, contentType, status)` | binary, e.g. `image/jpeg` - passed in-process, no Base64 |
| `TnsResponse.NoContent()` | `204` |
| `TnsResponse.Error(status, message)` | `{ "error": message }` with that status |

## Timeouts

Touch'N'Stars waits **10 seconds** for an answer and then responds `504` to the app. Routes that
take longer announce it when they are registered:

```csharp
bridge.Post("calibrate", CalibrateAsync, timeout: TimeSpan.FromMinutes(5));
```

The timeout can only be raised (minimum 10 s, maximum 10 min). Touch'N'Stars **never retries** a
request on timeout, because a command may move hardware - but your handler keeps running after a
`504`. For really long work, prefer starting it in a `POST` that returns at once and reporting
progress through `GET status` and events.

## Events

Push changes to the app without waiting for its next request:

```csharp
await bridge.PublishEventAsync("progress", new { percent = 40 });
```

The app receives `{ event: "progress", data: { percent: 40 } }` in every view subscribed to your
plugin id (over the `/ws/ext` WebSocket).

**Events are additive.** A phone that was locked or switched apps misses them. Everything an
event reports must also be readable by a request, typically `GET status`, which the app view
keeps polling; events only make it feel immediate.

## The app side

App-side views for your plugin are part of the Touch'N'Stars app (`src/plugins/<name>/` in the
Touch-N-Stars repository) and call your routes like this:

```js
import apiService from '@/services/apiService';
import { usePluginBridgeEvents } from '@/composables/usePluginBridgeEvents';

const plugin = apiService.ext('my-plugin');
const status = await plugin.get('status');
await plugin.post('run', { exposures: 10 }, { timeout: 125000 });

usePluginBridgeEvents('my-plugin', ({ event, data }) => { /* ... */ });
```

Discovery: `apiService.extListPlugins()` returns the installed bridge plugins, so the view can
show "plugin not installed" instead of an error.

## Status codes

| Status | Source | Meaning |
| --- | --- | --- |
| `2xx`, `4xx`, `5xx` | your handler | whatever you returned |
| `404` with `"source": "touchnstars"` | Touch'N'Stars | no bridge plugin with that id is installed |
| `409` | Touch'N'Stars | your plugin speaks a contract version Touch'N'Stars does not support |
| `500` | your plugin | unhandled exception in the handler |
| `503` | Touch'N'Stars | the bridge is not running (Touch'N'Stars shutting down) |
| `504` | Touch'N'Stars | your handler did not answer within its timeout |

## Contract reference (version 1)

You do not need this if you use `TnsBridge.cs`; it documents what goes over the broker.

| Topic | Direction | Content | Headers (`CustomHeaders`) |
| --- | --- | --- | --- |
| `TouchNStars.Discover` | TNS → all | – | `contractVersion` |
| `TouchNStars.Announce` | plugin → TNS | JSON `{ id, name, version, contractVersion, routes: [{ method, path, timeoutMs }] }` | `contractVersion` |
| `TouchNStars.<id>.Request` | TNS → plugin | body: `string`, `byte[]` or null | `method`, `path`, `query`, `contentType`, `contractVersion` |
| `TouchNStars.<id>.Response` | plugin → TNS | `string`, `byte[]` or null; `CorrelationId` = request `MessageId` | `status`, `contentType` |
| `TouchNStars.<id>.Event` | plugin → TNS | JSON string or null | `event` |

- Plugins load in any order. A plugin announces itself on `Start()` and again whenever it receives
  `Discover`, which Touch'N'Stars sends at its own start.
- Use only `IMessageBroker.Publish(IMessage)`. The overload with a `CancellationToken` exists in
  the PINS fork only and is missing on Windows N.I.N.A.
- The broker awaits every subscriber, so `OnMessageReceived` must return immediately and do the
  work on a separate task. `TnsBridge.cs` does this for you.
- A future contract version 2 will be announced here. Touch'N'Stars lists plugins with an
  unsupported version as `compatible: false` and rejects their requests with `409`.
