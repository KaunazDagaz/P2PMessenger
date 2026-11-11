using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NSec.Cryptography;

namespace P2PMessenger.Spikes.Nearby;

public static class EnvironmentReport
{
    public static void Print()
    {
        Console.WriteLine($"OS: {RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}; process {RuntimeInformation.ProcessArchitecture}; page size {Environment.SystemPageSize} bytes");
        Console.WriteLine($".NET: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.RuntimeIdentifier}); code generation at run time: {RuntimeFeature.IsDynamicCodeSupported}");
        Console.WriteLine($"NSec.Cryptography {VersionOf(typeof(Key).Assembly)}");
        Console.WriteLine("Started at " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
    }

    private static string VersionOf(Assembly assembly)
    {
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
        return version is null ? "(unknown version)" : version.Split('+')[0];
    }
}
