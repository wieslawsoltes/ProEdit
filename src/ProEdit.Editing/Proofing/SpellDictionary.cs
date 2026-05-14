using System.Text;
using WeCantSpell.Hunspell;

namespace ProEdit.Editing;

public sealed class SpellDictionary
{
    private readonly HashSet<string> _userWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredWords = new(StringComparer.OrdinalIgnoreCase);

    public WordList WordList { get; }

    private SpellDictionary(WordList wordList)
    {
        WordList = wordList ?? throw new ArgumentNullException(nameof(wordList));
    }

    public static SpellDictionary LoadHunspell(Stream affStream, Stream dicStream)
    {
        ArgumentNullException.ThrowIfNull(affStream);
        ArgumentNullException.ThrowIfNull(dicStream);
        var affBytes = ReadAllBytes(affStream);
        var dicBytes = ReadAllBytes(dicStream);
        var encoding = DetectEncoding(affBytes);
        var words = ParseDictionaryWords(dicBytes, encoding);

        WordList wordList;
        try
        {
            using var affMemory = new MemoryStream(affBytes, writable: false);
            using var dicMemory = new MemoryStream(dicBytes, writable: false);
            wordList = HunspellWordListLoader.Load(affMemory, dicMemory);

            if (words.Count > 0 && !ContainsAny(wordList, words))
            {
                wordList = WordList.CreateFromWords(words);
            }
        }
        catch
        {
            wordList = words.Count > 0 ? WordList.CreateFromWords(words) : WordList.CreateFromWords(Array.Empty<string>());
        }

        return new SpellDictionary(wordList);
    }

    public bool Check(ReadOnlySpan<char> word)
    {
        if (word.IsEmpty)
        {
            return true;
        }

        var text = word.ToString();
        if (_ignoredWords.Contains(text) || _userWords.Contains(text))
        {
            return true;
        }

        return WordList.Check(text);
    }

    public IReadOnlyList<string> Suggest(ReadOnlySpan<char> word, int maxSuggestions)
    {
        if (word.IsEmpty)
        {
            return Array.Empty<string>();
        }

        var text = word.ToString();
        var suggestions = WordList.Suggest(text);
        if (suggestions is null)
        {
            return Array.Empty<string>();
        }

        if (maxSuggestions <= 0)
        {
            return suggestions.ToArray();
        }

        var result = new List<string>(Math.Min(maxSuggestions, 8));
        foreach (var suggestion in suggestions)
        {
            result.Add(suggestion);
            if (result.Count >= maxSuggestions)
            {
                break;
            }
        }

        return result;
    }

    public void AddUserWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        _userWords.Add(word.Trim());
    }

    public void IgnoreWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        _ignoredWords.Add(word.Trim());
    }

    private static class HunspellWordListLoader
    {
        public static WordList Load(Stream affStream, Stream dicStream)
        {
            var wordListType = typeof(WordList);
            var createFromStreams = wordListType.GetMethod("CreateFromStreams", new[] { typeof(Stream), typeof(Stream) });
            if (createFromStreams is not null)
            {
                return (WordList)createFromStreams.Invoke(null, new object[] { affStream, dicStream })!;
            }

            var createFromFiles = wordListType.GetMethod("CreateFromFiles", new[] { typeof(string), typeof(string) });
            if (createFromFiles is not null)
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "proedit-proofing");
                Directory.CreateDirectory(tempDir);
                var affPath = Path.Combine(tempDir, Path.GetRandomFileName() + ".aff");
                var dicPath = Path.Combine(tempDir, Path.GetRandomFileName() + ".dic");
                using (var affFile = File.Create(affPath))
                {
                    affStream.CopyTo(affFile);
                }
                using (var dicFile = File.Create(dicPath))
                {
                    dicStream.CopyTo(dicFile);
                }

                try
                {
                    return (WordList)createFromFiles.Invoke(null, new object[] { affPath, dicPath })!;
                }
                finally
                {
                    TryDeleteFile(affPath);
                    TryDeleteFile(dicPath);
                }
            }

            throw new NotSupportedException("Hunspell WordList loader not found.");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var buffer))
        {
            return buffer.AsSpan(0, (int)memoryStream.Length).ToArray();
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static Encoding DetectEncoding(byte[] affBytes)
    {
        if (affBytes.Length == 0)
        {
            return Encoding.UTF8;
        }

        var text = Encoding.UTF8.GetString(affBytes);
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("SET", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    try
                    {
                        return Encoding.GetEncoding(parts[1]);
                    }
                    catch
                    {
                        return Encoding.UTF8;
                    }
                }
            }
        }

        return Encoding.UTF8;
    }

    private static List<string> ParseDictionaryWords(byte[] dicBytes, Encoding encoding)
    {
        var words = new List<string>();
        if (dicBytes.Length == 0)
        {
            return words;
        }

        using var reader = new StreamReader(new MemoryStream(dicBytes, writable: false), encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        string? line;
        var isFirst = true;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (isFirst)
            {
                isFirst = false;
                if (int.TryParse(line, out _))
                {
                    continue;
                }
            }

            var word = line;
            var whitespaceIndex = word.IndexOfAny(new[] { ' ', '\t' });
            if (whitespaceIndex >= 0)
            {
                word = word.Substring(0, whitespaceIndex);
            }

            var flagIndex = word.IndexOf('/');
            if (flagIndex > 0)
            {
                word = word.Substring(0, flagIndex);
            }

            if (word.Length > 0)
            {
                words.Add(word);
            }
        }

        return words;
    }

    private static bool ContainsAny(WordList wordList, List<string> words)
    {
        if (words.Count == 0)
        {
            return false;
        }

        var limit = Math.Min(words.Count, 10);
        for (var i = 0; i < limit; i++)
        {
            if (wordList.Check(words[i]))
            {
                return true;
            }
        }

        return false;
    }
}
