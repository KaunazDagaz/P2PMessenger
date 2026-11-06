using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace P2PMessenger.Core.Tests.Architecture;

public sealed class CoreLayerTests
{
    private static readonly string[] ForbiddenAssemblies =
    [
        "P2PMessenger",
        "Avalonia",
        "Mono.Android",
        "Java.Interop",
        "Microsoft.Maui",
        "Microsoft.Data.Sqlite",
        "SQLitePCLRaw",
        "System.Net",
    ];

    private static readonly string[] ForbiddenNamespaces =
    [
        "System.Net",
        "System.IO.Pipes",
        "System.IO.Ports",
        "System.IO.MemoryMappedFiles",
        "System.IO.IsolatedStorage",
        "Microsoft.Win32",
        "Avalonia",
        "Android",
        "Java",
    ];

    private static readonly string[] ForbiddenTypes =
    [
        "System.IO.File",
        "System.IO.FileInfo",
        "System.IO.FileStream",
        "System.IO.FileSystemInfo",
        "System.IO.FileSystemWatcher",
        "System.IO.Directory",
        "System.IO.DirectoryInfo",
        "System.IO.DriveInfo",
        "System.IO.Path",
        "System.Console",
        "System.Diagnostics.Process",
        "System.Runtime.InteropServices.NativeLibrary",
    ];

    [Fact]
    public void CoreAssembly_Inspected_UsesNoOtherLayerNoIoAndNoPlatformApi()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "architecture", "P2PMessenger.Core.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{path} is missing; the CopyCoreAssemblyForArchitectureTest target of P2PMessenger.Core.Tests.csproj copies it there on build.", path);
        }

        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        if (reader.TypeDefinitions.Any(handle => reader.StringComparer.StartsWith(reader.GetTypeDefinition(handle).Namespace, "Coverlet.")))
        {
            throw new InvalidOperationException($"{path} was instrumented for code coverage, so its references are not Core's own.");
        }

        var assemblies = reader.AssemblyReferences
            .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
            .Where(assembly => ForbiddenAssemblies.Any(name => IsNameOrChild(assembly, name)))
            .Select(assembly => $"Core references the assembly {assembly}.");

        var types = reader.TypeReferences
            .Select(handle => FullName(reader, handle))
            .Where(type => ForbiddenNamespaces.Any(ns => IsNameOrChild(type, ns)) || ForbiddenTypes.Any(name => IsTypeOrNested(type, name)))
            .Select(type => $"Core uses {type}.");

        var nativeMethods = reader.MethodDefinitions
            .Select(reader.GetMethodDefinition)
            .Where(method => (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .Select(method => $"Core declares the native method {reader.GetString(reader.GetTypeDefinition(method.GetDeclaringType()).Name)}.{reader.GetString(method.Name)}.");

        var violations = assemblies.Concat(types).Concat(nativeMethods).ToList();

        Assert.Empty(violations);
    }

    private static string FullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);

        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return FullName(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
        }

        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static bool IsNameOrChild(string name, string prefix) =>
        name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal);

    private static bool IsTypeOrNested(string typeName, string type) =>
        typeName == type || typeName.StartsWith(type + "+", StringComparison.Ordinal);
}
