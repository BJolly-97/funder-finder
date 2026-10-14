# Funder Finder

A small Windows tray panel that collects UK grant and funding calls into one list, in the same style
as Master Tasks. A keyword filter tuned for AI, modelling and simulation in materials science decides
what shows. Each morning the app scans automatically and reports anything new and relevant. **Early
framework.** The structure, styling and features are all expected to change.

## Sources

| Source | What it covers | How it's read | Show everything by default |
|---|---|---|---|
| Innovation Funding Service | Innovate UK competitions, mostly for businesses | HTML search pages (no feed or API exists) | no |
| UKRI Funding Finder | EPSRC, STFC, NERC and the other councils, plus cross-council calls, mostly for academics | HTML listing of open and upcoming calls (the RSS feed only holds the latest 20) | no |
| Henry Royce Institute | Royce's own calls: access schemes, MATcelerate | "Current Funding Opportunities" cards on the funding page (no dates, so shown as "See page") | yes |
| Royal Academy of Engineering | Research and industrial fellowships, chairs, international programmes | Each programme page, for its "Application deadline" | yes |
| Royal Society | Fellowships, industry fellowships, international schemes | Grant listings page, open cards only | yes |
| Leverhulme Trust | Research project grants, fellowships and prizes (any discipline) | "Schemes at a glance", schemes that aren't marked closed | yes |
| Faraday Institution | Battery industry fellowships and sprints | Opportunities page signposts (no dates) | yes |
| ARIA | High-risk programme calls | Open calls on the funding opportunities page | no |
| Horizon Europe | All open and forthcoming EU topics, which the UK can apply to as an associated country | The Funding & Tenders portal's public search API (titles and call codes only, so the filter sees less text) | no |

Innovate UK competitions appear on both IFS and UKRI. The scanner merges them by comparing the words in
each title, so a call is listed once even when the two sites word its title differently.

**Show everything from this source** (on the Sources tab) skips the keyword filter for that source. It
suits sources whose schemes are open to materials work without saying so, such as "Research
Fellowships" or "any discipline", which the filter would otherwise hide. These calls still get a
relevance score, so they rank sensibly against everything else.

## Views

- **Opportunities**: open calls that match your filter, in two sections: *Due within two weeks*
  and *General*. Each section is ranked by relevance: strong matches first, then keyword score
  (title hits count double), then closing date. Each row shows the keywords it matched, with *NEW*
  on calls you haven't seen yet or first saw today. Tick *Include non-matching* to see everything,
  with the non-matching calls dimmed.
- **Shortlist**: the calls you've shortlisted with the leaf, ranked the same way, with a *Closed*
  section at the bottom. Tick the calls you want, then use **Export to PDF** to save a shareable
  A4 document. Each call's title is a link, and its full web address is printed underneath.
- **Seen**: matching calls move here 7 days after you first had them on screen in Opportunities.
  The clock only starts while the window is open on that tab, so nothing ages out while you're
  away. ↺ brings a call back for another week. (The 7 days is `SeenAfterDays` in `data.json`.)
- **Sources**: turn individual sources on or off.
- **Filter** (right-hand side): edit the keyword groups.

Click anywhere on a call to open its page. Use the leaf to shortlist it, or ✕ to hide it for good.

## The keyword filter

Keywords are matched against each call's title and summary as whole words, ignoring case and treating
singular and plural alike. Hyphens and spaces count as the same ("thin film" matches "thin-films"). A
trailing `*` matches any ending, so `metallurg*` matches metallurgy and metallurgical.

Groups are fully editable on the Filter tab. Click a group's name to rename it, use **+ Add group** for
a new one, or ✕ to remove one. The app isn't tied to materials science: make the groups whatever suits
your field. The starting groups (also what *Reset to defaults* restores) are:

- **Materials** and **AI, modelling & digital**: a match in either is enough for a call to show.
- **Sectors** and **General**: these only push calls that already matched further up the list.
  Words like *research* or *engineering* appear in almost every call, so on their own they'd let
  everything through.

A call that matches terms from two or more of the first kind of group is a **strong match**. With the
default groups, that means digital methods applied to materials.

Any group can be switched to count on its own. *Reset to defaults* restores the starting groups.

## Daily scan

While the app is running, it scans once a day after 9:00. If you start it later than that, it scans
as soon as it starts. When something new matches, a tray notification tells you. The app doesn't
start with Windows yet. Add a shortcut to the Startup folder for that.

## Adding a source

Implement `IFundingSource` (`FunderFinder/Services/Sources/`) and add it to
`FundingScanner.AllSources`. Order matters: when two sources list the same call, the one earlier in
the list wins.

## Where things live

| To change...             | Look in                                   |
|--------------------------|-------------------------------------------|
| Colours                  | `Styles/Theme.xaml`                       |
| Shared control styles    | `Styles/Controls.xaml`                    |
| Layout, row templates    | `MainWindow.xaml`                         |
| Behaviour                | `MainWindow.xaml.cs`                      |
| Default keywords         | `Models/KeywordProfile.cs`                |
| Matching rules           | `Services/KeywordMatcher.cs`              |
| Fetching and merging     | `Services/FundingScanner.cs`              |
| PDF export layout        | `Services/ShortlistPdfExporter.cs`        |

## Building

Requires the .NET 8 SDK. From `FunderFinder/`:

```
dotnet build -c Release
```

Then run `bin\Release\net8.0-windows\FunderFinder.exe`. For a taskbar pin, right-click the exe (or
a desktop shortcut to it) and choose *Pin to taskbar*. Windows doesn't let apps pin themselves. The
icon is `Assets\icon.ico`, drawn from the shapes in `Assets\icon.svg`. Data, including your edited keywords, is
stored at `%AppData%\FunderFinder\data.json`.
