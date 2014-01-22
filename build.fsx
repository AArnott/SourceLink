#r "packages/FAKE.2.6.0.0/tools/FakeLib.dll"
#load "packages/SourceLink.Fake.0.3.0-a1401221522-b7bd7f00/tools/Fake.fsx"

open System
open System.IO
open Fake
open Fake.AssemblyInfoFile
open SourceLink

let cfg = getBuildConfig __SOURCE_DIRECTORY__
let versionAssembly = cfg.AppSettings.["versionAssembly"].Value // change when incompatible
let versionFile = cfg.AppSettings.["versionFile"].Value // matches nuget version
let versionPre = "a" // emtpy, a for alpha, b for beta

let repo = new GitRepo(__SOURCE_DIRECTORY__)
let dt = DateTime.UtcNow
let versionInfo = sprintf "%s %s %s" versionAssembly (dt.ToString "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'") repo.Revision
// pre-release limited to 20 chars after the dash, using 10 for the date and first 8 of revision "-a1312241626-dcc582c2"
let versionNuget = if versionPre.Length = 0 then versionFile else sprintf "%s-%s%s-%s" versionFile versionPre (dt.ToString "yyMMddHHmm") (repo.Revision.Substring(0,8))

Target "Clean" (fun _ -> 
    !! "**/bin/"
    ++ "**/obj/" 
    |> CleanDirs 
)

Target "BuildNumber" (fun _ -> 
    use tb = getTfsBuild()
    tb.Build.BuildNumber <- sprintf "SourceLink.%s" versionNuget
    tb.Build.Save()
)

Target "AssemblyInfo" (fun _ ->
    CreateFSharpAssemblyInfo "SourceLink/AssemblyInfo.fs"
        [ 
        Attribute.Version versionAssembly 
        Attribute.FileVersion versionFile
        Attribute.InformationalVersion versionInfo
        ]
    CreateFSharpAssemblyInfo "Build/AssemblyInfo.fs"
        [ 
        Attribute.Version versionAssembly 
        Attribute.FileVersion versionFile
        Attribute.InformationalVersion versionInfo
        ]
    CreateFSharpAssemblyInfo "Tfs/AssemblyInfo.fs"
        [ 
        Attribute.Version versionAssembly 
        Attribute.FileVersion versionFile
        Attribute.InformationalVersion versionInfo
        ]
)

Target "Build" (fun _ ->
    !! "SourceLink.sln"
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

Target "SourceLink" (fun _ ->
    !! "Tfs/Tfs.fsproj" 
    ++ "SourceLink/SourceLink.fsproj"
    |> Seq.iter (fun f ->
        let proj = VsProj.LoadRelease f
        let files = proj.Compiles -- "**/AssemblyInfo.fs"
        repo.VerifyChecksums files
        proj.VerifyPdbChecksums files
        proj.CreateSrcSrv "https://raw.github.com/ctaggart/SourceLink/{0}/%var2%" repo.Revision (repo.Paths files)
//        SrcSrv.write proj.OutputFilePdb proj.OutputFilePdbSrcSrv // internal bug
        Pdbstr.exec proj.OutputFilePdb proj.OutputFilePdbSrcSrv
    )
)

Target "NuGet" (fun _ ->
    let bin = if isTfsBuild then "../bin" else "bin"
    Directory.CreateDirectory bin |> ignore

    NuGet (fun p -> 
    { p with
        Version = versionNuget
        WorkingDir = "SourceLink/bin/Release"
        OutputPath = bin
    }) "SourceLink/SourceLink.nuspec"

    NuGet (fun p -> 
    { p with
        Version = versionNuget
        WorkingDir = "Tfs/bin/Release"
        OutputPath = bin
        DependenciesByFramework =
        [{ 
            FrameworkVersion = "net45"
            Dependencies =
            [
                "SourceLink", sprintf "[%s]" versionNuget // exact version
            ]
        }]
    }) "Tfs/Tfs.nuspec"

//    NuGet (fun p -> 
//    { p with
//        Version = versionNuget
//        WorkingDir = "Build/bin/Release"
//        OutputPath = bin
//    }) "Build/Build.nuspec"

    NuGet (fun p -> 
    { p with
        Version = versionNuget
        WorkingDir = "Fake"
        OutputPath = bin
    }) "Fake/Fake.nuspec"
)

"Clean"
    =?> ("BuildNumber", isTfsBuild)
    ==> "AssemblyInfo"
    ==> "Build"
    =?> ("SourceLink", isMono = false && hasBuildParam "skipSourceLink" = false)
    ==> "NuGet"

RunTargetOrDefault "NuGet"