﻿module SourceLink.Build.Git

open System
open System.IO
open SourceLink
open LibGit2Sharp
open System.Collections.Generic

let concatBytes (a:byte[]) (b:byte[]) =
    let c = Array.create (a.Length + b.Length) 0uy
    Buffer.BlockCopy(a, 0, c, 0, a.Length)
    Buffer.BlockCopy(b, 0, c, a.Length, b.Length)
    c

let toBytes (s:string) = Text.Encoding.UTF8.GetBytes s

let computeChecksums files =
    use sha1 = Security.Cryptography.SHA1.Create()
    files |> Seq.map (fun file ->
        let bytes = File.ReadAllBytes file
        let prefix = sprintf "blob %d%c" bytes.Length (char 0)
        let checksum = sha1.ComputeHash(concatBytes (toBytes prefix) bytes) |> Hex.encode
        checksum, file
    )
    |> Seq.toArray

let getRevision dir =
    use repo = new Repository(dir)
    repo.Head.Tip.Sha

let getChecksums dir files =
    use repo = new Repository(dir)
    let checksums = HashSet(StringComparer.OrdinalIgnoreCase)
    let missing = List<string>()
    files |> Seq.iter (fun (file:string) ->
        let f =
            if Path.IsPathRooted file then
                file.Substring(dir.Length + 1)
            else
                file
        let ie = repo.Index.[f]
        if ie <> null then
            checksums.Add ie.Id.Sha |> ignore
        else
            missing.Add f
        if missing.Count > 0 then
            Ex.failwithf "files not in repo: %A" missing
    )
    checksums