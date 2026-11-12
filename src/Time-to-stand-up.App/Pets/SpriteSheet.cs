using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using TimeToStandUp.Core;

namespace TimeToStandUp.App;

/// <summary>
/// 寵物的精靈圖:Assets/Pets/&lt;名稱&gt;.png(每列一個動畫、每格一個影格)加上同名 .json 的動畫表。
/// 兩個檔案都由 tools/make_pet_sprites.py 產生。影格在載入時切好並凍結,繪製時直接取用。
/// </summary>
public sealed class SpriteSheet
{
    private static readonly Dictionary<PetKind, SpriteSheet> Cache = new();

    private readonly Dictionary<string, Clip> _clips;
    private readonly Clip _fallback;

    private SpriteSheet(int cell, Dictionary<string, Clip> clips)
    {
        Cell = cell;
        _clips = clips;
        _fallback = clips.TryGetValue("idle", out var idle) ? idle : FirstClip(clips);
    }

    /// <summary>一格影格的邊長(像素)。</summary>
    public int Cell { get; }

    /// <summary>載入(並快取)寵物的精靈圖。該寵物的圖還沒做好時改用貓的。</summary>
    public static SpriteSheet Load(PetKind kind)
    {
        if (Cache.TryGetValue(kind, out var cached))
            return cached;

        SpriteSheet sheet;
        try
        {
            sheet = LoadFromResources(PetProfile.AssetName(kind));
        }
        catch (IOException) when (kind != PetKind.Cat)
        {
            sheet = Load(PetKind.Cat);
        }

        Cache[kind] = sheet;
        return sheet;
    }

    /// <summary>
    /// 取得要畫的影格。progress 有值(只播一次的動畫)時依進度取格,否則依時間循環。
    /// 找不到的動畫名稱改用 idle,不丟例外。
    /// </summary>
    public BitmapSource Frame(string animation, double time, double? progress)
    {
        var clip = _clips.TryGetValue(animation, out var found) ? found : _fallback;
        var count = clip.Frames.Length;

        int index;
        if (progress is { } p)
            index = Math.Clamp((int)(p * count), 0, count - 1);
        else if (clip.Loop)
            index = (int)(Math.Max(0, time) * clip.Fps) % count;
        else
            index = Math.Clamp((int)(Math.Max(0, time) * clip.Fps), 0, count - 1);

        return clip.Frames[index];
    }

    private static SpriteSheet LoadFromResources(string name)
    {
        BitmapSource image;
        using (var stream = OpenResource($"Assets/Pets/{name}.png"))
        {
            image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }

        using var json = OpenResource($"Assets/Pets/{name}.json");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var cell = root.GetProperty("cell").GetInt32();

        var clips = new Dictionary<string, Clip>(StringComparer.Ordinal);
        foreach (var entry in root.GetProperty("animations").EnumerateObject())
        {
            var row = entry.Value.GetProperty("row").GetInt32();
            var frames = new BitmapSource[entry.Value.GetProperty("frames").GetInt32()];
            for (var i = 0; i < frames.Length; i++)
            {
                var frame = new CroppedBitmap(image, new Int32Rect(i * cell, row * cell, cell, cell));
                frame.Freeze();
                frames[i] = frame;
            }

            clips[entry.Name] = new Clip(
                frames,
                entry.Value.GetProperty("fps").GetDouble(),
                entry.Value.GetProperty("loop").GetBoolean());
        }

        if (clips.Count == 0)
            throw new InvalidDataException($"精靈圖 {name} 沒有任何動畫");
        return new SpriteSheet(cell, clips);
    }

    private static Stream OpenResource(string path)
    {
        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/{path}"))
            ?? throw new IOException($"找不到資源 {path}");
        return info.Stream;
    }

    private static Clip FirstClip(Dictionary<string, Clip> clips)
    {
        foreach (var clip in clips.Values)
            return clip;
        throw new InvalidDataException("精靈圖沒有任何動畫");
    }

    private sealed record Clip(BitmapSource[] Frames, double Fps, bool Loop);
}
