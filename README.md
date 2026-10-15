# Funder Finder

A small desktop app that gathers UK and EU research and innovation funding calls into one list, filters
them by your keywords, and tells you each morning when something new and relevant appears.

It comes in two versions that share the same core:

| Folder | Platform | Built with |
|---|---|---|
| [`FunderFinder_Windows/`](FunderFinder_Windows) | Windows 10/11 | WPF (.NET 8) |
| [`FunderFinder_MacOS/`](FunderFinder_MacOS) | macOS 11+ (Apple Silicon or Intel) | Avalonia (.NET 8) |

**To install it, see [INSTALL.md](INSTALL.md).**

> **A note on lifespan.** Most sources are read by scraping funders' public web pages. When a funder
> redesigns its site, that source will stop returning calls until its reader is updated. The app tells
> you which source had a problem and carries on with the rest. Each source is one small file (see
> *Adding or fixing a source* below), so fixes are usually quick, but this is a tool that needs
> occasional care, not one that will work unattended forever.

The default keywords are tuned for AI, modelling and simulation in materials science. **Every keyword
group is editable**, though, so it works for any field.

## What it does

- **Scans nine sources** each morning at 9:00, catching up as soon as it starts if the computer was off.
  It can start quietly at login, minimised, without opening over your windows.
- **Filters by keyword groups** that you name and edit. A call that hits two or more "counts on its own"
  groups is marked as a **strong match**.
- **Ranks calls** within *Due within two weeks* and *General*: strong matches first, then keyword score,
  then closing date.
- **Shows what's new:** a *NEW* badge on calls you haven't seen, an *Only new* toggle, and a number badge
  on the taskbar (Windows) or Dock (Mac) icon after a scan you weren't watching.
- **Keeps a shortlist** (tap the leaf) that you can **export to a shareable PDF** with links.
- **Moves calls to *Seen*** a week after you first look at them, so Opportunities stays fresh.
  Nothing is lost: ↺ brings a call back.
- **Searches** across titles, funders, summaries and keywords, and reports hits hidden by other filters.
- **Grows a tree** while it scans, with leaves sprouting as each source finishes.

## Sources

| Source | What it covers | How it's read |
|---|---|---|
| Innovation Funding Service | Innovate UK competitions, mostly for businesses | HTML search pages |
| UKRI Funding Finder | EPSRC, STFC, NERC and the other research councils, plus cross-council calls | HTML listing of open and upcoming calls |
| Henry Royce Institute | Royce's own calls (access schemes, MATcelerate) | Funding page cards (no dates, so shown as "See page") |
| Royal Academy of Engineering | Research and industrial fellowships, chairs, international programmes | Each programme page's "Application deadline" |
| Royal Society | Fellowships, industry fellowships, international schemes | Grant listings, open schemes only |
| Leverhulme Trust | Research grants, fellowships and prizes (any discipline) | "Schemes at a glance", open schemes only |
| Faraday Institution | Battery industry fellowships and sprints | Opportunities page (no dates) |
| ARIA | High-risk programme calls | Open calls page |
| Horizon Europe | Open and forthcoming EU topics (open to the UK as an associated country) | The EU Funding & Tenders portal's public search API |

Innovate UK competitions appear on both the Innovation Funding Service and UKRI. They're merged by
comparing the words in each title, so each call is listed once.

Each source can be switched off under **Filters → Sources**. Switching a source off hides its calls but
doesn't delete them. Each source can also be set to **"show everything"**, which skips the keyword filter.
That suits small or general sources whose schemes ("Research Fellowships, any discipline") rarely mention
your keywords.

## The keyword filter

Terms are matched against each call's title and summary:
- whole words only, ignoring case and treating singular and plural alike
- spaces and hyphens count as the same, so `thin film` matches "thin-films"
- a trailing `*` matches any ending, so `metallurg*` matches metallurgy and metallurgical

Each group either **counts on its own**, meaning a hit is enough to show a call, or only **boosts**
calls that already matched. Boost-only suits words like *research* or *engineering*, which appear in
almost every call.

## Repository layout

```
FunderFinder_Windows/          Windows app (WPF) — and the shared core:
  Models/                        calls, keyword groups, saved settings          ← shared
  Services/                      scanner, keyword matching, data file, PDF      ← shared
  Services/Sources/              one file per funder                            ← shared
  MainWindow.xaml(.cs), Styles/  the Windows window and look
FunderFinder_MacOS/            Mac app (Avalonia): its own window, styles, Dock badge and menu-bar icon;
                               compiles the shared Models and Services from FunderFinder_Windows/
INSTALL.md                     how to build, install and auto-start on each platform
```

The sources, matching, scanning, data and PDF export exist **once**, in `FunderFinder_Windows/`. The Mac
project compiles those same files in, so fixing a source fixes both apps.

## Adding or fixing a source

Each source implements `IFundingSource` (in `FunderFinder_Windows/Services/Sources/`) and is registered
in `FundingScanner.AllSources`. `RoyalSocietySource.cs` is a short example to copy. Order in that list
matters: when two sources list the same call, the earlier one wins.

If a source starts reporting "Problems with: …" after a scan, open the funder's page, see what changed,
and update that one file.

## Your data

Calls, shortlist, keyword groups and settings are kept in one `data.json` file. It isn't part of the
repository.

| Platform | Location |
|---|---|
| Windows | `%AppData%\FunderFinder\data.json` |
| macOS | `~/Library/Application Support/FunderFinder/data.json` |

Set the environment variable `FUNDERFINDER_DATA_DIR` to use a different folder, for example to try a
build without touching your real data.

## Licence

[MIT](LICENSE). Use it, change it, share it.
