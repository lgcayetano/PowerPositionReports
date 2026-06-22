using Axpo;
using PowerPositionReports;
using Serilog;

// Configure Serilog first so startup errors are also logged.
// - Console sink.
// - File sink: daily rolling log retained for 30 days.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog();

builder.Services.AddHostedService<Worker>();

// Register PowerService as a singleton so the same instance is reused
// across all extract cycles.
builder.Services.AddSingleton<IPowerService, PowerService>();

var host = builder.Build();
host.Run();