module ProjectFileTests

open NUnit.Framework
open FsUnit

open FSharp.InteractiveAutocomplete
open System.IO

[<Test>]
let TestProjectFiles () =
  let p = ProjectParser.load "../ProjectLoading/data/Test1.fsproj"
  Option.isSome p |> should be True
  let rs = p.Value.GetFiles
  rs |> should haveLength 2
  rs |> Array.map Path.GetFileName
     |> should equal [| "Test1File1.fs"; "Test1File2.fs" |]

[<Test>]
let TestProjectFiles2 () =
  let p  = ProjectParser.load "../ProjectLoading/data/Test2.fsproj"
  Option.isSome p |> should be True
  let rs = p.Value.GetFiles
  rs |> should haveLength 2
  rs |> Array.map Path.GetFileName
     |> should equal [| "Test2File1.fs"; "Test2File2.fs" |]
