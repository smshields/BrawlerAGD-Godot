using System.Text.Json;
using BrawlerSim.Sim;

namespace BrawlerSim.Replay;

/// <summary>
/// Compact JSON for input traces: {"Players":2,"Ticks":[[h,v,j,a0,a1,a2,a3, ...], ...]}
/// — one flat array per tick, seven values per player (horizontal, vertical, jump,
/// action buttons 0–3, booleans as 0/1). Keys are PascalCase — the format every trace
/// on disk already uses (no naming policy is applied, deliberately kept that way).
///
/// 2026-07-08 multi-move controls: the format grew from 3 values per player
/// (h, jump, attack) to 7. Legacy rows are detected by length and upgraded on read —
/// attack maps to action button 0 (which triggers move 0 on every pre-feature genome)
/// and vertical to 0, so old traces replay with identical behavior. Old traces are
/// research artifacts; this reader must keep accepting them.
///
/// 2026-07-20 five buttons: 7 → 8 values per player. Legacy-7 rows upgrade with
/// a0..a2 in place and OLD BUTTON 3 → NEW BUTTON 4, mirroring the buttonMoves
/// migration (old last button = R1/L keeps its move at the new last index), so
/// v≤5 game + trace pairs replay bit-identically.
/// </summary>
public static class InputTraceJson
{
    private const int ValuesPerPlayer = 8;
    private const int FourButtonValuesPerPlayer = 7;
    private const int LegacyValuesPerPlayer = 3;

    public static string Serialize(InputTrace trace)
    {
        int players = trace.PlayerCount;
        var ticks = new List<float[]>(trace.TickCount);
        for (int t = 0; t < trace.TickCount; t++)
        {
            float[] row = new float[players * ValuesPerPlayer];
            for (int p = 0; p < players; p++)
            {
                InputFrame frame = trace.Get(t, p);
                int o = p * ValuesPerPlayer;
                row[o] = frame.Horizontal;
                row[o + 1] = frame.Vertical;
                row[o + 2] = frame.Jump ? 1f : 0f;
                for (int b = 0; b < InputFrame.ActionCount; b++)
                {
                    row[o + 3 + b] = frame.ActionPressed(b) ? 1f : 0f;
                }
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
                frame[p] = ReadFrame(row, p, doc.Players);
            }
            trace.Record(frame);
        }
        return trace;
    }

    private static InputFrame ReadFrame(float[] row, int player, int players)
    {
        if (row.Length == players * ValuesPerPlayer)
        {
            int o = player * ValuesPerPlayer;
            byte actions = 0;
            for (int b = 0; b < InputFrame.ActionCount; b++)
            {
                if (row[o + 3 + b] != 0f)
                {
                    actions |= InputFrame.ActionBit(b);
                }
            }
            return new InputFrame(row[o], row[o + 1], row[o + 2] != 0f, actions);
        }
        if (row.Length == players * FourButtonValuesPerPlayer)
        {
            // 2026-07-08..07-20 era: four buttons. Old button 3 was R1/L, whose move
            // migrated to index 4 — route the press with it.
            int o = player * FourButtonValuesPerPlayer;
            byte actions = 0;
            for (int b = 0; b < 3; b++)
            {
                if (row[o + 3 + b] != 0f)
                {
                    actions |= InputFrame.ActionBit(b);
                }
            }
            if (row[o + 6] != 0f)
            {
                actions |= InputFrame.ActionBit(4);
            }
            return new InputFrame(row[o], row[o + 1], row[o + 2] != 0f, actions);
        }
        if (row.Length == players * LegacyValuesPerPlayer)
        {
            int o = player * LegacyValuesPerPlayer;
            return new InputFrame(
                row[o], 0f, row[o + 1] != 0f,
                row[o + 2] != 0f ? InputFrame.ActionBit(0) : (byte)0);
        }
        throw new JsonException(
            $"trace row has {row.Length} values for {players} players — expected " +
            $"{players * ValuesPerPlayer} (current), {players * FourButtonValuesPerPlayer} (4-button era), " +
            $"or {players * LegacyValuesPerPlayer} (legacy).");
    }

    public static void Save(InputTrace trace, string path) => File.WriteAllText(path, Serialize(trace));

    public static InputTrace Load(string path) => Deserialize(File.ReadAllText(path));

    private sealed class TraceDoc
    {
        public int Players { get; set; }
        public List<float[]>? Ticks { get; set; }
    }
}
