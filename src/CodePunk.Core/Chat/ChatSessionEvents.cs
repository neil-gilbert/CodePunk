using System.Threading.Channels;

namespace CodePunk.Core.Chat;

public enum ChatSessionEventType
{
    MessageStart,
    StreamDelta,
    MessageComplete,
    ToolIterationStart,
    ToolIterationEnd,
    ToolLoopExceeded,
    SessionCleared
}

public record ChatSessionEvent(
    ChatSessionEventType Type,
    string? SessionId = null,
    int? Iteration = null,
    string? Delta = null,
    bool? IsFinal = null,
    DateTime? Utc = null)
{
    public DateTime Timestamp => Utc ?? DateTime.UtcNow;
}

public interface IChatSessionEventStream
{
    ChannelReader<ChatSessionEvent> Reader { get; }
    bool TryWrite(ChatSessionEvent evt);
}

public class ChatSessionEventStream : IChatSessionEventStream
{
    private readonly Channel<ChatSessionEvent> _channel;

    public ChatSessionEventStream()
    {
        _channel = Channel.CreateUnbounded<ChatSessionEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            AllowSynchronousContinuations = true
        });
    }

    public ChannelReader<ChatSessionEvent> Reader => _channel.Reader;

    public bool TryWrite(ChatSessionEvent evt)
    {
        return _channel.Writer.TryWrite(evt);
    }
}