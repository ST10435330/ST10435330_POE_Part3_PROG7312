using SmartX.DataStructures;

namespace SmartX;

public sealed partial class RealtimeEngine
{
    private readonly Dictionary<
        Guid,
        AvlTree<DateTimeOffset, StreamLog>> timelines = new();

    private readonly Queue<StreamLog> processingOrder = new();

    private long logSequence;

    private void RecordProcessed(QueuedWork item)
    {
        var work = item.Work;

        var row = new StreamLog(
            ++logSequence,
            work.SensorId,
            devices[work.SensorId].Sensor.Identifier,
            work.Timestamp,
            work.ValueText,
            work.Critical,
            work.Critical ? "Priority" : "FIFO",
            DateTimeOffset.UtcNow,
            work is FloatWork f
                ? f.Packet.Value
                : work is PowerWork p
                    ? p.Packet.Value
                    : null);

        if (!timelines.TryGetValue(work.SensorId, out var timeline))
        {
            timeline = new AvlTree<DateTimeOffset, StreamLog>();
            timelines[work.SensorId] = timeline;
        }

        timeline.Insert(work.Timestamp, row);

        while (timeline.Count > 2048)
        {
            timeline.RemoveMinimum();
        }

        processingOrder.Enqueue(row);

        while (processingOrder.Count > 300)
        {
            processingOrder.Dequeue();
        }
    }

    public object History(
        Guid id,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int take = 120,
        int skip = 0)
    {
        lock (sync)
        {
            gateway.Get(id);

            DateTimeOffset start = from ?? DateTimeOffset.MinValue;
            DateTimeOffset end = to ?? DateTimeOffset.MaxValue;

            if (start > end)
            {
                throw new GatewayException(
                    "Start time must be before end time.");
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();

            timelines.TryGetValue(id, out var timeline);

            IReadOnlyList<StreamLog> matches = timeline is null
                ? Array.Empty<StreamLog>()
                : timeline.Range(start, end);

            int limit = Math.Clamp(take, 1, 500);
            int offset = Math.Max(0, skip);
            int endIndex = Math.Max(0, matches.Count - offset);
            int startIndex = Math.Max(0, endIndex - limit);
            StreamLog[] rows = matches
                .Skip(startIndex)
                .Take(endIndex - startIndex)
                .ToArray();

            watch.Stop();

            return new
            {
                records = rows,
                stored = timeline?.Count ?? 0,
                matched = matches.Count,
                take = limit,
                skip = offset,
                hasOlder = startIndex > 0,
                hasNewer = offset > 0 && matches.Count > 0,
                algorithm =
                    "AVL timestamp index; insertion/removal O(log n), " +
                    "range search O(log n + k), where k is matching readings.",
                searchMilliseconds = watch.Elapsed.TotalMilliseconds
            };
        }
    }

    public StreamLog[] RecentProcessing(long after = 0)
    {
        lock (sync)
        {
            return processingOrder
                .Where(row => row.Sequence > after)
                .TakeLast(100)
                .ToArray();
        }
    }
}
