using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace SA.Inbetween.Architecture.Tests;

public enum Layer
{
    Soma = 0,
    Contract = 1,
    Engine = 2,
    Generators = 3,
}

public static class ArchitectureRules
{
    private const string Prefix = "SA.Inbetween.";

    private static readonly string[] CoreBannedNamespaces =
    {
        "System.IO",
        "System.Net",
        "System.Net.Sockets",
        "System.Net.Http",
        "UnityEngine",
        "Microsoft.ML.OnnxRuntime",
    };

    private static readonly string[] CoreBannedTypes =
    {
        "System.Console",
        "System.Diagnostics.Process",
        "System.Diagnostics.Stopwatch",
        "System.Environment",
        "System.DateTime.Now",
    };

    public static IReadOnlyList<string> Violations(string assemblyPath, Layer layer)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var found = new List<string>();

        foreach (var handle in reader.AssemblyReferences)
        {
            var name = reader.GetString(reader.GetAssemblyReference(handle).Name);
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var target = name.Substring(Prefix.Length);
            if (!Enum.TryParse<Layer>(target, out var targetLayer) || targetLayer >= layer)
            {
                found.Add($"{layer} must not reference {name}");
            }
        }

        if (layer == Layer.Generators)
        {
            return found;
        }

        foreach (var handle in reader.TypeReferences)
        {
            var type = reader.GetTypeReference(handle);
            var ns = reader.GetString(type.Namespace);
            var full = ns + "." + reader.GetString(type.Name);
            if (CoreBannedNamespaces.Any(b => ns == b || ns.StartsWith(b + ".", StringComparison.Ordinal))
                || CoreBannedTypes.Contains(full))
            {
                found.Add($"{layer} must not use {full}");
            }
        }

        return found.Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();
    }

    public static string PathOf(Layer layer) =>
        Path.Combine(AppContext.BaseDirectory, Prefix + layer + ".dll");
}

public class ArchitectureSpec
{
    [Theory]
    [InlineData(Layer.Soma)]
    [InlineData(Layer.Contract)]
    [InlineData(Layer.Engine)]
    [InlineData(Layer.Generators)]
    public void Dependencies_point_inward_and_the_core_has_no_effects(Layer layer) =>
        Assert.Empty(ArchitectureRules.Violations(ArchitectureRules.PathOf(layer), layer));

    [Fact]
    public void Planted_wrong_way_reference_and_effect_are_detected()
    {
        var planted = Path.Combine(AppContext.BaseDirectory, "Planted.WrongWay.dll");
        Assert.True(File.Exists(planted), planted);
        var violations = ArchitectureRules.Violations(planted, Layer.Soma);
        Assert.Contains("Soma must not reference SA.Inbetween.Engine", violations);
        Assert.Contains("Soma must not use System.IO.File", violations);
        Assert.Contains("Contract must not reference SA.Inbetween.Engine", ArchitectureRules.Violations(planted, Layer.Contract));
        Assert.Empty(ArchitectureRules.Violations(planted, Layer.Generators));
    }

    [Fact]
    public void Core_projects_target_netstandard21_and_net10()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SA.Inbetween.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var props = File.ReadAllText(Path.Combine(root.FullName, "Directory.Build.props"));
        Assert.Contains("<TargetFrameworks>netstandard2.1;net10.0</TargetFrameworks>", props, StringComparison.Ordinal);
        Assert.Contains("<LangVersion>10.0</LangVersion>", props, StringComparison.Ordinal);
    }
}
