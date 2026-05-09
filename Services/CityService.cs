using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace FlightSaver.Services;

public sealed record City(string Name, double Latitude, double Longitude);

public static class CityService
{
    private static IReadOnlyList<City>? _cities;
    public static IReadOnlyList<City> Cities => _cities ??= Load();

    private static IReadOnlyList<City> Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("FlightSaver.Resources.cities.json");
            if (stream is null) return Array.Empty<City>();
            using var doc = JsonDocument.Parse(stream);
            var list = new List<City>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 3) continue;
                var name = entry[0].GetString();
                if (string.IsNullOrEmpty(name)) continue;
                list.Add(new City(name, entry[1].GetDouble(), entry[2].GetDouble()));
            }
            return list;
        }
        catch
        {
            return Array.Empty<City>();
        }
    }
}
