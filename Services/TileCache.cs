using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace FlightSaver.Services;

public sealed class TileCache
{
    public static readonly TileCache Instance = new();

    private static string TilesBase => Path.Combine(CacheManager.RootPath, "tiles");

    private static readonly HttpClient Http = CreateClient();

    private readonly ConcurrentDictionary<(int z, int x, int y), BitmapSource> _memory = new();
    private readonly ConcurrentDictionary<(int z, int x, int y), byte> _inflight = new();
    private readonly ConcurrentDictionary<(int z, int x, int y), byte> _failed = new();

    private string _theme = "satellite";

    public string Theme
    {
        get => _theme;
        set
        {
            var normalized = value?.ToLowerInvariant() switch
            {
                "satellite" => "satellite",
                "light" => "light",
                _ => "dark",
            };
            if (_theme == normalized) return;
            _theme = normalized;
            _memory.Clear();
            _failed.Clear();
        }
    }

    private string CacheSubdir => _theme switch
    {
        "satellite" => "satellite",
        "light" => "light_nolabels",
        _ => "dark_nolabels",
    };

    private string FileExt => _theme == "satellite" ? ".jpg" : ".png";

    private string TileUrl(int z, int x, int y) => _theme switch
    {
        "satellite" => $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
        "light" => $"https://a.basemaps.cartocdn.com/light_nolabels/{z}/{x}/{y}.png",
        _ => $"https://a.basemaps.cartocdn.com/dark_nolabels/{z}/{x}/{y}.png",
    };

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FlightSaver/1.0 (hobby Windows screensaver)");
        return c;
    }

    public BitmapSource? TryGet(int z, int x, int y)
    {
        var key = (z, x, y);
        if (_memory.TryGetValue(key, out var bs)) return bs;
        if (_failed.ContainsKey(key)) return null;

        var path = PathFor(z, x, y);
        if (File.Exists(path))
        {
            try
            {
                var loaded = LoadFromFile(path);
                _memory[key] = loaded;
                return loaded;
            }
            catch
            {
                try { File.Delete(path); } catch { }
            }
        }

        Fetch(key, path);
        return null;
    }

    private void Fetch((int z, int x, int y) key, string path)
    {
        if (!_inflight.TryAdd(key, 0)) return;
        var url = TileUrl(key.z, key.x, key.y);
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            }
            catch
            {
                _failed.TryAdd(key, 0);
            }
            finally
            {
                _inflight.TryRemove(key, out _);
            }
        });
    }

    private string PathFor(int z, int x, int y) =>
        Path.Combine(TilesBase, CacheSubdir, z.ToString(), x.ToString(), $"{y}{FileExt}");

    private static BitmapSource LoadFromFile(string path)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(path);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
