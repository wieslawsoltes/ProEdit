using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Office.Editing;

public sealed class EditorProofingService : IProofingService, IProofingSpanProvider, IProofingToggleService, IDisposable
{
    private static readonly DocColor SpellingUnderlineColor = new DocColor(204, 0, 0);
    private static readonly DocColor GrammarUnderlineColor = new DocColor(0, 102, 204);
    private static readonly DocColor StyleUnderlineColor = new DocColor(0, 153, 0);
    private readonly IEditorSession _session;
    private readonly IEditorLayoutRefreshService? _layoutRefresh;
    private readonly IProofingProfileRegistry _profiles;
    private readonly IProofingProfileManager? _profileManager;
    private readonly ILanguageDetector? _languageDetector;
    private readonly Dictionary<int, string> _paragraphTextCache = new();
    private readonly Dictionary<int, string> _grammarTextCache = new();
    private readonly Dictionary<int, List<ProofingDiagnostic>> _diagnostics = new();
    private readonly Dictionary<int, List<ProofingUnderlineSpan>> _underlineSpans = new();
    private readonly object _sync = new();
    private readonly SynchronizationContext? _syncContext;
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _grammarCts;
    private volatile bool _isEnabled;
    private volatile bool _spellingEnabled = true;
    private volatile bool _grammarEnabled;
    private volatile bool _styleEnabled;

    public bool IsEnabled => _isEnabled;
    public bool IsSpellingEnabled => _spellingEnabled;
    public bool IsGrammarEnabled => _grammarEnabled;
    public bool IsStyleEnabled => _styleEnabled;

    public event EventHandler<ProofingUpdatedEventArgs>? Updated;

    public EditorProofingService(
        IEditorSession session,
        ISpellEngine spellEngine,
        ISpellDictionaryRegistry registry,
        IEditorLayoutRefreshService? layoutRefresh = null)
        : this(session, new ProofingProfileRegistry(new ProofingProfile("Default", spellEngine, registry)), layoutRefresh)
    {
    }

    public EditorProofingService(
        IEditorSession session,
        IProofingProfileRegistry profiles,
        IEditorLayoutRefreshService? layoutRefresh = null,
        ILanguageDetector? languageDetector = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _layoutRefresh = layoutRefresh;
        _languageDetector = languageDetector;
        _syncContext = SynchronizationContext.Current;
        _session.Changed += OnSessionChanged;

        if (profiles is IProofingProfileManager manager)
        {
            _profileManager = manager;
            _profileManager.OptionsChanged += OnProfileOptionsChanged;
        }
    }

    public IReadOnlyList<ProofingDiagnostic> GetParagraphDiagnostics(int paragraphIndex)
    {
        if (!_isEnabled)
        {
            return Array.Empty<ProofingDiagnostic>();
        }

        lock (_sync)
        {
            return _diagnostics.TryGetValue(paragraphIndex, out var diagnostics)
                ? diagnostics
                : Array.Empty<ProofingDiagnostic>();
        }
    }

    public bool TryGetDiagnosticAt(TextPosition position, out ProofingDiagnostic diagnostic)
    {
        diagnostic = default;
        if (!_isEnabled)
        {
            return false;
        }

        lock (_sync)
        {
            if (!_diagnostics.TryGetValue(position.ParagraphIndex, out var diagnostics))
            {
                return false;
            }

            foreach (var item in diagnostics)
            {
                if (position.Offset >= item.StartOffset && position.Offset <= item.StartOffset + item.Length)
                {
                    diagnostic = item;
                    return true;
                }
            }
        }

        return false;
    }

    public IReadOnlyList<string> GetSuggestions(ProofingDiagnostic diagnostic, int maxSuggestions = 5)
    {
        if (!_isEnabled)
        {
            return Array.Empty<string>();
        }

        if (diagnostic.Suggestions is { Count: > 0 })
        {
            if (maxSuggestions <= 0 || diagnostic.Suggestions.Count <= maxSuggestions)
            {
                return diagnostic.Suggestions;
            }

            var result = new List<string>(Math.Min(maxSuggestions, diagnostic.Suggestions.Count));
            for (var i = 0; i < diagnostic.Suggestions.Count && result.Count < maxSuggestions; i++)
            {
                result.Add(diagnostic.Suggestions[i]);
            }

            return result;
        }

        if (diagnostic.Kind == ProofingIssueKind.Spelling && !_spellingEnabled)
        {
            return Array.Empty<string>();
        }

        if (diagnostic.Kind == ProofingIssueKind.Grammar && !_grammarEnabled)
        {
            return Array.Empty<string>();
        }

        if (diagnostic.Kind == ProofingIssueKind.Style && !_styleEnabled)
        {
            return Array.Empty<string>();
        }

        if (diagnostic.Kind != ProofingIssueKind.Spelling)
        {
            return Array.Empty<string>();
        }

        var profile = _profiles.ResolveProfile(diagnostic.Language);
        return profile.SpellEngine.Suggest(diagnostic.Text.AsSpan(), diagnostic.Language, maxSuggestions);
    }

    public int GetTotalDiagnostics()
    {
        if (!_isEnabled)
        {
            return 0;
        }

        lock (_sync)
        {
            var count = 0;
            foreach (var items in _diagnostics.Values)
            {
                count += items.Count;
            }

            return count;
        }
    }

    public void AddToUserDictionary(string word, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        var resolved = ResolveLanguage(language);
        var profile = _profiles.ResolveProfile(resolved);
        if (profile.DictionaryRegistry.AddUserWord(resolved, word) && _isEnabled)
        {
            RefreshAll();
        }
    }

    public void IgnoreWord(string word, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        var resolved = ResolveLanguage(language);
        var profile = _profiles.ResolveProfile(resolved);
        if (profile.DictionaryRegistry.IgnoreWord(resolved, word) && _isEnabled)
        {
            RefreshAll();
        }
    }

    public void RefreshAll()
    {
        if (!_isEnabled)
        {
            return;
        }

        var paragraphs = DocumentEditHelpers.BuildParagraphList(_session.Document);
        var updated = new List<int>(paragraphs.Count);

        lock (_sync)
        {
            _diagnostics.Clear();
            _underlineSpans.Clear();
            _paragraphTextCache.Clear();
            _grammarTextCache.Clear();

            for (var i = 0; i < paragraphs.Count; i++)
            {
                if (TryRefreshParagraphSpelling(i, paragraphs[i], force: true))
                {
                    updated.Add(i);
                }
            }
        }

        NotifyUpdated(updated);
        _layoutRefresh?.RefreshLayout(null);
        ScheduleGrammarRefresh(null, force: true);
    }

    public void RefreshParagraph(int paragraphIndex)
    {
        if (!_isEnabled)
        {
            return;
        }

        var paragraph = _session.Document.GetParagraph(paragraphIndex);
        var updated = new List<int>(1);
        lock (_sync)
        {
            if (TryRefreshParagraphSpelling(paragraphIndex, paragraph, force: false))
            {
                updated.Add(paragraphIndex);
            }
        }

        if (updated.Count > 0)
        {
            NotifyUpdated(updated);
            _layoutRefresh?.RefreshLayout(paragraphIndex);
        }

        ScheduleGrammarRefresh(paragraphIndex, force: false);
    }

    public bool TryGetParagraphSpans(int paragraphIndex, out IReadOnlyList<ProofingUnderlineSpan> spans)
    {
        if (!_isEnabled)
        {
            spans = Array.Empty<ProofingUnderlineSpan>();
            return false;
        }

        lock (_sync)
        {
            if (_underlineSpans.TryGetValue(paragraphIndex, out var paragraphSpans))
            {
                spans = paragraphSpans;
                return paragraphSpans.Count > 0;
            }
        }

        spans = Array.Empty<ProofingUnderlineSpan>();
        return false;
    }

    public void Dispose()
    {
        _session.Changed -= OnSessionChanged;
        CancelRefreshes();
        if (_profileManager is not null)
        {
            _profileManager.OptionsChanged -= OnProfileOptionsChanged;
        }
    }

    private void OnProfileOptionsChanged(object? sender, EventArgs e)
    {
        if (!_isEnabled)
        {
            return;
        }

        RefreshAll();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!_isEnabled)
        {
            return;
        }

        if (_session is IEditorChangeInfo changeInfo && changeInfo.LastChangeKind == EditorChangeKind.Selection)
        {
            return;
        }

        var paragraphIndex = _session is IEditorChangeInfo info ? info.LastDirtyParagraphIndex : null;
        if (_spellingEnabled)
        {
            ScheduleRefresh(paragraphIndex);
        }
        else
        {
            ScheduleGrammarRefresh(paragraphIndex, force: false);
        }
    }

    private void ScheduleRefresh(int? paragraphIndex)
    {
        if (!_isEnabled || !_spellingEnabled)
        {
            return;
        }

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, cts.Token);
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                var paragraphCount = _session.Document.ParagraphCount;
                if (paragraphCount <= 0)
                {
                    return;
                }

                var effectiveIndex = paragraphIndex;
                if (!effectiveIndex.HasValue)
                {
                    effectiveIndex = Math.Clamp(_session.Caret.ParagraphIndex, 0, paragraphCount - 1);
                }

                if (_syncContext is null)
                {
                    RefreshParagraph(effectiveIndex.Value);
                }
                else
                {
                    _syncContext.Post(_ =>
                    {
                        RefreshParagraph(effectiveIndex.Value);
                    }, null);
                }
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
        });
    }

    private bool TryRefreshParagraphSpelling(int paragraphIndex, ParagraphBlock paragraph, bool force)
    {
        if (!_spellingEnabled)
        {
            return false;
        }

        var text = DocumentEditHelpers.GetParagraphText(paragraph);
        if (!force && _paragraphTextCache.TryGetValue(paragraphIndex, out var cached) && string.Equals(cached, text, StringComparison.Ordinal))
        {
            return false;
        }

        _paragraphTextCache[paragraphIndex] = text;
        var language = ResolveParagraphLanguage(paragraph, text);
        var profile = _profiles.ResolveProfile(language);
        if (profile.SpellEngine is NullSpellEngine || profile.SpellEngine is IProofingEngine)
        {
            return ClearSpelling(paragraphIndex);
        }

        var wordSpans = ProofingTokenizer.CollectWordSpans(text.AsSpan());
        var diagnostics = new List<ProofingDiagnostic>();
        var spans = new List<ProofingUnderlineSpan>();

        foreach (var wordSpan in wordSpans)
        {
            var word = text.AsSpan(wordSpan.Start, wordSpan.Length);
            var wordLanguage = ResolveWordLanguage(word, language);
            var wordProfile = wordLanguage == language ? profile : _profiles.ResolveProfile(wordLanguage);
            if (wordProfile.SpellEngine.Check(word, wordLanguage))
            {
                continue;
            }

            var wordText = word.ToString();
            diagnostics.Add(new ProofingDiagnostic(
                paragraphIndex,
                wordSpan.Start,
                wordSpan.Length,
                wordText,
                wordLanguage,
                ProofingIssueKind.Spelling));

            spans.Add(new ProofingUnderlineSpan(
                wordSpan.Start,
                wordSpan.Length,
                ProofingIssueKind.Spelling,
                DocUnderlineStyle.Wave,
                SpellingUnderlineColor));
        }

        _diagnostics[paragraphIndex] = diagnostics;
        _underlineSpans[paragraphIndex] = spans;
        return true;
    }

    private void ScheduleGrammarRefresh(int? paragraphIndex, bool force)
    {
        if (!_isEnabled)
        {
            return;
        }

        if (!_grammarEnabled && !_styleEnabled && !(_spellingEnabled && _profiles.HasProofingSpelling))
        {
            return;
        }

        if (!_profiles.HasGrammarOrStyle && !_profiles.HasProofingSpelling)
        {
            return;
        }

        _grammarCts?.Cancel();
        _grammarCts?.Dispose();
        var cts = new CancellationTokenSource();
        _grammarCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                if (paragraphIndex.HasValue)
                {
                    await RefreshGrammarForParagraphAsync(paragraphIndex.Value, force, cts.Token);
                }
                else
                {
                    await RefreshGrammarForAllAsync(force, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }, cts.Token);
    }

    private async Task RefreshGrammarForAllAsync(bool force, CancellationToken cancellationToken)
    {
        var paragraphs = DocumentEditHelpers.BuildParagraphList(_session.Document);
        var updated = new List<int>();
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (await TryRefreshParagraphGrammarAsync(i, paragraphs[i], force, cancellationToken).ConfigureAwait(false))
            {
                updated.Add(i);
            }
        }

        if (updated.Count == 0)
        {
            return;
        }

        NotifyUpdatedOnContext(updated);
        _layoutRefresh?.RefreshLayout(null);
    }

    private async Task RefreshGrammarForParagraphAsync(int paragraphIndex, bool force, CancellationToken cancellationToken)
    {
        var paragraph = _session.Document.GetParagraph(paragraphIndex);
        if (!await TryRefreshParagraphGrammarAsync(paragraphIndex, paragraph, force, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        NotifyUpdatedOnContext(new[] { paragraphIndex });
        _layoutRefresh?.RefreshLayout(paragraphIndex);
    }

    private async Task<bool> TryRefreshParagraphGrammarAsync(
        int paragraphIndex,
        ParagraphBlock paragraph,
        bool force,
        CancellationToken cancellationToken)
    {
        var text = DocumentEditHelpers.GetParagraphText(paragraph);
        if (!force && _grammarTextCache.TryGetValue(paragraphIndex, out var cached) && string.Equals(cached, text, StringComparison.Ordinal))
        {
            return false;
        }

        _grammarTextCache[paragraphIndex] = text;
        var language = ResolveParagraphLanguage(paragraph, text);
        var profile = _profiles.ResolveProfile(language);

        var spellProofingEngine = _spellingEnabled ? profile.SpellEngine as IProofingEngine : null;
        var grammarEngine = _grammarEnabled ? profile.GrammarEngine : null;
        var styleEngine = _styleEnabled ? profile.StyleEngine : null;
        var includeSpelling = spellProofingEngine is not null;
        var includeGrammar = grammarEngine is not null;
        var includeStyle = styleEngine is not null;

        if (!includeSpelling && !includeGrammar && !includeStyle)
        {
            return ClearGrammarStyle(paragraphIndex);
        }

        var matches = await CollectProofingMatchesAsync(
                text,
                language,
                spellProofingEngine,
                includeSpelling,
                grammarEngine,
                includeGrammar,
                styleEngine,
                includeStyle,
                cancellationToken)
            .ConfigureAwait(false);
        var diagnostics = new List<ProofingDiagnostic>(matches.Count);
        var spans = new List<ProofingUnderlineSpan>(matches.Count);

        foreach (var match in matches)
        {
            if (!profile.Rules.IsEnabled(match.RuleId, match.Category))
            {
                continue;
            }

            diagnostics.Add(new ProofingDiagnostic(
                paragraphIndex,
                match.StartOffset,
                match.Length,
                match.Text,
                language,
                match.Kind,
                match.RuleId,
                match.Category,
                match.Message,
                match.Suggestions));

            spans.Add(CreateUnderlineSpan(match));
        }

        return MergeGrammarStyle(paragraphIndex, diagnostics, spans);
    }

    private async Task<List<ProofingMatch>> CollectProofingMatchesAsync(
        string text,
        string language,
        IProofingEngine? spellingEngine,
        bool includeSpelling,
        IGrammarEngine? grammarEngine,
        bool includeGrammar,
        IStyleEngine? styleEngine,
        bool includeStyle,
        CancellationToken cancellationToken)
    {
        var result = new List<ProofingMatch>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var requests = new Dictionary<string, ProofingEngineRequest>(StringComparer.OrdinalIgnoreCase);
        AddEngineRequest(requests, spellingEngine, includeSpelling, includeGrammar: false, includeStyle: false);
        AddEngineRequest(requests, grammarEngine, includeSpelling: false, includeGrammar, includeStyle: false);
        AddEngineRequest(requests, styleEngine, includeSpelling: false, includeGrammar: false, includeStyle);

        foreach (var request in requests.Values)
        {
            var matches = await request.Engine.CheckAsync(text, language, cancellationToken).ConfigureAwait(false);
            if (matches.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (match.Kind == ProofingIssueKind.Spelling && !request.IncludeSpelling)
                {
                    continue;
                }

                if (match.Kind == ProofingIssueKind.Grammar && !request.IncludeGrammar)
                {
                    continue;
                }

                if (match.Kind == ProofingIssueKind.Style && !request.IncludeStyle)
                {
                    continue;
                }

                result.Add(match);
            }
        }

        return result;
    }

    private static void AddEngineRequest(
        Dictionary<string, ProofingEngineRequest> requests,
        IProofingEngine? engine,
        bool includeSpelling,
        bool includeGrammar,
        bool includeStyle)
    {
        if (engine is null || (!includeSpelling && !includeGrammar && !includeStyle))
        {
            return;
        }

        if (requests.TryGetValue(engine.EngineId, out var existing))
        {
            existing.IncludeSpelling |= includeSpelling;
            existing.IncludeGrammar |= includeGrammar;
            existing.IncludeStyle |= includeStyle;
            return;
        }

        requests[engine.EngineId] = new ProofingEngineRequest(engine, includeSpelling, includeGrammar, includeStyle);
    }

    private sealed class ProofingEngineRequest
    {
        public IProofingEngine Engine { get; }
        public bool IncludeSpelling { get; set; }
        public bool IncludeGrammar { get; set; }
        public bool IncludeStyle { get; set; }

        public ProofingEngineRequest(IProofingEngine engine, bool includeSpelling, bool includeGrammar, bool includeStyle)
        {
            Engine = engine;
            IncludeSpelling = includeSpelling;
            IncludeGrammar = includeGrammar;
            IncludeStyle = includeStyle;
        }
    }

    private bool MergeGrammarStyle(
        int paragraphIndex,
        List<ProofingDiagnostic> diagnostics,
        List<ProofingUnderlineSpan> spans)
    {
        lock (_sync)
        {
            var changed = false;

            if (_diagnostics.TryGetValue(paragraphIndex, out var existingDiagnostics))
            {
                var filtered = existingDiagnostics.Where(static item => item.Kind == ProofingIssueKind.Spelling).ToList();
                if (diagnostics.Count > 0)
                {
                    filtered.AddRange(diagnostics);
                }

                filtered.Sort(static (left, right) => left.StartOffset.CompareTo(right.StartOffset));
                changed = filtered.Count != existingDiagnostics.Count || diagnostics.Count > 0;
                _diagnostics[paragraphIndex] = filtered;
            }
            else if (diagnostics.Count > 0)
            {
                diagnostics.Sort(static (left, right) => left.StartOffset.CompareTo(right.StartOffset));
                _diagnostics[paragraphIndex] = diagnostics;
                changed = true;
            }

            if (_underlineSpans.TryGetValue(paragraphIndex, out var existingSpans))
            {
                var filtered = existingSpans.Where(static item => item.Kind == ProofingIssueKind.Spelling).ToList();
                if (spans.Count > 0)
                {
                    filtered.AddRange(spans);
                }

                filtered.Sort(static (left, right) => left.Start.CompareTo(right.Start));
                changed |= filtered.Count != existingSpans.Count || spans.Count > 0;
                _underlineSpans[paragraphIndex] = filtered;
            }
            else if (spans.Count > 0)
            {
                spans.Sort(static (left, right) => left.Start.CompareTo(right.Start));
                _underlineSpans[paragraphIndex] = spans;
                changed = true;
            }

            return changed;
        }
    }

    private bool ClearGrammarStyle(int paragraphIndex)
    {
        lock (_sync)
        {
            var changed = false;

            if (_diagnostics.TryGetValue(paragraphIndex, out var existingDiagnostics))
            {
                var filtered = existingDiagnostics.Where(static item => item.Kind == ProofingIssueKind.Spelling).ToList();
                changed = filtered.Count != existingDiagnostics.Count;
                _diagnostics[paragraphIndex] = filtered;
            }

            if (_underlineSpans.TryGetValue(paragraphIndex, out var existingSpans))
            {
                var filtered = existingSpans.Where(static item => item.Kind == ProofingIssueKind.Spelling).ToList();
                changed |= filtered.Count != existingSpans.Count;
                _underlineSpans[paragraphIndex] = filtered;
            }

            return changed;
        }
    }

    private bool ClearSpelling(int paragraphIndex)
    {
        lock (_sync)
        {
            var changed = false;

            if (_diagnostics.TryGetValue(paragraphIndex, out var existingDiagnostics))
            {
                var filtered = existingDiagnostics.Where(static item => item.Kind != ProofingIssueKind.Spelling).ToList();
                changed = filtered.Count != existingDiagnostics.Count;
                _diagnostics[paragraphIndex] = filtered;
            }

            if (_underlineSpans.TryGetValue(paragraphIndex, out var existingSpans))
            {
                var filtered = existingSpans.Where(static item => item.Kind != ProofingIssueKind.Spelling).ToList();
                changed |= filtered.Count != existingSpans.Count;
                _underlineSpans[paragraphIndex] = filtered;
            }

            return changed;
        }
    }

    private static ProofingUnderlineSpan CreateUnderlineSpan(ProofingMatch match)
    {
        return match.Kind switch
        {
            ProofingIssueKind.Grammar => new ProofingUnderlineSpan(
                match.StartOffset,
                match.Length,
                match.Kind,
                DocUnderlineStyle.Wave,
                GrammarUnderlineColor),
            ProofingIssueKind.Style => new ProofingUnderlineSpan(
                match.StartOffset,
                match.Length,
                match.Kind,
                DocUnderlineStyle.Dotted,
                StyleUnderlineColor),
            _ => new ProofingUnderlineSpan(
                match.StartOffset,
                match.Length,
                match.Kind,
                DocUnderlineStyle.Wave,
                SpellingUnderlineColor)
        };
    }

    private string ResolveLanguage(string? overrideLanguage)
    {
        if (!string.IsNullOrWhiteSpace(overrideLanguage))
        {
            return overrideLanguage.Trim();
        }

        var language = _session.Document.DefaultTextStyle.Language;
        if (!string.IsNullOrWhiteSpace(language))
        {
            return language.Trim();
        }

        var fallback = _profiles.DefaultProfile.DefaultLanguage;
        return string.IsNullOrWhiteSpace(fallback) ? "en-US" : fallback.Trim();
    }

    private string ResolveParagraphLanguage(ParagraphBlock paragraph, string text)
    {
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is RunInline run && !string.IsNullOrWhiteSpace(run.Style?.Language))
            {
                return run.Style!.Language!.Trim();
            }
        }

        if (_languageDetector is not null)
        {
            var detected = _languageDetector.DetectLanguage(text.AsSpan());
            if (!string.IsNullOrWhiteSpace(detected))
            {
                return detected.Trim();
            }
        }

        return ResolveLanguage(null);
    }

    private string ResolveWordLanguage(ReadOnlySpan<char> word, string paragraphLanguage)
    {
        if (_languageDetector is null)
        {
            return paragraphLanguage;
        }

        var detected = _languageDetector.DetectLanguage(word);
        if (string.IsNullOrWhiteSpace(detected))
        {
            return paragraphLanguage;
        }

        return detected.Trim();
    }

    private void NotifyUpdated(IReadOnlyList<int> paragraphIndices)
    {
        if (paragraphIndices.Count == 0)
        {
            return;
        }

        Updated?.Invoke(this, new ProofingUpdatedEventArgs(paragraphIndices));
    }

    private void NotifyUpdatedOnContext(IReadOnlyList<int> paragraphIndices)
    {
        if (paragraphIndices.Count == 0)
        {
            return;
        }

        if (_syncContext is null)
        {
            NotifyUpdated(paragraphIndices);
        }
        else
        {
            _syncContext.Post(_ => NotifyUpdated(paragraphIndices), null);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_isEnabled == enabled)
        {
            return;
        }

        _isEnabled = enabled;
        if (!enabled)
        {
            var updated = new List<int>();
            lock (_sync)
            {
                if (_diagnostics.Count > 0)
                {
                    updated.AddRange(_diagnostics.Keys);
                }

                foreach (var key in _underlineSpans.Keys)
                {
                    if (!updated.Contains(key))
                    {
                        updated.Add(key);
                    }
                }

                _diagnostics.Clear();
                _underlineSpans.Clear();
                _paragraphTextCache.Clear();
                _grammarTextCache.Clear();
            }

            CancelRefreshes();
            if (updated.Count > 0)
            {
                NotifyUpdated(updated);
            }

            _layoutRefresh?.RefreshLayout(null);
            return;
        }

        if (!_spellingEnabled && !_grammarEnabled && !_styleEnabled)
        {
            _spellingEnabled = true;
        }

        if (_spellingEnabled)
        {
            ScheduleRefresh(_session.Caret.ParagraphIndex);
        }
        else
        {
            ScheduleGrammarRefresh(_session.Caret.ParagraphIndex, force: true);
        }
    }

    public void SetSpellingEnabled(bool enabled)
    {
        if (_spellingEnabled == enabled)
        {
            return;
        }

        _spellingEnabled = enabled;
        _paragraphTextCache.Clear();
        _grammarTextCache.Clear();

        if (enabled)
        {
            if (!_isEnabled)
            {
                _isEnabled = true;
            }

            ScheduleRefresh(_session.Caret.ParagraphIndex);
            return;
        }

        if (!_grammarEnabled && !_styleEnabled)
        {
            SetEnabled(false);
            return;
        }

        var updated = ClearDiagnosticsForKinds(ProofingIssueKind.Spelling);
        if (updated.Count > 0)
        {
            NotifyUpdated(updated);
        }

        _layoutRefresh?.RefreshLayout(null);
    }

    public void SetGrammarEnabled(bool enabled)
    {
        if (_grammarEnabled == enabled)
        {
            return;
        }

        _grammarEnabled = enabled;
        _grammarTextCache.Clear();

        if (enabled)
        {
            if (!_isEnabled)
            {
                _isEnabled = true;
            }

            ScheduleGrammarRefresh(_session.Caret.ParagraphIndex, force: true);
            return;
        }

        if (!_spellingEnabled && !_styleEnabled)
        {
            SetEnabled(false);
            return;
        }

        var updated = ClearDiagnosticsForKinds(ProofingIssueKind.Grammar);
        if (updated.Count > 0)
        {
            NotifyUpdated(updated);
        }

        _layoutRefresh?.RefreshLayout(null);
    }

    public void SetStyleEnabled(bool enabled)
    {
        if (_styleEnabled == enabled)
        {
            return;
        }

        _styleEnabled = enabled;
        _grammarTextCache.Clear();

        if (enabled)
        {
            if (!_isEnabled)
            {
                _isEnabled = true;
            }

            ScheduleGrammarRefresh(_session.Caret.ParagraphIndex, force: true);
            return;
        }

        if (!_spellingEnabled && !_grammarEnabled)
        {
            SetEnabled(false);
            return;
        }

        var updated = ClearDiagnosticsForKinds(ProofingIssueKind.Style);
        if (updated.Count > 0)
        {
            NotifyUpdated(updated);
        }

        _layoutRefresh?.RefreshLayout(null);
    }

    private void CancelRefreshes()
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
        _grammarCts?.Cancel();
        _grammarCts?.Dispose();
        _grammarCts = null;
    }

    private List<int> ClearDiagnosticsForKinds(params ProofingIssueKind[] kinds)
    {
        if (kinds is null || kinds.Length == 0)
        {
            return new List<int>();
        }

        var updated = new HashSet<int>();
        var kindSet = new HashSet<ProofingIssueKind>(kinds);
        lock (_sync)
        {
            foreach (var pair in _diagnostics)
            {
                var filtered = pair.Value.Where(item => !kindSet.Contains(item.Kind)).ToList();
                if (filtered.Count == pair.Value.Count)
                {
                    continue;
                }

                _diagnostics[pair.Key] = filtered;
                updated.Add(pair.Key);
            }

            foreach (var pair in _underlineSpans)
            {
                var filtered = pair.Value.Where(item => !kindSet.Contains(item.Kind)).ToList();
                if (filtered.Count == pair.Value.Count)
                {
                    continue;
                }

                _underlineSpans[pair.Key] = filtered;
                updated.Add(pair.Key);
            }
        }

        return updated.Count == 0 ? new List<int>() : updated.ToList();
    }

}
