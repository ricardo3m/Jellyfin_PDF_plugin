using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.Pdf;

/// <summary>
/// Preloads native libraries required by SkiaSharp and PDFtoImage when the assembly is loaded.
///
/// Jellyfin's PluginManager scans all *.dll files recursively in the plugin folder and tries to
/// load them as managed assemblies. Native DLLs (libSkiaSharp, pdfium) cause a
/// BadImageFormatException which disables the entire plugin. To prevent this, the build renames
/// native files to *.native (e.g. libSkiaSharp.dll.native) so the *.dll glob skips them.
/// This class loads the renamed files via NativeLibrary.Load() and registers a DllImportResolver
/// on SkiaSharp/PDFtoImage assemblies so their P/Invoke calls resolve to the preloaded handles.
/// </summary>
internal static class NativeLibraryLoader
{
    private static readonly Dictionary<string, IntPtr> _handles = new(StringComparer.OrdinalIgnoreCase);

    [ModuleInitializer]
    internal static void Initialize()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(NativeLibraryLoader).Assembly.Location) ?? string.Empty;
        if (string.IsNullOrEmpty(assemblyDir))
            return;

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

        // Try arch-specific RID first (e.g. linux-x64), then OS-only (e.g. osx for universal binaries)
        var rids = new[] { $"{os}-{arch}", os };

        // Map P/Invoke library name → renamed native file name.
        // Note: libSkiaSharp is intentionally excluded — the plugin uses Jellyfin's
        // bundled SkiaSharp to avoid cross-context type conflicts.
        var libs = new (string PInvokeName, string FileName)[]
        {
            ("pdfium", GetNativeFileName(os, "pdfium")),
        };

        foreach (var (pinvokeName, fileName) in libs)
        {
            var handle = TryLoad(assemblyDir, rids, fileName);
            if (handle != IntPtr.Zero)
                _handles[pinvokeName] = handle;
        }

        // Register DllImportResolver on SkiaSharp/pdfium assemblies so their P/Invoke calls
        // resolve to the handles we loaded above. Hook both already-loaded and future assemblies.
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryRegisterResolver(e.LoadedAssembly);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            TryRegisterResolver(asm);

        // Remove runtime folders that don't belong to this platform. The zip ships all
        // platform binaries; this one-time cleanup reclaims the disk space on first load.
        PruneUnusedRuntimes(assemblyDir, rids);
    }

    private static void TryRegisterResolver(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? string.Empty;
        if (!name.Contains("Pdfium", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("PDFtoImage", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            NativeLibrary.SetDllImportResolver(assembly, DllImportResolver);
        }
        catch (InvalidOperationException)
        {
            // Resolver already registered for this assembly — ignore.
        }
    }

    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        foreach (var (key, handle) in _handles)
        {
            if (libraryName.Contains(key, StringComparison.OrdinalIgnoreCase))
                return handle;
        }

        return IntPtr.Zero;
    }

    private static string GetNativeFileName(string os, string baseName)
    {
        // Files are renamed by the MSBuild target: original extension + ".native"
        return os switch
        {
            "win" => $"{baseName}.dll.native",
            "osx" => $"{baseName}.dylib.native",
            _     => $"{baseName}.so.native",
        };
    }

    private static void PruneUnusedRuntimes(string baseDir, string[] ridsToKeep)
    {
        var runtimesDir = Path.Combine(baseDir, "runtimes");
        if (!Directory.Exists(runtimesDir))
            return;

        foreach (var dir in Directory.GetDirectories(runtimesDir))
        {
            var rid = Path.GetFileName(dir);
            if (Array.Exists(ridsToKeep, r => string.Equals(r, rid, StringComparison.OrdinalIgnoreCase)))
                continue;

            try { Directory.Delete(dir, recursive: true); }
            catch { /* Best-effort — ignore permission errors or locked files. */ }
        }
    }

    private static IntPtr TryLoad(string baseDir, string[] rids, string fileName)
    {
        foreach (var rid in rids)
        {
            var path = Path.Combine(baseDir, "runtimes", rid, "native", fileName);
            if (File.Exists(path))
            {
                try
                {
                    return NativeLibrary.Load(path);
                }
                catch
                {
                    // Missing system dependencies or wrong architecture — try next RID.
                }
            }
        }

        return IntPtr.Zero;
    }
}
