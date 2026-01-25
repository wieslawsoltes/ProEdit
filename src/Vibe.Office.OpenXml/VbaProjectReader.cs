using System.Text;
using OpenMcdf;
using Vibe.Office.Documents;

namespace Vibe.Office.OpenXml;

internal static class VbaProjectReader
{
    private const ushort RecordProjectCodePage = 0x0003;
    private const ushort RecordProjectModules = 0x0013;
    private const ushort RecordModuleName = 0x0019;
    private const ushort RecordModuleStreamName = 0x001A;
    private const ushort RecordModuleDocString = 0x001C;
    private const ushort RecordModuleOffset = 0x0031;
    private const ushort RecordModuleStreamNameUnicode = 0x0032;
    private const ushort RecordModuleNameUnicode = 0x0047;
    private const ushort RecordModuleTerminator = 0x002B;

    private static readonly HashSet<string> NonModuleStreams = new(StringComparer.OrdinalIgnoreCase)
    {
        "dir",
        "_VBA_PROJECT",
        "PROJECT",
        "PROJECTwm"
    };

    public static List<VbaModuleInfo> ListModules(byte[] data)
    {
        var project = ReadProject(data);
        return project.Modules;
    }

    public static VbaProjectInfo ReadProject(byte[] data)
    {
        var modules = new List<VbaModuleInfo>();
        if (data.Length == 0)
        {
            return new VbaProjectInfo(1252, modules);
        }

        var projectCodePage = 1252;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var stream = new MemoryStream(data, writable: false);
            using var compound = new CompoundFile(stream);
            var root = compound.RootStorage;
            CFStorage vbaStorage;
            try
            {
                vbaStorage = root.GetStorage("VBA");
            }
            catch (Exception)
            {
                return new VbaProjectInfo(1252, modules);
            }

            var dirStream = TryGetStream(vbaStorage, "dir");
            if (dirStream is null)
            {
                return new VbaProjectInfo(1252, modules);
            }

            var dirCompressed = dirStream.GetData() ?? Array.Empty<byte>();
            var dirDecompressed = VbaCompression.Decompress(dirCompressed);
            var metadata = ParseDirStream(dirDecompressed);
            projectCodePage = metadata.CodePage;
            var encoding = ResolveEncoding(metadata.CodePage);

            foreach (var module in metadata.Modules)
            {
                var streamName = module.StreamName;
                if (string.IsNullOrWhiteSpace(streamName))
                {
                    continue;
                }

                var moduleStream = TryGetStream(vbaStorage, streamName);
                if (moduleStream is null)
                {
                    continue;
                }

                var dataBytes = moduleStream.GetData() ?? Array.Empty<byte>();
                if (module.TextOffset < 0 || module.TextOffset >= dataBytes.Length)
                {
                    continue;
                }

                var compressed = dataBytes.AsSpan(module.TextOffset).ToArray();
                var decompressed = VbaCompression.Decompress(compressed);
                var source = encoding.GetString(decompressed);

                var info = new VbaModuleInfo
                {
                    Name = module.Name ?? streamName,
                    StreamName = streamName,
                    Source = source
                };

                foreach (var procedure in ExtractProcedures(source))
                {
                    info.Procedures.Add(procedure);
                }

                modules.Add(info);
            }
        }
        catch (Exception)
        {
            return new VbaProjectInfo(1252, modules);
        }

        return new VbaProjectInfo(projectCodePage, modules);
    }

    private static Encoding ResolveEncoding(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception)
        {
            return Encoding.GetEncoding(1252);
        }
    }

    private static CFStream? TryGetStream(CFStorage storage, string name)
    {
        try
        {
            return storage.GetStream(name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static VbaProjectMetadata ParseDirStream(byte[] data)
    {
        var modules = new List<ModuleMetadata>();
        var codePage = 1252;
        var current = (ModuleMetadata?)null;

        using var reader = new BinaryReader(new MemoryStream(data, writable: false));
        while (reader.BaseStream.Position + 6 <= reader.BaseStream.Length)
        {
            var id = reader.ReadUInt16();
            var size = reader.ReadUInt32();
            if (size > int.MaxValue)
            {
                break;
            }

            if (reader.BaseStream.Position + size > reader.BaseStream.Length)
            {
                break;
            }

            var payload = reader.ReadBytes((int)size);
            switch (id)
            {
                case RecordProjectCodePage:
                    if (payload.Length >= 2)
                    {
                        codePage = BitConverter.ToUInt16(payload, 0);
                    }
                    break;
                case RecordProjectModules:
                    current = null;
                    break;
                case RecordModuleName:
                    FinalizeModule(ref current, modules);
                    current = new ModuleMetadata
                    {
                        Name = DecodeText(payload, codePage)
                    };
                    break;
                case RecordModuleNameUnicode:
                    if (current is not null && string.IsNullOrWhiteSpace(current.Name))
                    {
                        current.Name = DecodeUnicode(payload);
                    }
                    break;
                case RecordModuleStreamName:
                    if (current is not null)
                    {
                        current.StreamName = DecodeText(payload, codePage);
                    }
                    break;
                case RecordModuleStreamNameUnicode:
                    if (current is not null && string.IsNullOrWhiteSpace(current.StreamName))
                    {
                        current.StreamName = DecodeUnicode(payload);
                    }
                    break;
                case RecordModuleDocString:
                    break;
                case RecordModuleOffset:
                    if (current is not null && payload.Length >= 4)
                    {
                        current.TextOffset = BitConverter.ToInt32(payload, 0);
                    }
                    break;
                case RecordModuleTerminator:
                    FinalizeModule(ref current, modules);
                    break;
            }
        }

        FinalizeModule(ref current, modules);
        return new VbaProjectMetadata(codePage, modules);
    }

    private static void FinalizeModule(ref ModuleMetadata? current, List<ModuleMetadata> modules)
    {
        if (current is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(current.StreamName))
        {
            current.StreamName = current.StreamName.TrimEnd('\0');
        }

        if (!string.IsNullOrWhiteSpace(current.Name))
        {
            current.Name = current.Name.TrimEnd('\0');
        }

        modules.Add(current);
        current = null;
    }

    private static string DecodeText(byte[] data, int codePage)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var encoding = ResolveEncoding(codePage);
        var text = encoding.GetString(data);
        return text.TrimEnd('\0');
    }

    private static string DecodeUnicode(byte[] data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var text = Encoding.Unicode.GetString(data);
        return text.TrimEnd('\0');
    }

    private static IEnumerable<string> ExtractProcedures(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            yield break;
        }

        var reader = new StringReader(source);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("'", StringComparison.Ordinal))
            {
                continue;
            }

            var words = trimmed.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
            {
                continue;
            }

            var index = 0;
            while (index < words.Length)
            {
                var token = words[index];
                if (IsAccessModifier(token))
                {
                    index++;
                    continue;
                }

                if (IsProcedureKeyword(token))
                {
                    if (index + 1 < words.Length)
                    {
                        var name = words[index + 1];
                        var parenIndex = name.IndexOf('(');
                        if (parenIndex > 0)
                        {
                            name = name[..parenIndex];
                        }

                        if (!string.IsNullOrWhiteSpace(name) && !name.Equals("Sub", StringComparison.OrdinalIgnoreCase))
                        {
                            yield return name;
                        }
                    }

                    break;
                }

                if (token.Equals("End", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                break;
            }
        }
    }

    private static bool IsAccessModifier(string token)
    {
        return token.Equals("Public", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Private", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Friend", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Static", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProcedureKeyword(string token)
    {
        return token.Equals("Sub", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Function", StringComparison.OrdinalIgnoreCase)
               || token.Equals("Property", StringComparison.OrdinalIgnoreCase);
    }

    public sealed record VbaProjectInfo(int CodePage, List<VbaModuleInfo> Modules);

    private sealed record ModuleMetadata
    {
        public string? Name { get; set; }
        public string? StreamName { get; set; }
        public int TextOffset { get; set; }
    }

    private sealed record VbaProjectMetadata(int CodePage, List<ModuleMetadata> Modules);
}
