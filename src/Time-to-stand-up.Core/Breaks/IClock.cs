namespace TimeToStandUp.Core;

/// <summary>可注入的時鐘,讓時間相關邏輯可以用假時鐘測試。</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
