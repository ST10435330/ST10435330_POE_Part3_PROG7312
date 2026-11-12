using System.Text.Json.Serialization;
using SmartX.DataStructures;

//Adds:
//A snapshot of sensors, links, health, BFS traversal and the MST.
//Route lookup by the sensor’s registered identifier or MAC address.
//Failure simulation that immediately excludes a node from routing.
//Recalculation using current sensor health on each request.

namespace SmartX;

public sealed record MeshDeviceHealth(
    Sensor Sensor,
    string Status,
    DateTimeOffset? LastReceived,
    bool Paused);

public sealed record TopologyNode(
    Guid? SensorId,
    string Identifier,
    string Category,
    bool Online,
    bool Reachable,
    string Status,
    string TelemetryStatus,
    DateTimeOffset? LastReceived);

public sealed record TopologySnapshot(
    DateTimeOffset GeneratedAt,
    string Gateway,
    TopologyNode[] Nodes,
    MeshLink[] Links,
    MeshTreeResult Tree,
    string[] Traversal);

public sealed record TopologyRoute(
    Guid SensorId,
    string Identifier,
    bool Online,
    string[] BfsPath,
    string[] MstPath);

public sealed record TopologyFailureRequest(
    [property: JsonRequired] Guid SensorId,
    [property: JsonRequired] bool Failed);

public sealed partial class Gateway
{
    public MeshDeviceHealth[] MeshDevices()
    {
        lock (gate)
        {
            return sensors.Values.Select(sensor =>
                new MeshDeviceHealth(
                    sensor,
                    Status(sensor),
                    received.TryGetValue(sensor.Id, out var timestamp)
                        ? timestamp
                        : null,
                    paused.Contains(sensor.Id)))
                .ToArray();
        }
    }
}

public sealed class NetworkTopology(Gateway gateway)
{
    public const string GatewayIdentifier = "CENTRAL GATEWAY";

    private readonly object sync = new();

    private readonly HashSet<Guid> forcedOffline = new();

    public TopologySnapshot Snapshot()
    {
        lock (sync)
        {
            return Build().Snapshot;
        }
    }

    public TopologySnapshot SetFailure(TopologyFailureRequest request)
    {
        lock (sync)
        {
            gateway.Get(request.SensorId);

            if (request.Failed)
            {
                forcedOffline.Add(request.SensorId);
            }
            else
            {
                forcedOffline.Remove(request.SensorId);
            }

            return Build().Snapshot;
        }
    }

    public TopologyRoute Route(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new GatewayException(
                "Enter a sensor identifier or MAC address.");
        }

        lock (sync)
        {
            var (graph, snapshot) = Build();

            var node = snapshot.Nodes.FirstOrDefault(node =>
                node.SensorId.HasValue &&
                node.Identifier.Equals(
                    identifier.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (node is null)
            {
                throw new GatewayException(
                    "Sensor identifier not found.", 404);
            }

            string[] bfsPath = graph.FindPath(
                node.Identifier,
                GatewayIdentifier);

            var parents = snapshot.Tree.Links.ToDictionary(
                link => link.To,
                link => link.From,
                StringComparer.OrdinalIgnoreCase);

            var mstPath = new List<string>();

            if (node.Reachable)
            {
                string current = node.Identifier;
                mstPath.Add(current);

                while (parents.TryGetValue(current, out var parent))
                {
                    mstPath.Add(parent);
                    current = parent;
                }
            }

            return new TopologyRoute(
                node.SensorId!.Value,
                node.Identifier,
                node.Online,
                bfsPath,
                mstPath.ToArray());
        }
    }

    private (MeshGraph Graph, TopologySnapshot Snapshot) Build()
    {
        var devices = gateway.MeshDevices();
        var graph = new MeshGraph();

        graph.AddNode(GatewayIdentifier);

        foreach (var device in devices)
        {
            graph.AddNode(device.Sensor.Identifier);
        }

        for (int i = 0; i < devices.Length; i++)
        {
            string current = devices[i].Sensor.Identifier;

            // Neighbouring sensors provide multiple possible paths.
            if (i > 0)
            {
                graph.Connect(
                    current,
                    devices[i - 1].Sensor.Identifier,
                    1 + i % 5);
            }

            if (i > 1)
            {
                graph.Connect(
                    current,
                    devices[i - 2].Sensor.Identifier,
                    2 + i % 4);
            }

            // Some sensors have a direct gateway connection.
            if (i < 3 || i % 25 == 0)
            {
                graph.Connect(
                    current,
                    GatewayIdentifier,
                    2 + i % 7);
            }

            bool online =
                !forcedOffline.Contains(devices[i].Sensor.Id) &&
                !devices[i].Paused &&
                devices[i].Status != "Disconnected";

            graph.SetOnline(current, online);
        }

        var tree = graph.MinimumSpanningTree(GatewayIdentifier);

        var reachable = new HashSet<string>(
            tree.Reachable,
            StringComparer.OrdinalIgnoreCase);

        var nodes = new List<TopologyNode>
        {
            new(
                null,
                GatewayIdentifier,
                "Gateway",
                true,
                true,
                "Online",
                "Gateway",
                null)
        };

        foreach (var device in devices)
        {
            var sensor = device.Sensor;

            string status =
                forcedOffline.Contains(sensor.Id)
                    ? "Simulated failure"
                    : device.Paused
                        ? "Paused"
                        : device.Status == "Disconnected"
                            ? "Disconnected"
                            : "Online";

            nodes.Add(new TopologyNode(
                sensor.Id,
                sensor.Identifier,
                sensor.Category,
                graph.IsOnline(sensor.Identifier),
                reachable.Contains(sensor.Identifier),
                status,
                device.Status,
                device.LastReceived));
        }

        var snapshot = new TopologySnapshot(
            DateTimeOffset.UtcNow,
            GatewayIdentifier,
            nodes.ToArray(),
            graph.Links,
            tree,
            graph.Traverse(GatewayIdentifier));

        return (graph, snapshot);
    }
}