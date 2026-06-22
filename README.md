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
    "TimeIntervalMinutes": "15",
    "FolderPath": "C:\\Reports\\PowerPosition",
    "MaxRetries": "5",
    "RetryDelaySeconds": "10"
  }
}
```

| Setting | Description |
|---|---|
| `TimeIntervalMinutes` | How often the extract runs (in minutes) |
| `FolderPath` | Folder where CSV files are saved |
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