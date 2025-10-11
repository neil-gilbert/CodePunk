using System.Runtime.CompilerServices;

namespace CodePunk.Core.Tests.Shared;

/// <summary>
/// Provides a controllable time source for tests.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _current = DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow() => _current;

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void Advance(TimeSpan interval)
    {
        _current = _current.Add(interval);
    }
}
