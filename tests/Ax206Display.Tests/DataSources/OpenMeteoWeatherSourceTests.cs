using System.Net;
using System.Text;
using Ax206Display.DataSources.Weather;
using Ax206Display.Tests.TestSupport;

namespace Ax206Display.Tests.DataSources;

public class OpenMeteoWeatherSourceTests
{
    [Fact]
    public async Task GetCurrentWeatherAsync_ParsesCurrentBlock()
    {
        const string json = """
            {
              "latitude": 52.52,
              "longitude": 13.41,
              "current": {
                "time": "2026-07-03T12:00",
                "temperature_2m": 21.4,
                "wind_speed_10m": 9.8,
                "weather_code": 3
              }
            }
            """;

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        var client = new HttpClient(handler);
        var source = new OpenMeteoWeatherSource(client);

        var snapshot = await source.GetCurrentWeatherAsync(52.52, 13.41);

        Assert.Equal(21.4, snapshot.TemperatureCelsius);
        Assert.Equal(9.8, snapshot.WindSpeedKmh);
        Assert.Equal(3, snapshot.WeatherCode);
        Assert.Single(handler.Requests);
        Assert.Contains("latitude=52.52", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_MissingCurrentBlock_Throws()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var source = new OpenMeteoWeatherSource(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetCurrentWeatherAsync(0, 0));
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_HttpError_Throws()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var source = new OpenMeteoWeatherSource(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetCurrentWeatherAsync(0, 0));
    }
}
