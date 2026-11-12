using System;
using System.Collections.Generic;

namespace SmartX.DataStructures;

public sealed class AvlTree<TKey, TValue>
    where TKey : IComparable<TKey>
{
    public bool ContainsKey(TKey key)
{
    Node? current = root;

    while (current is not null)
    {
        int comparison = key.CompareTo(current.Key);

        if (comparison == 0)
        {
            return true;
        }

        current = comparison < 0
            ? current.Left
            : current.Right;
    }

    return false;
}

public IEnumerable<TValue> Values => Enumerate(root);

private static IEnumerable<TValue> Enumerate(Node? node)
{
    if (node is null)
    {
        yield break;
    }

    foreach (TValue value in Enumerate(node.Left))
    {
        yield return value;
    }

    yield return node.Value;

    foreach (TValue value in Enumerate(node.Right))
    {
        yield return value;
    }
}
    
    private Node? root;

    public int Count { get; private set; }

    public int Height => GetHeight(root);

public bool RemoveMinimum()
{
    if (root is null)
    {
        return false;
    }

    root = RemoveMinimumNode(root);
    Count--;

    return true;
}

private static Node? RemoveMinimumNode(Node node)
{
    if (node.Left is null)
    {
        return node.Right;
    }

    node.Left = RemoveMinimumNode(node.Left);

    UpdateHeight(node);

    return Rebalance(node);
}

    private sealed class Node
    {
        public TKey Key;
        public TValue Value;

        public Node? Left;
        public Node? Right;

        public int Height = 1;

        public Node(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }

    public void Insert(TKey key, TValue value)
    {
        root = InsertNode(root, key, value);
    }

    public IReadOnlyList<TValue> Range(TKey from, TKey to)
    {
        if (from.CompareTo(to) > 0)
        {
            throw new ArgumentException(
                "The start of the range must be before or equal to the end.");
        }

        var results = new List<TValue>();

        CollectRange(root, from, to, results);

        return results;
    }

    private Node InsertNode(Node? node, TKey key, TValue value)
    {
        if (node is null)
        {
            Count++;
            return new Node(key, value);
        }

        int comparison = key.CompareTo(node.Key);

        if (comparison < 0)
        {
            node.Left = InsertNode(node.Left, key, value);
        }
        else if (comparison > 0)
        {
            node.Right = InsertNode(node.Right, key, value);
        }
        else
        {
            // An existing key receives the updated value.
            node.Value = value;
            return node;
        }

        UpdateHeight(node);

        return Rebalance(node);
    }

    private static void CollectRange(
        Node? node,
        TKey from,
        TKey to,
        List<TValue> results)
    {
        if (node is null)
        {
            return;
        }

        int fromComparison = from.CompareTo(node.Key);
        int toComparison = to.CompareTo(node.Key);

        // Only search the left branch if it could contain matches.
        if (fromComparison < 0)
        {
            CollectRange(node.Left, from, to, results);
        }

        if (fromComparison <= 0 && toComparison >= 0)
        {
            results.Add(node.Value);
        }

        // Only search the right branch if it could contain matches.
        if (toComparison > 0)
        {
            CollectRange(node.Right, from, to, results);
        }
    }

    private static int GetHeight(Node? node)
    {
        return node?.Height ?? 0;
    }

    private static int GetBalance(Node node)
    {
        return GetHeight(node.Left) - GetHeight(node.Right);
    }

    private static void UpdateHeight(Node node)
    {
        node.Height = 1 + Math.Max(
            GetHeight(node.Left),
            GetHeight(node.Right));
    }

    private static Node Rebalance(Node node)
    {
        int balance = GetBalance(node);

        if (balance > 1)
        {
            Node left = node.Left!;

            if (GetBalance(left) < 0)
            {
                node.Left = RotateLeft(left);
            }

            return RotateRight(node);
        }

        if (balance < -1)
        {
            Node right = node.Right!;

            if (GetBalance(right) > 0)
            {
                node.Right = RotateRight(right);
            }

            return RotateLeft(node);
        }

        return node;
    }

    private static Node RotateRight(Node node)
    {
        Node newRoot = node.Left!;
        Node? middleBranch = newRoot.Right;

        newRoot.Right = node;
        node.Left = middleBranch;

        UpdateHeight(node);
        UpdateHeight(newRoot);

        return newRoot;
    }

    private static Node RotateLeft(Node node)
    {
        Node newRoot = node.Right!;
        Node? middleBranch = newRoot.Left;

        newRoot.Left = node;
        node.Right = middleBranch;

        UpdateHeight(node);
        UpdateHeight(newRoot);

        return newRoot;
    }
}