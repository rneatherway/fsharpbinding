// --------------------------------------------------------------------------------------
// (c) Robin Neatherway
// --------------------------------------------------------------------------------------
namespace FSharp.InteractiveAutocomplete

open System
open Microsoft.Build
open Microsoft.Build.Execution

open FSharp.CompilerBinding

type DotNetProjectParser private (p: ProjectInstance) =

  let loadtime = DateTime.Now

  static member Load (uri : string) : Option<IProjectParser> =
    try
      let p = new ProjectInstance(uri, dict [ "VisualStudioVersion", "12.0" ], "4.0")
      Some (new DotNetProjectParser(p) :> IProjectParser)
    with
      | :? Exceptions.InvalidProjectFileException -> None

  member private x.Dir = p.Directory

  member x.GetAssemblyReferences =
    ignore <| p.Build([|"ResolveAssemblyReferences"|], [])
    [| for i in p.GetItems("ReferencePath") do
         yield "-r:" + i.EvaluatedInclude |]

  interface IProjectParser with
    member x.FileName = p.FullPath
    member x.LoadTime = loadtime
    member x.Directory = x.Dir

    member x.GetFiles =
      let fs  = p.GetItems("Compile")
      let dir = (x :> IProjectParser).Directory
      [| for f in fs do yield IO.Path.Combine(dir, f.EvaluatedInclude) |]

    member x.FrameworkVersion =
      match p.GetPropertyValue("TargetFrameworkVersion") with
      | "v2.0" -> FSharpTargetFramework.NET_2_0
      | "v3.0" -> FSharpTargetFramework.NET_3_0
      | "v3.5" -> FSharpTargetFramework.NET_3_5
      | "v4.0" -> FSharpTargetFramework.NET_4_0
      | "v4.5" -> FSharpTargetFramework.NET_4_5
      | _      -> FSharpTargetFramework.NET_4_5

    member x.Output =
      if p.Build([|"GetTargetPath"|], []) then
        p.GetPropertyValue "TargetPath"
      else
        "Build failed"

    // On .NET MSBuild, it seems to be the case that child projects
    // are built with the 'default targets' when trying to resolve
    // assembly references recursively. This is a) overkill, and
    // b) doesn't always succeed. Here we recursively descend through
    // the projects getting their dependencies.
    //
    // This may not actually be necessary if parent projects are always
    // required to explicitly reference assemblies containing types they
    // need anyway.
    member x.GetReferences =
      [|
         yield! x.GetAssemblyReferences
         for cp in p.GetItems("ProjectReferenceWithConfiguration") do
           if cp.GetMetadataValue("ReferenceOutputAssembly")
                .ToLower() = "true"
           then
             match DotNetProjectParser.Load (cp.GetMetadataValue("FullPath")) with
             | None -> ()
             | Some p -> yield! p.GetReferences
      |]

    member x.GetOptions =
      let getprop s = p.GetPropertyValue s
      let split (s: string) (cs: char[]) =
        if s = null then [||]
        else s.Split(cs, StringSplitOptions.RemoveEmptyEntries)
      let getbool (s: string) =
        match (Boolean.TryParse s) with
        | (true, result) -> result
        | (false, _) -> false
      let optimize     = getprop "Optimize" |> getbool
      let tailcalls    = getprop "Tailcalls" |> getbool
      let debugsymbols = getprop "DebugSymbols" |> getbool
      let defines = split (getprop "DefineConstants") [|';';',';' '|]
      let otherflags = getprop "OtherFlags" 
      let otherflags = if otherflags = null
                       then [||]
                       else split otherflags [|' '|]
      [|
        yield "--noframework"
        for symbol in defines do yield "--define:" + symbol
        yield if debugsymbols then  "--debug+" else  "--debug-"
        yield if optimize then "--optimize+" else "--optimize-"
        yield if tailcalls then "--tailcalls+" else "--tailcalls-"
        yield! otherflags
        yield! (x :> IProjectParser).GetReferences
       |]



