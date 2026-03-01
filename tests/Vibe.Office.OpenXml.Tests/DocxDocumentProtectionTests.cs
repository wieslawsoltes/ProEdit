using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Vibe.Office.OpenXml;
using VibeDocs = Vibe.Office.Documents;
using Xunit;

namespace Vibe.Office.OpenXml.Tests;

public sealed class DocxDocumentProtectionTests
{
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void Importer_ReadsDocumentProtectionAndFormsDesignSettings()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateProtectedDoc(inputPath);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            Assert.Equal("forms", document.Protection.EditMode);
            Assert.True(document.Protection.Enforcement);
            Assert.False(document.Protection.Formatting);
            Assert.Equal("rsaAES", document.Protection.CryptProviderType);
            Assert.Equal("hash", document.Protection.CryptAlgorithmClass);
            Assert.Equal("typeAny", document.Protection.CryptAlgorithmType);
            Assert.Equal(14, document.Protection.CryptAlgorithmSid);
            Assert.Equal(100000, document.Protection.CryptSpinCount);
            Assert.Equal("QUJDRA==", document.Protection.Hash);
            Assert.Equal("RUZHSA==", document.Protection.Salt);
            Assert.True(document.FormsDesignMode);
            Assert.True(document.ReadOnlyRecommended);
            Assert.False(document.UpdateFieldsOnOpen);
            Assert.Equal(48f, document.DefaultTabStop);
            Assert.True(document.AutoHyphenation);
            Assert.Equal(2, document.ConsecutiveHyphenLimit);
            Assert.Equal(24f, document.HyphenationZone);
            Assert.True(document.DoNotHyphenateCaps);
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
    public void RoundTrip_PreservesDocumentProtectionSettings()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var outputPath = Path.Combine(tempRoot, "output.docx");

        try
        {
            var document = new VibeDocs.Document();
            document.Protection.EditMode = "readOnly";
            document.Protection.Enforcement = true;
            document.Protection.Formatting = false;
            document.Protection.CryptProviderType = "rsaFull";
            document.Protection.CryptAlgorithmClass = "hash";
            document.Protection.CryptAlgorithmType = "typeAny";
            document.Protection.CryptAlgorithmSid = 12;
            document.Protection.CryptSpinCount = 50000;
            document.Protection.Hash = "SGFzaA==";
            document.Protection.Salt = "U2FsdA==";
            document.FormsDesignMode = false;
            document.ReadOnlyRecommended = true;
            document.UpdateFieldsOnOpen = false;
            document.DefaultTabStop = 64f;
            document.AutoHyphenation = true;
            document.ConsecutiveHyphenLimit = 3;
            document.HyphenationZone = 36f;
            document.DoNotHyphenateCaps = true;

            var exporter = new DocxExporter();
            exporter.Save(document, outputPath);

            using (var outputDoc = WordprocessingDocument.Open(outputPath, false))
            {
                var settings = outputDoc.MainDocumentPart?.DocumentSettingsPart?.Settings;
                Assert.NotNull(settings);

                var protection = settings!.ChildElements.FirstOrDefault(IsDocumentProtectionElement);
                Assert.NotNull(protection);
                Assert.Equal("readOnly", ReadWordAttribute(protection!, "edit"));
                Assert.Equal("1", ReadWordAttribute(protection!, "enforcement"));
                Assert.Equal("0", ReadWordAttribute(protection!, "formatting"));
                Assert.Equal("rsaFull", ReadWordAttribute(protection!, "cryptProviderType"));
                Assert.Equal("hash", ReadWordAttribute(protection!, "cryptAlgorithmClass"));
                Assert.Equal("typeAny", ReadWordAttribute(protection!, "cryptAlgorithmType"));
                Assert.Equal("12", ReadWordAttribute(protection!, "cryptAlgorithmSid"));
                Assert.Equal("50000", ReadWordAttribute(protection!, "cryptSpinCount"));
                Assert.Equal("SGFzaA==", ReadWordAttribute(protection!, "hash"));
                Assert.Equal("U2FsdA==", ReadWordAttribute(protection!, "salt"));

                var formsDesign = settings.ChildElements.FirstOrDefault(IsFormsDesignElement);
                Assert.NotNull(formsDesign);
                Assert.Equal("0", ReadWordAttribute(formsDesign!, "val"));

                var readOnlyRecommended = settings.ChildElements.FirstOrDefault(IsReadOnlyRecommendedElement);
                Assert.NotNull(readOnlyRecommended);
                Assert.Equal("1", ReadWordAttribute(readOnlyRecommended!, "val"));

                var writeProtection = FindSettingsChild(settings, "writeProtection");
                Assert.NotNull(writeProtection);
                Assert.Equal("1", ReadWordAttribute(writeProtection!, "recommended"));

                var updateFields = settings.ChildElements.FirstOrDefault(IsUpdateFieldsElement);
                Assert.NotNull(updateFields);
                Assert.Equal("0", ReadWordAttribute(updateFields!, "val"));

                var defaultTabStop = FindSettingsChild(settings, "defaultTabStop");
                Assert.NotNull(defaultTabStop);
                Assert.Equal("960", ReadWordAttribute(defaultTabStop!, "val"));

                var autoHyphenation = FindSettingsChild(settings, "autoHyphenation");
                Assert.NotNull(autoHyphenation);
                Assert.True(IsOnValue(ReadWordAttribute(autoHyphenation!, "val")));

                var consecutiveHyphenLimit = FindSettingsChild(settings, "consecutiveHyphenLimit");
                Assert.NotNull(consecutiveHyphenLimit);
                Assert.Equal("3", ReadWordAttribute(consecutiveHyphenLimit!, "val"));

                var hyphenationZone = FindSettingsChild(settings, "hyphenationZone");
                Assert.NotNull(hyphenationZone);
                Assert.Equal("540", ReadWordAttribute(hyphenationZone!, "val"));

                var doNotHyphenateCaps = FindSettingsChild(settings, "doNotHyphenateCaps");
                Assert.NotNull(doNotHyphenateCaps);
                Assert.True(IsOnValue(ReadWordAttribute(doNotHyphenateCaps!, "val")));
            }

            var importer = new DocxImporter();
            var roundTripped = importer.Load(outputPath);
            Assert.Equal(document.Protection.EditMode, roundTripped.Protection.EditMode);
            Assert.Equal(document.Protection.Enforcement, roundTripped.Protection.Enforcement);
            Assert.Equal(document.Protection.Formatting, roundTripped.Protection.Formatting);
            Assert.Equal(document.Protection.CryptProviderType, roundTripped.Protection.CryptProviderType);
            Assert.Equal(document.Protection.CryptAlgorithmClass, roundTripped.Protection.CryptAlgorithmClass);
            Assert.Equal(document.Protection.CryptAlgorithmType, roundTripped.Protection.CryptAlgorithmType);
            Assert.Equal(document.Protection.CryptAlgorithmSid, roundTripped.Protection.CryptAlgorithmSid);
            Assert.Equal(document.Protection.CryptSpinCount, roundTripped.Protection.CryptSpinCount);
            Assert.Equal(document.Protection.Hash, roundTripped.Protection.Hash);
            Assert.Equal(document.Protection.Salt, roundTripped.Protection.Salt);
            Assert.Equal(document.FormsDesignMode, roundTripped.FormsDesignMode);
            Assert.Equal(document.ReadOnlyRecommended, roundTripped.ReadOnlyRecommended);
            Assert.Equal(document.UpdateFieldsOnOpen, roundTripped.UpdateFieldsOnOpen);
            Assert.Equal(document.DefaultTabStop, roundTripped.DefaultTabStop);
            Assert.Equal(document.AutoHyphenation, roundTripped.AutoHyphenation);
            Assert.Equal(document.ConsecutiveHyphenLimit, roundTripped.ConsecutiveHyphenLimit);
            Assert.Equal(document.HyphenationZone, roundTripped.HyphenationZone);
            Assert.Equal(document.DoNotHyphenateCaps, roundTripped.DoNotHyphenateCaps);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Importer_ReadsReadOnlyRecommended_FromWriteProtectionRecommendedAttribute(bool recommended)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateWriteProtectionRecommendedDoc(inputPath, recommended);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            Assert.Equal(recommended, document.ReadOnlyRecommended);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static void CreateProtectedDoc(string filePath)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text("Protected")))));

        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        var settings = new Settings();

        var protection = new OpenXmlUnknownElement("w", "documentProtection", WordprocessingNamespace);
        SetWordAttribute(protection, "edit", "forms");
        SetWordAttribute(protection, "enforcement", "on");
        SetWordAttribute(protection, "formatting", "off");
        SetWordAttribute(protection, "cryptProviderType", "rsaAES");
        SetWordAttribute(protection, "cryptAlgorithmClass", "hash");
        SetWordAttribute(protection, "cryptAlgorithmType", "typeAny");
        SetWordAttribute(protection, "cryptAlgorithmSid", "14");
        SetWordAttribute(protection, "cryptSpinCount", "100000");
        SetWordAttribute(protection, "hash", "QUJDRA==");
        SetWordAttribute(protection, "salt", "RUZHSA==");
        settings.AppendChild(protection);

        var formsDesign = new OpenXmlUnknownElement("w", "formsDesign", WordprocessingNamespace);
        SetWordAttribute(formsDesign, "val", "1");
        settings.AppendChild(formsDesign);

        var readOnlyRecommended = new OpenXmlUnknownElement("w", "readOnlyRecommended", WordprocessingNamespace);
        SetWordAttribute(readOnlyRecommended, "val", "1");
        settings.AppendChild(readOnlyRecommended);

        var updateFields = new OpenXmlUnknownElement("w", "updateFields", WordprocessingNamespace);
        SetWordAttribute(updateFields, "val", "0");
        settings.AppendChild(updateFields);

        var defaultTabStop = new OpenXmlUnknownElement("w", "defaultTabStop", WordprocessingNamespace);
        SetWordAttribute(defaultTabStop, "val", "720");
        settings.AppendChild(defaultTabStop);

        var autoHyphenation = new OpenXmlUnknownElement("w", "autoHyphenation", WordprocessingNamespace);
        SetWordAttribute(autoHyphenation, "val", "1");
        settings.AppendChild(autoHyphenation);

        var consecutiveHyphenLimit = new OpenXmlUnknownElement("w", "consecutiveHyphenLimit", WordprocessingNamespace);
        SetWordAttribute(consecutiveHyphenLimit, "val", "2");
        settings.AppendChild(consecutiveHyphenLimit);

        var hyphenationZone = new OpenXmlUnknownElement("w", "hyphenationZone", WordprocessingNamespace);
        SetWordAttribute(hyphenationZone, "val", "360");
        settings.AppendChild(hyphenationZone);

        var doNotHyphenateCaps = new OpenXmlUnknownElement("w", "doNotHyphenateCaps", WordprocessingNamespace);
        SetWordAttribute(doNotHyphenateCaps, "val", "1");
        settings.AppendChild(doNotHyphenateCaps);

        settingsPart.Settings = settings;
        mainPart.Document.Save();
    }

    private static void CreateWriteProtectionRecommendedDoc(string filePath, bool recommended)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text("Protected")))));

        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        var settings = new Settings();
        var writeProtection = new OpenXmlUnknownElement("w", "writeProtection", WordprocessingNamespace);
        SetWordAttribute(writeProtection, "recommended", recommended ? "1" : "0");
        settings.AppendChild(writeProtection);
        settingsPart.Settings = settings;
        mainPart.Document.Save();
    }

    private static bool IsDocumentProtectionElement(OpenXmlElement element)
    {
        return string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase)
               && string.Equals(element.LocalName, "documentProtection", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFormsDesignElement(OpenXmlElement element)
    {
        return string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase)
               && string.Equals(element.LocalName, "formsDesign", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReadOnlyRecommendedElement(OpenXmlElement element)
    {
        return string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase)
               && string.Equals(element.LocalName, "readOnlyRecommended", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUpdateFieldsElement(OpenXmlElement element)
    {
        return string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase)
               && string.Equals(element.LocalName, "updateFields", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetWordAttribute(OpenXmlElement element, string localName, string value)
    {
        element.SetAttribute(new OpenXmlAttribute("w", localName, WordprocessingNamespace, value));
    }

    private static string? ReadWordAttribute(OpenXmlElement element, string localName)
    {
        foreach (var attribute in element.GetAttributes())
        {
            if (string.Equals(attribute.LocalName, localName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(attribute.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    private static OpenXmlElement? FindSettingsChild(Settings settings, string localName)
    {
        return settings.ChildElements.FirstOrDefault(element =>
            string.Equals(element.NamespaceUri, WordprocessingNamespace, StringComparison.OrdinalIgnoreCase)
            && string.Equals(element.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOnValue(string? value)
    {
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
