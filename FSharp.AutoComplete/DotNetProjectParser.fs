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

    // We really want the output of ResolveAssemblyReferences. However, this
    // needs as input ChildProjectReferences, which is populated by
    // ResolveProjectReferences. For some reason ResolveAssemblyReferences
    // does not depend on ResolveProjectReferences, so if we don't run it first
    // then we won't get the dll files for imported projects in this list.
    // We can therefore build ResolveReferences, which depends on both of them,
    // or [|"ResolveProjectReferences";"ResolveAssemblyReferences"|]. These seem
    // to be equivalent. See Microsoft.Common.targets if you want more info.
    member x.GetReferences =
      if p.Build([|"ResolveReferences"|], []) then
        [| for i in p.GetItems("ReferencePath") do
             yield "-r:" + i.EvaluatedInclude |]
      else
        [|"Build failed"|]

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



