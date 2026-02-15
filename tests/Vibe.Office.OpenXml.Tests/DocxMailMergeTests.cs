using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Vibe.Office.OpenXml;
using VibeDocs = Vibe.Office.Documents;
using VibeDocument = Vibe.Office.Documents.Document;
using Xunit;

namespace Vibe.Office.OpenXml.Tests;

public sealed class DocxMailMergeTests
{
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Theory]
    [InlineData("ADDRESSBLOCK", VibeDocs.FieldKind.AddressBlock)]
    [InlineData("GREETINGLINE", VibeDocs.FieldKind.GreetingLine)]
    [InlineData("IF \"City\" = \"Paris\" \"A\" \"B\"", VibeDocs.FieldKind.MergeRule)]
    [InlineData("NEXT", VibeDocs.FieldKind.MergeRule)]
    [InlineData("NEXTIF \"City\" = \"Paris\"", VibeDocs.FieldKind.MergeRule)]
    [InlineData("SKIPIF \"City\" = \"Paris\"", VibeDocs.FieldKind.MergeRule)]
    public void FieldInstructionParser_ClassifiesAdditionalMailMergeInstructions(string instruction, VibeDocs.FieldKind expectedKind)
    {
        var definition = VibeDocs.FieldInstructionParser.Parse(instruction);
        Assert.NotNull(definition);
        Assert.Equal(expectedKind, definition!.Kind);
    }

    [Fact]
    public void Importer_ClassifiesMergeField_AndPopulatesMailMergeData()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateMergeFieldDoc(inputPath);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            var paragraph = Assert.IsType<VibeDocs.ParagraphBlock>(document.Blocks.First());
            var fieldStart = paragraph.Inlines.OfType<VibeDocs.FieldStartInline>().First();

            Assert.NotNull(fieldStart.Definition);
            Assert.Equal(VibeDocs.FieldKind.MergeField, fieldStart.Definition!.Kind);
            Assert.Equal("First Name", fieldStart.Definition.Arguments.First().Value);

            Assert.NotNull(document.MailMergeData);
            Assert.Contains("First Name", document.MailMergeData!.FieldNames);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void Importer_DetectsMailMergeFromAddressBlockField_WithoutMergeSettings()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateSimpleFieldDoc(inputPath, "ADDRESSBLOCK \\f \"{0}\"", "Address block");

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            var paragraph = Assert.IsType<VibeDocs.ParagraphBlock>(document.Blocks.First());
            var fieldStart = paragraph.Inlines.OfType<VibeDocs.FieldStartInline>().First();

            Assert.NotNull(fieldStart.Definition);
            Assert.Equal(VibeDocs.FieldKind.AddressBlock, fieldStart.Definition!.Kind);

            Assert.NotNull(document.MailMergeData);
            Assert.Equal(VibeDocs.MailMergeData.DefaultMainDocumentType, document.MailMergeData!.MainDocumentType);
            Assert.Empty(document.MailMergeData.FieldNames);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void Importer_DoesNotDetectMailMergeFromGenericIfFieldWithoutMergeFieldOperand()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateSimpleFieldDoc(inputPath, "IF 1 = 1 \"Yes\" \"No\"", "Yes");

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            var paragraph = Assert.IsType<VibeDocs.ParagraphBlock>(document.Blocks.First());
            var fieldStart = paragraph.Inlines.OfType<VibeDocs.FieldStartInline>().First();
            Assert.NotNull(fieldStart.Definition);
            Assert.Equal(VibeDocs.FieldKind.MergeRule, fieldStart.Definition!.Kind);

            Assert.Null(document.MailMergeData);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void Importer_DetectsMailMergeFromIfFieldWithMergeFieldOperand()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateSimpleFieldDoc(inputPath, "IF \"{ MERGEFIELD City }\" = \"Paris\" \"A\" \"B\"", "A");

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            var paragraph = Assert.IsType<VibeDocs.ParagraphBlock>(document.Blocks.First());
            var fieldStart = paragraph.Inlines.OfType<VibeDocs.FieldStartInline>().First();
            Assert.NotNull(fieldStart.Definition);
            Assert.Equal(VibeDocs.FieldKind.MergeRule, fieldStart.Definition!.Kind);

            Assert.NotNull(document.MailMergeData);
            Assert.Equal(VibeDocs.MailMergeData.DefaultMainDocumentType, document.MailMergeData!.MainDocumentType);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void Importer_DetectsMailMergeSettingsWithoutFields()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateMailMergeSettingsDoc(inputPath);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            Assert.NotNull(document.MailMergeData);
            Assert.Equal("formLetters", document.MailMergeData!.MainDocumentType);
            Assert.Empty(document.MailMergeData!.FieldNames);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void Importer_ReadsMainDocumentTypeAndFieldMapNames_FromMailMergeSettings()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateMailMergeSettingsDoc(inputPath, "mailingLabels", " First Name ", "Last Name", "first name");

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            Assert.NotNull(document.MailMergeData);
            Assert.Equal("mailingLabels", document.MailMergeData!.MainDocumentType);
            Assert.Equal(2, document.MailMergeData.FieldNames.Count);
            Assert.Contains("First Name", document.MailMergeData.FieldNames);
            Assert.Contains("Last Name", document.MailMergeData.FieldNames);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void Exporter_WritesMailMergeSettings_WhenMailMergeDataPresent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var outputPath = Path.Combine(tempRoot, "output.docx");

        try
        {
            var document = new VibeDocument();
            document.Blocks.Clear();
            document.Blocks.Add(new VibeDocs.ParagraphBlock("Body"));
            document.MailMergeData = new VibeDocs.MailMergeData();
            document.MailMergeData.FieldNames.Add("First Name");

            var exporter = new DocxExporter();
            exporter.Save(document, outputPath);

            using var outputDoc = WordprocessingDocument.Open(outputPath, false);
            var settings = outputDoc.MainDocumentPart!.DocumentSettingsPart?.Settings;
            Assert.NotNull(settings);

            var mailMerge = settings!.ChildElements.FirstOrDefault(IsMailMergeElement);
            Assert.NotNull(mailMerge);

            var mainDocumentType = mailMerge!.ChildElements.FirstOrDefault(IsMainDocumentTypeElement);
            Assert.NotNull(mainDocumentType);
            Assert.Equal(
                "formLetters",
                mainDocumentType!.GetAttribute("val", WordprocessingNamespace).Value);

            var fieldNames = ExtractFieldMapNames(mailMerge);
            Assert.Single(fieldNames);
            Assert.Equal("First Name", fieldNames[0]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void Exporter_WritesConfiguredMailMergeMainDocumentTypeAndFieldMapData()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var outputPath = Path.Combine(tempRoot, "output.docx");

        try
        {
            var document = new VibeDocument();
            document.Blocks.Clear();
            document.Blocks.Add(new VibeDocs.ParagraphBlock("Body"));
            document.MailMergeData = new VibeDocs.MailMergeData
            {
                MainDocumentType = "catalog"
            };
            document.MailMergeData.FieldNames.Add(" First Name ");
            document.MailMergeData.FieldNames.Add("Last Name");
            document.MailMergeData.FieldNames.Add("first name");

            var exporter = new DocxExporter();
            exporter.Save(document, outputPath);

            using var outputDoc = WordprocessingDocument.Open(outputPath, false);
            var settings = outputDoc.MainDocumentPart!.DocumentSettingsPart?.Settings;
            Assert.NotNull(settings);

            var mailMerge = settings!.ChildElements.FirstOrDefault(IsMailMergeElement);
            Assert.NotNull(mailMerge);

            var mainDocumentType = mailMerge!.ChildElements.FirstOrDefault(IsMainDocumentTypeElement);
            Assert.NotNull(mainDocumentType);
            Assert.Equal(
                "catalog",
                mainDocumentType!.GetAttribute("val", WordprocessingNamespace).Value);

            var fieldNames = ExtractFieldMapNames(mailMerge);
            Assert.Equal(2, fieldNames.Count);
            Assert.Equal("First Name", fieldNames[0]);
            Assert.Equal("Last Name", fieldNames[1]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static bool IsMailMergeElement(OpenXmlElement element)
    {
        return string.Equals(element.LocalName, "mailMerge", StringComparison.OrdinalIgnoreCase)
               && string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMainDocumentTypeElement(OpenXmlElement element)
    {
        return string.Equals(element.LocalName, "mainDocumentType", StringComparison.OrdinalIgnoreCase)
               && string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ExtractFieldMapNames(OpenXmlElement mailMerge)
    {
        var names = new List<string>();
        foreach (var fieldMapData in mailMerge.Descendants().Where(IsFieldMapDataElement))
        {
            var nameElement = fieldMapData.ChildElements.FirstOrDefault(IsNameElement);
            var value = nameElement?.GetAttribute("val", WordprocessingNamespace).Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            names.Add(value.Trim());
        }

        return names;
    }

    private static bool IsFieldMapDataElement(OpenXmlElement element)
    {
        return string.Equals(element.LocalName, "fieldMapData", StringComparison.OrdinalIgnoreCase)
               && string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNameElement(OpenXmlElement element)
    {
        return string.Equals(element.LocalName, "name", StringComparison.OrdinalIgnoreCase)
               && string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateMergeFieldDoc(string filePath)
    {
        CreateSimpleFieldDoc(filePath, "MERGEFIELD \"First Name\" \\* MERGEFORMAT", "Alice");
    }

    private static void CreateSimpleFieldDoc(string filePath, string instruction, string resultText)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body());

        var field = new SimpleField
        {
            Instruction = instruction
        };
        field.AppendChild(new Run(new Text(resultText)));

        var paragraph = new Paragraph(field);
        mainPart.Document.Body!.AppendChild(paragraph);
        mainPart.Document.Save();
    }

    private static void CreateMailMergeSettingsDoc(string filePath, string mainDocumentTypeValue = "formLetters", params string[] fieldNames)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body(new Paragraph(new Run(new Text("Body")))));

        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        var settings = new Settings();
        var mailMerge = new OpenXmlUnknownElement("w", "mailMerge", WordprocessingNamespace);
        var mainDocumentTypeElement = new OpenXmlUnknownElement("w", "mainDocumentType", WordprocessingNamespace);
        mainDocumentTypeElement.SetAttribute(new OpenXmlAttribute("w", "val", WordprocessingNamespace, mainDocumentTypeValue));
        mailMerge.AppendChild(mainDocumentTypeElement);
        if (fieldNames.Length > 0)
        {
            var odso = new OpenXmlUnknownElement("w", "odso", WordprocessingNamespace);
            for (var i = 0; i < fieldNames.Length; i++)
            {
                var fieldMapData = new OpenXmlUnknownElement("w", "fieldMapData", WordprocessingNamespace);
                var name = new OpenXmlUnknownElement("w", "name", WordprocessingNamespace);
                name.SetAttribute(new OpenXmlAttribute("w", "val", WordprocessingNamespace, fieldNames[i]));
                fieldMapData.AppendChild(name);
                odso.AppendChild(fieldMapData);
            }

            mailMerge.AppendChild(odso);
        }

        settings.AppendChild(mailMerge);
        settingsPart.Settings = settings;

        mainPart.Document.Save();
    }
}
