namespace Ax206Display.DataSources.Weather;

public interface IWeatherSource
{
    Task<WeatherSnapshot> GetCurrentWeatherAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
