# Vitage Story _Pet Map Markers_ mod

Automatically track PetAI-compatible pets with waypoints on your minimap.

[ModsDB](https://mods.vintagestory.at/petmapmarkers)

______________________________________________________________________

## Build

- clone https://github.com/G3rste/petai to `./petai/` subdirectory inside this repo, or use symlink
or a junction to point it to local copy:
```pwsh
New-Item -ItemType Junction -Path petai -value (Resolve-Path ..\petai)
```
- `dotnet run --project ZZCakeBuild`

## Test

### Init the world:

- (temporarily) remove `"petmapmarkers"` from `PetMapMarkers.gametests\modinfo.json`
- (temporarily) redefine modpaths in `ZZCakeBuild\Program.cs`:

```cs
protected override IEnumerable<string> GetModBinaryPaths(BuildContext context) =>
    [ Path.GetFullPath($"../{context.AutotestsProjectName}/bin/{context.BuildConfiguration}/Mods") ];
```

- init the world `dotnet run --project ZZCakeBuild -- --manual-mode true --test-world init`
- in game use `/init` to create entities
- undo ^ chages

### Run tests

```
dotnet run --project ZZCakeBuild
```
