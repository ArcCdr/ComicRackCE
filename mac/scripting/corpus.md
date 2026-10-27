# Plugin corpus for the compatibility scan (P4.6b)

The corpus is what `crcli script-scan` measures to rank the work of the Script UI Compatibility Layer (`mac/scripting/compat-usage.md`). Plugins run as they are (owner requirement R11): the corpus decides the order of the work, no plugin gets code of its own.

## Rules

- **Untrusted.** Every plugin is a download. Each one sits in its own folder `mac/fixtures/plugins/<name>/` (git ignored; the fixtures budget is 1.5 GB, the corpus is about 30 MB) next to a `SOURCE.txt` that records URL, pin and licence. Nothing in these folders is executed, built, imported or unpacked: `mac/scripting/fetch-corpus.sh` only fetches one pinned commit with git (no hooks, no submodules, no LFS) or one release file whose SHA-256 it checks. The scanner reads scripts with a parser and assemblies by metadata, packages in memory. Plugins are only ever run inside the application's plugin host.
- **Pinned.** A source is a full commit hash or a SHA-256, so a scan can be repeated; to move to a newer version change the pin in the script and regenerate the report.
- **Licences.** Several repositories state no licence (GitHub reports none); their files stay local and only scan results (type and member names with counts, plugin folder names, Windows-only API names) are committed.
- **Not committed:** the folders, the full JSON (`mac/artifacts/script-corpus/scan.json`, git ignored).
- **Left out of the scan** (`mac/scripting/corpus-exclude.txt`): build tools, tests and reference copies of the program's assemblies inside source checkouts.

## Commands

    mac/scripting/fetch-corpus.sh          # fetch what is missing into mac/fixtures/plugins (--list, --only NAME, --root DIR)
    mac/scripting/scan-corpus.sh [--fetch] # scan the corpus and ComicRack/Output/Scripts, write mac/scripting/compat-usage.md

## Sources

Also scanned: the seven bundled samples in `ComicRack/Output/Scripts` (part of the program, licence of the program) and `mac/fixtures/owner/scripts/` when it exists (checkpoint C1; absent on 2026-10-27, so the owner's own scripts are not in the figures).

| Folder | Plugin | Kind | URL | Licence | Pin (commit or SHA-256) | Notes |
|---|---|---|---|---|---|---|
| `bedetheque-scraper-2-v6.08` | Bedetheque Scraper 2 | release | https://github.com/maforget/Bedetheque-Scrapper-2/releases/download/v6.08/Bedetheque.Scraper.2_v6.08.crplugin | none stated | `666016de2439ba179c27fb267b64d9896faa3bae71200f9b372969991954e5c5` | Metadata scraper for bedetheque.com (the plugin named in R11); ships a native Windows daemon and the Python 2 library |
| `data-manager-v2.07.06.830` | Data Manager | release | https://github.com/maforget/CRDataManager/releases/download/2.07.06.830/Data.Manager_v2.07.06.830.crplugin | none stated | `d520fe4d9a3e6a5ef47a94b738068e9ef55c4dbbf508c61decd0ec55bf49f1bd` | Data Manager (CR Data Manager): a program assembly with a GUI plus Python hooks |
| `amazon-scrapper-v1.1` | Amazon Scrapper | release | https://github.com/maforget/ComicRack_AmazonScrapper/releases/download/v1.1/AmazonScrapper.crplugin | GPL-2.0 | `48c3dc3c9143f5cc0b144390628230f1ab7905f87111f72cee4debbe853d4659` | C# assembly plugin compiled against .NET Framework WinForms |
| `find-image-resolution-v1.0` | Find Image Resolution | release | https://github.com/maforget/ComicRack_FindImageResolution/releases/download/v1.0/FindImageResolutionNET.crplugin | GPL-2.0 | `d47837afabbbe05b4098ea2a9f3a3abec9df8d2a608c9d6dea1781d3ff71e8e3` | C# assembly plugin: fetches the page resolution of a book |
| `library-organizer-2.1.13` | Library Organizer | release | https://github.com/Stonepaw/comicrack-library-organizer/releases/download/2.1.13/Library.Organizer.2.1.13.crplugin | none stated | `9ef9617d349fc212396409d84a3c3b2c927105257bb18f5fc63cabd284312320` | Configurable file organiser |
| `from-ducks-3.0.1` | From Ducks | release | https://github.com/ducksmanager/ComicRack_FromDucks/releases/download/v3.0.1/FromDucks-3.0.1.crplugin | GPL-3.0 | `3249f424c5ac726611ca13f91a9ed656f683739d4848ea19596c03f7b87f1b3e` | Fetches Inducks data for Disney comics |
| `grand-comics-db-scraper-v1.0.0` | Local Grand Comics DB Scraper | release | https://github.com/strumples/ComicRack-Grand-Comics-Scraper/releases/download/v1.0.0/Local.Grand.Comics.DB.Scraper.crplugin | MIT | `3a791d3648924b8e109ce5ef4813c4d6291e5e15fc485aee66a5f3ddeae5aac2` | Offline Grand Comics Database scraper |
| `cvinfowrite-v1.4` | CVInfoWrite | release | https://github.com/killo3967/CVInfoWrite/releases/download/v1.4/CVInfoWrite.crplugin | none stated | `06ff040247dff74e9546f758be137393d018033df1235918ba478d9da2d1421e` | Writes a CVInfo.txt file |
| `mifs-v1.0.0` | MIFS | release | https://github.com/faqy003/ComicRack-Plugin-MIFS/releases/download/v1.0.0/ComicRack_Plugin_MIFS.crplugin | Unlicense | `d341a94550248170e7efe0b4a8fa361a8fba626e24c278f555c9d79867212c38` | Modern image format decoder (AVIF, JPEG XL) with a Harmony patch library |
| `comic-vine-scraper` | Comic Vine Scraper | git | https://github.com/cbanack/comic-vine-scraper.git | none stated | `a4c100bcaa031881f009a28acd398b6a19d35c3d` | The Comic Vine scraper (source checkout; the plugin is src/; the CE fork named in the plan was not found, this is the original repository) |
| `manga-update-scraper` | Manga Update Scraper | git | https://github.com/maforget/ComicRack_MangaUpdateScraper.git | none stated | `14600310f52939af8ef2b36323c769f714a339fc` | Mini scraper without a UI (genres from mangaupdates.com) |
| `pescuma-plugins` | pescuma plugins | git | https://github.com/pescuma/comicrack-plugins.git | none stated | `4d62d7263b3219dde75b5d5c4eb1a2924b5a1f48` | Series Info Panel, Next Issues To Read, Files Not Found |
| `duplicates-manager` | Duplicates Manager | git | https://github.com/pescuma/comicrack-duplicates-manager.git | none stated | `162d6e10225b9e9eb3b4e618c3343eec08c92887` | Duplicates manager |
| `copy-move-field` | Copy Move Field | git | https://github.com/Stonepaw/comicrack-copy-move-field.git | MIT | `ce576868cefa4612c76e25c4e21e007d143a3c4b` | Moves or copies info from one field to another |
| `scan-information-from-filename` | Scan Information From Filename | git | https://github.com/Stonepaw/comicrack-scan-information-from-filename.git | MIT | `e40a507fb8c7d0fa9e8610a43f8e9cae1759e881` | Adds the scanner information from the file name |
| `weekly-comic-releases` | Weekly Comic Releases | git | https://github.com/Stonepaw/comicrack-weekly-comic-releases.git | other | `ef968cea2d85152b168c3eef91b97a2bb698dc45` | Information on new comic releases for every week |
| `webcomic-helper` | Webcomic Helper | git | https://github.com/Stonepaw/comicrack-webcomic-helper.git | GPL-3.0 | `c3788c98a1c31aad23a32d1c7b494112b0ccc665` | Creates .cbw files from web comic sites |
| `new-entry-via-barcode` | New Entry Via Barcode | git | https://github.com/Stonepaw/comicrack-new-entry-via-barcode.git | other | `73c285bc568d25fd46ec4d636baed3bd1075a449` | Adds a new comic via a barcode |
| `story-arc-from-titles` | Story Arc From Titles | git | https://github.com/SenorSmartyPants/ComicRack-StoryArcFromTitles.git | GPL-3.0 | `cd00338ffa0a12cabcfa4e41e34788b55e4b79fe` | Story arcs from book titles |
| `mcl-updater-py3` | MCL Updater (Python 3) | git | https://github.com/azuravian/ComicRack_mcl_Updater_Py3.git | CC0-1.0 | `7f4e47398e557a3adf1e9371e926fb822f42ee8e` | Python 3 script that updates mcl files for the Find Missing Issues Offline plugin (not IronPython 2.7) |
| `auto-folders` | Auto Folders | git | https://github.com/scinos/ComicRackAutoFolders.git | none stated | `d15dc04325be8c5460f776f8c67dbca16e64c46d` | Script that creates automatic lists for one-shots, series and arcs |
| `guided-rename-mod` | Guided Rename (mod) | git | https://github.com/Northrup69/GuidedRename-Mod.git | none stated | `8da26db6b85b26e18a994976b7bd29e965949763` | Modified Guided Rename script |
| `get-comicrack-info` | get comicrack info | git | https://github.com/killo3967/get_comicrack_info.git | GPL-3.0 | `8d219f6fdd9a0bca4dbd41f2ddd3f9cec0a4f682` | Scripts that document the ComicRack API (not a plugin) |

## Named in the plan but not found

| Wanted | Result (2026-10-27) |
|---|---|
| FrederikBaerentsen/ComicRack_Scripts | no such public repository; the account has no public repositories |
| Comic Vine Scraper CE | no fork under that name found; `cbanack/comic-vine-scraper` (the original, last push 2026-09) stands in |
| CVIssueCount | no public repository found |
| Data Manager | `maforget/CRDataManager`, release 2.07.06.830 |
| maforget's AmazonScrapper | `maforget/ComicRack_AmazonScrapper`, release v1.1 |
| owner scripts | `mac/fixtures/owner/scripts/` does not exist (checkpoint C1 is optional) |

Other public plugin sources worth adding when someone finds them: forum archives (not scriptable), the owner's list. Adding a plugin is one line in `fetch-corpus.sh` (name, kind, URL, pin, licence) and a re-run of `scan-corpus.sh`.
