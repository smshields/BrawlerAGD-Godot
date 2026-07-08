using System.Text.Json;
using BrawlerSim.Sim;

namespace BrawlerSim.Replay;

/// <summary>
/// Compact JSON for input traces: {"players":2,"ticks":[[h,j,a, h,j,a], ...]} — one flat
/// array per tick, three values per player (horizontal, jump, attack as 0/1).
/// </summary>
public static class InputTraceJson
{
    public static string Serialize(InputTrace trace)
    {
        int players = trace.PlayerCount;
        var ticks = new List<float[]>(trace.TickCount);
        for (int t = 0; t < trace.TickCount; t++)
        {
            float[] row = new float[players * 3];
            for (int p = 0; p < players; p++)
            {
                InputFrame frame = trace.Get(t, p);
                row[p * 3] = frame.Horizontal;
                row[p * 3 + 1] = frame.Jump ? 1f : 0f;
                row[p * 3 + 2] = frame.Attack ? 1f : 0f;
            }
            ticks.Add(row);
        }
        return JsonSerializer.Serialize(new TraceDoc { Players = players, Ticks = ticks });
    }

    public static InputTrace Deserialize(string json)
    {
        TraceDoc doc = JsonSerializer.Deserialize<TraceDoc>(json)
            ?? throw new JsonException("trace parsed to null");
        var trace = new InputTrace();
        var frame = new InputFrame[doc.Players];
        foreach (float[] row in doc.Ticks ?? new List<float[]>())
        {
            for (int p = 0; p < doc.Players; p++)
            {
                frame[p] = new InputFrame(row[p * 3], row[p * 3 + 1] != 0f, row[p * 3 + 2] != 0f);
            }
            trace.Record(frame);
        }
        return trace;
    }

    public static void Save(InputTrace trace, string path) => File.WriteAllText(path, Serialize(trace));

    public static InputTrace Load(string path) => Deserialize(File.ReadAllText(path));

    private sealed class TraceDoc
    {
        public int Players { get; set; }
        public List<float[]>? Ticks { get; set; }
    }
}
