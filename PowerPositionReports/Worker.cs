using Axpo;
using System.Text;
using System.Globalization;

namespace PowerPositionReports
{
    public class Worker : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<Worker> _logger;
        private readonly IPowerService _service;

        // "GMT Standard Time" for Europe/London time.
        // Cached here, it never changes.
        private readonly TimeZoneInfo _londonTz = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

        public Worker(
            ILogger<Worker> logger,
            IConfiguration configuration,
            IPowerService service)
        {
            _logger = logger;
            _configuration = configuration;
            _service = service;
        }

        // Main loop of the background service.
        // Runs an extract immediately on startup, then repeats every
        // intervalMinutes. Uses a lightweight 5-second polling approach.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var intervalMinutes = int.Parse(_configuration["PowerPositionReport:TimeIntervalMinutes"]!);

            if (intervalMinutes <= 0)
            {
                throw new InvalidOperationException(
                    "TimeIntervalMinutes must be greater than zero.");
            }

            var folderPath = _configuration["PowerPositionReport:FolderPath"];

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new InvalidOperationException("FolderPath is not configured in appsettings.");
            }

            // Creates the folder if it doesn't exist yet. Avoids a crash on first run.
            Directory.CreateDirectory(folderPath);

            _logger.LogInformation(
                "Worker started. Interval: {Interval} min. Output folder: {Folder}",
                intervalMinutes, folderPath);

            // Triggers the first extract immediately.
            var nextRun = DateTime.Now;

            // Runs until the service is stopped.
            while (!stoppingToken.IsCancellationRequested)
            {
                // If an extract took too long or the service was paused, this catches up without skipping any slot.
                while (DateTime.Now >= nextRun)
                {
                    // Advance first so a slow extract doesn't run twice for the same slot.
                    nextRun = nextRun.AddMinutes(intervalMinutes);

                    _logger.LogInformation("Scheduled run triggered. Next run at: {NextRun}", nextRun);

                    await GenerateReport(folderPath, stoppingToken);
                }

                // Short sleep to avoid busy-waiting.
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // Gets tomorrow's trades, groups them by hour and writes the CSV.
        // Retries up to MaxRetries times if the trading system fails.
        // Logs a critical error if all retries fail.
        private async Task GenerateReport(string folderPath, CancellationToken stoppingToken)
        {
            // Always work in London time (Europe/London market timezone) regardless of where
            // the application is deployed. This ensures consistent report dates, filenames
            // and market-day boundaries across any server location.
            var nowLondon = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _londonTz);

            // Day-ahead position: traders need tomorrow's data.
            // .Date strips time component before passing to GetTradesAsync().
            var reportDate = nowLondon.Date.AddDays(1);

            _logger.LogInformation(
                "Starting report generation for {ReportDate}. Extract time (London): {NowLondon}.",
                reportDate, nowLondon);

            var maxRetries = int.Parse(_configuration["PowerPositionReport:MaxRetries"] ?? "5");
            var retryDelay = int.Parse(_configuration["PowerPositionReport:RetryDelaySeconds"] ?? "10");

            // Retry loop: PowerService may be transiently unavailable.
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Async call so the thread isn't blocked while waiting for the response.
                    var trades = await _service.GetTradesAsync(reportDate);

                    // Flatten all trade periods across all trades, sum volumes per period and sort by
                    // period number so the CSV rows are always in order (23:00, 00:00, ... , 22:00).
                    var aggregated = trades
                        .SelectMany(t => t.Periods)
                        .GroupBy(p => p.Period)
                        .Select(g => new PowerPosition
                        {
                            Period = g.Key,
                            LocalTime = PeriodToLocalTime(g.Key),
                            Volume = g.Sum(x => x.Volume)
                        })
                        .OrderBy(x => x.Period)
                        .ToList();

                    var csvContent = GenerateCsv(aggregated);

                    var fileName = GenerateFileName(nowLondon);

                    var fullPath = Path.Combine(folderPath, fileName);

                    File.WriteAllText(fullPath, csvContent);

                    _logger.LogInformation(
                        "CSV report generated: {Path}. Report date: {ReportDate}. Extract time (London): {NowLondon}.",
                        fullPath, reportDate, nowLondon);

                    break; // Success, breaks retry/loop.
                }
                catch (PowerServiceException ex)
                {
                    _logger.LogError(ex,
                        "Error fetching trades for {ReportDate}. Extract time (London): {NowLondon}. Attempt {Attempt}/{MaxRetries}. Will retry on next attempt.",
                        reportDate, nowLondon, attempt, maxRetries);

                    if (attempt == maxRetries)
                    {
                        _logger.LogCritical(
                            "All {MaxRetries} attempts failed for {ReportDate}. Extract time (London): {NowLondon}. Extract was NOT generated.",
                            maxRetries, reportDate, nowLondon);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(retryDelay), stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    // Unexpected error, no point retrying.
                    _logger.LogCritical(ex,
                        "Unexpected error generating report for {ReportDate}. Extract time (London): {NowLondon}. Extract was NOT generated.",
                        reportDate, nowLondon);

                    break;
                }
            }
            
        }

        // Converts a period number to a time string.
        // Period 1 = 23:00, Period 2 = 00:00 ... Period 24 = 22:00.
        private static string PeriodToLocalTime(int period)
        {
            int hour = (period + 22) % 24;
            return $"{hour:D2}:00";
        }

        // Builds the CSV content from the aggregated positions.
        // Format: header row + one row per period, comma-separated.
        private static string GenerateCsv(IEnumerable<PowerPosition> positions)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Local Time,Volume");

            foreach (var position in positions)
            {
                sb.AppendLine(
                    $"{position.LocalTime},{position.Volume.ToString(CultureInfo.InvariantCulture)}");
            }

            return sb.ToString();
        }

        // Generates the output filename using London local time of extraction.
        // Format: PowerPosition_YYYYMMDD_HHmm.csv.
        // Using London time (not server local time) ensures consistent filenames
        // regardless of where the application is deployed.
        private static string GenerateFileName(DateTime nowLondon)
        {
            return $"PowerPosition_{nowLondon:yyyyMMdd_HHmm}.csv";
        }
    }
}
