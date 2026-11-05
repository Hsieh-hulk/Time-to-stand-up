namespace StandUp.Core;

public enum BreakState
{
    /// <summary>工作中,累計工作時間。</summary>
    Working,

    /// <summary>休息倒數中,沒有任何可以提早解鎖的入口。</summary>
    BreakLocked,

    /// <summary>倒數完成,畫面保留,等使用者按下解鎖。</summary>
    BreakReady,
}

/// <summary>
/// 休息狀態機(SPEC 3.2)。所有時間都以時鐘時間戳計算,不累加 tick,
/// 因此睡眠或鎖定期間的時間照常流逝。呼叫端定期呼叫 <see cref="Update"/> 推進狀態。
/// </summary>
public sealed class BreakStateMachine
{
    private readonly IClock _clock;
    private DateTimeOffset _workStart;
    private DateTimeOffset _breakStart;

    public BreakStateMachine(IClock clock, BreakSettings settings)
    {
        _clock = clock;
        Settings = settings;
        _workStart = clock.UtcNow;
    }

    public BreakSettings Settings { get; private set; }

    public BreakState State { get; private set; } = BreakState.Working;

    /// <summary>狀態改變時觸發,參數為 (舊狀態, 新狀態)。</summary>
    public event Action<BreakState, BreakState>? StateChanged;

    /// <summary>距離進入休息還有多久。非 Working 狀態回傳完整工作間隔。</summary>
    public TimeSpan WorkRemaining
    {
        get
        {
            if (State != BreakState.Working)
                return Settings.WorkInterval;
            return Max(Settings.WorkInterval - Elapsed(_workStart), TimeSpan.Zero);
        }
    }

    /// <summary>是否在「休息前提示」的時間窗內(Working 且剩餘時間 ≤ 提示提前量)。</summary>
    public bool IsWarning => State == BreakState.Working && WorkRemaining <= Settings.WarningLead;

    /// <summary>休息倒數剩餘時間。Working 回傳完整休息時長,BreakReady 回傳 0。</summary>
    public TimeSpan BreakRemaining => State switch
    {
        BreakState.Working => Settings.BreakDuration,
        BreakState.BreakReady => TimeSpan.Zero,
        _ => Max(Settings.BreakDuration - Elapsed(_breakStart), TimeSpan.Zero),
    };

    /// <summary>休息進度 0~1(0 = 剛開始,1 = 倒數完成)。</summary>
    public double BreakProgress => State switch
    {
        BreakState.Working => 0,
        BreakState.BreakReady => 1,
        _ => Math.Clamp(Elapsed(_breakStart) / Settings.BreakDuration, 0, 1),
    };

    /// <summary>依目前時鐘推進狀態。若睡眠後已超過休息時長,一次直接進入 BreakReady。</summary>
    public void Update()
    {
        switch (State)
        {
            case BreakState.Working when Elapsed(_workStart) >= Settings.WorkInterval:
                EnterBreak();
                break;
            case BreakState.BreakLocked when Elapsed(_breakStart) >= Settings.BreakDuration:
                Transition(BreakState.BreakReady);
                break;
        }
    }

    /// <summary>立即進入休息(例如系統匣的「立即休息」)。只在 Working 狀態有效。</summary>
    public bool StartBreakNow()
    {
        if (State != BreakState.Working)
            return false;
        EnterBreak();
        return true;
    }

    /// <summary>
    /// 換上新設定。保留已累計的工作時間(工作起點不變):工作 20 分鐘後把間隔 30 改成 60,
    /// 還剩 40 分鐘;改成 15 則下一次 <see cref="Update"/> 就進入休息。
    /// 只在 Working 狀態有效,其他狀態回傳 false 且不改任何東西。
    /// </summary>
    public bool ApplySettings(BreakSettings settings)
    {
        if (State != BreakState.Working)
            return false;
        Settings = settings;
        return true;
    }

    /// <summary>
    /// 解鎖並重新開始工作計時。只有 BreakReady 可以解鎖,倒數期間一律回傳 false。
    /// 下一輪工作的起點是按下解鎖的時間。
    /// </summary>
    public bool Unlock()
    {
        Update();
        if (State != BreakState.BreakReady)
            return false;
        _workStart = _clock.UtcNow;
        Transition(BreakState.Working);
        return true;
    }

    private void EnterBreak()
    {
        _breakStart = _clock.UtcNow;
        Transition(BreakState.BreakLocked);
    }

    private void Transition(BreakState next)
    {
        var previous = State;
        State = next;
        StateChanged?.Invoke(previous, next);
    }

    // 時鐘被往回調時不讓經過時間變成負數。
    private TimeSpan Elapsed(DateTimeOffset since) => Max(_clock.UtcNow - since, TimeSpan.Zero);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
