using System;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace TimeToStandUp.App;

/// <summary>
/// WPF 的 TextBlock 沒有字距設定,所以在字與字之間插入極細空白(U+200A)來撐開一點點。
/// 連續的英數字(單字、數字)不插,避免單字被拆開。
/// </summary>
internal sealed class LetterSpacing : IValueConverter
{
    private const char HairSpace = ' ';

    public static string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var sb = new StringBuilder(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            sb.Append(text[i]);
            if (i + 1 < text.Length && NeedsGap(text[i], text[i + 1]))
                sb.Append(HairSpace);
        }
        return sb.ToString();
    }

    private static bool NeedsGap(char current, char next) =>
        !char.IsWhiteSpace(current) && !char.IsWhiteSpace(next) &&
        !(IsAsciiWord(current) && IsAsciiWord(next));

    private static bool IsAsciiWord(char c) => c < 128 && char.IsLetterOrDigit(c);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Apply(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
