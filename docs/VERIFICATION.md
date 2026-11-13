# Smart-X Part 3 verification record

## Recorded CI baseline

| Item | Recorded result |
|---|---|
| Source commit | `00f4b9e03323f6e468857e6fef9038036664e322` |
| Confirmation date | 12 November 2026 |
| GitHub Actions | Green run confirmed by the repository owner |
| Workflow | `.github/workflows/build.yml`, job `verify` |
| Runner configuration | `ubuntu-latest`, .NET `10.0.x`, Node `24`, Python `3.12` |
| Exact SDK patch, job URL and printed assertion totals | Not transcribed into this record |

[Repository Actions page](https://github.com/ST10435330/ST10435330_POE_Part3_PROG7312/actions)

This baseline records the source revision for which the green CI result was confirmed. Later documentation or application changes receive their own workflow run and should be checked before submission.

## What the green workflow covers

| Check | Coverage |
|---|---|
| Release solution build | API and C# check project compile under the configured SDK |
| `dotnet run --project tests/SmartX.Checks -c Release --no-build` | Typed payloads, deployment recursion, operator overloads, queue/command behaviour, AVL balance/ranges/retention, BFS, Prim and history paging |
| Six JavaScript syntax checks | `app.js`, `navigation.js`, `stream.js`, `stream-api.js`, `topology.js`, `topology-history.js` |
| `python scripts/http_checks.py` | Real isolated HTTP server, Part 1/Part 2 workflows and the Part 3 helper suite |

The Part 3 HTTP helper checks served dashboard assets, registered identities, valid weighted links, BFS/MST reachability, tree totals, identifier/MAC lookup, relay failure and alternative routing, failed-source history access, restoration, pause exclusion, invalid controls, automatic timeout/recovery, timestamp bounds and paging metadata.

The HTTP runner starts an application on port 5089 with a temporary `DataDirectory` and removes that test data when it exits. It verifies the real receiver routes rather than substituting mocked responses. C# load timings are in-process measurements; they exclude HTTP transport. Query timings exclude browser rendering and network transfer.

## Reproduce the automated checks

From the folder containing `SmartX.sln`, run each command after the previous one succeeds:

```powershell
dotnet build SmartX.sln -c Release -m:1
dotnet run --project tests/SmartX.Checks -c Release --no-build
python scripts/http_checks.py
```

The final HTTP line reports the actual number of checks performed across Parts 1–3. Keep the actual terminal or CI output if a numerical total is needed in the final report. Do not reuse the older Part 2 totals below as a current Part 3 count.

## Final browser checklist

A green syntax/HTTP run does not itself exercise browser clicks or responsive layout. The following Part 3 checklist still needs a recorded final browser pass:

- [ ] Open the main menu and all three module views; return without losing the current selection/inputs.
- [ ] Start the 20-sensor demo, select `ESP32-0004` and inspect mesh health and last-reception information.
- [ ] Switch BFS/MST highlighting and confirm that the displayed route endpoints are the selected sensor and central gateway.
- [ ] Fail an intermediate relay, observe an alternative where available, select that relay and clear its failure.
- [ ] Fail the source, confirm no route is available and confirm its existing historical readings remain accessible.
- [ ] Search for a sensor outside the initial graph window and confirm it becomes selectable/visible.
- [ ] Send normal telemetry and confirm it appears after queue processing.
- [ ] Filter history by local dates, test an empty range and reject an inverted range, then clear the filter.
- [ ] Generate enough readings for multiple pages; inspect older/newer/latest navigation and verify an older page does not auto-refresh.
- [ ] Switch sensors while a query is pending and confirm that rows belong to the final selected sensor.
- [ ] Check a narrow viewport around 390 px. The graph/table may scroll inside their containers; the whole page should not overflow horizontally.
- [ ] Check the browser console for uncaught errors and confirm selected sensor/history filters restore as documented after a page reload.

The existing optional `scripts/browser_checks.cjs` exercises earlier Part 1/Part 2 interactions. It has not been extended to provide this Part 3 browser evidence.

## Docker verification

Dockerfile and Compose configuration are included. Container execution is not part of the confirmed workflow and remains pending a recorded runtime check.

With the local app stopped, run:

```powershell
docker compose up --build -d
docker compose ps
docker compose logs --tail 30 gateway
Invoke-RestMethod http://localhost:5080/api/health
```

Then open the application, check all views, start HTTP simulation and inspect topology/history. Register a test profile or upload a small attachment, stop with `docker compose down`, start again, and confirm the profile/attachment remains available in the named volume. Telemetry and other in-memory state are expected to reset.

Record the actual date, Docker environment and outcome after executing these steps. A successful native .NET run should not be described as a Docker pass.

## Earlier Part 2 evidence

The prior repository verification record dated **17 October 2026** reported .NET SDK **10.0.402**, a successful Release build, **37 C# checks**, **27 HTTP checks**, and headless Chromium checks of the earlier telemetry/command views. Those browser checks included priority bypass, command undo, new pump linking, simulator controls and desktop/mobile page width.

These are historical Part 2 results. They do not establish the current Part 3 assertion count or verify the newly added topology/history browser interactions. The Part 3 CI baseline above is the current automated result.

## Interpretation and remaining evidence

The application models devices, command effects and mesh connectivity locally. Physical ESP32 radios, real pump acknowledgements, measured wireless latency and physical energy savings have not been verified. Profiles and attachments persist; volatile telemetry, undo, failure flags and learned associations reset on restart.

Before submission, confirm the workflow for the final commit, complete the browser/Docker records, and ensure any demonstration video and PDF report refer to the same final application behaviour.
