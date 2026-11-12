namespace SmartX.DataStructures;

public sealed record MeshLink(string From, string To, int Cost);

public sealed partial class MeshGraph
{
    private readonly Dictionary<string, Dictionary<string, int>> adjacency =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> offline =
        new(StringComparer.OrdinalIgnoreCase);

    public string[] Nodes => adjacency.Keys.ToArray();

    public MeshLink[] Links => adjacency
        .SelectMany(node => node.Value
            .Where(neighbour =>
                StringComparer.OrdinalIgnoreCase.Compare(
                    node.Key, neighbour.Key) < 0)
            .Select(neighbour =>
                new MeshLink(node.Key, neighbour.Key, neighbour.Value)))
        .ToArray();

    public void AddNode(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new GatewayException("A node identifier is required.");
        }

        if (!adjacency.ContainsKey(identifier))
        {
            adjacency.Add(
                identifier,
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase));
        }
    }

    public void Connect(string first, string second, int cost)
    {
        RequireNode(first);
        RequireNode(second);

        if (StringComparer.OrdinalIgnoreCase.Equals(first, second))
        {
            throw new GatewayException(
                "A node cannot connect to itself.");
        }

        if (cost <= 0)
        {
            throw new GatewayException(
                "Connection cost must be greater than zero.");
        }

        // Wireless links are modelled as bidirectional.
        adjacency[first][second] = cost;
        adjacency[second][first] = cost;
    }

    public bool IsOnline(string identifier)
    {
        RequireNode(identifier);
        return !offline.Contains(identifier);
    }

    public void SetOnline(string identifier, bool online)
    {
        RequireNode(identifier);

        if (online)
        {
            offline.Remove(identifier);
        }
        else
        {
            offline.Add(identifier);
        }
    }

    public string[] Traverse(string start)
    {
        RequireNode(start);

        if (!IsOnline(start))
        {
            return Array.Empty<string>();
        }

        var visited = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var queue = new Queue<string>();
        var result = new List<string>();

        visited.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            result.Add(current);

            foreach (string neighbour in adjacency[current].Keys)
            {
                if (offline.Contains(neighbour))
                {
                    continue;
                }

                if (visited.Add(neighbour))
                {
                    queue.Enqueue(neighbour);
                }
            }
        }

        return result.ToArray();
    }

    public string[] FindPath(string start, string destination)
    {
        RequireNode(start);
        RequireNode(destination);

        if (!IsOnline(start) || !IsOnline(destination))
        {
            return Array.Empty<string>();
        }

        var previous = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        var queue = new Queue<string>();

        previous.Add(start, null);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();

            if (StringComparer.OrdinalIgnoreCase.Equals(
                current, destination))
            {
                var path = new List<string>();
                string? step = current;

                while (step is not null)
                {
                    path.Add(step);
                    step = previous[step];
                }

                path.Reverse();
                return path.ToArray();
            }

            foreach (string neighbour in adjacency[current].Keys)
            {
                if (offline.Contains(neighbour) ||
                    previous.ContainsKey(neighbour))
                {
                    continue;
                }

                previous.Add(neighbour, current);
                queue.Enqueue(neighbour);
            }
        }

        return Array.Empty<string>();
    }

    private void RequireNode(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) ||
            !adjacency.ContainsKey(identifier))
        {
            throw new GatewayException(
                "The mesh node does not exist.", 404);
        }
    }
}