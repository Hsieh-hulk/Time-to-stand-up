using System.Text.Json.Serialization;

namespace StandUp.Core;

/// <summary>視窗的深淺色。System 表示跟隨 Windows 的「應用程式模式」。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    System,
    Dark,
    Light,
}
