namespace CodePunk.Console.Planning;

internal static class DiffBuilder
{
    public static string Unified(string before, string after, string path, int context = 3)
    {
        var beforeLines = (before ?? string.Empty).Replace("\r\n","\n").Split('\n');
        var afterLines = (after ?? string.Empty).Replace("\r\n","\n").Split('\n');
        var diffs = new List<string>();
        int i=0,j=0; var hunks = new List<(int aStart,int aLen,int bStart,int bLen,List<string> lines)>();
        while(i<beforeLines.Length || j<afterLines.Length)
        {
            if(i<beforeLines.Length && j<afterLines.Length && beforeLines[i]==afterLines[j]) { i++; j++; continue; }
            int aStart=i,bStart=j; var hunkLines=new List<string>();
            while(i<beforeLines.Length && (j>=afterLines.Length || beforeLines[i]!=afterLines[j])) { hunkLines.Add("-"+beforeLines[i]); i++; }
            while(j<afterLines.Length && (i>=beforeLines.Length || beforeLines[i]!=afterLines[j])) { hunkLines.Add("+"+afterLines[j]); j++; }
            hunks.Add((aStart, i-aStart, bStart, j-bStart, hunkLines));
        }
        if(hunks.Count==0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- a/{path}");
        sb.AppendLine($"+++ b/{path}");
        foreach(var h in hunks)
        {
            int aLine = h.aStart+1; int bLine = h.bStart+1;
            sb.AppendLine($"@@ -{aLine},{h.aLen} +{bLine},{h.bLen} @@");
            foreach(var l in h.lines) sb.AppendLine(l);
        }
        return sb.ToString();
    }
}