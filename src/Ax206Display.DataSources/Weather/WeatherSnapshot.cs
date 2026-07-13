namespace Ax206Display.DataSources.Weather;

public sealed record WeatherSnapshot
{
    public required double TemperatureCelsius { get; init; }

    public required double WindSpeedKmh { get; init; }

    public required int WeatherCode { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }
}
