namespace TimeToStandUp.Core;

public enum PetActivity
{
    Idle,
    Walk,
    Run,

    /// <summary>原地或往前的小跳。</summary>
    Hop,

    /// <summary>跳上平台或從平台跳下。</summary>
    Jump,
    Sit,
    Sleep,
    Special,

    /// <summary>注意到滑鼠游標:走過去、坐下看著它。</summary>
    Curious,
}

/// <summary>
/// 寵物的行為:在 <see cref="PetWorld"/> 的軌道上隨機走、跑、跳、坐、睡、做特殊動作,
/// 偶爾跳上平台,游標靠近時會過去看。純邏輯、不碰 UI;亂數由外部注入,固定種子即可重現。
/// 位置 (X, Y) 是腳底中心,單位為參考單位。
/// </summary>
public sealed class PetBrain
{
    private const double LeapPrepareSeconds = 0.15;
    private const double LeapLandSeconds = 0.15;
    private const double CursorRadius = 320;
    private const double CursorCooldownSeconds = 12;
    private const double CursorStopDistance = 90;

    private readonly PetProfile _profile;
    private readonly Random _random;
    private readonly Queue<Step> _queue = new();
    private PetWorld _world;
    private Step? _current;
    private int _platform = -1;
    private double _cursorCooldown;
    private bool _celebrating;
    private (double X, double Y)? _cursor;

    public PetBrain(PetWorld world, PetProfile profile, Random random, double startX)
    {
        _world = world;
        _profile = profile;
        _random = random;
        X = ClampX(startX);
        Y = PetWorld.GroundY;
    }

    public double X { get; private set; }

    public double Y { get; private set; }

    /// <summary>+1 朝右、-1 朝左。</summary>
    public int Facing { get; private set; } = 1;

    public PetActivity Activity { get; private set; } = PetActivity.Sit;

    /// <summary>精靈圖中的動畫名稱。</summary>
    public string Animation { get; private set; } = "sit";

    /// <summary>目前動畫已播放的秒數(循環動畫用它算影格)。</summary>
    public double AnimationTime { get; private set; }

    /// <summary>只播一次的動畫的進度 0~1;循環動畫為 null。</summary>
    public double? AnimationProgress { get; private set; }

    public double Size => _profile.Size;

    public bool OnPlatform => _platform >= 0;

    public PetWorld World => _world;

    /// <summary>推進 dt 秒。cursor 是游標在軌道座標的位置,不在任何螢幕上時傳 null。</summary>
    public void Update(double dt, (double X, double Y)? cursor = null)
    {
        if (!(dt > 0))
            return;
        dt = Math.Min(dt, 0.25);

        _cursor = cursor;
        _cursorCooldown = Math.Max(0, _cursorCooldown - dt);
        if (cursor is { } position && CanBeCurious() && IsNear(position))
            StartCurious(position);

        _current ??= NextStep();
        if (!_current.Started)
            Begin(_current);

        AnimationTime += dt;
        if (Advance(_current, dt))
            _current = null;
    }

    /// <summary>螢幕或平台改變時更新世界;寵物留在原地(必要時拉回範圍內或落回地面)。</summary>
    public void SetWorld(PetWorld world)
    {
        var samePlatforms = world.Platforms.SequenceEqual(_world.Platforms);
        var sameScreens = world.ScreenLengths.SequenceEqual(_world.ScreenLengths);
        _world = world;
        if (samePlatforms && sameScreens)
            return;

        // 進行中的計畫是依舊世界訂的,丟掉重來。
        _queue.Clear();
        if (_current is { Kind: StepKind.Leap, Started: true } leap)
        {
            if (leap.TargetPlatform >= 0 && leap.TargetPlatform < world.Platforms.Count)
            {
                var platform = world.Platforms[leap.TargetPlatform];
                leap.ToX = Math.Clamp(leap.ToX, platform.Left, platform.Right);
                leap.ToY = platform.Top;
            }
            else
            {
                leap.TargetPlatform = -1;
                leap.ToX = ClampX(leap.ToX);
                leap.ToY = PetWorld.GroundY;
            }

            return;
        }

        _current = null;
        if (_platform >= 0 && _platform < world.Platforms.Count)
        {
            var platform = world.Platforms[_platform];
            X = Math.Clamp(X, platform.Left, platform.Right);
            Y = platform.Top;
        }
        else
        {
            _platform = -1;
            X = ClampX(X);
            Y = PetWorld.GroundY;
        }
    }

    /// <summary>休息完成:跳兩下慶祝,然後坐著等解鎖。</summary>
    public void Celebrate()
    {
        if (_celebrating)
            return;
        _celebrating = true;

        _queue.Clear();
        if (_current is not { Kind: StepKind.Leap })
            _current = null;

        _queue.Enqueue(HopStep(0));
        _queue.Enqueue(HopStep(0));
    }

    // ------------------------------------------------------------ planning

    private Step NextStep()
    {
        if (_queue.Count == 0)
            Plan();
        return _queue.Dequeue();
    }

    private void Plan()
    {
        if (_celebrating)
        {
            _queue.Enqueue(new Step
            {
                Kind = StepKind.Hold,
                Activity = PetActivity.Sit,
                Animation = "sit",
                Forever = true,
                FaceCursor = true,
            });
            return;
        }

        if (_platform >= 0)
            PlanOnPlatform();
        else
            PlanOnGround();
    }

    private void PlanOnGround()
    {
        var hasPlatforms = _world.Platforms.Count > 0;
        var options = new (double Weight, Action Enqueue)[]
        {
            (30, () => _queue.Enqueue(MoveStep(PetActivity.Walk, "walk", PickTarget(150), _profile.WalkSpeed))),
            (10, () => _queue.Enqueue(MoveStep(PetActivity.Run, "run", PickTarget(400), _profile.RunSpeed))),
            (14, () => _queue.Enqueue(HoldStep(PetActivity.Idle, "idle", Range(1.5, 4)))),
            (10, () => _queue.Enqueue(HoldStep(PetActivity.Sit, "sit", Range(3, 7)))),
            (8, () => _queue.Enqueue(HopStep(_random.NextDouble() < 0.5 ? 0 : Facing * Range(80, 160)))),
            (14, EnqueueSpecial),
            (_profile.Sleeps ? 4 : 0, () => _queue.Enqueue(HoldStep(PetActivity.Sleep, "sleep", Range(8, 14)))),
            (hasPlatforms ? 8 : 0, EnqueuePlatformVisit),
        };

        var roll = _random.NextDouble() * options.Sum(option => option.Weight);
        foreach (var (weight, enqueue) in options)
        {
            roll -= weight;
            if (roll < 0)
            {
                enqueue();
                return;
            }
        }

        options[0].Enqueue();
    }

    private void PlanOnPlatform()
    {
        var roll = _random.NextDouble();
        if (roll < 0.35)
            _queue.Enqueue(HoldStep(PetActivity.Sit, "sit", Range(3, 6)));
        else if (roll < 0.5)
            _queue.Enqueue(HoldStep(PetActivity.Idle, "idle", Range(2, 4)));
        else if (roll < 0.7)
            EnqueueSpecial();
        else
            EnqueueLeaveForPlatformOrGround();
    }

    private void EnqueueSpecial()
    {
        if (_profile.Specials.Count == 0)
        {
            _queue.Enqueue(HoldStep(PetActivity.Sit, "sit", Range(3, 6)));
            return;
        }

        var special = _profile.Specials[_random.Next(_profile.Specials.Count)];
        _queue.Enqueue(new Step
        {
            Kind = StepKind.Hold,
            Activity = PetActivity.Special,
            Animation = special.Animation,
            Duration = special.Seconds,
            Timed = true,
        });
    }

    private void EnqueuePlatformVisit()
    {
        var index = _random.Next(_world.Platforms.Count);
        var platform = _world.Platforms[index];
        var fromLeft = X < (platform.Left + platform.Right) / 2;
        var takeoff = ClampX(fromLeft ? platform.Left - Range(90, 200) : platform.Right + Range(90, 200));

        _queue.Enqueue(MoveStep(PetActivity.Walk, "walk", takeoff, _profile.WalkSpeed));
        _queue.Enqueue(LeapStep(PointOn(platform), index));
    }

    private void EnqueueLeaveForPlatformOrGround()
    {
        if (_world.Platforms.Count > 1 && _random.NextDouble() < 0.4)
        {
            var other = _random.Next(_world.Platforms.Count - 1);
            if (other >= _platform)
                other++;
            _queue.Enqueue(LeapStep(PointOn(_world.Platforms[other]), other));
            return;
        }

        EnqueueJumpDown();
    }

    private void EnqueueJumpDown()
    {
        var landing = X;
        if (_platform >= 0 && _platform < _world.Platforms.Count)
        {
            var platform = _world.Platforms[_platform];
            landing = _random.NextDouble() < 0.5
                ? platform.Left - Range(80, 200)
                : platform.Right + Range(80, 200);
        }

        _queue.Enqueue(LeapStep(ClampX(landing), -1));
    }

    private double PointOn(PetPlatform platform)
    {
        var inset = Math.Min(_profile.Size / 2, (platform.Right - platform.Left) / 2);
        return Range(platform.Left + inset, platform.Right - inset);
    }

    private double PickTarget(double minimumDistance)
    {
        var target = X;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            target = ClampX(Range(0, _world.Length));
            if (Math.Abs(target - X) >= minimumDistance)
                break;
        }

        return target;
    }

    // ------------------------------------------------------------ cursor

    private bool CanBeCurious()
    {
        if (_celebrating || _platform >= 0 || _cursorCooldown > 0 || _queue.Count > 0)
            return false;
        if (_current is null)
            return true;
        return _current.Kind != StepKind.Leap
            && Activity is PetActivity.Idle or PetActivity.Walk or PetActivity.Sit;
    }

    private bool IsNear((double X, double Y) cursor)
    {
        var dx = cursor.X - X;
        var dy = cursor.Y - (Y - _profile.Size / 2);
        return dx * dx + dy * dy < CursorRadius * CursorRadius;
    }

    private void StartCurious((double X, double Y) cursor)
    {
        _cursorCooldown = CursorCooldownSeconds;
        _current = null;

        var side = Math.Sign(cursor.X - X);
        var target = ClampX(cursor.X - side * CursorStopDistance);
        if (Math.Abs(target - X) > 20)
            _queue.Enqueue(MoveStep(PetActivity.Curious, "walk", target, _profile.WalkSpeed));

        _queue.Enqueue(new Step
        {
            Kind = StepKind.Hold,
            Activity = PetActivity.Curious,
            Animation = "sit",
            Duration = Range(2.5, 4.5),
            FaceCursor = true,
        });
    }

    // ------------------------------------------------------------ steps

    private static Step HoldStep(PetActivity activity, string animation, double seconds) => new()
    {
        Kind = StepKind.Hold,
        Activity = activity,
        Animation = animation,
        Duration = seconds,
    };

    private static Step MoveStep(PetActivity activity, string animation, double targetX, double speed) => new()
    {
        Kind = StepKind.Move,
        Activity = activity,
        Animation = animation,
        ToX = targetX,
        Speed = speed,
    };

    /// <summary>留在同一個高度的小跳,forward 是往前的距離(可為 0 或負)。</summary>
    private static Step HopStep(double forward) => new()
    {
        Kind = StepKind.Leap,
        Activity = PetActivity.Hop,
        Animation = "jump",
        Timed = true,
        SameLevel = true,
        Forward = forward,
    };

    private static Step LeapStep(double targetX, int targetPlatform) => new()
    {
        Kind = StepKind.Leap,
        Activity = PetActivity.Jump,
        Animation = "jump",
        Timed = true,
        ToX = targetX,
        TargetPlatform = targetPlatform,
    };

    private void Begin(Step step)
    {
        step.Started = true;
        step.FromX = X;
        step.FromY = Y;

        switch (step.Kind)
        {
            case StepKind.Move:
                Face(step.ToX - X);
                break;

            case StepKind.Leap:
                if (step.SameLevel)
                {
                    step.TargetPlatform = _platform;
                    step.ToX = _platform >= 0 ? X : ClampX(X + step.Forward);
                    step.ToY = Y;
                    step.Apex = 70;
                }
                else
                {
                    if (step.TargetPlatform >= _world.Platforms.Count)
                        step.TargetPlatform = -1;
                    step.ToY = step.TargetPlatform >= 0 ? _world.Platforms[step.TargetPlatform].Top : PetWorld.GroundY;
                    step.Apex = 60 + 0.2 * Math.Abs(step.ToY - Y);
                }

                var distance = Math.Sqrt(Math.Pow(step.ToX - X, 2) + Math.Pow(step.ToY - Y, 2));
                step.AirSeconds = 0.4 + distance / 1200;
                Face(step.ToX - X);
                break;
        }

        Activity = step.Activity;
        if (step.Animation != Animation || step.Timed)
            AnimationTime = 0;
        Animation = step.Animation;
        AnimationProgress = step.Timed ? 0 : null;
    }

    /// <summary>回傳 true 表示這一步做完了。</summary>
    private bool Advance(Step step, double dt)
    {
        switch (step.Kind)
        {
            case StepKind.Hold:
                step.Elapsed += dt;
                if (step.FaceCursor && _cursor is { } cursor)
                    Face(cursor.X - X, deadZone: 6);
                if (step.Timed)
                    AnimationProgress = Math.Min(1, step.Elapsed / step.Duration);
                return !step.Forever && step.Elapsed >= step.Duration;

            case StepKind.Move:
                var remaining = step.ToX - X;
                var stride = step.Speed * dt;
                if (Math.Abs(remaining) <= stride)
                {
                    X = step.ToX;
                    return true;
                }

                X += Math.Sign(remaining) * stride;
                return false;

            default:
                return AdvanceLeap(step, dt);
        }
    }

    private bool AdvanceLeap(Step step, double dt)
    {
        step.Elapsed += dt;
        var airEnd = LeapPrepareSeconds + step.AirSeconds;

        if (step.Elapsed < LeapPrepareSeconds)
        {
            AnimationProgress = 0.25 * step.Elapsed / LeapPrepareSeconds;
            return false;
        }

        if (step.Elapsed < airEnd)
        {
            var u = (step.Elapsed - LeapPrepareSeconds) / step.AirSeconds;
            X = step.FromX + (step.ToX - step.FromX) * u;
            Y = step.FromY + (step.ToY - step.FromY) * u - 4 * step.Apex * u * (1 - u);
            _platform = -1; // 在空中
            AnimationProgress = 0.25 + 0.5 * u;
            return false;
        }

        X = step.ToX;
        Y = step.ToY;
        _platform = step.TargetPlatform;
        var landed = (step.Elapsed - airEnd) / LeapLandSeconds;
        AnimationProgress = Math.Min(1, 0.75 + 0.25 * landed);
        return landed >= 1;
    }

    private void Face(double direction, double deadZone = 0)
    {
        if (Math.Abs(direction) > deadZone)
            Facing = direction > 0 ? 1 : -1;
    }

    private double ClampX(double x)
    {
        var margin = _profile.Size / 2;
        return Math.Clamp(x, margin, Math.Max(margin, _world.Length - margin));
    }

    private double Range(double min, double max) => min + _random.NextDouble() * (max - min);

    private enum StepKind
    {
        Hold,
        Move,
        Leap,
    }

    private sealed class Step
    {
        public StepKind Kind;
        public PetActivity Activity;
        public string Animation = "idle";
        public bool Started;
        public double Elapsed;

        // Hold
        public double Duration;
        public bool Forever;
        public bool FaceCursor;

        /// <summary>只播一次、以進度決定影格的動畫。</summary>
        public bool Timed;

        // Move / Leap
        public double Speed;
        public double FromX;
        public double FromY;
        public double ToX;
        public double ToY;

        // Leap
        public bool SameLevel;
        public double Forward;
        public int TargetPlatform = -1;
        public double Apex;
        public double AirSeconds;
    }
}
