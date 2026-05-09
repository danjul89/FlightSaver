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

    private static readonly string TilesBase = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlightSaver", "tiles");

    private static readonly HttpClient Http = CreateClient();

    private readonly ConcurrentDictionary<(int z, int x, int y), BitmapSource> _memory = new();
    private readonly ConcurrentDictionary<(int z, int x, int y), byte> _inflight = new();
    private readonly ConcurrentDictionary<(int z, int x, int y), byte> _failed = new();

    private string _theme = "dark";

    public string Theme
    {
        get => _theme;
        set
        {
            var normalized = string.Equals(value, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
            if (_theme == normalized) return;
            _theme = normalized;
            _memory.Clear();
            _failed.Clear();
        }
    }

    private string Variant => _theme == "light" ? "light_nolabels" : "dark_nolabels";

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
        var variantAtFetch = Variant;
        _ = Task.Run(async () =>
        {
            try
            {
                var url = $"https://a.basemaps.cartocdn.com/{variantAtFetch}/{key.z}/{key.x}/{key.y}.png";
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
        Path.Combine(TilesBase, Variant, z.ToString(), x.ToString(), $"{y}.png");

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
