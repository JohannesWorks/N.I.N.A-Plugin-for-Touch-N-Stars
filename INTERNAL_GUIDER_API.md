# Internal Guider API

REST and WebSocket API for the pins internal guider: the guider that implements `IAdvancedGuider`
(`NINA.Equipment.Interfaces`) and that pins exports to plugins. The generic guider endpoints keep working for every guider; these endpoints add
the internal guider's live data (frames, stars, guide steps, statistics), its settings and controls, the Guiding
Coach and the flight recorder.

The Guiding Coach and the flight recorder are optional parts of the contract (`IGuidingCoach`,
`IGuideIncidentRecorder`); their routes answer 501 for a guider without them. The DTOs named below are the
contract's classes in `NINA.Equipment.Equipment.MyGuider.Advanced`, serialized as they are. The contract has
no version: pins, this plugin and the guider are built together.

- REST: `http://<host>:<port>/api/internal-guider/...`
- WebSocket: `ws://<host>:<port>/ws/internal-guider`

The frontend decides whether to show the internal guider page from `GET /status` (`isNative`) or from the
WebSocket `hello` message.

## Conventions

- JSON property names are camelCase. Timestamps are UTC ISO 8601. `NaN` and infinite numbers are written as `null`.
- Success: `{ "success": true, "response": ... }` with HTTP 200, or 202 for work that continues in the background.
- Failure: `{ "success": false, "error": "...", "code": "..." }` with a real HTTP status. A rejection by the
  guider can also carry a stable `messageCode` (for example `coach.busy`) and its `messageParameters` for
  localisation.
- Bodies are checked for their JSON shape and types only (400 when malformed). Value ranges are the guider's
  business; it rejects with 409 and a `messageCode`, or clamps.

| HTTP | `code` | Meaning |
|---|---|---|
| 202 | | Accepted; the work continues. Its outcome follows as an `action` WebSocket message. |
| 400 | `InvalidRequest` | Malformed body, query or incident id. |
| 400 | `InvalidValue` | The guider rejected a setting value. |
| 404 | `NoFrame`, `NotFound`, `NoImage` | No guide frame yet, unknown incident, or an incident frame without such an image. |
| 409 | `NotAvailable` | No internal guider is connected (or selected, for the routes that work without a connection). |
| 409 | `Rejected` | The guider refused the action in its current state. |
| 409 | `Cancelled` | The call was cancelled. |
| 500 | `Error` | Unexpected error. |
| 501 | `NotSupported` | The guider does not implement the Guiding Coach or the flight recorder. |

Short guider calls (`loop`, `stop`, `pause`, ...) and the coach calls wait up to 15 s for the guider. A call
that has not returned by then answers 202 with `pending: true`, keeps running, and publishes its outcome as an
`action` message.

## Status, data and settings

The guide camera is the one in the guide camera slot of pins' equipment: choose and configure it there. The guider's
`GuideSource` setting chooses between that camera and the built-in simulator.

| Method | Route | Parameters | Response |
|---|---|---|---|
| GET | `/status` | | `{ available, connected, deviceId, deviceName, isNative, reason, status }`, always 200 so it can be polled. `status` is the `AdvancedGuiderStatus` (with the live coaching `hints` and `coachRunning`, and `decDrift: { direction, driftArcsecPerMin, safetyValveOpen }` in Dec guide mode Drift, else `null`), `null` unless a internal guider is connected. `status.timing` is the guide loop's timing, `null` before the first cycle and while the loop does not run: `{ fps, cycles, cycleMs, exposureMs, cameraMs, processingMs, frameToPulseMs, pulseMs, otherMs }` as medians over the last `cycles` (up to 20) cycles, and the last cycle in `lastCycleMs`, `lastCameraMs`, `lastProcessingMs`, `lastFrameToPulseMs`, `lastPulseMs`, `lastOtherMs`; see below. |
| GET | `/steps` | `max` (400, 1..5000) | `AdvancedGuideStep[]`, oldest first. |
| GET | `/alerts` | `max` (100, 1..1000) | `AdvancedGuiderAlert[]`, oldest first. |
| GET | `/calibration` | | `AdvancedGuiderCalibration` or `null`. |
| GET | `/settings` | | `{ connected, settings: AdvancedGuiderSetting[] }`. Works before the first connect, so the guide camera can be set up first. |
| POST | `/settings` | `{ name, value }` | The updated `AdvancedGuiderSetting`. `value` may be a string, number or boolean; it is passed on as an invariant-culture string. |
| GET | `/image` | `maxWidth` (1024), `stretch` (0.2), `gamma` (1), `quality` (80), `frame` | Auto-stretched JPEG of the latest frame, or of frame `frame` while it is among the last three served. Headers `X-Frame-Number`, `X-Frame-Width`, `X-Frame-Height` (size of the original frame). |
| GET | `/frame-info` | `cropSize` (31, 0..128, 0 = none), `secondaries` (0, 0..8), `frame` | `{ frameNumber, timestamp, width, height, bitDepth, lockX, lockY, stars, primaryCrop, secondaryCrops, levels }`. `primaryCrop` is `{ x0, y0, width, height, pixels }`, the raw pixels around the primary star (`null` without one). `secondaryCrops` is `[{ star, crop }]` for the strongest secondaries that are stars, highest SNR first. |

### Timing

A cycle runs from the start of one capture to the start of the next; `fps` is 1000 / median `cycleMs`. `exposureMs`,
`cameraMs`, `processingMs`, `pulseMs` and `otherMs` add up to the cycle; `frameToPulseMs` overlaps them. `timing` is
`null` while the guide loop does not run.

| Field | Part of the cycle |
|---|---|
| `exposureMs` | The exposure asked for. |
| `cameraMs` | The capture beyond the exposure: starting it, reading out and transferring the frame. pins sees when the frame arrives, not when the exposure ended. |
| `processingMs` | Frame arrived to frame processed (stars, algorithms). |
| `frameToPulseMs` | Frame arrived to the first pulse handed to the guide output, over cycles with pulses (`null` without). The command still has to reach the mount, about 9 ms on a 9600 baud serial line. |
| `pulseMs` | Pulses handed over to the output reporting them done, over cycles with pulses. With pins' native OnStepX driver "done" means the controller no longer reports a pulse. |
| `otherMs` | The rest: between processing and the pulses, then events, logs and the mount check before the next capture. |

## Guiding actions

| Method | Route | Parameters | Response |
|---|---|---|---|
| POST | `/loop` | | Start looping exposures. `{ action, state }`. |
| POST | `/stop` | | Stop guiding (through N.I.N.A.) and all exposures. `{ action, state }`. |
| POST | `/start-guiding` | `calibrate` (false) | 202 `{ action: "start-guiding" \| "calibrate", accepted }`. Guiding starts through N.I.N.A.'s guider mediator; `calibrate=true` forces a new calibration. A start while another is still calibrating or settling replaces it, and both report the newer one's outcome. |
| POST | `/stop-guiding` | | Stop guiding through N.I.N.A.'s guider mediator. `{ action, state }`. |
| POST | `/pause`, `/resume` | | Pause or resume guiding; exposures continue. `{ action, state }`. |
| POST | `/dither` | `pixels` (0 < pixels <= 100), `raOnly` (false) | 202 `{ action: "dither", accepted, pixels, raOnly }`. Without `pixels`, N.I.N.A.'s dither settings apply. Settling is reported by `settle` messages. 409 while a dither started here still runs. |
| POST | `/select-star` | `x`, `y` (camera px, as in `/frame-info`; required) | Make the star nearest (x, y), within the search region, the guide star; only while looping without guiding. In multi-star mode its secondary stars are found around it. Answers after the next frame: `{ action: "select-star", star, secondaryStars, state }` (`star` is an `AdvancedGuideStar`). 409 `Rejected` with `messageCode` `NoStar`, `NearEdge`, `Busy` (guiding, calibrating, Coach), `NotLooping`, `Cancelled` or `TimedOut`; 400 without a position. |
| POST | `/clear-calibration` | | Forget the calibration. `{ action, state }`. |
| POST | `/darks/build` | `{ minExposure: 0.5, maxExposure: 4, frames: 5 }` (seconds; all optional) | 202 `{ action: "darks", accepted, minExposure, maxExposure, frames }`. The guide scope must be covered and the guider stopped (409 otherwise). Progress arrives as `darks` messages. |
| POST | `/darks/cancel` | | `{ action: "darks-cancel" }`; 409 when no build runs. |

## Guiding Coach

| Method | Route | Parameters | Response |
|---|---|---|---|
| GET | `/coach` | | `AdvancedCoachStatus` of the current or last session (`phase` `Idle` before the first). It always carries the camera's gain range and current exposure and gain. |
| POST | `/coach/start` | `AdvancedCoachOptions`: `{ steps, exposureSeconds, gains, framesPerCombination, driftSeconds, trialSeconds, repeatBaseline, allowCalibration }`, all optional | `{ action: "coach-start", accepted, pending, status }`. `steps` are `CameraCheck`, `Drift`, `MountResponse` and `Trials` (empty: all); empty lists mean the guider's defaults. A refusal is 409 `Rejected` with `messageCode` and `messageParameters`, and leaves the status of a running or last session untouched. Progress arrives as `coach` messages. |
| POST | `/coach/skip` | | Skip the running step; its usable partial results are kept. `{ action: "coach-skip", accepted, pending, status }`. |
| POST | `/coach/cancel` | | Cancel the session; temporary settings are restored. `{ action: "coach-cancel", accepted, pending, status }`. |
| POST | `/coach/apply` | `{ ids }`: finding ids, `trial:<id>`, or ids of active live hints; at most 50 | `{ action: "coach-apply", accepted, pending, status, applied }`. Applied hints are dismissed. 400 for unknown ids or ids without setting changes; 409 when there is nothing to apply. |
| GET | `/coach/history` | `max` (20, 1..200) | Stored `AdvancedCoachReport`s, newest first, without raw samples. |
| POST | `/hints/dismiss` | `{ id }` | `{ dismissed, hints }` with the hints still active. Hides the hint for the rest of the guiding session; 409 when it is no longer active. |

## Flight recorder (incidents)

Incidents can be reviewed while the internal guider is not connected, also while another guider such as PHD2 is;
only `mark` needs the internal guider connected. Incident ids (`yyyyMMdd-HHmmss-Kind[-n]`) consist of ASCII letters, digits and `-`, at most
100 characters; any other id is answered with 400 before it reaches the guider.

| Method | Route | Parameters | Response |
|---|---|---|---|
| GET | `/incidents` | | `AdvancedIncidentList`: `{ incidents, enabled, recordingId, usedBytes, budgetBytes, maxIncidents }`, the incident summaries newest first. |
| GET | `/incidents/{id}` | | `AdvancedIncident` with the telemetry of all its frames, its markers and its diagnosis. |
| GET | `/incidents/{id}/image` | `frame` (required), `kind` (`context` or `key`), `maxWidth`, `stretch`, `gamma`, `quality` | JPEG of a stored image, rendered like `/image`: `context` is the whole field, binned; `key` is full resolution, kept at the key moments. Headers `X-Frame-Number`, `X-Image-Binning` (sensor pixels per JPEG pixel), `X-Image-X0`, `X-Image-Y0` (sensor position of the top-left pixel), `X-Frame-Width`, `X-Frame-Height` (size of the JPEG). |
| GET | `/incidents/{id}/crops` | `frame` (required) | `{ frame, crops: [{ star, x0, y0, width, height, pixels }] }`: the raw star crops of the frame, primary first; empty when none were kept. |
| POST | `/incidents/{id}/keep` | `{ kept }` | The updated `AdvancedIncidentSummary`. A kept incident is skipped by the budget rotation. |
| DELETE | `/incidents/{id}` | | `{ deleted: true }`. |
| DELETE | `/incidents` | | `{ deleted: n }`: deletes all incidents that are not kept. |
| POST | `/incidents/mark` | `{ note }` (optional, one line, at most 500 characters) | `{ id }`. Records the last 2 minutes and the next 30 seconds as a manual incident; the guider must guide or calibrate (409 `Rejected` with its reason otherwise). |
| GET | `/incidents/{id}/download` | | `application/zip`, `Content-Disposition: attachment; filename="pins-incident-<id>.zip"`: frames as FITS, telemetry, diagnosis, settings, and guide-log and PINS-log excerpts. It is streamed while the guider writes it; an error after the first 64 KB can only be logged, and the client then has a truncated zip. |

## WebSocket `/ws/internal-guider`

Every message is `{ "type", "timestamp", "payload" }`. A client may send `{ "type": "ping" }` and gets a `pong`.
Each client has its own bounded queue; a slow client loses its own oldest messages.

Guider events (`AdvancedGuiderEventTypes`), forwarded whatever their type:

| Type | Payload |
|---|---|
| `step` | `AdvancedGuideStep` |
| `alert` | `AdvancedGuiderAlert`; `incidentId` names the incident it started or joined. |
| `state` | `AdvancedGuiderStatus` |
| `calibration` | Calibration progress, the finished `AdvancedGuiderCalibration`, or a failure. |
| `settle` | Settling progress and result. |
| `dither` | The dither offset. |
| `starlost` | Frame, SNR, star mass and status of a lost star. |
| `frame` | `{ frameNumber, timestamp, width, height, starCount, starsUsed }`. Pixels are never pushed; fetch `/image` and `/frame-info`. |
| `stats` | Window and session statistics. |
| `darks` | Dark library progress. |
| `coach` | `AdvancedCoachStatus` of the Guiding Coach. |
| `hint` | A live coaching hint (`AdvancedCoachFinding`). |
| `incident` | `{ action: "started" \| "saved" \| "deleted", id, summary }`; the summary carries no per-frame data and is `null` for `deleted`. |

Messages of this plugin:

| Type | Payload |
|---|---|
| `hello` | On connect: `{ available, connected, deviceId, deviceName, isNative }`. |
| `device` | The same, when the guider is connected, disconnected or changed. |
| `heartbeat` | The same, every 10 s. |
| `action` | `{ action, success, error }`: the outcome of `start-guiding`, `calibrate`, `dither`, `darks`, and of every call that answered 202. |
| `pong` | Answer to a `ping`. |
