using SmartX.DataStructures;

public static class MeshGraphChecks
{
    public static void Run(
        Action<bool, string> check,
        Action<Action, string> reject)
    {
        var graph = new MeshGraph();

        foreach (string identifier in new[]
        {
            "GATEWAY", "A", "B", "C", "D", "ISOLATED"
        })
        {
            graph.AddNode(identifier);
        }

        graph.Connect("A", "B", 1);
        graph.Connect("B", "GATEWAY", 1);

        graph.Connect("A", "C", 2);
        graph.Connect("C", "D", 2);
        graph.Connect("D", "GATEWAY", 3);

        check(
            graph.Nodes.Length == 6 && graph.Links.Length == 5,
            "mesh stores nodes and undirected weighted links");

        check(
            graph.Traverse("GATEWAY")
                .ToHashSet()
                .SetEquals(new[] { "GATEWAY", "A", "B", "C", "D" }),
            "BFS discovers connected nodes and excludes isolated nodes");

        check(
            graph.FindPath("A", "GATEWAY")
                .SequenceEqual(new[] { "A", "B", "GATEWAY" }),
            "BFS finds the route with the fewest hops");

        graph.SetOnline("B", false);

        check(
            graph.FindPath("A", "GATEWAY")
                .SequenceEqual(new[] { "A", "C", "D", "GATEWAY" }),
            "mesh reroutes around an offline node");

        check(
            !graph.Traverse("GATEWAY").Contains("B"),
            "BFS excludes offline nodes");

        graph.SetOnline("D", false);

        check(
            graph.FindPath("A", "GATEWAY").Length == 0,
            "mesh reports no route when every gateway path is blocked");

        graph.SetOnline("B", true);

        check(
            graph.FindPath("A", "GATEWAY")
                .SequenceEqual(new[] { "A", "B", "GATEWAY" }),
            "restoring a node restores its route");

        graph.SetOnline("A", false);

        check(
            !graph.IsOnline("A") &&
            graph.FindPath("A", "GATEWAY").Length == 0,
            "an offline source cannot send through the mesh");

        reject(
            () => graph.Connect("A", "A", 1),
            "self-connections are rejected");

        reject(
            () => graph.Connect("A", "B", 0),
            "non-positive connection costs are rejected");

        reject(
            () => graph.FindPath("UNKNOWN", "GATEWAY"),
            "unknown mesh nodes are rejected");
    }
}