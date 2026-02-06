using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Order;
using Vibe.Office.Documents;
using Vibe.Office.FlowDocument.Documents;
using FlowDocumentModel = Vibe.Office.FlowDocument.FlowDocument;

namespace Vibe.Office.Layout.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class FlowDocumentSyncBenchmarks
{
    private const int PlainWordsPerParagraph = 80;
    private const string MutationSuffix = " sync";

    [Params(200, 1000)]
    public int ParagraphCount { get; set; }

    private DocumentToFlowDocumentConverter _converter = null!;
    private Document _plainBaseline = null!;
    private Document _heavyTableBaseline = null!;
    private int _plainDirtyParagraphIndex;
    private int _heavyDirtyParagraphIndex;
    private string _plainOriginalText = string.Empty;
    private string _heavyOriginalText = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _converter = new DocumentToFlowDocumentConverter();

        _plainBaseline = DocumentBenchmarkFactory.CreateLargeDocument(
            paragraphCount: ParagraphCount,
            wordsPerParagraph: PlainWordsPerParagraph,
            tableFrequency: 0);
        _plainDirtyParagraphIndex = Math.Clamp(ParagraphCount / 2, 0, Math.Max(0, _plainBaseline.ParagraphCount - 1));
        _plainOriginalText = _plainBaseline.GetParagraph(_plainDirtyParagraphIndex).Text ?? string.Empty;

        _heavyTableBaseline = DocumentBenchmarkFactory.CreateLargeDocument(
            paragraphCount: ParagraphCount,
            wordsPerParagraph: PlainWordsPerParagraph,
            tableFrequency: 5);
        _heavyDirtyParagraphIndex = Math.Clamp(ParagraphCount / 2, 0, Math.Max(0, _heavyTableBaseline.ParagraphCount - 1));
        _heavyOriginalText = _heavyTableBaseline.GetParagraph(_heavyDirtyParagraphIndex).Text ?? string.Empty;
    }

    [Benchmark(Baseline = true)]
    public SyncMetrics FullSync_PlainDocument()
    {
        var (mutatedDocument, targetFlowDocument) = PreparePlainMutation();
        var converted = _converter.Convert(mutatedDocument);
        ReplaceBlocks(converted, targetFlowDocument);
        return new SyncMetrics(targetFlowDocument.Blocks.Count, incrementalApplied: false);
    }

    [Benchmark]
    public SyncMetrics IncrementalSync_PlainDocument()
    {
        var (mutatedDocument, targetFlowDocument) = PreparePlainMutation();
        if (!_converter.TryConvertTopLevelBlock(mutatedDocument, _plainDirtyParagraphIndex, out var block))
        {
            var converted = _converter.Convert(mutatedDocument);
            ReplaceBlocks(converted, targetFlowDocument);
            return new SyncMetrics(targetFlowDocument.Blocks.Count, incrementalApplied: false);
        }

        targetFlowDocument.Blocks[_plainDirtyParagraphIndex] = block;
        return new SyncMetrics(targetFlowDocument.Blocks.Count, incrementalApplied: true);
    }

    [Benchmark]
    public SyncMetrics FullSync_HeavyTableDocument()
    {
        var (mutatedDocument, targetFlowDocument) = PrepareHeavyTableMutation();
        var converted = _converter.Convert(mutatedDocument);
        ReplaceBlocks(converted, targetFlowDocument);
        return new SyncMetrics(targetFlowDocument.Blocks.Count, incrementalApplied: false);
    }

    [Benchmark]
    public SyncMetrics IncrementalAttempt_HeavyTableDocument_WithFallback()
    {
        var (mutatedDocument, targetFlowDocument) = PrepareHeavyTableMutation();
        if (CanApplyIncrementalTopLevelParagraphSync(mutatedDocument, targetFlowDocument, _heavyDirtyParagraphIndex)
            && _converter.TryConvertTopLevelBlock(mutatedDocument, _heavyDirtyParagraphIndex, out var block))
        {
            targetFlowDocument.Blocks[_heavyDirtyParagraphIndex] = block;
            return new SyncMetrics(targetFlowDocument.Blocks.Count, incrementalApplied: true);
        }

        var converted = _converter.Convert(mutatedDocument);
        ReplaceBlocks(converted, targetFlowDocument);
        return new SyncMetrics(targetFlowDocument.Blocks.Count, incrementalApplied: false);
    }

    private (Document MutatedDocument, FlowDocumentModel TargetFlowDocument) PreparePlainMutation()
    {
        var mutated = DocumentClone.Clone(_plainBaseline);
        MutateParagraph(mutated, _plainDirtyParagraphIndex, _plainOriginalText);
        var target = _converter.Convert(_plainBaseline);
        return (mutated, target);
    }

    private (Document MutatedDocument, FlowDocumentModel TargetFlowDocument) PrepareHeavyTableMutation()
    {
        var mutated = DocumentClone.Clone(_heavyTableBaseline);
        MutateParagraph(mutated, _heavyDirtyParagraphIndex, _heavyOriginalText);
        var target = _converter.Convert(_heavyTableBaseline);
        return (mutated, target);
    }

    private static void ReplaceBlocks(FlowDocumentModel source, FlowDocumentModel target)
    {
        target.Blocks.Clear();
        while (source.Blocks.Count > 0)
        {
            var block = source.Blocks[0];
            source.Blocks.RemoveAt(0);
            target.Blocks.Add(block);
        }
    }

    private static bool CanApplyIncrementalTopLevelParagraphSync(Document source, FlowDocumentModel target, int dirtyParagraphIndex)
    {
        if (source.Blocks.Count == 0
            || source.Blocks.Count != target.Blocks.Count
            || source.ParagraphCount != source.Blocks.Count
            || (uint)dirtyParagraphIndex >= (uint)source.Blocks.Count)
        {
            return false;
        }

        for (var index = 0; index < source.Blocks.Count; index++)
        {
            if (source.Blocks[index] is not ParagraphBlock paragraph || paragraph.ListInfo is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static void MutateParagraph(Document document, int paragraphIndex, string original)
    {
        var paragraph = document.GetParagraph(paragraphIndex);
        paragraph.Text = original + MutationSuffix;
    }

    public readonly struct SyncMetrics
    {
        public int BlockCount { get; }
        public bool IncrementalApplied { get; }

        public SyncMetrics(int blockCount, bool incrementalApplied)
        {
            BlockCount = blockCount;
            IncrementalApplied = incrementalApplied;
        }

        public override string ToString()
        {
            return $"Blocks={BlockCount},Incremental={IncrementalApplied}";
        }
    }

    private sealed class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            AddColumnProvider(DefaultColumnProviders.Instance);
            AddExporter(MarkdownExporter.GitHub);
            AddDiagnoser(MemoryDiagnoser.Default);
        }
    }
}
