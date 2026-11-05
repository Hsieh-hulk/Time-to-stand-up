namespace StandUp.Core;

/// <summary>使用者「不在電腦前」的原因,可以同時有多個(例如鎖定後又睡眠)。</summary>
[Flags]
public enum AwayReason
{
    None = 0,
    Locked = 1,
    Suspended = 2,
    Idle = 4,
}

/// <summary>
/// 追蹤使用者是否離開(鎖定、睡眠、閒置),回來時調整工作計時:
/// 離開不到休息時長 → 那段時間不算工作;離開達到休息時長 → 視為已休息,從回來那刻重新計時。
/// 只在 Working 狀態調整;休息中的離開不影響倒數,回來仍要按解鎖。
/// </summary>
public sealed class PresenceTracker
{
    private readonly IClock _clock;
    private readonly BreakStateMachine _machine;
    private AwayReason _reasons;
    private DateTimeOffset _awaySince;

    public PresenceTracker(IClock clock, BreakStateMachine machine)
    {
        _clock = clock;
        _machine = machine;
    }

    /// <summary>回來時觸發,參數為 (離開多久, 是否視為已休息而重新計時)。只在 Working 狀態下觸發。</summary>
    public event Action<TimeSpan, bool>? Returned;

    public bool IsAway => _reasons != AwayReason.None;

    public AwayReason Reasons => _reasons;

    /// <summary>最近一次「全部原因都解除」的時間。用來避免睡眠事件與 tick 間隔備援重複計算。</summary>
    public DateTimeOffset LastReturnAt { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>
    /// 開啟或關閉一個離開原因。<paramref name="since"/> 是離開實際開始的時間(例如閒置是從最後一次輸入算起),
    /// 省略則為現在。已開啟的原因再開一次不會改變開始時間。
    /// </summary>
    public void SetAway(AwayReason reason, bool on, DateTimeOffset? since = null)
    {
        var now = _clock.UtcNow;
        if (on)
        {
            if ((_reasons & reason) == reason)
                return;
            var start = since is { } s && s < now ? s : now;
            // 不早於上次回來的時間:同一段離開若已由 ReportGap 算過,晚到的睡眠事件不會再扣一次。
            if (start < LastReturnAt)
                start = LastReturnAt;
            if (_reasons == AwayReason.None || start < _awaySince)
                _awaySince = start;
            _reasons |= reason;
            return;
        }

        if ((_reasons & reason) == AwayReason.None)
            return;
        _reasons &= ~reason;
        if (_reasons == AwayReason.None)
            Return(now - _awaySince, now);
    }

    /// <summary>
    /// 一段已經結束的離開(例如沒收到睡眠事件,但發現兩次 tick 之間隔了很久)。
    /// 正在離開中時不處理,交給離開結束時一起算。
    /// </summary>
    public void ReportGap(DateTimeOffset since)
    {
        var now = _clock.UtcNow;
        if (IsAway || since >= now)
            return;
        Return(now - since, now);
    }

    private void Return(TimeSpan away, DateTimeOffset now)
    {
        LastReturnAt = now;
        if (_machine.State != BreakState.Working || away <= TimeSpan.Zero)
            return;

        var reset = away >= _machine.Settings.BreakDuration;
        if (reset)
            _machine.ResetWork();
        else
            _machine.ExcludeAway(away);
        Returned?.Invoke(away, reset);
    }
}
