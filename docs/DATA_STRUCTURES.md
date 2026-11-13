# Smart-X Part 3: data structure implementation

## Scope and terminology

A vertex represents a registered sensor identifier or the central gateway. A weighted edge represents a simulated bidirectional wireless connection. Link costs are generated positive integers; they are not measured RSSI, latency, energy consumption or radio range.

The ASP.NET Core service builds the graph from the registered profiles and current health state. Its purpose is to demonstrate traversal, spanning-tree selection and recovery of available routes. Telemetry transport remains HTTP ingestion, and the physical radio mesh is simulated.

In the complexity descriptions below, `n` is one sensor's retained timeline size, `k` is its number of matching timestamps, `V` is the number of vertices and `E` is the number of links. Graph costs assume the bounded identifier strings used by this application.

## AVL tree: timestamp-indexed telemetry

Source: `src/SmartX.Api/DataStructures/AvlTree.cs`, integrated by `StreamHistory.cs` and checked by `HistoryPagingChecks.cs` and the main C# checks.

Each sensor has its own `AvlTree<DateTimeOffset, StreamLog>` in a dictionary keyed by sensor GUID. Separate trees prevent equal timestamps from different devices colliding. The root and child links form a binary search tree: smaller timestamps are on the left and larger timestamps on the right.

Each node stores its height. Insertion updates heights while returning through the recursive calls. A balance factor outside -1 to +1 triggers a rotation. A left-heavy subtree uses a right rotation, and a right-heavy subtree uses a left rotation. Left-right and right-left cases first rotate the child, then the unbalanced parent. This prevents chronological arrivals from turning the search tree into a long chain.

The generic tree replaces the value when inserting an existing key. The ingestion layer separately rejects a queued or stored timestamp replay before it becomes a second stream record. `ContainsKey` supports that validation. `Values` provides chronological enumeration for the existing moisture trend calculation.

### Efficient inclusive range search

The recursive query compares both bounds with the current timestamp:

```csharp
int fromComparison = from.CompareTo(node.Key);
int toComparison = to.CompareTo(node.Key);

if (fromComparison < 0)
{
    CollectRange(node.Left, from, to, results);
}

if (fromComparison <= 0 && toComparison >= 0)
{
    results.Add(node.Value);
}

if (toComparison > 0)
{
    CollectRange(node.Right, from, to, results);
}
```

A branch outside the possible range is not searched. Matching values are collected in timestamp order. A narrow search takes O(log n + k), including the work required to return its results. A range covering every reading necessarily visits every matching reading.

### Retention and integration

After a queued packet is successfully stored, `RecordProcessed` creates its `StreamLog` and inserts it into that sensor's tree:

```csharp
timeline.Insert(work.Timestamp, row);

while (timeline.Count > 2048)
{
    timeline.RemoveMinimum();
}
```

`RemoveMinimum` follows left links to the oldest key, removes it and rebalances while returning to the root. Insertion, key lookup and oldest-key removal each take O(log n). Retained storage is O(n), bounded to 2,048 entries per sensor. The tree itself has no internal locks; `RealtimeEngine` protects access with its existing lock.

The raw `History<T>` lists remain available for the original typed APIs and preload data. The new AVL timeline contains successfully processed ingestion-queue traffic. This preserves Part 1 behaviour while giving the new date-range endpoint an explicit index.

### Paging after range selection

`StreamHistory.History` first calls `Range(start, end)`, then calculates a page from the newest end of the matching list. `skip=0` returns the newest page; increasing `skip` by `take` requests older matches. The API keeps each page ascending for the existing chart; the topology table reverses it for display.

The response reports retained, matched and displayed counts separately. This prevents a 25-row display from implying only 25 readings exist. Paging does not remove the range-query output cost: the method still materialises the `k` matching records before slicing its page.

## Graph: a weighted adjacency list

Source: `src/SmartX.Api/DataStructures/MeshGraph.cs`; application integration: `NetworkTopology.cs`.

The graph stores neighbours in:

```csharp
Dictionary<string, Dictionary<string, int>> adjacency;
```

The outer dictionary maps a node identifier to its neighbour/cost dictionary. Both use case-insensitive identifier comparisons. `Connect(first, second, cost)` validates both endpoints, rejects self-links and non-positive costs, then writes both directions:

```csharp
adjacency[first][second] = cost;
adjacency[second][first] = cost;
```

The graph retains configured edges even when a node is unavailable. A separate case-insensitive `HashSet<string>` records offline identifiers. Traversal and Prim skip these nodes; restoring availability reuses the existing connections. `Links` exposes each undirected edge once for display and API output.

Adjacency storage uses O(V + E) space. Adding a known connection uses expected O(1) dictionary operations. Enumerating configured edges takes O(V + E). An adjacency list suits this sparse simulation because nodes have only a small number of peer connections.

### Generated topology and current health

In registration order, a sensor connects to its previous one and previous two neighbours when present. The first three sensors and every twenty-fifth sensor also connect directly to the gateway. The generator assigns small positive link costs. Newly registered sensors join the model on the next snapshot.

Before routing, `NetworkTopology` applies three availability conditions: simulated failure, device pause and the gateway's `Disconnected` telemetry state. The reporting timeout is three expected intervals; startup seeds and registration grace affect the initial freshness state. Current availability is evaluated for every new snapshot or route calculation.

This rebuilding is O(V + E) before the graph algorithms run. It makes new profiles and current health immediately visible without retaining a stale graph cache. It also means snapshot and route requests can observe different health moments. The service lock protects simulated-failure changes and the current calculation; the gateway lock protects its profile/health snapshot.

## BFS: connected devices and fewest-hop routes

Source: `MeshGraph.Traverse` and `MeshGraph.FindPath`.

BFS uses a FIFO queue. `Traverse` marks the start as visited, enqueues it, then repeatedly dequeues a node and admits each unvisited online neighbour. A visited set prevents cycles and repeated expansion. Its output identifies the online component connected to the chosen root.

`FindPath` additionally stores a predecessor dictionary. A predecessor is assigned when a node is first discovered:

```csharp
previous.Add(neighbour, current);
queue.Enqueue(neighbour);
```

When the destination is reached, the method follows those predecessor links back to the source and reverses the resulting list. Discovery expands in hop layers, so the first destination reached has the fewest edges. Weight values do not affect BFS.

Time is O(V + E) for a complete traversal, with O(V) additional queue/visited/predecessor space. Path reconstruction is O(h), where `h` is the number of nodes on the returned path. Offline sources, offline destinations and disconnected pairs return no path; unknown identifiers are rejected.

In the dashboard, BFS powers the full-network reachability calculation and the fewest-hop route option. A failed relay is skipped on the next calculation, allowing a different neighbour sequence when one exists.

## Min-heap and Prim's Minimum Spanning Tree

Source: `src/SmartX.Api/DataStructures/MeshSpanningTree.cs`.

Prim starts with the gateway as its first visited vertex. A `PriorityQueue<MeshLink, int>` stores candidate edges, with each edge's cost supplied as the min-heap priority:

```csharp
candidates.Enqueue(
    new MeshLink(from, destination, cost),
    cost);
```

The main loop removes the cheapest remaining candidate. An edge is accepted only if its destination is not already visited:

```csharp
while (candidates.TryDequeue(out var link, out _))
{
    if (!visited.Add(link.To))
    {
        continue;
    }

    selectedLinks.Add(link);
    AddCandidates(link.To);
}
```

Every accepted edge adds one previously unseen vertex, so it cannot create a cycle. Candidates left behind after a cheaper connection reaches their destination are discarded when popped. This is a lazy Prim implementation: its frontier may contain multiple candidate edges to the same destination.

The heap avoids scanning every candidate for the cheapest edge at every step. A heap insertion/removal is O(log E) in the worst frontier size. The complete lazy algorithm is O(E log E), plus vertex bookkeeping, with O(V + E) auxiliary storage. The tree's total cost is summed as `long` to avoid an integer sum overflow when many positive link costs are combined.

### Disconnected components and path reconstruction

The result describes the gateway's reachable component. With `Vc` reachable vertices, it has `Vc - 1` selected edges. Other online vertices are listed as `Unreachable`, and unavailable vertices as `OfflineNodes`. A disconnected whole network has no single spanning tree covering all vertices.

Each selected link points from an already visited vertex to its newly reached destination. `NetworkTopology.Route` therefore builds a `To -> From` parent dictionary and follows it from the selected sensor back to the gateway. This yields the route through the selected MST.

Equal-cost alternatives can produce different valid MSTs. Tests should compare required connectivity, total cost and structural validity rather than assuming one unique edge arrangement when weights tie.

### Why MST and fewest-hop routing are separate

Consider these links:

| Link | Cost |
|---|---:|
| Gateway–A | 2 |
| Gateway–B | 3 |
| A–B | 2 |
| B–C | 1 |

An MST selects Gateway–A, A–B and B–C for total cost 5. B's tree route costs 4 through A, although its direct gateway connection costs 3. The MST minimises the cost of connecting the component collectively; it does not optimise each device's path independently. BFS would use B's one-hop direct connection because it minimises hops.

The UI consequently labels its modes **BFS — fewest hops** and **MST — tree route**. It does not describe the MST as every device's fastest route.

## Relationship with earlier requirements

| Earlier structure/concept | Continued role |
|---|---|
| Generic `TelemetryPacket<T>` and `History<T>` | Preserve float/int/bool payloads in the original sensor storage path |
| Jagged packet arrays | Variable-sized nested ingestion batches |
| Recursive deployment validation | Validate a selected hierarchical facility path before registration |
| Operator-overloaded `MeterReading` | Add meter readings and calculate wattage differences |
| FIFO queue and separate critical heap | Preserve normal arrival order and allow urgent telemetry to bypass a backlog |
| Stack | Undo the latest manual command or grouped shutdown |
| Device dictionary and alert sets | Resolve GUIDs and maintain unique error states |

The topology graph is a connectivity model, while the deployment tree describes facility hierarchy. They represent different relationships and are not interchangeable.

## Validation and limits

C# checks exercise chronological AVL insertion and height, inclusive ranges, oldest-entry removal, duplicate handling, BFS rerouting, known MST costs, disconnected nodes, gateway failure and history paging. The HTTP suite verifies actual endpoints, malformed requests, case-insensitive/MAC lookup, failure recovery, reporting timeout and date-range page boundaries. The current CI result is recorded in [VERIFICATION.md](VERIFICATION.md).

The model is bounded to 5,000 registered sensors and runs in one process. Large numbers of per-sensor histories can still consume substantial memory. Rebuilding and returning the full graph has a cost even though the browser displays a smaller window. In-process load timings, HTTP checks and routing calculations are different measurements and should not be reported as physical wireless-network throughput or energy savings.
