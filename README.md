# PowerPositionReports

Background service that generates intra-day power position reports for energy traders.
Fetches day-ahead trade positions from the trading system, aggregates volumes by hour
and writes the result to a CSV file on a configurable schedule.

---

## Requirements

- .NET 8 or higher
- `Axpo.PowerService.dll` (provided separately, must be placed in the project folder)

---

## Configuration

All settings are in `appsettings.json`:

```json
{
  "PowerPositionReport": {
    "FolderPath": "C:\\Temp\\PowerReports",
    "TimeIntervalMinutes": 1,
    "MaxRetries": 10,
    "RetryDelaySeconds": 5
  }
}
```

| Setting | Description |
|---|---|
| `FolderPath` | Folder where CSV files are saved |
| `TimeIntervalMinutes` | How often the extract runs (in minutes) |
| `MaxRetries` | Number of retry attempts if the trading system fails |
| `RetryDelaySeconds` | Seconds to wait between retries |

---

## How to run

```bash
dotnet build
dotnet run
```

The first extract runs immediately on startup. Subsequent extracts run every `TimeIntervalMinutes`.

---

## Output

CSV files are saved to the configured `FolderPath` with the following format:

**Filename:** `PowerPosition_YYYYMMDD_HHmm.csv`  
**Example:** `PowerPosition_20260622_1430.csv`

**Content:**

Local Time,Volume
23:00,150
00:00,150
01:00,80
...
22:00,80

---

## Logs

Logs are written to console and to daily rolling files under the `logs/` folder:

logs/20260622.log

Log files are retained for 30 days.

---

## Design decisions

**London timezone throughout**  
All dates, report filenames and market-day boundaries use Europe/London time, regardless
of where the application is deployed. This ensures consistent behaviour and filenames
across any server location.

**Day-ahead convention**  
`GetTrades()` is called with tomorrow's date. Period 1 starts at 23:00 on the previous
calendar day, so the report always covers the next full market day (23:00 → 22:00).

**Retry strategy**  
On `PowerServiceException` the extract retries up to `MaxRetries` times with a delay
between attempts. If all retries fail, a Critical log is written so monitoring systems
can alert. Unexpected errors abort immediately as retrying is unlikely to help.

**No missed extracts**  
`nextRun` is advanced before the extract executes. If an extract takes longer than the
interval, the scheduler catches up without skipping any slot.