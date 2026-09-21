using System.Text;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace XivMiniUtil.Services.TitleBackground;

internal sealed record TitleBackgroundLoginConfirmationPrompt(string Prefix, string Suffix)
{
    public static TitleBackgroundLoginConfirmationPrompt? Create(ReadOnlySeString template)
    {
        var prefix = new StringBuilder();
        var suffix = new StringBuilder();
        var foundName = false;
        foreach (var payload in template)
        {
            var part = foundName ? suffix : prefix;
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                part.Append(payload.ToString());
                continue;
            }
            switch (payload.MacroCode)
            {
                case MacroCode.String when !foundName:
                    foundName = true;
                    break;
                case MacroCode.NewLine:
                    part.Append('\n');
                    break;
                case MacroCode.ColorType:
                case MacroCode.EdgeColorType:
                    break;
                default:
                    return null;
            }
        }

        return foundName && prefix.Length + suffix.Length > 0
            ? new(prefix.ToString(), suffix.ToString()) : null;
    }

    public bool Matches(string prompt)
    {
        if (prompt.Length > 4096)
            return false;
        var text = prompt.Replace("\r", string.Empty, StringComparison.Ordinal);
        if (text.Length <= Prefix.Length + Suffix.Length
            || !text.StartsWith(Prefix, StringComparison.Ordinal)
            || !text.EndsWith(Suffix, StringComparison.Ordinal))
            return false;
        var name = text.AsSpan(Prefix.Length, text.Length - Prefix.Length - Suffix.Length);
        return !name.IsWhiteSpace() && !name.Contains('\n');
    }
}
