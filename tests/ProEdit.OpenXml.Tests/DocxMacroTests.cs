using System;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenMcdf;
using ProEdit.Documents;
using ProEdit.OpenXml;
using Xunit;
using ProEditDocument = ProEdit.Documents.Document;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace ProEdit.OpenXml.Tests;

public sealed class DocxMacroTests
{
    [Fact]
    public void Importer_LoadsMacroCustomPartAndVbaProject()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProEditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "macro.docm");

        try
        {
            var module = new VbaModuleFixture("Module1", "Module1", "Sub Hello()\nEnd Sub");
            var vbaProject = CreateVbaProject(module);

            var macroStore = new DocumentMacros { IsTrusted = true };
            macroStore.Items.Add(new MacroDefinition
            {
                Id = Guid.NewGuid(),
                Name = "Hello",
                Language = MacroLanguage.Vba,
                IsTrusted = true,
                Source = module.Source
            });
            macroStore.VbaModules.Add(new VbaModuleInfo
            {
                Name = module.Name,
                Source = module.Source
            });
            macroStore.References.Add(new VbaProjectReference
            {
                Name = "VBA",
                Identifier = "{00000000-0000-0000-0000-000000000000}"
            });

            using (var document = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.MacroEnabledDocument))
            {
                var mainPart = document.AddMainDocumentPart();
                mainPart.Document = new WordDocument(new Body(new Paragraph(new Run(new Text("Hello")))));

                var customPart = mainPart.AddCustomXmlPart(DocxMacroSerializer.MacroCustomPartContentType);
                using (var writer = new StreamWriter(customPart.GetStream(FileMode.Create, FileAccess.Write), Encoding.UTF8))
                {
                    writer.Write(DocxMacroSerializer.Serialize(macroStore));
                }

                var vbaPart = mainPart.AddNewPart<VbaProjectPart>();
                using var vbaStream = vbaPart.GetStream(FileMode.Create, FileAccess.Write);
                vbaStream.Write(vbaProject, 0, vbaProject.Length);
            }

            var importer = new DocxImporter();
            var loaded = importer.Load(filePath);

            Assert.NotNull(loaded.Macros.VbaProject);
            Assert.NotEmpty(loaded.Macros.VbaProject!);
            Assert.Contains(loaded.Macros.Items, macro => macro.Language == MacroLanguage.Vba && macro.Name == "Hello");
            var moduleInfo = loaded.Macros.VbaModules.Single(info => info.Name == "Module1");
            Assert.Equal("Module1", moduleInfo.StreamName);
            Assert.Contains("Sub Hello()", moduleInfo.Source ?? string.Empty);
            Assert.Contains("Hello", moduleInfo.Procedures);
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
    public void Exporter_WritesMacroCustomPartAndVbaProject()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProEditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "export.docm");

        try
        {
            var module = new VbaModuleFixture("Module1", "Module1", "Sub Exported()\nEnd Sub");
            var vbaProject = CreateVbaProject(module);

            var document = new ProEditDocument();
            document.Macros.IsTrusted = true;
            document.Macros.VbaProject = vbaProject;
            document.Macros.Items.Add(new MacroDefinition
            {
                Id = Guid.NewGuid(),
                Name = "Exported",
                Language = MacroLanguage.Vba,
                IsTrusted = true,
                Source = module.Source
            });
            document.Macros.VbaModules.Add(new VbaModuleInfo
            {
                Name = module.Name,
                StreamName = module.StreamName,
                Source = module.Source
            });
            document.Macros.References.Add(new VbaProjectReference
            {
                Name = "VBIDE",
                Identifier = "{11111111-1111-1111-1111-111111111111}"
            });

            var exporter = new DocxExporter();
            exporter.Save(document, filePath);

            using var outputDoc = WordprocessingDocument.Open(filePath, false);
            var mainPart = outputDoc.MainDocumentPart;
            Assert.NotNull(mainPart);

            var macroPart = mainPart!.CustomXmlParts
                .FirstOrDefault(part => string.Equals(part.ContentType, DocxMacroSerializer.MacroCustomPartContentType, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(macroPart);

            var restored = new DocumentMacros();
            using (var reader = new StreamReader(macroPart!.GetStream(FileMode.Open, FileAccess.Read), Encoding.UTF8))
            {
                var json = reader.ReadToEnd();
                Assert.True(DocxMacroSerializer.TryDeserialize(json, restored));
            }

            Assert.Contains(restored.Items, macro => macro.Name == "Exported" && macro.Language == MacroLanguage.Vba);
            Assert.Contains(restored.VbaModules, info => info.Name == "Module1");
            Assert.Contains(restored.References, reference => reference.Name == "VBIDE");

            var vbaPart = mainPart.VbaProjectPart;
            Assert.NotNull(vbaPart);
            using var stream = vbaPart!.GetStream(FileMode.Open, FileAccess.Read);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            Assert.Equal(vbaProject, buffer.ToArray());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static byte[] CreateVbaProject(params VbaModuleFixture[] modules)
    {
        if (modules.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var dirStream = BuildDirStream(modules);
        using var root = RootStorage.CreateInMemory(OpenMcdf.Version.V3, StorageModeFlags.Transacted);
        var vbaStorage = root.CreateStorage("VBA");
        using (var dir = vbaStorage.CreateStream("dir"))
        {
            dir.Write(dirStream);
        }

        foreach (var module in modules)
        {
            var moduleStream = BuildModuleStream(module.Source);
            using var stream = vbaStorage.CreateStream(module.StreamName);
            stream.Write(moduleStream);
        }

        root.Commit();
        using var output = new MemoryStream();
        var baseStream = root.BaseStream;
        if (baseStream.CanSeek)
        {
            baseStream.Position = 0;
        }

        baseStream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] BuildDirStream(IReadOnlyList<VbaModuleFixture> modules)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true))
        {
            WriteRecord(writer, RecordProjectCodePage, BitConverter.GetBytes((ushort)1252));
            WriteRecord(writer, RecordProjectModules, BitConverter.GetBytes((ushort)modules.Count));

            foreach (var module in modules)
            {
                WriteRecord(writer, RecordModuleName, Encoding.ASCII.GetBytes(module.Name));
                WriteRecord(writer, RecordModuleStreamName, Encoding.ASCII.GetBytes(module.StreamName));
                WriteRecord(writer, RecordModuleOffset, BitConverter.GetBytes(0));
                WriteRecord(writer, RecordModuleTerminator, Array.Empty<byte>());
            }
        }

        var raw = buffer.ToArray();
        var output = new byte[raw.Length + 1];
        output[0] = 0x00;
        Buffer.BlockCopy(raw, 0, output, 1, raw.Length);
        return output;
    }

    private static byte[] BuildModuleStream(string source)
    {
        var payload = Encoding.ASCII.GetBytes(source ?? string.Empty);
        var output = new byte[payload.Length + 1];
        output[0] = 0x00;
        Buffer.BlockCopy(payload, 0, output, 1, payload.Length);
        return output;
    }

    private static void WriteRecord(BinaryWriter writer, ushort id, byte[] payload)
    {
        writer.Write(id);
        writer.Write((uint)payload.Length);
        if (payload.Length > 0)
        {
            writer.Write(payload);
        }
    }

    private sealed record VbaModuleFixture(string Name, string StreamName, string Source);

    private const ushort RecordProjectCodePage = 0x0003;
    private const ushort RecordProjectModules = 0x0013;
    private const ushort RecordModuleName = 0x0019;
    private const ushort RecordModuleStreamName = 0x001A;
    private const ushort RecordModuleOffset = 0x0031;
    private const ushort RecordModuleTerminator = 0x002B;
}
