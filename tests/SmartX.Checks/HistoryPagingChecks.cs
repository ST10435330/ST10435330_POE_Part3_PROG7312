using System.Text.Json;
using SmartX;

public static class HistoryPagingChecks
{
    public static void Run(
        Gateway gateway,
        Action<bool, string> check,
        Action<Action, string> reject)
    {
        var sensor = gateway.Register(new(
            "HISTORY-PAGING-CHECK",
            "Environmental",
            ["Facility A", "Zone 1", "Sub-Zone B"],
            3600));

        var engine = new RealtimeEngine(gateway);
        var start = DateTimeOffset.UtcNow.AddSeconds(-10);

        for (int i = 0; i < 6; i++)
        {
            engine.Enqueue(new FloatWork(new(
                sensor.Id, start.AddMilliseconds(i * 100), 40 + i)));
        }

        check(engine.Process(6) == 6, "history paging fixture is processed");

        JsonElement Query(
            int take,
            int skip = 0,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null) => JsonSerializer.SerializeToElement(
                engine.History(sensor.Id, from, to, take, skip),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        double[] Values(JsonElement result) => result.GetProperty("records")
            .EnumerateArray()
            .Select(row => row.GetProperty("numericValue").GetDouble())
            .ToArray();

        var latest = Query(2);
        check(
            Values(latest).SequenceEqual(new double[] { 44, 45 }) &&
            latest.GetProperty("matched").GetInt32() == 6 &&
            latest.GetProperty("hasOlder").GetBoolean() &&
            !latest.GetProperty("hasNewer").GetBoolean(),
            "default history page retains chronological latest-reading behaviour");

        check(
            Values(Query(2, 2)).SequenceEqual(new double[] { 42, 43 }),
            "older history pages do not overlap the latest page");

        var oldest = Query(2, 4);
        check(
            Values(oldest).SequenceEqual(new double[] { 40, 41 }) &&
            !oldest.GetProperty("hasOlder").GetBoolean() &&
            oldest.GetProperty("hasNewer").GetBoolean(),
            "oldest history page has correct navigation metadata");

        var filtered = Query(
            2, 2, start.AddMilliseconds(100), start.AddMilliseconds(400));
        check(
            Values(filtered).SequenceEqual(new double[] { 41, 42 }) &&
            filtered.GetProperty("matched").GetInt32() == 4,
            "paging is applied after inclusive AVL range filtering");

        check(
            Values(Query(2, 0, start.AddMilliseconds(200), start.AddMilliseconds(200)))
                .SequenceEqual(new double[] { 42 }),
            "equal range boundaries retrieve an exact timestamp");

        check(
            Values(Query(2, 100)).Length == 0 &&
            Query(0).GetProperty("take").GetInt32() == 1 &&
            Values(Query(2, -10)).SequenceEqual(new double[] { 44, 45 }),
            "history handles out-of-range offsets and normalises page limits");

        check(
            Query(2, 0, start.AddSeconds(2), start.AddSeconds(3))
                .GetProperty("matched").GetInt32() == 0,
            "empty ranges report zero matches");

        reject(
            () => engine.History(sensor.Id, start.AddSeconds(1), start),
            "paged history rejects inverted date ranges");
    }
}
