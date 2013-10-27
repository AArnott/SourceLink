﻿
open System
open System.Collections.Generic
open System.IO
open System.Text
open SourceLink
open SourceLink.Build
open SourceLink.PdbModify
open SourceLink.SrcSrv
open Microsoft.Dia
open SourceLink.Dia

let printChecksumsProj proj =
    let compiles = Proj.getCompiles proj (HashSet())
    for checksum, file in Git.computeChecksums compiles do
        printfn "%s %s"checksum file

let printRevision dir =
    let r = Git.getRevision dir
    printfn "revision: %s" r

let printChecksumsGit dir files =
    for checksum in Git.getChecksums dir files do
        printfn "%s"checksum

let printChecksumsPdb path =
    use pdb = new PdbFile(path)
    let checksums = PdbChecksums(pdb)
    for KeyValue(filename, checksum) in checksums.FilenameToChecksum do
        printfn "%s %s" checksum filename

let pagesToString (pages:int[]) =
    let sb = StringBuilder()
    sb.Appendf "%d pages: " pages.Length
    for p in pages do 
        sb.Appendf "%X " p
    sb.ToString()

let printStreamPages (file:PdbFile) =
    printfn "root page is %X" file.RootPage
    printfn "root stream, %d bytes, %s" file.RootPdbStream.ByteCount (pagesToString file.RootPdbStream.Pages)
    let root = file.Root
    for i in 0 .. root.Streams.Count - 1 do
        let s = root.Streams.[i]
        printfn "stream %d, %d bytes, %s" i s.ByteCount (pagesToString s.Pages)

let printOrphanedPages (file:PdbFile) =
    for page in file.OrphanedPages do
        printf "%x " page
    printfn ""

let writeFile file bytes =
    use fs = File.OpenWrite file
    fs.WriteBytes bytes

let diffBytes (a:byte[]) (b:byte[]) =
    let n = if a.Length < b.Length then a.Length else b.Length
    for i in 0 .. n - 1 do
        if a.[i] <> b.[i] then
            printfn "%X %X %X" i a.[i] b.[i]

let diffFiles a b =
    diffBytes (File.ReadAllBytes a) (File.ReadAllBytes b)

let diffFilesForStream a b stream =
    diffFiles (a+"."+stream) (b+"."+stream)

let copyTo file copy =
    if File.Exists copy then File.Delete copy
    File.Copy(file, copy)

let createCopy file i =
    let ext = Path.GetExtension file
    let copy = Path.ChangeExtension(file, sprintf ".%d%s" i ext)
    copyTo file copy
    copy

let diffStreamPages a b =
    use af = new PdbFile(a)
    use bf = new PdbFile(b)
    let ssa = af.Root.Streams
    let ssb = bf.Root.Streams
    printfn "stream count %d, %d" ssa.Count ssb.Count
    let n = if ssa.Count <= ssb.Count then ssa.Count - 1 else ssb.Count - 1
    for i in 0 .. n do
        let sa = ssa.[i]
        let sb = ssb.[i]
        if sa.ByteCount <> sb.ByteCount then
            printfn "stream %d, different byte count, %X, %X" i sa.ByteCount sb.ByteCount
        if sa.Pages.Length <> sb.Pages.Length then
            printfn "stream %d, different # pages, %d, %d" i sa.Pages.Length sb.Pages.Length
        if false = sa.Pages.CollectionEquals sb.Pages then
            printfn "stream %d, pages not same, %A, %A" i sa.Pages sb.Pages

let diffStreamBytes a b =
    use fa = new PdbFile(a)
    use fb = new PdbFile(b)

    // root stream
    let ra = fa.RootPdbStream
    let rb = fb.RootPdbStream
    let rba = fa.ReadPdbStreamBytes ra
    let rbb = fb.ReadPdbStreamBytes rb
    if false = rba.CollectionEquals rbb then
        printfn "root length, %X <> %X" ra.ByteCount rb.ByteCount
        writeFile (sprintf "%s.root" a) rba
        writeFile (sprintf "%s.root" b) rbb

    // other streams
    let ssa = fa.Root.Streams
    let ssb = fb.Root.Streams
    printfn "stream count %d, %d" ssa.Count ssb.Count
    let n = if ssa.Count <= ssb.Count then ssa.Count - 1 else ssb.Count - 1
    for i in 0 .. n do
        let sa = ssa.[i]
        let sb = ssb.[i]
        let ba = fa.ReadPdbStreamBytes sa
        let bb = fb.ReadPdbStreamBytes sb
        if false = ba.CollectionEquals bb then
            printfn "stream %d length, %X <> %X" i sa.ByteCount sb.ByteCount
            writeFile (sprintf "%s.%d" a i) ba
            writeFile (sprintf "%s.%d" b i) bb

let diffInfoStreams a b =
    use fa = new PdbFile(a)
    use fb = new PdbFile(b)
    let ia = fa.Info
    let ib = fb.Info
    if ia.Version <> ib.Version then
        printfn "Version %d <> %d" ia.Version ib.Version
    if ia.Signature <> ib.Signature then
        printfn "Signature %d <> %d" ia.Signature ib.Signature
    if ia.Guid <> ib.Guid then
        printfn "Signature %A <> %A" ia.Guid ib.Guid
    if ia.Age <> ib.Age then
        printfn "Signature %d <> %d" ia.Age ib.Age
    if ia.FlagIndexMax <> ib.FlagIndexMax then
        printfn "NameIndexMax %d <> %d" ia.FlagIndexMax ib.FlagIndexMax
    if false = ia.SrcSrv.CollectionEquals ib.SrcSrv then
        printfn "SrcSrv %A <> %A" ia.SrcSrv ib.SrcSrv
    if false = ia.Tail.CollectionEquals ib.Tail then
        printfn "Tail %A <> %A" ia.Tail ib.Tail

    // compare names
    for n in ia.NameToPdbName.Values do
        if false = ib.NameToPdbName.ContainsKey n.Name then
            printfn "name only in A %s" n.Name
        else
            let sa = ia.NameToPdbName.[n.Name].Stream
            let sb = ib.NameToPdbName.[n.Name].Stream
            if sa <> sb then
                printfn "diff streams for %s, %d, %d" n.Name sa sb
    for n in ib.NameToPdbName.Values do
        if false = ia.NameToPdbName.ContainsKey n.Name then
            printfn "name only in B %s" n.Name

    let an = fa.Root.Streams.Count
    let bn = fb.Root.Streams.Count
    if an <> bn then
        printfn "# of streams: %d <> %d" an bn
    let n = if an <= bn then an else bn
    for i in 0..n-1 do
        let sa = fa.Root.Streams.[i]
        let sb = fb.Root.Streams.[i]
        if sa.Pages.Length <> sb.Pages.Length then
            printfn "# of pages in stream %d, %d <> %d" i sa.Pages.Length sb.Pages.Length
    
    // flag indexes
    let an = ia.FlagIndexMax
    let bn = ib.FlagIndexMax
    if an <> bn then
        printfn "# of flags: %d <> %d" an bn
    let n = if an <= bn then an else bn
    for i in 0..n-1 do
        let ahas = ia.FlagIndexes.Contains i
        let bhas = ib.FlagIndexes.Contains i
        if ahas && not bhas then
            let pn = ia.FlagIndexToPdbName.[i]
            printfn "a has flag %d for %s" i pn.Name
        else if not ahas && bhas then
            let pn = ib.FlagIndexToPdbName.[i]
            printfn "b has flag %d for %s" i pn.Name

    printfn "done compairing info streams"

let printDia file =
    let sn = openPdb file
    let gs = sn.globalScope
    printfn "%A %d" gs.guid gs.age

    let sfs = sn.getTables().SourceFiles
    printfn "# of source files %d" sfs.count
    
    for sf in sfs.toSeq() do
        printfn "%d %s" sf.uniqueId sf.fileName
//        for sym in sf.compilands.toSeq() do
//            printfn "  %s" sym.name
//    
//    for ds in sn.getSeqDebugStreams() do
//        printfn "%A %d" ds.name ds.count

let printSrcSrv file = 
    for line in PdbFile.ReadSrcSrvLines file do
        printfn "%s" line

let printNamesByStream file =
    use pdb = new PdbFile(file)
    for name in pdb.Info.StreamToPdbName.Values do
        printfn "%3d %3d %s" name.FlagIndex name.Stream name.Name 

let printNamesByFlagIndex file =
    use pdb = new PdbFile(file)
    for name in pdb.Info.FlagIndexToPdbName.Values do
        printfn "%3d %3d %s" name.FlagIndex name.Stream name.Name 

[<EntryPoint>]
let main argv = 
    
    let af = @"C:\Projects\pdb\Autofac.pdb\D77905B67A5046138298AF1CC87D57D51\Autofac.pdb"

    let sl = @"C:\Projects\SourceLink\SourceLink\obj\Debug\SourceLink.pdb"
    let sl1 = @"C:\Projects\SourceLink\SourceLink\obj\Debug\SourceLink.1.pdb"
    let sl2 = @"C:\Projects\SourceLink\SourceLink\obj\Debug\SourceLink.2.pdb"

    let core = @"C:\Projects\fsharp\lib\release\4.0\FSharp.Core.pdb"

    let data = @"C:\Projects\FSharp.Data\src\bin\Release\FSharp.Data.pdb"
    let data1 = @"C:\Projects\FSharp.Data\src\bin\Release\FSharp.Data.1.pdb"
    let data2 = @"C:\Projects\FSharp.Data\src\bin\Release\FSharp.Data.2.pdb"
    let data3 = @"C:\Projects\FSharp.Data\src\bin\Release\FSharp.Data.3.pdb"
    let data4 = @"C:\Projects\FSharp.Data\src\bin\Release\FSharp.Data.4.pdb"
    let data5 = @"C:\Projects\FSharp.Data\src\bin\Release\FSharp.Data.5.pdb"
    let datass = @"C:\Projects\FSharp.Data\src\bin\Release\FSharp.Data.pdb.srcsrv.txt"

    let edt1 = @"C:\Projects\FSharp.Data\src\bin\sl5-compiler\Release\FSharp.Data.Experimental.DesignTime.1.pdb"
    let edt2 = @"C:\Projects\FSharp.Data\src\bin\sl5-compiler\Release\FSharp.Data.Experimental.DesignTime.2.pdb"
    // pdbstr -w -s:srcsrv -i:FSharp.Data.Experimental.DesignTime.pdb.srcsrv.txt -p:FSharp.Data.Experimental.DesignTime.3.pdb
    // pdbstr -r -s:srcsrv -p:FSharp.Data.Experimental.DesignTime.3.pdb
    let edt3 = @"C:\Projects\FSharp.Data\src\bin\sl5-compiler\Release\FSharp.Data.Experimental.DesignTime.3.pdb"
    let edt4 = @"C:\Projects\FSharp.Data\src\bin\sl5-compiler\Release\FSharp.Data.Experimental.DesignTime.4.pdb"
    let edtss = @"C:\Projects\FSharp.Data\src\bin\sl5-compiler\Release\FSharp.Data.Experimental.DesignTime.pdb.srcsrv.txt"

    let dt1 = @"C:\Projects\FSharp.Data\src\bin\Release\FSharp.Data.DesignTime.1.pdb"
    // pdbstr -w -s:srcsrv -i:FSharp.Data.DesignTime.pdb.srcsrv.txt -p:FSharp.Data.DesignTime.3.pdb
    // pdbstr -r -s:srcsrv -p:FSharp.Data.DesignTime.3.pdb
    let dt3 = @"C:\Projects\FSharp.Data\src\bin\Release\FSharp.Data.DesignTime.3.pdb"

    printNamesByFlagIndex data

//    diffFilesForStream data3 data5 "root"
//    diffInfoStreams data3 data5
//    printSrcSrv data4

//    diffStreamPages data2 data3
//    diffStreamBytes data2 data3

//    printChecksumsPdb data2

//    copyTo data2 data4
//    do
//        use pdb = new PdbFile(data4)
//        pdb.Defrag()
    
//    copyTo data3 data5
//    do
//        use pdb = new PdbFile(data5)
//        pdb.Defrag()

//    copyTo edt1 edt4
//    PdbFile.WriteSrcSrvFileTo edtss edt4
//    printNamesByFlagIndex edt4

//    diffStreamBytes data4 data5
//    diffFilesForStream data4 data5 "1"
//    diffInfoStreams data4 data5
//    printfn "data4"

//    printfn "data5"
//    printNamesByFlagIndex data5
//    do
//        use pdb = new PdbFile(af)
//        printfn "name count %d" pdb.Info.NameToPdbName.Count // pdb.Info.FlagIndexes.Count
//    printNamesByFlagIndex af

//    printNamesByFlagIndex data3
//    printfn ""
//    printNamesByFlagIndex dt3

//    let lg2 = @"C:\Projects\libgit2sharp\LibGit2Sharp\bin\Release\LibGit2Sharp.pdb"
//    let lg3 = @"C:\Projects\libgit2sharp\LibGit2Sharp\obj\Release\LibGit2Sharp.pdb"
//    printNamesByFlagIndex lg2
//    printNamesByFlagIndex 
//    diffInfoStreams lg2 lg3
//    diffStreamBytes lg2 lg3

    0 // exit code
