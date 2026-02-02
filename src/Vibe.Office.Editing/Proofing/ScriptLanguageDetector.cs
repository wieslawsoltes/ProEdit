namespace Vibe.Office.Editing;

public sealed class ScriptLanguageDetector : ILanguageDetector
{
    private readonly string _defaultLanguage;

    public ScriptLanguageDetector(string defaultLanguage = "en-US")
    {
        _defaultLanguage = string.IsNullOrWhiteSpace(defaultLanguage) ? "en-US" : defaultLanguage.Trim();
    }

    public string? DetectLanguage(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return null;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!char.IsLetter(ch))
            {
                continue;
            }

            var code = ch;
            if (code >= 0x0400 && code <= 0x052F)
            {
                return "ru-RU";
            }

            if (code >= 0x0370 && code <= 0x03FF)
            {
                return "el-GR";
            }

            if (code >= 0x0590 && code <= 0x05FF)
            {
                return "he-IL";
            }

            if ((code >= 0x0600 && code <= 0x06FF) || (code >= 0x0750 && code <= 0x077F))
            {
                return "ar-SA";
            }

            if (code >= 0x3040 && code <= 0x30FF)
            {
                return "ja-JP";
            }

            if (code >= 0x4E00 && code <= 0x9FFF)
            {
                return "zh-CN";
            }

            if (code >= 0xAC00 && code <= 0xD7AF)
            {
                return "ko-KR";
            }

            if (code <= 0x024F)
            {
                return _defaultLanguage;
            }
        }

        return null;
    }
}
