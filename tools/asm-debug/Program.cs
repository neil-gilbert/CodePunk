using System;
using System.Text;
using System.Text.Json;
using CodePunk.Console.Utilities;

class Program
{
    static void Main()
    {
        var rationale = "Fix the issue 💡 in code";
        var payload = new { files = new[] { new { path = "a.txt", rationale } } };
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        var emojiUtf8 = Encoding.UTF8.GetBytes("💡");
        int idx = -1;
        for (int i = 0; i <= bytes.Length - emojiUtf8.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < emojiUtf8.Length; j++) { if (bytes[i + j] != emojiUtf8[j]) { match = false; break; } }
            if (match) { idx = i; break; }
        }
        Console.WriteLine($"json={json}");
        Console.WriteLine($"emojiIndex={idx}");
        var split = idx + 1;
        var part1 = new ReadOnlyMemory<byte>(bytes, 0, split);
        var part2 = new ReadOnlyMemory<byte>(bytes, split, bytes.Length - split);
        var asm = new StreamingJsonAssembler(2048);
        asm.Append(part1);
        Console.WriteLine($"After part1: len={asm.CurrentLength}");
        var ok1 = asm.TryGetNext(out var e1, out var r1, out var d1);
        Console.WriteLine($"TryGetNext after part1: ok={ok1} diag={d1} len={asm.CurrentLength}");
        asm.Append(part2);
        Console.WriteLine($"After part2: len={asm.CurrentLength}");
        var snap = asm.DebugSnapshot();
        Console.WriteLine($"Snapshot len={snap.Length} text={Encoding.UTF8.GetString(snap)}");
        var ok2 = asm.TryGetNext(out var e2, out var r2, out var d2);
        Console.WriteLine($"TryGetNext after part2: ok={ok2} diag={d2} len={asm.CurrentLength}");
        if (ok2) Console.WriteLine($"Parsed root kind={e2.ValueKind} rationale={e2.GetProperty("files")[0].GetProperty("rationale")}\nraw={r2}");
    }
}
