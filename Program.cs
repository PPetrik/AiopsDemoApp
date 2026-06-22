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

app.MapGet("/", async (HttpContext context, IConfiguration config, ILogger<Program> logger) =>
{
    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var userAgent = context.Request.Headers.UserAgent.ToString();
    var serverHost = Environment.MachineName;
    var visitTime = DateTime.Now;

    var visits = new List<(DateTime VisitTime, string ClientIp, string UserAgent, string ServerHost)>();

    try
    {
        var connectionString = config.GetConnectionString("AiopsLabDb");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string insertSql = @"
        INSERT INTO PageVisits (VisitTime, ClientIp, UserAgent, ServerHost)
        VALUES (@VisitTime, @ClientIp, @UserAgent, @ServerHost);";

        await using (var insertCommand = new SqlCommand(insertSql, connection))
        {
            insertCommand.Parameters.AddWithValue("@VisitTime", visitTime);
            insertCommand.Parameters.AddWithValue("@ClientIp", clientIp);
            insertCommand.Parameters.AddWithValue("@UserAgent", userAgent);
            insertCommand.Parameters.AddWithValue("@ServerHost", serverHost);

            await insertCommand.ExecuteNonQueryAsync();
        }

        const string selectSql = @"
        SELECT TOP 5 VisitTime, ClientIp, UserAgent, ServerHost
        FROM PageVisits
        ORDER BY VisitTime DESC;";

        await using var selectCommand = new SqlCommand(selectSql, connection);
        await using var reader = await selectCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            visits.Add((
                reader.GetDateTime(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3)
            ));
        }

        logger.LogInformation("Root page visited from {ClientIp}", clientIp);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to process root page visit");
    }

    var rows = string.Join("", visits.Select(v => $"""
        <tr>
        <td>{System.Net.WebUtility.HtmlEncode(v.VisitTime.ToString("yyyy-MM-dd HH:mm:ss"))}</td>
        <td>{System.Net.WebUtility.HtmlEncode(v.ClientIp)}</td>
        <td>{System.Net.WebUtility.HtmlEncode(v.ServerHost)}</td>
        <td>{System.Net.WebUtility.HtmlEncode(v.UserAgent)}</td>
        </tr>
        """));

    var html = $$"""
        <!doctype html>
        <html>
        <head>
            <meta charset="utf-8">
            <title>AiopsDemoApp</title>
            <style>
                body {
                    font-family: Segoe UI, Arial, sans-serif;
                    margin: 40px;
                    background: #f5f5f5;
                }
                .card {
                    background: white;
                    padding: 24px;
                    border-radius: 8px;
                    box-shadow: 0 2px 8px rgba(0,0,0,.08);
                    margin-bottom: 24px;
                }
                table {
                    border-collapse: collapse;
                    width: 100%;
                    background: white;
                }
                th, td {
                    border: 1px solid #ddd;
                    padding: 8px;
                    vertical-align: top;
                }
                th {
                    background: #eee;
                }
                code {
                    background: #eee;
                    padding: 2px 4px;
                    border-radius: 4px;
                }
            </style>
            </head>
            <body>
            <div class="card">
                <h1>AiopsDemoApp</h1>
                <p><b>Status:</b> running</p>
                <p><b>Server:</b> <code>{{serverHost}}</code></p>
                <p><b>Client IP:</b> <code>{{clientIp}}</code></p>
                <p><b>User-Agent:</b> <code>{{userAgent}}</code></p>
                <p><b>Time:</b> <code>{{visitTime:yyyy-MM-dd HH:mm:ss}}</code></p>
            </div>

            <div class="card">
                <h2>Last 5 visits</h2>
                <table>
                    <tr>
                        <th>Visit time</th>
                        <th>Client IP</th>
                        <th>Server</th>
                        <th>User-Agent</th>
                    </tr>
                    {{rows}}
                </table>
            </div>

            <div class="card">
                <h2>API endpoints</h2>
                <p><a href="/api/health">/api/health</a></p>
                <p><a href="/api/dbcheck">/api/dbcheck</a></p>
                <p><a href="/api/status">/api/status</a></p>
                <p><a href="/api/slow?delay=3000">/api/slow?delay=3000</a></p>
                <p><a href="/api/error">/api/error</a></p>
                <p><a href="/swagger">/swagger</a></p>
            </div>
        </body>
        </html>
        """;

    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();