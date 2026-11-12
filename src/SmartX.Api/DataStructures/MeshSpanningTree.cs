namespace SmartX.DataStructures;

public sealed record MeshTreeResult(
    string Root,
    MeshLink[] Links,
    long TotalCost,
    string[] Reachable,
    string[] Unreachable,
    string[] OfflineNodes);

public sealed partial class MeshGraph
{
    public MeshTreeResult MinimumSpanningTree(string root)
    {
        RequireNode(root);

        var visited = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var selectedLinks = new List<MeshLink>();

        var candidates = new PriorityQueue<MeshLink, int>();

        if (!offline.Contains(root))
        {
            visited.Add(root);
            AddCandidates(root);
        }

        while (candidates.TryDequeue(out var link, out _))
        {
            // Another cheaper connection may have reached this node.
            // Skipping it also prevents cycles.
            if (!visited.Add(link.To))
            {
                continue;
            }

            selectedLinks.Add(link);
            AddCandidates(link.To);
        }

        return new MeshTreeResult(
            root,
            selectedLinks.ToArray(),
            selectedLinks.Sum(link => (long)link.Cost),
            visited.ToArray(),
            adjacency.Keys
                .Where(node =>
                    !offline.Contains(node) &&
                    !visited.Contains(node))
                .ToArray(),
            offline.ToArray());

        void AddCandidates(string from)
        {
            foreach (var neighbour in adjacency[from])
            {
                string destination = neighbour.Key;
                int cost = neighbour.Value;

                if (offline.Contains(destination) ||
                    visited.Contains(destination))
                {
                    continue;
                }

                candidates.Enqueue(
                    new MeshLink(from, destination, cost),
                    cost);
            }
        }
    }
}