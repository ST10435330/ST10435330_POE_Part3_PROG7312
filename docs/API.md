# Smart-X API — Parts 1–3

Base: `http://localhost:5080`. JSON uses camelCase. Use a registered sensor GUID; its readable identifier is not its GUID.

## Register a device

`POST /api/sensors` -> 201:

```json
{"identifier":"PUMP-A","category":"Actuator","path":["Facility A","Zone 1","Sub-Zone B"],"expectedIntervalSeconds":5}
```

Categories: `Environmental`, `Power Consumption`, `Actuator`. Get IDs with `GET /api/sensors?take=10` or `GET /api/stream/devices?search=PUMP-A`.

## Admit typed telemetry

`POST /api/telemetry/environmental` -> 202 and a receipt:

```json
{"sensorId":"REPLACE-WITH-ENVIRONMENTAL-GUID","timestamp":"{{$isoTimestamp}}","value":5.0}
```

The timestamp placeholder is for Postman; other clients must send the current ISO UTC timestamp. Moisture 5% is critical. Use `/api/telemetry/power` with integer `8000` for critical power, or `/api/telemetry/actuator` with JSON boolean `true`/`false`.

Read the response `id` and request `GET /api/stream/receipts/{id}` until `Processed` or `Rejected`. `Queued` is not confirmation of storage. Receipt `sequence` is admission order; stream log `sequence` is processing order.

For batches, POST to `/api/telemetry/environmental/batch` (or power/actuator):

```json
{"batches":[[{"sensorId":"GUID","timestamp":"CURRENT-UTC-TIME","value":50.0}],[{"sensorId":"GUID","timestamp":"A-LATER-UTC-TIME","value":51.0}]]}
```

Up to 1,000 total packets. Valid-shaped batches return 200 with `received`, `queued`, `rejected`, per-packet receipts and admission errors. Processing failures are reported later by individual receipts. Empty/null/malformed batches return 400. Invalid single packet admission returns 400/404/409; a full queue returns 429.

## Queue controls and history

| Method | Route | Result / body |
|---|---|---|
| GET | `/api/health` | API health |
| GET | `/api/stream/queue` | Pending, admitted, processed, failed, capacity, pause state |
| POST | `/api/stream/processing` | `{"running":false}` pauses normal processing; critical work continues |
| GET | `/api/stream/devices?category=Environmental&status=Critical&search=ESP32&skip=0&take=30` | Filtered registry, total count, search timing |
| GET | `/api/stream/devices/{guid}` | Latest device and override state |
| GET | `/api/stream/history/{guid}?take=120&skip=0&from=ISO-TIME&to=ISO-TIME` | Paged AVL timestamp timeline; omit date limits for all retained times |
| GET | `/api/stream/recent?after=0` | Recent processing order |
| GET | `/api/stream/alerts` | Unique active states and recent transitions |
| GET | `/api/stream/events` | `text/event-stream`; named `refresh` event every second |
| GET | `/api/telemetry/environmental/{guid}?count=120` | Original typed raw history (also power/actuator variants) |

## Commands and insights

| Method | Route | Body / response |
|---|---|---|
| POST | `/api/stream/commands` | `{"sensorId":"ACTUATOR-GUID","state":true}` changes state and pushes undo |
| POST | `/api/stream/commands/shutdown-all` | `{}`; grouped OFF command |
| POST | `/api/stream/commands/undo` | `{}`; restores last command's saved states |
| GET | `/api/stream/commands` | Undo depth and command audit |
| POST | `/api/stream/links` | `{"sensorId":"MOISTURE-GUID","actuatorId":"PUMP-GUID"}` |
| POST | `/api/stream/observations/search` | `{"sensorId":"GUID"}`; deliberate user selection, not automatic refresh |
| GET | `/api/stream/insights` | Rule, learned frequency and trend suggestions with evidence |
| GET | `/api/stream/patterns` | Inspect links, bounded search contexts and learned counts |

Commands require an Actuator and a boolean state. Empty undo returns 409. Commands simulate physical state changes locally; there is no real hardware acknowledgement. Recommendations are not automatically executed.

## HTTP simulator

- `POST /api/stream/simulation` with `{"running":true,"devices":20}` starts first N registered devices; false stops.
- `GET /api/stream/simulation` returns state/count/transport.
- `POST /api/simulator/{guid}/sample` with `{"spike":true}` sends one simulated reading through an internal HTTP client to the actual telemetry API; returns its admission receipt.
- `POST /api/simulator/{guid}/pause` with `{"paused":true}` stops automatic samples for that device.
- `POST /api/simulator/load` with `{"rounds":2}` sends two HTTP rounds for the configured device count. Returns admission/round-trip time; inspect queues for completed processing.

Original registration, deployment validation, attachments, health, summary and meter aggregation endpoints remain available. All volatile state resets on server restart; profiles and uploaded files persist.


## Part 3: topology snapshot

`GET /api/topology` returns the full registered network and the current routing calculation:

| Field | Meaning |
|---|---|
| `generatedAt` | UTC time at which the snapshot was produced |
| `gateway` | Root identifier, `CENTRAL GATEWAY` |
| `nodes` | Gateway plus registered sensors; each has `sensorId`, `identifier`, `category`, `online`, `reachable`, `status`, `telemetryStatus` and `lastReceived` |
| `links` | Configured bidirectional links, each emitted once with `from`, `to` and positive integer `cost` |
| `tree` | Prim result containing `root`, `links`, `totalCost`, `reachable`, `unreachable` and `offlineNodes` |
| `traversal` | BFS traversal starting at the gateway |

The gateway node has `sensorId: null`. Sensor GUIDs are the same values used by registration, ingestion and history endpoints. Graph edges use the readable sensor identifiers.

`online` indicates mesh eligibility; `reachable` additionally requires a path to the gateway. Paused sensors, sensors with expired telemetry and sensors with simulated failures are excluded from traversal and Prim's algorithm. Offline nodes and online-but-unreachable nodes are reported separately.

Links and weights are generated simulation data. The returned MST spans the gateway's connected online component. The snapshot does not claim a whole-network spanning tree when components are disconnected. `totalCost` is the sum of the selected tree's link costs, not a measured transport delay.

## Part 3: route lookup

```http
GET /api/topology/route?identifier=ESP32-0004
GET /api/topology/route?identifier=AA%3ABB%3ACC%3ADD%3AEE%3A03
```

The lookup accepts an exact registered identifier or MAC address, case-insensitively. The MAC example requires a sensor registered with that address. Encode the query with `URLSearchParams`, Postman's Params tab or an equivalent URL encoder.

| Response field | Meaning |
|---|---|
| `sensorId` | Selected sensor GUID |
| `identifier` | Canonical registered identifier |
| `online` | Current mesh eligibility |
| `bfsPath` | Fewest-hop route from the sensor to the gateway |
| `mstPath` | Route from the sensor through the current Prim tree to the gateway |

Both arrays list the source first and the gateway last. They are empty when no route is available. An online sensor may still be unreachable. BFS ignores weights; an MST minimises total tree cost and does not guarantee an individually cheapest path.

Missing/blank identifiers return 400. An unknown identifier returns 404. Snapshot and route calls calculate from current state independently, so health changes between calls can produce different results.

## Part 3: simulated failure control

`POST /api/topology/failure` returns an updated topology snapshot:

```json
{
  "sensorId": "REPLACE-WITH-REGISTERED-SENSOR-GUID",
  "failed": true
}
```

Use `false` to clear the simulated fault. Both fields are required, and `failed` must be a JSON boolean. The gateway has no sensor GUID and is not controlled by this endpoint. An unknown sensor returns 404; missing or malformed fields return 400.

The control changes simulated mesh availability. Existing telemetry remains readable, and the HTTP receiver continues operating. Clearing a simulated fault does not make a paused or telemetry-disconnected device immediately online. Pause/resume and reception freshness remain separate availability conditions. Failure flags reset on server restart.

## Part 3: filtered and paged history

```http
GET /api/stream/history/{guid}?take=25&skip=0
GET /api/stream/history/{guid}?take=25&skip=25
```

| Query parameter | Meaning |
|---|---|
| `from` | Optional inclusive lower timestamp bound |
| `to` | Optional inclusive upper timestamp bound |
| `take` | Page size; default 120, clamped to 1–500 |
| `skip` | Number of newest matching readings to skip; default 0, negative values normalised to 0 |

Dates are ISO timestamps with an explicit UTC `Z` or offset. URL-encode offsets containing `+`. The topology client converts its local datetime inputs to UTC ISO strings before calling the API. An inverted range or malformed date returns 400. An unknown sensor GUID returns 404.

The AVL range query selects matching readings first. Paging then works backwards from the newest match, while each response's `records` array remains in ascending timestamp order. The topology table reverses the page for newest-first display; the command-stream chart can continue using the ascending response.

| Response field | Meaning |
|---|---|
| `records` | Current page of `StreamLog` entries |
| `stored` | Total retained processed readings for this sensor, before date filtering |
| `matched` | Retained readings matching the inclusive date range |
| `take`, `skip` | Normalised paging values |
| `hasOlder`, `hasNewer` | Whether another direction of navigation is available |
| `algorithm` | Description of the AVL timestamp index |
| `searchMilliseconds` | Server-side query/materialisation time; excludes HTTP transfer and browser rendering |

A stream record contains `sequence`, `sensorId`, `identifier`, `timestamp`, `value`, `critical`, `lane`, `processedAt` and `numericValue`. `timestamp` is the sensor's sample time; `processedAt` is when the worker recorded it. `sequence` describes processing order and can differ from timestamp order.

An empty range or offset beyond the available matches returns an empty `records` array with HTTP 200. Each sensor retains up to 2,048 AVL entries. These logs contain successfully processed queue traffic since startup, while original typed raw history also contains startup seed samples and direct manual actuator states.

Older pages are not immutable snapshots: new samples or retention trimming can change subsequent page boundaries. The topology UI pauses automatic refresh when viewing an older page and retains its applied filters while navigating between views.

## Automated endpoint verification

`python scripts/http_checks.py` starts an isolated application and runs the existing Part 1/Part 2 checks followed by `scripts/topology_http_checks.py`. Part 3 cases exercise route lookup, simulated failure/recovery, pause exclusion, malformed controls, MAC identifiers, automatic health timeout, inclusive date bounds, open-ended ranges and history paging.
