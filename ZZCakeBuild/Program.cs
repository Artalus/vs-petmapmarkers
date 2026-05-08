using System;
using System.Collections.Generic;
using System.IO;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using VinTest.Cake;

namespace CakeBuild;

public static class Program
{
    public static int Main(string[] args)
    {
        return new CakeHost().UseContext<BuildContext>().Run(args);
    }
}

public class BuildContext : ContextBase
{
    public override string ProjectName => "PetMapMarkers";
    public string Version { get; }
    public string Name { get; }
    public bool SkipJsonValidation { get; }

    public BuildContext(ICakeContext context)
        : base(context)
    {
        SkipJsonValidation = context.Argument("skipJsonValidation", false);
        var modInfo = context.DeserializeJsonFromFile<ModInfo>($"../{ProjectName}/modinfo.json");
        Version = modInfo.Version;
        Name = modInfo.ModID;
    }
}

[TaskName("ValidateJson")]
public sealed class ValidateJsonTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        if (context.SkipJsonValidation)
        {
            return;
        }
        var jsonFiles = context.GetFiles($"../{context.ProjectName}/assets/**/*.json");
        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file.FullPath);
                JToken.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new Exception(
                    $"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}",
                    ex
                );
            }
        }
    }
}

[TaskName("Build")]
[IsDependentOn(typeof(ValidateJsonTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.DotNetClean(
            $"../{context.ProjectName}/{context.ProjectName}.csproj",
            new DotNetCleanSettings { Configuration = context.BuildConfiguration }
        );

        context.DotNetPublish(
            $"../{context.ProjectName}/{context.ProjectName}.csproj",
            new DotNetPublishSettings { Configuration = context.BuildConfiguration }
        );
    }
}

[TaskName("RunGameTests")]
[IsDependentOn(typeof(ValidateJsonTask))]
public sealed class RunGameTestsTask : GameTestsTaskBase<BuildContext>
{
    protected override string[] AdditionalLogCapture => ["[PetMapMarkersTest]", "[PetMapMarkers]"];
    protected override IEnumerable<(string Pattern, LogLevel? TargetLevel)> LogSuppressions =>
        [
            // petai@4.0.3 & wolftaming@4.1.4 are known to contain suboptimal patches
            (@"Patch \d+ in (wolftaming|petai):.+not found\. Hint:", null),
        ];

    protected override void Prepare(BuildContext context)
    {
        var configDir = Path.Combine(context.DataPath, "ModConfig");
        context.EnsureDirectoryExists(configDir);
        File.WriteAllText(
            Path.Combine(configDir, "petmapmarkersconfig.json"),
            @"{
                ""DefaultColor"": ""#00ff00"",
                ""DownedColor"": ""red"",
                ""UpdateIntervalSeconds"": 0.2
            }"
        );
    }
}

[TaskName("Package")]
[IsDependentOn(typeof(RunGameTestsTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.EnsureDirectoryExists("../Releases");
        context.CleanDirectory("../Releases");
        context.EnsureDirectoryExists($"../Releases/{context.Name}");
        context.CopyFiles(
            $"../{context.ProjectName}/bin/{context.BuildConfiguration}/Mods/mod/publish/*",
            $"../Releases/{context.Name}"
        );
        if (context.DirectoryExists($"../{context.ProjectName}/assets"))
        {
            context.CopyDirectory(
                $"../{context.ProjectName}/assets",
                $"../Releases/{context.Name}/assets"
            );
        }
        context.CopyFile(
            $"../{context.ProjectName}/modinfo.json",
            $"../Releases/{context.Name}/modinfo.json"
        );
        if (context.FileExists($"../{context.ProjectName}/modicon.png"))
        {
            context.CopyFile(
                $"../{context.ProjectName}/modicon.png",
                $"../Releases/{context.Name}/modicon.png"
            );
        }
        context.Zip(
            $"../Releases/{context.Name}",
            $"../Releases/{context.Name}_{context.Version}.zip"
        );
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTask))]
public class DefaultTask : FrostingTask { }
