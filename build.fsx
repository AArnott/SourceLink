#I "packages/FAKE/tools"
#r "FakeLib.dll"
#load "packages/SourceLink.Fake/tools/SourceLink.fsx"

open System
open System.IO
open Fake
open Fake.AssemblyInfoFile
open SourceLink

let dt = DateTime.UtcNow
let cfg = getBuildConfig __SOURCE_DIRECTORY__
let revision =
    use repo = new GitRepo(__SOURCE_DIRECTORY__)
    repo.Revision

let versionAssembly = cfg.AppSettings.["versionAssembly"].Value // change when incompatible
let versionFile = cfg.AppSettings.["versionFile"].Value // matches nuget version
let prerelease =
    if hasBuildParam "prerelease" then getBuildParam "prerelease"
    else sprintf "ci%s" (dt.ToString "yyMMddHHmm") // 20 char limit
let versionInfo = sprintf "%s %s %s" versionAssembly dt.IsoDateTime revision
let buildVersion = if String.IsNullOrEmpty prerelease then versionFile else sprintf "%s-%s" versionFile prerelease

Target "Clean" (fun _ -> !! "**/bin/" ++ "**/obj/" |> CleanDirs)

Target "BuildVersion" (fun _ ->
    let args = sprintf "UpdateBuild -Version \"%s\"" buildVersion
    Shell.Exec("appveyor", args) |> ignore
)

Target "AssemblyInfo" (fun _ ->
    let common = [ 
        Attribute.Version versionAssembly 
        Attribute.FileVersion versionFile
        Attribute.InformationalVersion versionInfo ]
    common |> CreateFSharpAssemblyInfo "SourceLink/AssemblyInfo.fs"
    common |> CreateFSharpAssemblyInfo "Build/AssemblyInfo.fs"
    common |> CreateFSharpAssemblyInfo "Tfs/AssemblyInfo.fs"
    common |> CreateFSharpAssemblyInfo "Git/AssemblyInfo.fs"
)

Target "Build" (fun _ ->
    !! "SourceLink.sln" |> MSBuildRelease "" "Rebuild" |> ignore
)

Target "SourceLink" (fun _ ->
    !! "Tfs/Tfs.fsproj" 
    ++ "SourceLink/SourceLink.fsproj"
    ++ "Git/Git.fsproj"
    |> Seq.iter (fun f ->
        use repo = new GitRepo(__SOURCE_DIRECTORY__)
        let proj = VsProj.LoadRelease f
        logfn "source linking %s" proj.OutputFilePdb
        let files = proj.Compiles -- "**/AssemblyInfo.fs"
        repo.VerifyChecksums files
        proj.VerifyPdbChecksums files
        proj.CreateSrcSrv "https://raw.github.com/ctaggart/SourceLink/{0}/%var2%" repo.Revision (repo.Paths files)
        Pdbstr.exec proj.OutputFilePdb proj.OutputFilePdbSrcSrv
    )
)

Target "NuGet" (fun _ ->
    let bin = "bin"
    Directory.CreateDirectory bin |> ignore

    NuGet (fun p -> 
    { p with
        Version = buildVersion
        WorkingDir = "SourceLink/bin/Release"
        OutputPath = bin
    }) "SourceLink/SourceLink.nuspec"

    NuGet (fun p -> 
    { p with
        Version = buildVersion
        WorkingDir = "Tfs/bin/Release"
        OutputPath = bin
        DependenciesByFramework =
        [{ 
            FrameworkVersion = "net45"
            Dependencies = ["SourceLink", sprintf "[%s]" buildVersion] // exact version
        }]
    }) "Tfs/Tfs.nuspec"

//    NuGet (fun p -> 
//    { p with
//        Version = buildVersion
//        WorkingDir = "Build/bin/Release"
//        OutputPath = bin
//    }) "Build/Build.nuspec"

    NuGet (fun p -> 
    { p with
        Version = buildVersion
        WorkingDir = "Fake"
        OutputPath = bin
    }) "Fake/Fake.nuspec"

    NuGet (fun p -> 
    { p with
        Version = buildVersion
        WorkingDir = "Git/bin/Release"
        OutputPath = bin
        DependenciesByFramework =
        [{ 
            FrameworkVersion = "net45"
            Dependencies = ["LibGit2Sharp", GetPackageVersion "./packages/" "LibGit2Sharp"]
        }]
    }) "Git/Git.nuspec"
)

"Clean"
    =?> ("BuildVersion", buildServer = BuildServer.AppVeyor)
    ==> "AssemblyInfo"
    ==> "Build"
    =?> ("SourceLink", isMono = false && hasBuildParam "link")
    ==> "NuGet"

RunTargetOrDefault "NuGet"