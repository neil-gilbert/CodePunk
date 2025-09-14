using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;

namespace CodePunk.Core.Services
{
    public class HeuristicSessionSummarizer : ISessionSummarizer
    {
        private readonly IMessageRepository _messageRepository;

        public HeuristicSessionSummarizer(IMessageRepository messageRepository)
        {
            _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        }

    public async Task<SessionSummary?> SummarizeAsync(string sessionId, SessionSummaryOptions options, CancellationToken ct = default)
        {
            var messages = (await _messageRepository.GetBySessionAsync(sessionId, ct).ConfigureAwait(false))?.ToList() ?? new List<Message>();
            var userAndAssistant = messages.Where(m => m.Role == MessageRole.User || m.Role == MessageRole.Assistant || (options.IncludeToolMessages && m.Role == MessageRole.Tool)).ToList();
            int total = userAndAssistant.Count;
            if (total < 2)
                return null;

            int take = Math.Min(options.MaxMessages, total);
            var sample = userAndAssistant.Skip(Math.Max(0, total - take)).Take(take).ToList();

            string combinedText = string.Join("\n", sample.Select(m => ExtractTextFromMessage(m))).Trim();

            string? goal = InferGoalFromText(sample);
            if (string.IsNullOrWhiteSpace(goal))
                return null;

            var files = ExtractFilePaths(combinedText);

            return new SessionSummary
            {
                Goal = goal,
                CandidateFiles = files.Distinct(StringComparer.OrdinalIgnoreCase).Take(25).ToList(),
                Rationale = BuildRationale(sample, goal),
                Truncated = take < total,
                UsedMessages = take,
                TotalMessages = total
            };
        }

        private string BuildRationale(List<Message> sample, string goal)
        {
            var lastUser = sample.LastOrDefault(m => m.Role == MessageRole.User);
            var lastText = lastUser != null ? ExtractTextFromMessage(lastUser).Replace('\n', ' ') : goal;
            var fileCount = ExtractFilePaths(string.Join("\n", sample.Select(m => ExtractTextFromMessage(m)))).Count;
            return $"Based on recent conversation; last instruction: '{(lastText ?? goal)}'. Candidate files: {fileCount}.";
        }

    private string? InferGoalFromText(List<Message> sample)
        {
            var directives = new[] { "add", "update", "fix", "refactor", "remove", "replace", "implement", "create", "cleanup", "rename" };
            for (int i = sample.Count - 1; i >= 0; i--)
            {
                var m = sample[i];
                if (m.Role != MessageRole.User) continue;
                var text = ExtractTextFromMessage(m).Trim();
                var lower = text.ToLowerInvariant();
                if (directives.Any(d => lower.StartsWith(d + " ") || lower.Contains(" " + d + " ") || lower.Contains(d + " the ") || lower.Contains(d + " this ")))
                    return Shorten(text, 200);
            }

            var last = sample.LastOrDefault(m => m.Role == MessageRole.User);
            if (last == null) return null;
            return Shorten(ExtractTextFromMessage(last).Trim(), 200);
        }

        private string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Length <= max) return text;
            return text.Substring(0, max) + "...";
        }

        private static readonly Regex FilePathRegex = new Regex(@"(?im)\b([A-Za-z0-9_./\\\-]+\.(cs|ts|js|json|md|yml|yaml|toml|csproj))\b", RegexOptions.Compiled);

        private List<string> ExtractFilePaths(string text)
        {
            var matches = FilePathRegex.Matches(text ?? string.Empty);
            var list = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (m.Groups.Count > 1)
                {
                    list.Add(m.Groups[1].Value);
                }
            }
            return list;
        }

        private string ExtractTextFromMessage(Message m)
        {
            if (m?.Parts == null || m.Parts.Count == 0) return string.Empty;
            var texts = new List<string>();
            foreach (var p in m.Parts)
            {
                if (p is TextPart tp)
                    texts.Add(tp.Content);
                else if (p is ToolResultPart trp)
                    texts.Add(trp.Content);
            }
            return string.Join("\n", texts);
        }
    }
}
