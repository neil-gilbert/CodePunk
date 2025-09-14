using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using Xunit;

namespace CodePunk.Core.Tests.Services;

public class HeuristicSessionSummarizerTests
{
    [Fact]
    public async Task Summarize_ReturnsNull_OnEmptySession()
    {
        var repo = new FakeMessageRepository(new List<Message>());
        var sut = new HeuristicSessionSummarizer(repo);
        var res = await sut.SummarizeAsync("sess-1", new SessionSummaryOptions { MaxMessages = 10 }, CancellationToken.None);
        Assert.Null(res);
    }

    [Fact]
    public async Task Summarize_ReturnsNull_OnAssistantOnly()
    {
        var msgs = new List<Message>
        {
            Message.Create("s1", MessageRole.Assistant, new []{ new TextPart("I will do that") }),
            Message.Create("s1", MessageRole.Assistant, new []{ new TextPart("Done") })
        };
        var repo = new FakeMessageRepository(msgs);
        var sut = new HeuristicSessionSummarizer(repo);
        var res = await sut.SummarizeAsync("s1", new SessionSummaryOptions { MaxMessages = 10 }, CancellationToken.None);
        Assert.Null(res);
    }

    [Fact]
    public async Task Summarize_Extracts_Paths_From_CodeFence_And_Truncates()
    {
        var msgs = new List<Message>();
        msgs.AddRange(Enumerable.Range(0,50).Select(i => Message.Create("s1", MessageRole.User, new []{ new TextPart("user message " + i) })));
        msgs.Add(Message.Create("s1", MessageRole.User, new []{ new TextPart("Please update src/Service.cs and src/Helper.cs\n```\nsrc/Other.cs\n```") }));
        var repo = new FakeMessageRepository(msgs);
        var sut = new HeuristicSessionSummarizer(repo);
        var res = await sut.SummarizeAsync("s1", new SessionSummaryOptions { MaxMessages = 5 }, CancellationToken.None);
        Assert.NotNull(res);
        Assert.True(res.Truncated);
        Assert.Contains("src/Service.cs", res.CandidateFiles);
        Assert.Contains("src/Helper.cs", res.CandidateFiles);
        Assert.Contains("src/Other.cs", res.CandidateFiles);
    }

    [Fact]
    public async Task Summarize_Includes_Tool_Messages_When_Requested()
    {
        var msgs = new List<Message>
        {
            Message.Create("s1", MessageRole.User, new []{ new TextPart("Please update README.md") }),
            Message.Create("s1", MessageRole.Tool, new []{ new ToolResultPart("t1","Tool produced content") }),
        };
        var repo = new FakeMessageRepository(msgs);
        var sut = new HeuristicSessionSummarizer(repo);
        var res = await sut.SummarizeAsync("s1", new SessionSummaryOptions { MaxMessages = 10, IncludeToolMessages = true }, CancellationToken.None);
        Assert.NotNull(res);
        Assert.Contains("README.md", res.CandidateFiles);
    }

    [Fact]
    public async Task Summarize_InfersGoal_And_ExtractsFiles()
    {
        var msgs = new List<Message>
        {
            Message.Create("s1", MessageRole.User, new []{ new TextPart("Please add a new endpoint in src/Api/Controller.cs") }),
            Message.Create("s1", MessageRole.Assistant, new []{ new TextPart("Sure, I will scaffold it") }),
            Message.Create("s1", MessageRole.User, new []{ new TextPart("Also update tests/TestApi.cs") })
        };

        var repo = new FakeMessageRepository(msgs);
        var sut = new HeuristicSessionSummarizer(repo);
        var res = await sut.SummarizeAsync("s1", new SessionSummaryOptions { MaxMessages = 10 }, CancellationToken.None);
        Assert.NotNull(res);
    var g = res.Goal.ToLower();
    Assert.True(g.Contains("add") || g.Contains("update"));
        Assert.Contains("src/Api/Controller.cs", res.CandidateFiles);
        Assert.Contains("tests/TestApi.cs", res.CandidateFiles);
    }

    class FakeMessageRepository : IMessageRepository
    {
        private readonly IReadOnlyList<Message> _messages;
        public FakeMessageRepository(IReadOnlyList<Message> messages) => _messages = messages;
        public Task<Message?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Message?>(null);
        public Task<IReadOnlyList<Message>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult(_messages);
        public Task<Message> CreateAsync(Message message, CancellationToken cancellationToken = default) => Task.FromResult(message);
        public Task<Message> UpdateAsync(Message message, CancellationToken cancellationToken = default) => Task.FromResult(message);
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteBySessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
