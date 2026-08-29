using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Collections.Concurrent;
using GribSharp;

namespace ForecastCenter.Services;

public sealed record HrrrRadarFrame(DateTimeOffset Time, string Image, double[][] Bounds);

/// <summary>Downloads a small NOAA HRRR composite-reflectivity region and renders it for Leaflet.</summary>
public sealed class HrrrRadarService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(18) };
    private readonly Dictionary<string, CacheEntry> _cache = [];

    public HrrrRadarService() => _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.NetworkUserAgent);

    public async Task<IReadOnlyList<HrrrRadarFrame>> GetFramesAsync(
        double latitude,
        double longitude,
        bool snowPalette,
        CancellationToken cancellationToken = default)
    {
        if (latitude is < 21 or > 52 || longitude is < -135 or > -60) return [];

        var bounds = GetBounds(latitude, longitude);
        var key = $"{Math.Round(latitude, 1)}|{Math.Round(longitude, 1)}|{snowPalette}";
        if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.SavedAt < TimeSpan.FromMinutes(45))
            return cached.Frames;

        var probe = await FindLatestCycleAsync(bounds, cancellationToken);
        if (probe is null)
        {
            WriteDiagnostic("No available HRRR cycle was found.");
            return cached?.Frames ?? [];
        }

        var errors = new ConcurrentBag<string>();
        var downloads = Enumerable.Range(1, 7).Select(async hour =>
        {
            try
            {
                await Task.Delay((hour - 1) * 90, cancellationToken);
                var bytes = hour == 1
                    ? probe.FirstHour
                    : await _http.GetByteArrayAsync(BuildUrl(probe.Cycle, hour, bounds), cancellationToken);
                return Decode(bytes, bounds, snowPalette);
            }
            catch (Exception ex)
            {
                errors.Add($"f{hour:00}: {ex.GetType().Name}: {ex.Message}");
                return [];
            }
        });
        var groups = await Task.WhenAll(downloads);
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        var through = DateTimeOffset.UtcNow.AddHours(6.25);
        var frames = groups.SelectMany(group => group)
            .Where(frame => frame.Time >= now && frame.Time <= through)
            .GroupBy(frame => frame.Time)
            .Select(group => group.First())
            .OrderBy(frame => frame.Time)
            .ToList();
        WriteDiagnostic($"Cycle {probe.Cycle:yyyy-MM-dd HH}Z; decoded {groups.Sum(group => group.Count)}; " +
                        $"usable {frames.Count}; errors: {(errors.IsEmpty ? "none" : string.Join(" | ", errors))}");

        if (frames.Count > 0)
        {
            _cache[key] = new(DateTimeOffset.UtcNow, frames);
            while (_cache.Count > 8) _cache.Remove(_cache.OrderBy(pair => pair.Value.SavedAt).First().Key);
        }
        return frames.Count > 0 ? frames : (cached?.Frames ?? []);
    }

    private async Task<CycleProbe?> FindLatestCycleAsync(RadarBounds bounds, CancellationToken cancellationToken)
    {
        var hour = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month,
            DateTimeOffset.UtcNow.Day, DateTimeOffset.UtcNow.Hour, 0, 0, TimeSpan.Zero).AddHours(-1);
        for (var offset = 0; offset < 5; offset++)
        {
            var candidate = hour.AddHours(-offset);
            try
            {
                var bytes = await _http.GetByteArrayAsync(BuildUrl(candidate, 1, bounds), cancellationToken);
                if (bytes.Length > 1000 && bytes.AsSpan(0, 4).SequenceEqual("GRIB"u8)) return new(candidate, bytes);
            }
            catch when (!cancellationToken.IsCancellationRequested) { }
        }
        return null;
    }

    private static string BuildUrl(DateTimeOffset cycle, int forecastHour, RadarBounds bounds)
    {
        var culture = CultureInfo.InvariantCulture;
        return "https://nomads.ncep.noaa.gov/cgi-bin/filter_hrrr_sub.pl" +
               $"?dir=%2Fhrrr.{cycle:yyyyMMdd}%2Fconus" +
               $"&file=hrrr.t{cycle:HH}z.wrfsubhf{forecastHour:00}.grib2" +
               "&var_REFC=on&lev_entire_atmosphere=on&subregion=" +
               $"&leftlon={bounds.West.ToString(culture)}&rightlon={bounds.East.ToString(culture)}" +
               $"&toplat={bounds.North.ToString(culture)}&bottomlat={bounds.South.ToString(culture)}";
    }

    private static IReadOnlyList<HrrrRadarFrame> Decode(byte[] bytes, RadarBounds bounds, bool snowPalette)
    {
        PatchLambertGridTemplate(bytes);
        var file = Grib2Parser.ParseFile(bytes);
        return file.Fields
            .Where(field => field.ParameterName.Contains("reflectivity", StringComparison.OrdinalIgnoreCase))
            .Select(field => new HrrrRadarFrame(
                new DateTimeOffset(field.ReferenceTime, TimeSpan.Zero).AddMinutes(field.ForecastTime),
                RenderDataUri(field.Values, field.Grid.Ni, field.Grid.Nj),
                [[bounds.South, bounds.West], [bounds.North, bounds.East]]))
            .ToList();
    }

    // GribSharp 1.0.16 decodes HRRR's data packing but rejects grid template 3.30.
    // The renderer uses the requested geographic bounds, so substituting template 3.0 lets the
    // library decode values while deliberately ignoring its synthetic grid coordinates.
    private static void PatchLambertGridTemplate(byte[] bytes)
    {
        for (var offset = 0; offset + 14 < bytes.Length; offset++)
        {
            if (bytes[offset + 4] != 3 || bytes[offset + 12] != 0 || bytes[offset + 13] != 30) continue;
            var length = (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
            if (length is >= 70 and <= 100) bytes[offset + 13] = 0;
        }
    }

    private static string RenderDataUri(float[] values, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[Math.Abs(data.Stride) * height];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = height - y - 1;
            for (var x = 0; x < width; x++)
            {
                var value = values[sourceRow * width + x];
                var color = ReflectivityColor(value);
                var target = y * data.Stride + x * 4;
                pixels[target] = color.B;
                pixels[target + 1] = color.G;
                pixels[target + 2] = color.R;
                pixels[target + 3] = color.A;
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private static Color ReflectivityColor(float dbz)
    {
        if (float.IsNaN(dbz) || dbz < 5) return Color.Transparent;
        var alpha = (byte)Math.Clamp(125 + (dbz - 5) * 2.4, 125, 230);
        if (dbz < 10) return Color.FromArgb(alpha, 4, 233, 231);
        if (dbz < 15) return Color.FromArgb(alpha, 1, 160, 246);
        if (dbz < 20) return Color.FromArgb(alpha, 0, 0, 246);
        if (dbz < 25) return Color.FromArgb(alpha, 0, 255, 0);
        if (dbz < 30) return Color.FromArgb(alpha, 0, 200, 0);
        if (dbz < 35) return Color.FromArgb(alpha, 0, 144, 0);
        if (dbz < 40) return Color.FromArgb(alpha, 255, 255, 0);
        if (dbz < 45) return Color.FromArgb(alpha, 231, 192, 0);
        if (dbz < 50) return Color.FromArgb(alpha, 255, 144, 0);
        if (dbz < 55) return Color.FromArgb(alpha, 255, 0, 0);
        if (dbz < 60) return Color.FromArgb(alpha, 214, 0, 0);
        if (dbz < 65) return Color.FromArgb(alpha, 192, 0, 0);
        if (dbz < 70) return Color.FromArgb(alpha, 255, 0, 255);
        return Color.FromArgb(alpha, 153, 85, 201);
    }

    private static RadarBounds GetBounds(double latitude, double longitude) => new(
        Math.Max(21, latitude - 3.25), Math.Min(52, latitude + 3.25),
        Math.Max(-135, longitude - 4.75), Math.Min(-60, longitude + 4.75));

    private readonly record struct RadarBounds(double South, double North, double West, double East);
    private sealed record CycleProbe(DateTimeOffset Cycle, byte[] FirstHour);
    private sealed record CacheEntry(DateTimeOffset SavedAt, IReadOnlyList<HrrrRadarFrame> Frames);

    private static void WriteDiagnostic(string message)
    {
        try
        {
            var folder = AppIdentity.DataRoot;
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "hrrr-radar-status.txt"), $"{DateTimeOffset.Now:O}\n{message}");
        }
        catch { }
    }
}
