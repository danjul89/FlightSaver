using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace FlightSaver.Services;

public sealed record AircraftPhoto(BitmapSource Image, string Photographer);

public sealed class PhotoCache
{
    public static readonly PhotoCache Instance = new();

    private static string CacheRoot => Path.Combine(CacheManager.RootPath, "photos");

    private static readonly HttpClient Http = CreateClient();

    private readonly ConcurrentDictionary<string, AircraftPhoto> _memory = new();
    private readonly ConcurrentDictionary<string, byte> _negative = new();
    private readonly ConcurrentDictionary<string, byte> _inflight = new();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FlightSaver/1.0 (hobby Windows screensaver)");
        return c;
    }

    public AircraftPhoto? TryGet(string icao24)
    {
        var key = icao24.ToLowerInvariant();
        if (_memory.TryGetValue(key, out var entry)) return entry;
        if (_negative.ContainsKey(key)) return null;

        var imagePath = Path.Combine(CacheRoot, $"{key}.jpg");
        var metaPath = Path.Combine(CacheRoot, $"{key}.txt");
        if (File.Exists(imagePath))
        {
            try
            {
                var bi = LoadFromFile(imagePath);
                var photographer = File.Exists(metaPath) ? File.ReadAllText(metaPath).Trim() : "";
                var loaded = new AircraftPhoto(bi, photographer);
                _memory[key] = loaded;
                return loaded;
            }
            catch
            {
                try { File.Delete(imagePath); } catch { }
            }
        }

        Fetch(key);
        return null;
    }

    private void Fetch(string icao24)
    {
        if (!_inflight.TryAdd(icao24, 0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var apiUrl = $"https://api.planespotters.net/pub/photos/hex/{icao24}";
                using var apiResp = await Http.GetAsync(apiUrl).ConfigureAwait(false);
                if (!apiResp.IsSuccessStatusCode) { _negative.TryAdd(icao24, 0); return; }
                var json = await apiResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("photos", out var photos) ||
                    photos.ValueKind != JsonValueKind.Array || photos.GetArrayLength() == 0)
                {
                    _negative.TryAdd(icao24, 0);
                    return;
                }

                var first = photos[0];
                var thumb = first.TryGetProperty("thumbnail_large", out var tl) ? tl
                    : first.TryGetProperty("thumbnail", out var ts) ? ts
                    : default;
                if (thumb.ValueKind != JsonValueKind.Object ||
                    !thumb.TryGetProperty("src", out var srcEl) ||
                    srcEl.GetString() is not { } imageUrl ||
                    string.IsNullOrEmpty(imageUrl))
                {
                    _negative.TryAdd(icao24, 0);
                    return;
                }

                var photographer = first.TryGetProperty("photographer", out var pe) ? pe.GetString() ?? "" : "";

                var imageBytes = await Http.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
                Directory.CreateDirectory(CacheRoot);
                var imagePath = Path.Combine(CacheRoot, $"{icao24}.jpg");
                var metaPath = Path.Combine(CacheRoot, $"{icao24}.txt");
                await File.WriteAllBytesAsync(imagePath, imageBytes).ConfigureAwait(false);
                await File.WriteAllTextAsync(metaPath, photographer).ConfigureAwait(false);

                var bi = LoadFromFile(imagePath);
                _memory[icao24] = new AircraftPhoto(bi, photographer);
            }
            catch
            {
                _negative.TryAdd(icao24, 0);
            }
            finally
            {
                _inflight.TryRemove(icao24, out _);
            }
        });
    }

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
