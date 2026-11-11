using System.Globalization;
using System.Text.Json;
using Windows.Devices.Geolocation;

namespace EdgeDashboard.Weather;

public sealed record WeatherNow(double TempC, int Code, bool IsDay, double HighC, double LowC)
{
    public double TempF => ToF(TempC);
    public double HighF => ToF(HighC);
    public double LowF => ToF(LowC);
    private static double ToF(double c) => c * 9 / 5 + 32;

    /// <summary>Plain-language condition for the WMO weather code.</summary>
    public string Condition => Code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Cloudy",
        45 or 48 => "Fog",
        51 or 53 or 55 or 56 or 57 => "Drizzle",
        61 or 80 => "Light rain",
        63 or 81 => "Rain",
        65 or 82 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 or 85 => "Light snow",
        73 or 77 => "Snow",
        75 or 86 => "Heavy snow",
        95 or 96 or 99 => "Thunderstorm",
        _ => "",
    };

    public string Emoji => Code switch
    {
        0 or 1 => IsDay ? "☀️" : "🌙",
        2 => IsDay ? "⛅" : "☁️",
        3 => "☁️",
        45 or 48 => "🌫️",
        >= 51 and <= 67 or >= 80 and <= 82 => "🌧️",
        >= 71 and <= 77 or 85 or 86 => "🌨️",
        >= 95 => "⛈️",
        _ => "🌡️",
    };
}

/// <summary>
/// Current weather for where Windows says this PC is. The position comes from Windows' location
/// setting; the forecast comes from Open-Meteo (no account or key). Only the position, rounded to
/// about a kilometre, is sent.
/// </summary>
public static class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static (double Lat, double Lon)? _place;

    /// <summary>Returns the weather, or a short reason it could not be read.</summary>
    public static async Task<(WeatherNow? Weather, string? Problem)> GetAsync()
    {
        if (_place is null)
        {
            try
            {
                try { await Geolocator.RequestAccessAsync(); } catch (Exception) { /* desktop apps may not need to ask */ }
                var position = await new Geolocator { DesiredAccuracyInMeters = 5000 }.GetGeopositionAsync(TimeSpan.FromHours(6), TimeSpan.FromSeconds(20));
                var p = position.Coordinate.Point.Position;
                _place = (Math.Round(p.Latitude, 2), Math.Round(p.Longitude, 2));
            }
            catch (Exception) { return (null, "Windows location is unavailable"); }
        }

        try
        {
            var (lat, lon) = _place.Value;
            string url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var current = doc.RootElement.GetProperty("current");
            var daily = doc.RootElement.GetProperty("daily");
            return (new WeatherNow(
                current.GetProperty("temperature_2m").GetDouble(),
                current.GetProperty("weather_code").GetInt32(),
                current.GetProperty("is_day").GetInt32() == 1,
                daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                daily.GetProperty("temperature_2m_min")[0].GetDouble()), null);
        }
        catch (Exception) { return (null, "Weather service is unreachable"); }
    }
}
