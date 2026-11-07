namespace TimeToStandUp.Core;

/// <summary>
/// 例外程式執行期間暫停工作計時:每次 <see cref="Update"/> 時若例外程式在執行,就把工作計時重設為現在,
/// 因此不會累計工作時間、不會出現休息前提示、也不會進入休息;程式全部關閉後,工作計時從關閉時重新開始。
/// 只在 Working 狀態介入:休息倒數照常進行,解鎖回到工作後才開始暫停。
/// </summary>
public sealed class ProcessPauseGate
{
    private readonly BreakStateMachine _machine;

    public ProcessPauseGate(BreakStateMachine machine)
    {
        _machine = machine;
    }

    /// <summary>目前是否因例外程式而暫停。</summary>
    public bool IsPaused { get; private set; }

    /// <summary>暫停狀態改變時觸發,參數為新的 <see cref="IsPaused"/>。</summary>
    public event Action<bool>? PausedChanged;

    /// <param name="exemptProcessRunning">清單中是否有程式正在執行。</param>
    public void Update(bool exemptProcessRunning)
    {
        var paused = exemptProcessRunning && _machine.State == BreakState.Working;
        if (paused)
            _machine.ResetWork();

        if (paused == IsPaused)
            return;
        IsPaused = paused;
        PausedChanged?.Invoke(paused);
    }
}
