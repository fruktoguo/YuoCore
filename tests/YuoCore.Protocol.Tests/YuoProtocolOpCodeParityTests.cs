using System.Reflection;
using System.Text.RegularExpressions;

namespace YuoCore.Protocol.Tests;

public sealed class YuoProtocolOpCodeParityTests
{
    [Fact]
    public void Csharp_and_ts_opcode_tables_stay_in_sync()
    {
        var csharpTable = typeof(YuoCoreProtocolOpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral)
            .ToDictionary(
                field => ToCamelCase(field.Name),
                field => (int)field.GetRawConstantValue()!);

        var tsSource = File.ReadAllText(LocateTypeScriptClientSource());
        var tsMatch = Regex.Match(
            tsSource,
            @"YuoProtocolOpCodes\s*=\s*\{(?<body>.*?)\}\s*as const",
            RegexOptions.Singleline);
        Assert.True(tsMatch.Success, "TS client must export YuoProtocolOpCodes table.");

        var tsTable = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match entry in Regex.Matches(tsMatch.Groups["body"].Value, @"(?<name>[a-zA-Z0-9_]+)\s*:\s*(?<value>\d+)"))
        {
            tsTable[entry.Groups["name"].Value] = int.Parse(entry.Groups["value"].Value);
        }

        Assert.Equal(
            csharpTable.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"),
            tsTable.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static string ToCamelCase(string name)
    {
        return string.Concat(name[..1].ToLowerInvariant(), name[1..]);
    }

    private static string LocateTypeScriptClientSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "clients", "web", "yuocore-client-ts", "src", "index.ts");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate clients/web/yuocore-client-ts/src/index.ts from test output directory.");
    }
}
