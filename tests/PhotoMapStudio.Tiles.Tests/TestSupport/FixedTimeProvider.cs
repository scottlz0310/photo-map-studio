namespace PhotoMapStudio.Tiles.Tests.TestSupport;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "テストから生成される")]
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset current = now;
    public void Advance(TimeSpan elapsed) => this.current += elapsed;
    public override DateTimeOffset GetUtcNow() => this.current;
    public override long GetTimestamp() => this.current.UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
}
