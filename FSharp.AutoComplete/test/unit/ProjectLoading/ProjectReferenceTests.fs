module ProjectReferenceTests

open NUnit.Framework
open FsUnit

open FSharp.InteractiveAutocomplete
open System.IO

[<Test>]
let TestProjectLibraryResolution () =
  let p = ProjectParser.load "../ProjectLoading/data/Test1.fsproj"
  Option.isSome p |> should be True
  let rs = p.Value.GetReferences
  rs |> should haveLength 4

[<Test>]
let Test2ndLevelDepsResolution () =
  let p  = ProjectParser.load "../ProjectLoading/data/Test2.fsproj"
  Option.isSome p |> should be True
  let rs = p.Value.GetReferences
  rs |> should haveLength 5

  for r in rs do
    r |> should startWith "-r:"

  let subProjectStr = "data/Test1/bin/Debug/Test1.dll".Replace('/',Path.DirectorySeparatorChar)
  Seq.last rs |> should endWith subProjectStr

[<Test>]
let TestIssue761ContainsSystem () =
  let p = ProjectParser.load "../ProjectLoading/data/Main/main.fsproj"
  Option.isSome p |> should be True
  let rs = p.Value.GetReferences
           |> Array.map Path.GetFileName

  rs |> should contain "System.dll"
  
