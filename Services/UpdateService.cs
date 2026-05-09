using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FlightSaver.Services;

public sealed class UpdateService
{
    public static readonly UpdateService Instance = new();

    private const string ReleasesUrl = "https://api.github.com/repos/danjul89/FlightSaver/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    public string CurrentVersion { get; } = ResolveCurrentVersion();
    public string? LatestVersion { get; private set; }
    public string? LatestDownloadUrl { get; private set; }
    public string? LatestNotes { get; private set; }
    public bool IsUpdateAvailable { get; private set; }
    public bool IsChecking { get; private set; }
    public DateTime LastCheckUtc { get; private set; }
    public string? LastError { get; private set; }

    public event EventHandler? StatusChanged;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FlightSaver/1.0 (hobby Windows screensaver)");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    private static string ResolveCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        IsChecking = true;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            using var resp = await Http.GetAsync(ReleasesUrl, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                LatestVersion = null;
                LatestDownloadUrl = null;
                IsUpdateAvailable = false;
                LastError = null;
                return;
            }
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"HTTP {(int)resp.StatusCode}";
                return;
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var version = tag.TrimStart('v', 'V');
            LatestVersion = version;
            LatestNotes = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() : null;

            string? scrUrl = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (!name.EndsWith(".scr", StringComparison.OrdinalIgnoreCase)) continue;
                    if (asset.TryGetProperty("browser_download_url", out var u))
                    {
                        scrUrl = u.GetString();
                        break;
                    }
                }
            }
            LatestDownloadUrl = scrUrl;
            IsUpdateAvailable = TryParseVersion(version, out var latest) &&
                                TryParseVersion(CurrentVersion, out var current) &&
                                latest > current;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            LastCheckUtc = DateTime.UtcNow;
            IsChecking = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool TryParseVersion(string s, out Version v)
    {
        v = new Version(0, 0);
        var parts = s.Split('.');
        if (parts.Length is < 2 or > 4) return false;
        var padded = string.Join('.', parts) + (parts.Length < 3 ? ".0" : "");
        return Version.TryParse(padded, out v!);
    }

    public async Task<string?> DownloadAsync(IProgress<double>? progress, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(LatestDownloadUrl)) return null;
        var tempPath = Path.Combine(Path.GetTempPath(), "FlightSaver-update.scr");
        try
        {
            using var resp = await Http.GetAsync(LatestDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(tempPath);
            var buf = new byte[64 * 1024];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
            return tempPath;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Spawns an elevated PowerShell script that waits for any FlightSaver.scr process
    /// to exit, then copies the downloaded binary to System32. Returns true if the
    /// elevated process was launched (UAC accepted); false if the user dismissed UAC.
    /// </summary>
    public bool LaunchInstaller(string newScrPath)
    {
        if (string.IsNullOrEmpty(newScrPath) || !File.Exists(newScrPath)) return false;

        var scriptPath = Path.Combine(Path.GetTempPath(), "FlightSaver-update.ps1");
        var script = $@"$ErrorActionPreference = 'Continue'
Get-Process -Name 'FlightSaver.scr' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
$attempts = 0
while ($attempts -lt 30) {{
    try {{
        Copy-Item -LiteralPath '{newScrPath}' -Destination 'C:\Windows\System32\FlightSaver.scr' -Force -ErrorAction Stop
        break
    }} catch {{
        Start-Sleep -Seconds 1
        $attempts++
    }}
}}
";
        File.WriteAllText(scriptPath, script);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
