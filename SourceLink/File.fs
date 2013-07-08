﻿module SourceLink.File

open System
open System.IO
open System.Collections.Generic

let computeChecksums files =
    use md5 = Security.Cryptography.MD5.Create()
    let computeHash file =
        use fs = File.OpenRead file
        md5.ComputeHash fs
    let checksums = Dictionary<string, string>()
    for f in files do
        checksums.[f |> computeHash |> Hex.encode] <- f
    checksums

let readLines (bytes:byte[]) =
    use sr = new StreamReader(new MemoryStream(bytes))
    seq {
        while not sr.EndOfStream do
            yield sr.ReadLine()
    }
    |> Seq.toArray