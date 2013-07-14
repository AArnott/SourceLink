﻿module SourceLink.Dia

open System
open Microsoft.Dia

//let unixEpoch = DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
//let fromUnixEpoch (seconds:uint32) = unixEpoch.AddSeconds (double seconds)

let openPdb file =
    let ds = DiaSourceClass()
    ds.loadDataFromPdb file
    ds.openSession()

type IDiaSession with
    member x.getSeqTables() =
        seq {
            let tables = x.getEnumTables()
            let table = ref Unchecked.defaultof<IDiaTable>
            let celt = ref 1u
            while !celt = 1u do
                tables.Next(1u, table, celt)
                if !celt = 1u then
                    yield !table
        }
    member x.getSeqDebugStreams() =
        seq {
            let streams = x.getEnumDebugStreams()
            let stream = ref Unchecked.defaultof<IDiaEnumDebugStreamData>
            let celt = ref 1u
            while !celt = 1u do
                streams.Next(1u, stream, celt)
                if !celt = 1u then
                    yield !stream
        }

type DiaTables(sn:IDiaSession) =
    let map = sn.getSeqTables() |> Seq.map (fun t -> t.name, t) |> Map.ofSeq
    member x.Symbols = map.["Symbols"] :?> IDiaEnumSymbols
    member x.SourceFiles = map.["SourceFiles"] :?> IDiaEnumSourceFiles
    member x.LineNumbers  = map.["LineNumbers "] :?> IDiaEnumLineNumbers 
//    member x.Sections = map.["Sections"] :?>
//    member x.SegmentMap  = map.["SegmentMap "] :?>
    member x.InjectedSources = map.["InjectedSource"] :?> IDiaEnumInjectedSources
//    member x.StackFrames = map.["FrameData"] :?> IDiaEnumStackFrames

type IDiaSession with
    member x.getTables() = DiaTables(x)

type IDiaEnumSourceFiles with
    member x.toSeq() =
        seq {
            let sf = ref Unchecked.defaultof<IDiaSourceFile>
            let celt = ref 1u
            while !celt = 1u do
                x.Next(1u, sf, celt)
                if !celt = 1u then
                    yield !sf
        }

//type IDiaSymbol with
//    member x.DateTime with get() = fromUnixEpoch x.timeStamp

type IDiaEnumSymbols with
    member x.toSeq() =
        seq {
            let sym = ref Unchecked.defaultof<IDiaSymbol>
            let celt = ref 1u
            while !celt = 1u do
                x.Next(1u, sym, celt)
                if !celt = 1u then
                    yield !sym
        }