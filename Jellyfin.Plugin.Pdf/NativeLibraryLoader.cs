using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.Pdf;

/// <summary>
/// Preloads native libraries required by SkiaSharp and PDFtoImage when the assembly is loaded.
/// Jellyfin's plugin AssemblyLoadContext does not automatically probe runtimes/{rid}/native/,
/// so without this the P/Invoke calls in libSkiaSharp/libpdfium fail and the plugin malfunctions.
/// </summary>
internal static class NativeLibraryLoader
{
    /// <summary>
    /// Module initializer: runs once when this assembly is first loaded, before any type is used.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(NativeLibraryLoader).Assembly.Location) ?? string.Empty;
        if (string.IsNullOrEmpty(assemblyDir))
        {
            return;
        }

        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
                  : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
                  : "linux";

        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "x64"
        };

        // Try arch-specific RID first (e.g. linux-x64), then OS-only (e.g. osx for SkiaSharp universal binary)
        var rids = new[] { $"{os}-{arch}", os };

        string skiaSharpName = os == "win" ? "libSkiaSharp.dll"
                             : os == "osx" ? "libSkiaSharp.dylib"
                             : "libSkiaSharp.so";

        string pdfiumName = os == "win" ? "pdfium.dll"
                          : os == "osx" ? "libpdfium.dylib"
                          : "libpdfium.so";

        TryLoadFromRuntimes(assemblyDir, rids, skiaSharpName);
        TryLoadFromRuntimes(assemblyDir, rids, pdfiumName);
    }

    private static void TryLoadFromRuntimes(string baseDir, string[] rids, string libName)
    {
        foreach (var rid in rids)
        {
            var path = Path.Combine(baseDir, "runtimes", rid, "native", libName);
            if (File.Exists(path))
            {
                NativeLibrary.Load(path);
                return;
            }
        }
    }
}
