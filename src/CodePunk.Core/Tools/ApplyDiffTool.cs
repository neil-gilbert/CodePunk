using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using CodePunk.Core.Services;

namespace CodePunk.Core.Tools;


public class ApplyDiffTool : ITool
{
    public string Name => "apply_diff";
    public string Description => "Apply a unified diff patch to a text file. Minimizes tokens by sending only the changes.";
    public JsonElement Parameters => JsonDocument.Parse(@"{
      ""type"": ""object"",
      ""properties"": {
        ""filePath"": { ""type"": ""string"", ""description"": ""Relative path to the file to patch."" },
        ""originalHash"": { ""type"": ""string"", ""description"": ""SHA256 hash of the original file (optional)."" },
        ""patchFormat"": { ""type"": ""string"", ""enum"": [""unified"", ""hunks""], ""default"": ""unified"" },
        ""patch"": { ""type"": ""string"", ""description"": ""Unified diff text."" },
        ""maxRejects"": { ""type"": ""integer"", ""default"": 0 },
        ""strategy"": { ""type"": ""string"", ""enum"": [""strict"", ""best-effort""], ""default"": ""strict"" },
        ""createIfMissing"": { ""type"": ""boolean"", ""default"": false },
        ""expectedNewHash"": { ""type"": ""string"", ""description"": ""Expected SHA256 hash after patch (optional)."" }
      },
      ""required"": [""filePath"", ""patch""],
      ""additionalProperties"": false
    }").RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var filePath = arguments.GetProperty("filePath").GetString() ?? string.Empty;
        var patch = arguments.GetProperty("patch").GetString() ?? string.Empty;
        var strategy = arguments.TryGetProperty("strategy", out var strat) ? strat.GetString() : "strict";
        var createIfMissing = arguments.TryGetProperty("createIfMissing", out var cim) && cim.GetBoolean();
        var maxRejects = arguments.TryGetProperty("maxRejects", out var mr) ? mr.GetInt32() : 0;
        var patchFormat = arguments.TryGetProperty("patchFormat", out var pf) ? pf.GetString() : "unified";
        var originalHash = arguments.TryGetProperty("originalHash", out var oh) ? oh.GetString() : null;
        var expectedNewHash = arguments.TryGetProperty("expectedNewHash", out var enh) ? enh.GetString() : null;

        var validation = ValidateFilePath(filePath);
        if (validation != null) return validation;
        var fullPath = Path.GetFullPath(filePath, Directory.GetCurrentDirectory());
        var fileExists = File.Exists(fullPath);
    if (!fileExists) {
      if (!createIfMissing)
        return Error("File does not exist and createIfMissing is false.");
      // Create an empty file if createIfMissing is true
      await File.WriteAllTextAsync(fullPath, string.Empty, cancellationToken);
      fileExists = true;
    }
        if (fileExists)
        {
            var fi = new FileInfo(fullPath);
            var maxSize = GetEnvInt("CODEPUNK_DIFF_MAX_FILE_SIZE", 200_000);
            if (fi.Length > maxSize)
                return Error($"File too large (>" + maxSize + " bytes).");
            if (await IsBinaryFile(fullPath, cancellationToken))
                return Error("File appears to be binary.");
        }
        var original = fileExists ? await File.ReadAllTextAsync(fullPath, cancellationToken) : string.Empty;
        var originalLF = NormalizeLineEndings(original);
        var origHash = Sha256Hex(originalLF);
        if (!string.IsNullOrEmpty(originalHash) && !origHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase))
        {
            if (strategy == "strict") return Error("Original hash mismatch.");
        }
        var maxPatch = GetEnvInt("CODEPUNK_DIFF_MAX_PATCH", 100_000);
        if (patch.Length > maxPatch)
            return Error($"Patch too large (>" + maxPatch + " chars).");

    var patchResult = ApplyPatch(originalLF, patch, patchFormat, maxRejects, strategy);
    if (patchResult.IsError)
      return Error(patchResult.ErrorMessage ?? "Patch failed");

        var tmp = fullPath + ".tmp";
        await File.WriteAllTextAsync(tmp, patchResult.NewContent, cancellationToken);
        try { File.Replace(tmp, fullPath, null); }
        catch { File.Copy(tmp, fullPath, true); File.Delete(tmp); }

        var newHash = Sha256Hex(patchResult.NewContent);
        if (!string.IsNullOrEmpty(expectedNewHash) && !newHash.Equals(expectedNewHash, StringComparison.OrdinalIgnoreCase))
        {
            if (strategy == "strict") return Error("New hash mismatch after patch.");
        }
        var tokensSaved = (originalLF.Length + patchResult.NewContent.Length - patch.Length) / 4;
        return new ToolResult
        {
            Content = $"Patch applied. {patchResult.Message} Rejected hunks: {patchResult.RejectedHunks}. Tokens saved (est): {tokensSaved}.",
            IsError = false
        };
    }

    private static ToolResult? ValidateFilePath(string filePath)
    {
        if (filePath.Contains("..") || filePath.StartsWith("/") || filePath.StartsWith("\\"))
            return Error("Invalid file path.");
        var fullPath = Path.GetFullPath(filePath, Directory.GetCurrentDirectory());
        if (!fullPath.StartsWith(Directory.GetCurrentDirectory()))
            return Error("File path outside workspace.");
        return null;
    }

    private static async Task<bool> IsBinaryFile(string path, CancellationToken cancellationToken)
    {
        using var fs = File.OpenRead(path);
        var buf = new byte[1024];
        int read = await fs.ReadAsync(buf, 0, buf.Length, cancellationToken);
        for (int i = 0; i < read; i++) if (buf[i] == 0) return true;
        return false;
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static PatchResult ApplyPatch(string original, string patch, string patchFormat, int maxRejects, string strategy)
    {
        if (patchFormat != "unified")
            return PatchResult.Error("Only unified diff format is supported in this version.");
        try
        {
            var diff = UnifiedDiff.Parse(patch);
            var applier = new UnifiedDiffApplier();
            var applyResult = applier.Apply(original, diff, maxRejects, strategy == "strict");
            if (!applyResult.Applied)
                return PatchResult.Error(applyResult.Message);
            return PatchResult.Success(applyResult.NewText, applyResult.RejectedHunks, applyResult.Message);
        }
        catch (Exception ex)
        {
            return PatchResult.Error($"Patch parse/apply error: {ex.Message}");
        }
    }

    private static ToolResult Error(string msg) => new ToolResult { Content = msg, IsError = true, ErrorMessage = msg };
    private static int GetEnvInt(string name, int def) => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : def;
    private static string Sha256Hex(string text)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = sha.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private class PatchResult
    {
        public bool IsError { get; private set; }
        public string? ErrorMessage { get; private set; }
        public string NewContent { get; private set; } = string.Empty;
        public int RejectedHunks { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public static PatchResult Success(string newContent, int rejected, string message) => new PatchResult { NewContent = newContent, RejectedHunks = rejected, Message = message };
        public static PatchResult Error(string msg) => new PatchResult { IsError = true, ErrorMessage = msg };
    }


  private class UnifiedDiff
  {
    public List<Hunk> Hunks { get; } = new();
    public static UnifiedDiff Parse(string diff)
    {
      var lines = diff.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
      var ud = new UnifiedDiff();
      Hunk? h = null;
      foreach (var line in lines)
      {
        if (line.StartsWith("@@ "))
        {
          if (h != null) ud.Hunks.Add(h);
          h = Hunk.ParseHeader(line);
        }
        else if (h != null)
        {
          h.Lines.Add(line);
        }
      }
      if (h != null) ud.Hunks.Add(h);
      return ud;
    }
    public class Hunk
    {
      public int StartOld, CountOld, StartNew, CountNew;
      public List<string> Lines = new();
      public static Hunk ParseHeader(string header)
      {
        var parts = header.Split(' ');
        var oldPart = parts[1].TrimStart('-').Split(',');
        var newPart = parts[2].TrimStart('+').Split(',');
        return new Hunk
        {
          StartOld = int.Parse(oldPart[0]),
          CountOld = int.Parse(oldPart.Length > 1 ? oldPart[1] : "1"),
          StartNew = int.Parse(newPart[0]),
          CountNew = int.Parse(newPart.Length > 1 ? newPart[1] : "1"),
        };
      }
    }
  }

  private class UnifiedDiffApplier
  {
    public (bool Applied, string NewText, int RejectedHunks, string Message) Apply(string original, UnifiedDiff diff, int maxRejects, bool strict)
    {
      var origLines = original.Split('\n').ToList();
      int rejected = 0;
      foreach (var h in diff.Hunks)
      {
        int idx = h.StartOld - 1;
        int origIdx = idx;
        bool contextOk = true;
        var toRemove = new List<int>();
        var toAdd = new List<(int, string)>();
        foreach (var l in h.Lines)
        {
          if (l.StartsWith("-"))
          {
            if (origIdx >= origLines.Count || origLines[origIdx] != l.Substring(1))
            {
              contextOk = false; break;
            }
            toRemove.Add(origIdx);
            origIdx++;
          }
          else if (l.StartsWith("+"))
          {
            toAdd.Add((origIdx, l.Substring(1)));
          }
          else if (l.StartsWith(" "))
          {
            if (origIdx >= origLines.Count || origLines[origIdx] != l.Substring(1))
            {
              contextOk = false; break;
            }
            origIdx++;
          }
        }
        if (!contextOk)
        {
          rejected++;
          if (strict || rejected > maxRejects)
            return (false, original, rejected, "Context mismatch");
          continue;
        }
        // Remove lines first
        foreach (var r in toRemove.OrderByDescending(x => x))
          if (r < origLines.Count) origLines.RemoveAt(r);
        // Insert lines, adjusting for prior removals
        int insertOffset = 0;
        foreach (var (pos, val) in toAdd)
        {
          int insertAt = pos;
          foreach (var removed in toRemove)
            if (removed < pos) insertAt--;
          if (insertAt <= origLines.Count) origLines.Insert(insertAt, val);
          else origLines.Add(val);
          insertOffset++;
        }
      }
      return (true, string.Join("\n", origLines), rejected, "All hunks applied.");
    }
  }
}
