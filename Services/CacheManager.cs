using System;
using System.IO;
using System.Linq;

namespace FlightSaver.Services;

/// <summary>
/// Cache size policy:
/// 0  = no persistent cache; data goes to %TEMP%/FlightSaver-cache and is wiped on every startup
/// -1 = unlimited (no eviction); persists in %LocalAppData%/FlightSaver
/// >0 = persistent in %LocalAppData%/FlightSaver, capped at this many MB; oldest files evicted on startup
/// </summary>
public static class CacheManager
{
    private static readonly string PersistentRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlightSaver");

    private static readonly string EphemeralRoot = Path.Combine(
        Path.GetTempPath(), "FlightSaver-cache");

    public static string RootPath { get; private set; } = PersistentRoot;

    public static void Apply(int limitMb)
    {
        if (limitMb == 0)
        {
            RootPath = EphemeralRoot;
            try { if (Directory.Exists(EphemeralRoot)) Directory.Delete(EphemeralRoot, true); } catch { }
            return;
        }

        RootPath = PersistentRoot;
        if (limitMb < 0) return;

        var budgetBytes = (long)limitMb * 1024 * 1024;
        try
        {
            if (!Directory.Exists(PersistentRoot)) return;
            var files = new[] { "tiles", "photos" }
                .Select(s => Path.Combine(PersistentRoot, s))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();

            long running = 0;
            foreach (var fi in files)
            {
                running += fi.Length;
                if (running <= budgetBytes) continue;
                try { fi.Delete(); } catch { }
            }
        }
        catch
        {
            // Best-effort; never block startup.
        }
    }

    public sealed record CacheStats(long TotalBytes, int TileCount, int PhotoCount);

    public static CacheStats GetStats()
    {
        long bytes = 0;
        int tiles = 0;
        int photos = 0;

        try
        {
            var tilesDir = Path.Combine(RootPath, "tiles");
            if (Directory.Exists(tilesDir))
            {
                foreach (var f in Directory.EnumerateFiles(tilesDir, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; tiles++; } catch { }
                }
            }
            var photosDir = Path.Combine(RootPath, "photos");
            if (Directory.Exists(photosDir))
            {
                foreach (var f in Directory.EnumerateFiles(photosDir, "*.jpg", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; photos++; } catch { }
                }
            }
        }
        catch { }

        return new CacheStats(bytes, tiles, photos);
    }

    public static void ClearAll()
    {
        try { var d = Path.Combine(RootPath, "tiles"); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        try { var d = Path.Combine(RootPath, "photos"); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
    }
}
