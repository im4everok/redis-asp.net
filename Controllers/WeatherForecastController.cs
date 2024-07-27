using Microsoft.AspNetCore.Mvc;
using redis_asp.net.Models;
using StackExchange.Redis;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace redis_asp.net.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {

        private readonly ILogger<WeatherForecastController> _logger;
        private readonly IDatabase _redis;
        private readonly IConnectionMultiplexer _multiplexer;
        private readonly HttpClient _httpClient;

        public WeatherForecastController(ILogger<WeatherForecastController> logger, 
            IConnectionMultiplexer multiplexer, 
            HttpClient httpClient)
        {
            _logger = logger;
            _redis = multiplexer.GetDatabase();
            _multiplexer = multiplexer;
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weatherCachingApp", "1.0"));
        }

        private async Task<string> GetForecast(double latitude, double longitude)
        {
            NumberFormatInfo nfi = new ();
            nfi.NumberDecimalSeparator = ".";

            var pointsRequestQuery = $"https://api.weather.gov/points/{latitude.ToString(nfi)},{longitude.ToString(nfi)}"; //get the URI
            var result = await _httpClient.GetFromJsonAsync<JsonObject>(pointsRequestQuery);
            var gridX = result["properties"]["gridX"].ToString();
            var gridY = result["properties"]["gridY"].ToString();
            var gridId = result["Properties"]["gridId"].ToString();
            var forecastRequestQuery = $"https://api.weather.gov/gridpoints/{gridId}/{gridX},{gridY}/forecast";
            var forecastResult = await _httpClient.GetFromJsonAsync<JsonObject>(forecastRequestQuery);
            var periodsJson = forecastResult["properties"]["periods"].ToJsonString();
            return periodsJson;
        }

        [HttpGet("GetWeatherForecast")]
        public async Task<ForecastResult> Get([FromQuery] double latitude, [FromQuery] double longtitude)
        {
            string json;
            var watch = Stopwatch.StartNew();
            var keyName = $"forecast:{latitude},{longtitude}";

            #region clean up cache for testing by pattern: starts with 'forecast'
            //RedisKey[] keys = new RedisKey[1];

            //foreach(var endpoint in _multiplexer.GetEndPoints())
            //{
            //    var server = _multiplexer.GetServer(endpoint);
            //    keys = server.Keys(database: _redis.Database, pattern: "forecast*").ToArray();
            //}

            //await _redis.KeyDeleteAsync(keys);
            #endregion

            json = await _redis.StringGetAsync(keyName);

            if (string.IsNullOrEmpty(json))
            {
                json = await GetForecast(latitude, longtitude);
                var setTask = _redis.StringSetAsync(keyName, json);
                var expireTask = _redis.KeyExpireAsync(keyName, TimeSpan.FromSeconds(3600));
                await Task.WhenAll(setTask, expireTask);
            }

            var forecast =
                JsonSerializer.Deserialize<IEnumerable<WeatherForecast>>(json);

            watch.Stop();
            var result = new ForecastResult(forecast, watch.ElapsedMilliseconds);

            return result;
        }
    }
}