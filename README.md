# Funder Finder

A small Windows tray panel that gathers grant and funding opportunities from a set of sources into
one list, in the same style as Master Tasks. **Early skeleton.** The structure, styling and features
are all expected to change.

## What's here so far

- **Opportunities**: open calls from every enabled source, grouped as closing within two weeks,
  open, or rolling. Calls that have closed are hidden. Click a title to open its web page, star it
  to shortlist it, or press ✕ to hide it for good.
- **Shortlist**: the calls you've starred, closed ones included.
- **Sources**: turn individual sources on or off. Only a made-up *Sample data* source exists so far.
- **Scan**: fetches from every enabled source and merges the results into what's saved. Calls
  already seen are updated but keep their star or dismissal.
- The app lives in the tray and only runs one copy at a time. It uses acrylic blur and the Master
  Tasks palette.

## Adding a source

Implement `IFundingSource` (`FunderFinder/Services/Sources/`) and add it to
`FundingScanner.AllSources`. `SampleSource.cs` is the minimal example.

## Where things live

| To change...             | Look in                                   |
|--------------------------|-------------------------------------------|
| Colours                  | `Styles/Theme.xaml`                       |
| Shared control styles    | `Styles/Controls.xaml`                    |
| Layout, row templates    | `MainWindow.xaml`                         |
| Behaviour                | `MainWindow.xaml.cs`                      |
| Call data and grouping   | `Models/FundingCall.cs`                   |
| Fetch and merge logic    | `Services/FundingScanner.cs`              |

## Building

Requires the .NET 8 SDK. From `FunderFinder/`:

```
dotnet build -c Release
```

Then run `bin\Release\net8.0-windows\FunderFinder.exe`. Data is stored at
`%AppData%\FunderFinder\data.json`.
