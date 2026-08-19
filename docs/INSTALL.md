# Install, update, uninstall

The add-in is a single assembly plus a manifest, installed per Revit year into the per-user
add-ins folder. There is no installer yet (plan section 12, Phase 4); the build produces the
files and, for Debug configurations, copies them into place.

## Requirements

| | |
|---|---|
| Operating system | Windows (Revit is Windows-only; the projects target `net8.0-windows` / `net10.0-windows`) |
| Revit | 2025, 2026 or 2027. Each year needs its own build configuration |
| .NET runtime | Nothing extra. Revit 2025 and 2026 host .NET 8, Revit 2027 hosts .NET 10, and each Revit installs the runtime it needs |
| To build | .NET SDK 10.0.300 or later (`global.json`, `rollForward: latestFeature`). The Revit API comes from the `Nice3point.Revit.Api.*` NuGet packages, so a machine without Revit can still build |

## Where the files go

One folder and one manifest per Revit year, under the current user's roaming profile:

```
%AppData%\Autodesk\Revit\Addins\2025\CrappyRevitModelGenerator.addin
%AppData%\Autodesk\Revit\Addins\2025\CrappyRevitModelGenerator\CrappyRevitModelGenerator.dll

%AppData%\Autodesk\Revit\Addins\2026\CrappyRevitModelGenerator.addin
%AppData%\Autodesk\Revit\Addins\2026\CrappyRevitModelGenerator\CrappyRevitModelGenerator.dll

%AppData%\Autodesk\Revit\Addins\2027\CrappyRevitModelGenerator.addin
%AppData%\Autodesk\Revit\Addins\2027\CrappyRevitModelGenerator\CrappyRevitModelGenerator.dll
```

`%AppData%` is normally `C:\Users\<you>\AppData\Roaming`. The manifest's `<Assembly>` element is
the relative path `CrappyRevitModelGenerator\CrappyRevitModelGenerator.dll`, so the `.addin` and
the folder must sit side by side. Debug builds also place `CrappyRevitModelGenerator.pdb` and
`CrappyRevitModelGenerator.deps.json` in the folder; Release builds omit the `.pdb`.

The manifest (`source/CrappyRevitModelGenerator/CrappyRevitModelGenerator.addin`) declares an
`Application` add-in with `FullClassName` `CrappyRevitModelGenerator.App`, `ClientId`
`C728349E-5B0C-4C16-AA50-4A026A3A0D91` and `VendorId` `Design Tech Unraveled`.

## Debug build = install

`Nice3point.Revit.Build.Tasks` (referenced by the csproj) runs two targets after every build:

1. `_GenerateAddinPackage` (all configurations) assembles a publish folder:

   ```
   source\CrappyRevitModelGenerator\bin\<Configuration>\publish\Revit <year> <Configuration> addin\
       CrappyRevitModelGenerator.addin
       CrappyRevitModelGenerator\CrappyRevitModelGenerator.dll  (+ .deps.json, .pdb in Debug)
   ```

   for example `bin\Debug R26\publish\Revit 2026 Debug R26 addin\`.

2. `_PublishRevitAddin` (only when `PublishAddinFiles` is true, which the csproj sets for every
   configuration whose name starts with `Debug`) copies that folder's contents into
   `%AppData%\Autodesk\Revit\Addins\<year>\`.

So, with Revit closed:

```
dotnet build -c "Debug R25"     # installs for Revit 2025
dotnet build -c "Debug R26"     # installs for Revit 2026 (a plain `dotnet build` does the same)
dotnet build -c "Debug R27"     # installs for Revit 2027
```

Building from Visual Studio or Rider with the `Debug R26` configuration does the same thing; the
project's start action launches `C:\Program Files\Autodesk\Revit <year>\Revit.exe`, so F5 builds,
installs and starts Revit.

Close Revit before rebuilding. Revit locks the loaded assembly and the copy into `%AppData%` fails
until it is closed. Passing `-p:PublishAddinFiles=false` builds without touching `%AppData%`.

## Release build = publish folder only

```
dotnet build -c "Release R26"
```

produces `source\CrappyRevitModelGenerator\bin\Release R26\publish\Revit 2026 Release R26 addin\`
(and the R25/R27 equivalents) and does **not** copy anything into the Revit add-ins folder. To
install a Release build, copy the two items in that folder — the `.addin` file and the
`CrappyRevitModelGenerator\` folder — into `%AppData%\Autodesk\Revit\Addins\<year>\`. That is also
what a packaging step or a hand-off to another machine should ship.

## First start

1. Start the matching Revit version.
2. Revit shows its unsigned add-in security prompt for `CrappyRevitModelGenerator.dll` (the
   assembly is not code-signed). Choose the option that loads it — *Always Load* to stop the
   prompt reappearing, or *Load Once* to try it.
3. The **Add-Ins** tab gains a panel named **Crappy Model Generator** with the buttons
   **Generate Bad Model**, **Clean Generated** and **View Last Report**.

If the panel does not appear:

- Check that both the `.addin` and the folder exist under the correct year and that the DLL is
  the build for that year (an R26 build will not load in 2027 and vice versa).
- Check the Revit journal (`%LocalAppData%\Autodesk\Revit\Autodesk Revit <year>\Journals\`) for
  a load error naming `CrappyRevitModelGenerator`.
- If the manifest was edited by hand, make sure it is still UTF-8 XML with the `<Assembly>`
  path relative to the manifest.

## Update

Rebuild (Debug) or copy the new publish folder over the old one (Release), with Revit closed.
The manifest's `ClientId` is stable across versions, so Revit treats it as the same add-in.

## Uninstall

Either

```
dotnet clean -c "Debug R26"     # also R25 / R27
```

which, for Debug configurations, removes
`%AppData%\Autodesk\Revit\Addins\<year>\CrappyRevitModelGenerator\` and the `.addin`
(`_CleanRevitAddinFolder` target), or delete those two items by hand. Nothing is written
anywhere else on the machine — no registry entries, no files under `Program Files`.

Documents that were generated into keep the two Extensible Storage schemas
(`CrappyGeneratedElement`, `CrappyGenerationRun`) and any run records until **Clean Generated**
is run. Both schemas are public read/write, so those documents open normally without the add-in.
