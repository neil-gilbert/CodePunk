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
        ""expectedNewHash"": { ""type"": ""string"", ""description"": ""Expected SHA256 hash after patch (optional)."" },
        ""dryRun"": { ""type"": ""boolean"", ""default"": false, ""description"": ""If true, validate and compute result but do not write file."" },
        ""contextScanRadius"": { ""type"": ""integer"", ""default"": 12, ""description"": ""Lines above/below original hunk start to scan when context mismatch occurs (best-effort only)."" }
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
  var dryRun = arguments.TryGetProperty("dryRun", out var dr) && dr.GetBoolean();
  var contextScanRadius = arguments.TryGetProperty("contextScanRadius", out var csr) ? csr.GetInt32() : GetEnvInt("CODEPUNK_DIFF_FUZZ_RADIUS", 12);

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

  var patchResult = ApplyPatch(originalLF, patch, patchFormat ?? "unified", maxRejects, strategy ?? "strict", dryRun, contextScanRadius);
    if (patchResult.IsError)
      return Error(patchResult.ErrorMessage ?? "Patch failed");
    if (!dryRun)
    {
      var tmp = fullPath + ".tmp";
      await File.WriteAllTextAsync(tmp, patchResult.NewContent, cancellationToken);
      try { File.Replace(tmp, fullPath, null); }
      catch { File.Copy(tmp, fullPath, true); File.Delete(tmp); }
    }

        var newHash = Sha256Hex(patchResult.NewContent);
        if (!string.IsNullOrEmpty(expectedNewHash) && !newHash.Equals(expectedNewHash, StringComparison.OrdinalIgnoreCase))
        {
            if (strategy == "strict") return Error("New hash mismatch after patch.");
        }
    var tokensSavedRaw = (originalLF.Length + patchResult.NewContent.Length - patch.Length) / 4;
    if (tokensSavedRaw < 0) tokensSavedRaw = 0;
    var details = patchResult.Details?.Count > 0 ? string.Join(" | ", patchResult.Details) : string.Empty;
        return new ToolResult
        {
      Content = $"Patch {(dryRun ? "validated" : "applied")}. {patchResult.Message} Rejected hunks: {patchResult.RejectedHunks}. Tokens saved (est): {tokensSavedRaw}." + (string.IsNullOrEmpty(details) ? string.Empty : " Details: " + details),
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

  private static PatchResult ApplyPatch(string original, string patch, string patchFormat, int maxRejects, string strategy, bool dryRun, int contextScanRadius)
    {
        if (patchFormat != "unified")
            return PatchResult.Error("Only unified diff format is supported in this version.");
        try
        {
            var diff = UnifiedDiff.Parse(patch);
      var applier = new UnifiedDiffApplier();
      var applyResult = applier.Apply(original, diff, maxRejects, strategy == "strict", dryRun, contextScanRadius);
            if (!applyResult.Applied)
                return PatchResult.Error(applyResult.Message);
      return PatchResult.Success(applyResult.NewText, applyResult.RejectedHunks, applyResult.Message, applyResult.HunkMessages);
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
    public List<string>? Details { get; private set; }
    public static PatchResult Success(string newContent, int rejected, string message, List<string>? details) => new PatchResult { NewContent = newContent, RejectedHunks = rejected, Message = message, Details = details };
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
    public class ApplyResult
    {
      public bool Applied { get; set; }
      public string NewText { get; set; } = string.Empty;
      public int RejectedHunks { get; set; }
      public string Message { get; set; } = string.Empty;
      public List<string> HunkMessages { get; set; } = new();
    }
    public ApplyResult Apply(string original, UnifiedDiff diff, int maxRejects, bool strict, bool dryRun, int contextScanRadius)
    {
      var origLinesWorking = original.Split('\n').ToList();
      var origLinesForDryRun = original.Split('\n').ToList();
      int rejected = 0;
      var hunkReports = new List<string>();
      int hunkIndex = 0;
      foreach (var h in diff.Hunks)
      {
        hunkIndex++;
        bool applied = TryApplyHunk(origLinesWorking, h, strict, ref rejected, maxRejects, contextScanRadius, out var report, out var fatal, dryRun);
        hunkReports.Add($"hunk {hunkIndex}:{report}");
        if (fatal)
        {
          return new ApplyResult { Applied = false, NewText = original, RejectedHunks = rejected, Message = report, HunkMessages = hunkReports };
        }
      }
      var finalLines = dryRun ? origLinesForDryRun : origLinesWorking;
      return new ApplyResult { Applied = true, NewText = string.Join("\n", finalLines), RejectedHunks = rejected, Message = "Completed", HunkMessages = hunkReports };
    }
    private bool TryApplyHunk(List<string> lines, UnifiedDiff.Hunk h, bool strict, ref int rejected, int maxRejects, int radius, out string report, out bool fatal, bool dryRun)
    {
      fatal = false;
      if (AttemptAt(lines, h, h.StartOld - 1, dryRun, out report)) return true;
      if (strict)
      {
        rejected++;
        if (strict || rejected > maxRejects) { fatal = true; report = "context mismatch strict"; }
        return false;
      }
      int startIdx = Math.Max(0, h.StartOld - 1 - radius);
      int endIdx = Math.Min(lines.Count - 1, h.StartOld - 1 + radius);
      for (int candidate = startIdx; candidate <= endIdx; candidate++)
      {
        if (candidate == h.StartOld - 1) continue;
        if (AttemptAt(lines, h, candidate, dryRun, out _))
        {
          report = $"relocated from {h.StartOld} to {candidate + 1}";
          return true;
        }
      }
      rejected++;
      report = "rejected (no match in radius)";
      if (rejected > maxRejects) { fatal = true; }
      return false;
    }
    private bool AttemptAt(List<string> lines, UnifiedDiff.Hunk h, int idx, bool dryRun, out string report)
    {
      int origIdx = idx;
      if (origIdx < 0) { report = "invalid start"; return false; }
      var toRemove = new List<int>();
      var toAdd = new List<(int, string)>();
      foreach (var l in h.Lines)
      {
        if (l.StartsWith("-"))
        {
          if (origIdx >= lines.Count || lines[origIdx] != l.Substring(1)) { report = "delete mismatch"; return false; }
          toRemove.Add(origIdx);
          origIdx++;
        }
        else if (l.StartsWith("+"))
        {
          toAdd.Add((origIdx, l.Substring(1)));
        }
        else if (l.StartsWith(" "))
        {
          if (origIdx >= lines.Count || lines[origIdx] != l.Substring(1)) { report = "context line mismatch"; return false; }
          origIdx++;
        }
      }
      if (!dryRun)
      {
        foreach (var r in toRemove.OrderByDescending(x => x)) if (r < lines.Count) lines.RemoveAt(r);
        foreach (var (pos, val) in toAdd)
        {
          int insertAt = pos;
          foreach (var removed in toRemove) if (removed < pos) insertAt--;
          if (insertAt <= lines.Count) lines.Insert(insertAt, val); else lines.Add(val);
        }
      }
      report = idx == h.StartOld - 1 ? "applied" : "relocated";
      return true;
    }
  }
}
