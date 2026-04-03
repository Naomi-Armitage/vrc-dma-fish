using System.Text.RegularExpressions;
using VrcDmaFish.Models;

namespace VrcDmaFish.Providers;

public sealed class Il2CppInspectorProResolver
{
    private static readonly Regex TypeDeclarationRegex = new(
        @"^\s*(?:\[[^\]]+\]\s*)*(?:public|internal|private|protected|abstract|sealed|static|unsafe|partial|\s)*\b(?:class|struct)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex OffsetAttributeRegex = new(
        @"\[(?:global::)?(?:[\w\.]+)?(?<kind>FieldOffset|StaticFieldOffset)\s*\(\s*(?:Offset\s*=\s*)?(?:(?:""0x(?<hex>[0-9A-Fa-f]+)"")|0x(?<hex2>[0-9A-Fa-f]+)|(?<dec>\d+))",
        RegexOptions.Compiled);

    private static readonly Regex FieldDeclarationRegex = new(
        @"^\s*(?:public|private|protected|internal)\s+(?:new\s+|static\s+|readonly\s+|volatile\s+|unsafe\s+|const\s+|ref\s+|partial\s+)*(?:global::)?[\w<>\[\],\.\?\s]+\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\s*(?:;|=)",
        RegexOptions.Compiled);

    public bool TryResolve(
        Il2CppInspectorFieldSelection selection,
        out ResolvedIl2CppInspectorLayout layout,
        out string failureReason)
    {
        layout = default!;
        failureReason = string.Empty;

        var inputPath = Path.GetFullPath(selection.CSharpOutputPath);
        var candidateFiles = ResolveCandidateFiles(inputPath);
        if (candidateFiles.Count == 0)
        {
            failureReason = $"未找到 Il2CppInspectorPro 导出的 C# 文件：{inputPath}";
            return false;
        }

        var requestedTypeName = ExtractSimpleTypeName(selection.TargetTypeName);
        foreach (var filePath in candidateFiles)
        {
            if (TryExtractTypeLayout(filePath, requestedTypeName, out layout))
            {
                return true;
            }
        }

        failureReason = $"在 {inputPath} 中未找到类型 {selection.TargetTypeName}。";
        return false;
    }

    private static IReadOnlyList<string> ResolveCandidateFiles(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            return new[] { inputPath };
        }

        if (Directory.Exists(inputPath))
        {
            return Directory.EnumerateFiles(inputPath, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static bool TryExtractTypeLayout(
        string filePath,
        string requestedTypeName,
        out ResolvedIl2CppInspectorLayout layout)
    {
        layout = default!;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(filePath);
        }
        catch
        {
            return false;
        }

        var fieldOffsets = new Dictionary<string, Il2CppInspectorFieldOffset>(StringComparer.OrdinalIgnoreCase);
        var braceDepth = 0;
        var targetBaseDepth = -1;
        var pendingTarget = false;
        var inTarget = false;
        var pendingOffset = default(Il2CppInspectorFieldOffset?);

        foreach (var line in lines)
        {
            if (!inTarget && !pendingTarget && IsMatchingTypeDeclaration(line, requestedTypeName))
            {
                pendingTarget = true;
                targetBaseDepth = braceDepth;
                pendingOffset = null;
            }
            else if (inTarget)
            {
                if (TryParseOffsetAttribute(line, out var offsetAttribute))
                {
                    pendingOffset = offsetAttribute;
                }

                if (TryParseFieldDeclaration(line, out var fieldName))
                {
                    if (pendingOffset is Il2CppInspectorFieldOffset fieldOffset)
                    {
                        fieldOffsets[fieldName] = fieldOffset with { DeclaredFieldName = fieldName };
                    }

                    pendingOffset = null;
                }
                else if (!string.IsNullOrWhiteSpace(line) &&
                         !line.TrimStart().StartsWith("[", StringComparison.Ordinal) &&
                         !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    pendingOffset = null;
                }
            }

            braceDepth += CountBraceDelta(line);

            if (pendingTarget && braceDepth > targetBaseDepth)
            {
                pendingTarget = false;
                inTarget = true;
                continue;
            }

            if (inTarget && braceDepth <= targetBaseDepth)
            {
                if (fieldOffsets.Count == 0)
                {
                    return false;
                }

                layout = new ResolvedIl2CppInspectorLayout(filePath, requestedTypeName, fieldOffsets);
                return true;
            }
        }

        if (inTarget && fieldOffsets.Count > 0)
        {
            layout = new ResolvedIl2CppInspectorLayout(filePath, requestedTypeName, fieldOffsets);
            return true;
        }

        return false;
    }

    private static bool IsMatchingTypeDeclaration(string line, string requestedTypeName)
    {
        var match = TypeDeclarationRegex.Match(line);
        return match.Success &&
               string.Equals(match.Groups["name"].Value, requestedTypeName, StringComparison.Ordinal);
    }

    private static bool TryParseOffsetAttribute(string line, out Il2CppInspectorFieldOffset fieldOffset)
    {
        fieldOffset = default;

        var match = OffsetAttributeRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var offsetText = match.Groups["hex"].Success
            ? match.Groups["hex"].Value
            : match.Groups["hex2"].Success
                ? match.Groups["hex2"].Value
                : match.Groups["dec"].Value;

        var numberStyle = match.Groups["dec"].Success
            ? System.Globalization.NumberStyles.Integer
            : System.Globalization.NumberStyles.HexNumber;

        if (!ulong.TryParse(offsetText, numberStyle, System.Globalization.CultureInfo.InvariantCulture, out var offset))
        {
            return false;
        }

        var kind = match.Groups["kind"].Value;
        fieldOffset = new Il2CppInspectorFieldOffset(
            DeclaredFieldName: string.Empty,
            Offset: offset,
            IsStatic: string.Equals(kind, "StaticFieldOffset", StringComparison.Ordinal));
        return true;
    }

    private static bool TryParseFieldDeclaration(string line, out string fieldName)
    {
        fieldName = string.Empty;
        var match = FieldDeclarationRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        fieldName = match.Groups["name"].Value.TrimStart('@');
        return !string.IsNullOrWhiteSpace(fieldName);
    }

    private static int CountBraceDelta(string line)
    {
        var delta = 0;
        foreach (var ch in line)
        {
            if (ch == '{')
            {
                delta++;
            }
            else if (ch == '}')
            {
                delta--;
            }
        }

        return delta;
    }

    private static string ExtractSimpleTypeName(string typeName)
    {
        var trimmed = typeName.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot >= 0 ? trimmed[(lastDot + 1)..] : trimmed;
    }
}

public sealed class ResolvedIl2CppInspectorLayout
{
    private readonly Dictionary<string, Il2CppInspectorFieldOffset> _fieldOffsets;

    public ResolvedIl2CppInspectorLayout(
        string sourcePath,
        string typeName,
        IReadOnlyDictionary<string, Il2CppInspectorFieldOffset> fieldOffsets)
    {
        SourcePath = sourcePath;
        TypeName = typeName;
        _fieldOffsets = new Dictionary<string, Il2CppInspectorFieldOffset>(fieldOffsets, StringComparer.OrdinalIgnoreCase);
    }

    public string SourcePath { get; }

    public string TypeName { get; }

    public IEnumerable<Il2CppInspectorFieldOffset> Fields => _fieldOffsets.Values;

    public bool TryGetField(string fieldName, out Il2CppInspectorFieldOffset fieldOffset) =>
        _fieldOffsets.TryGetValue(fieldName, out fieldOffset);
}

public readonly record struct Il2CppInspectorFieldOffset(string DeclaredFieldName, ulong Offset, bool IsStatic);
