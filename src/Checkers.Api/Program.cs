using System.Text.Json.Serialization;
using Checkers.Application;
using Checkers.Engine;

var builder = WebApplication.CreateBuilder(args);

// One JSON object per log line.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});

// Application layer.
var configuration = builder.Configuration;
var cache = configuration.GetSection("Cache").Get<CacheOptions>() ?? new CacheOptions();
var limits = configuration.GetSection("Limits").Get<LimitsOptions>() ?? new LimitsOptions();
var levels = configuration.GetSection("Levels").Get<LevelsOptions>() ?? new LevelsOptions();

builder.Services.AddSingleton(limits);
builder.Services.AddSingleton(new LevelPolicy(levels, limits));
builder.Services.AddSingleton(new LruTtlCache<string, SuggestResult>(
    cache.Capacity, TimeSpan.FromMinutes(cache.TtlMinutes), TimeProvider.System));
builder.Services.AddSingleton<MoveSuggestService>();
builder.Services.AddSingleton<PositionService>();

// Infrastructure: the KingsRow worker pool, started in the background when the app starts.
builder.Services.AddKingsRowEngine(configuration);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();   // anything unexpected becomes a 500 problem response
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

app.Run();
