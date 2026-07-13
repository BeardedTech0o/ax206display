using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ax206Display.DataSources.Weather;

/// <summary>
/// Fetches current weather from Open-Meteo (https://open-meteo.com), a free
/// forecast API that requires no API key or account.
/// </summary>
public sealed class OpenMeteoWeatherSource : IWeatherSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _httpClient;

    public OpenMeteoWeatherSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WeatherSnapshot> GetCurrentWeatherAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={latitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&longitude={longitude.ToString(CultureInfo.InvariantCulture)}" +
                  "&current=temperature_2m,wind_speed_10m,weather_code";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var body = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Open-Meteo returned an empty response body.");

        if (body.Current is null)
        {
            throw new InvalidOperationException("Open-Meteo response did not include a 'current' block.");
        }

        return new WeatherSnapshot
        {
            TemperatureCelsius = body.Current.Temperature2m,
            WindSpeedKmh = body.Current.WindSpeed10m,
            WeatherCode = body.Current.WeatherCode,
            ObservedAt = DateTimeOffset.Parse(body.Current.Time, CultureInfo.InvariantCulture),
        };
    }

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("current")]
        public OpenMeteoCurrent? Current { get; set; }
    }

    private sealed class OpenMeteoCurrent
    {
        [JsonPropertyName("time")]
        public string Time { get; set; } = string.Empty;

        [JsonPropertyName("temperature_2m")]
        public double Temperature2m { get; set; }

        [JsonPropertyName("wind_speed_10m")]
        public double WindSpeed10m { get; set; }

        [JsonPropertyName("weather_code")]
        public int WeatherCode { get; set; }
    }
}
