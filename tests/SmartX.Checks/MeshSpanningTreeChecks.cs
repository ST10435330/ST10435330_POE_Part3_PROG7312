using SmartX.DataStructures;

public static class MeshSpanningTreeChecks
{
    public static void Run(
        Action<bool, string> check,
        Action<Action, string> reject)
    {
        var graph = new MeshGraph();

        foreach (string identifier in new[]
        {
            "GATEWAY", "A", "B", "C", "ISOLATED"
        })
        {
            graph.AddNode(identifier);
        }

        graph.Connect("GATEWAY", "A", 9);
        graph.Connect("GATEWAY", "B", 2);
        graph.Connect("B", "A", 1);
        graph.Connect("A", "C", 3);
        graph.Connect("B", "C", 8);

        var tree = graph.MinimumSpanningTree("GATEWAY");

        check(
            tree.TotalCost == 6 &&
            tree.Links.Select(link => link.Cost)
                .OrderBy(cost => cost)
                .SequenceEqual(new[] { 1, 2, 3 }),
            "Prim selects minimum-cost connections");

        check(
            tree.Reachable
                .ToHashSet()
                .SetEquals(new[] { "GATEWAY", "A", "B", "C" }),
            "MST includes every node reachable from the gateway");

        check(
            tree.Links.Length == tree.Reachable.Length - 1,
            "MST has one fewer edge than reachable nodes");

        check(
            tree.Unreachable.SequenceEqual(new[] { "ISOLATED" }),
            "MST reports disconnected online nodes");

        check(
            tree.OfflineNodes.Length == 0,
            "online disconnected nodes are not labelled offline");

        graph.SetOnline("B", false);

        var rerouted = graph.MinimumSpanningTree("GATEWAY");

        check(
            rerouted.TotalCost == 12 &&
            rerouted.Links.Length == 2 &&
            rerouted.Links.All(link =>
                link.From != "B" && link.To != "B"),
            "Prim recalculates connections around an offline node");

        check(
            rerouted.OfflineNodes.Contains("B") &&
            !rerouted.Unreachable.Contains("B"),
            "offline nodes are reported separately");

        graph.SetOnline("B", true);

        check(
            graph.MinimumSpanningTree("GATEWAY").TotalCost == 6,
            "restoring a node restores the cheaper MST");

        graph.SetOnline("GATEWAY", false);

        var gatewayDown = graph.MinimumSpanningTree("GATEWAY");

        check(
            gatewayDown.Links.Length == 0 &&
            gatewayDown.Reachable.Length == 0 &&
            gatewayDown.Unreachable.Length == 4,
            "an offline gateway has no reachable mesh nodes");

        reject(
            () => graph.MinimumSpanningTree("UNKNOWN"),
            "MST rejects an unknown root");

        var single = new MeshGraph();
        single.AddNode("GATEWAY");

        var singleTree = single.MinimumSpanningTree("GATEWAY");

        check(
            singleTree.TotalCost == 0 &&
            singleTree.Links.Length == 0 &&
            singleTree.Reachable.Length == 1,
            "a single-node mesh needs no connections");
    }
}