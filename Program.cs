using Microsoft.Data.SqlClient;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = "AiopsDemoApp";
        settings.LogName = "Application";
    });
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/api/health", (ILogger<Program> logger) =>
{
    logger.LogInformation("Health check requested");

    return Results.Ok(new
    {
        status = "OK",
        service = "AiopsDemoApp",
        host = Environment.MachineName,
        time = DateTime.UtcNow
    });
});

app.MapGet("/api/dbcheck", async (IConfiguration config, ILogger<Program> logger) =>
{
    try
    {
        var connectionString = config.GetConnectionString("AiopsLabDb");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("SELECT GETDATE()", connection);
        var dbTime = await command.ExecuteScalarAsync();

        logger.LogInformation("Database check successful");

        return Results.Ok(new
        {
            database = "UP",
            server = "SRV-DB01",
            dbTime
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database check failed");

        return Results.Problem(
            title: "Database check failed",
            detail: ex.Message,
            statusCode: 500
        );
    }
});

app.MapGet("/api/status", async (IConfiguration config, ILogger<Program> logger) =>
{
    try
    {
        var result = new List<object>();
        var connectionString = config.GetConnectionString("AiopsLabDb");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT Id, ServiceName, Status, LastUpdate FROM ServiceStatus";

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new
            {
                id = reader.GetInt32(0),
                serviceName = reader.GetString(1),
                status = reader.GetString(2),
                lastUpdate = reader.GetDateTime(3)
            });
        }

        logger.LogInformation("Service status requested");

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Service status request failed");

        return Results.Problem(
            title: "Service status request failed",
            detail: ex.Message,
            statusCode: 500
        );
    }
});

app.MapGet("/api/error", (ILogger<Program> logger) =>
{
    try
    {
        throw new InvalidOperationException("Synthetic application error generated for AIOps RCA testing");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Synthetic error endpoint triggered");

        return Results.Problem(
            title: "Synthetic application error",
            detail: ex.Message,
            statusCode: 500
        );
    }
});

app.MapGet("/api/slow", async (int delay, ILogger<Program> logger) =>
{
    if (delay < 0)
    {
        delay = 0;
    }

    if (delay > 60000)
    {
        delay = 60000;
    }

    logger.LogWarning("Slow endpoint triggered with delay {Delay} ms", delay);

    var sw = Stopwatch.StartNew();
    await Task.Delay(delay);
    sw.Stop();

    return Results.Ok(new
    {
        status = "OK",
        delay,
        elapsed = sw.ElapsedMilliseconds
    });
});

app.MapGet("/api/info", () =>
{
    return Results.Ok(new
    {
        application = "AiopsDemoApp",
        version = "1.0.0",
        host = Environment.MachineName,
        os = Environment.OSVersion.ToString(),
        time = DateTime.UtcNow
    });
});

app.Run();