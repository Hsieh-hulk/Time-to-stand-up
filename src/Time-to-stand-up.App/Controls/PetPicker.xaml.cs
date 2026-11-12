using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TimeToStandUp.Core;

namespace TimeToStandUp.App;

/// <summary>
/// 寵物選擇與即時預覽。預覽是一個單螢幕的小世界,用和休息畫面相同的 <see cref="PetBrain"/>、
/// <see cref="SpriteSheet"/> 與 <see cref="PetLayer"/>,只是沒有平台、也不理會游標。
/// 計時器只在控制項顯示期間(Loaded ~ Unloaded)運作;系統關閉動畫時只顯示靜止的一格。
/// </summary>
public partial class PetPicker : UserControl
{
    /// <summary>預覽區的高度對應多少參考單位:只看地面附近,寵物才夠大。</summary>
    private const double PreviewViewHeight = 200;

    /// <summary>地面離預覽區底邊的距離(參考單位)。</summary>
    private const double PreviewGroundMargin = 16;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();
    private PetBrain? _brain;
    private PetKind _selected = AppConfig.DefaultPet;
    private PetKind? _shown;

    public PetPicker()
    {
        InitializeComponent();

        Preview.ViewHeight = PreviewViewHeight;
        Preview.ViewTop = PetWorld.GroundY + PreviewGroundMargin - PreviewViewHeight;

        _timer.Tick += (_, _) => OnTick();
        Preview.SizeChanged += (_, _) => UpdatePreview();
        Loaded += (_, _) =>
        {
            UpdatePreview();
            if (SystemParameters.ClientAreaAnimation)
            {
                _clock.Restart();
                _timer.Start();
            }
        };
        Unloaded += (_, _) => _timer.Stop();

        RadioFor(_selected).IsChecked = true;
    }

    /// <summary>目前選的寵物。</summary>
    public PetKind Selected
    {
        get => _selected;
        set => RadioFor(value).IsChecked = true; // 觸發 OnPetChecked
    }

    private RadioButton RadioFor(PetKind kind) => kind switch
    {
        PetKind.Shiba => ShibaRadio,
        PetKind.Slime => SlimeRadio,
        PetKind.SandSprite => SandSpriteRadio,
        _ => CatRadio,
    };

    private void OnPetChecked(object sender, RoutedEventArgs e)
    {
        _selected = ReferenceEquals(sender, ShibaRadio) ? PetKind.Shiba
            : ReferenceEquals(sender, SlimeRadio) ? PetKind.Slime
            : ReferenceEquals(sender, SandSpriteRadio) ? PetKind.SandSprite
            : PetKind.Cat;
        UpdatePreview();
    }

    /// <summary>依預覽區目前的大小與選擇,建立或更新小世界裡的寵物。</summary>
    private void UpdatePreview()
    {
        // InitializeComponent 期間 Checked 可能先觸發,此時 Preview 還沒建立。
        if (Preview is null || Preview.ActualWidth <= 0 || Preview.ActualHeight <= 0)
            return;

        var unitsPerDip = PreviewViewHeight / Preview.ActualHeight;
        var world = new PetWorld(new[] { Preview.ActualWidth * unitsPerDip });

        if (_brain is not null && _shown == _selected)
        {
            _brain.SetWorld(world);
        }
        else
        {
            _brain = new PetBrain(world, PetProfile.For(_selected), new Random(), world.Length / 2);
            _shown = _selected;
            Preview.Attach(_brain, SpriteSheet.Load(_selected), segmentStart: 0);
        }

        Preview.InvalidateVisual();
    }

    private void OnTick()
    {
        var dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        _brain?.Update(dt);
        Preview.InvalidateVisual();
    }
}
