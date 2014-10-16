// --------------------------------------------------------------------------------------
// (c) Robin Neatherway
// --------------------------------------------------------------------------------------
namespace FSharp.InteractiveAutocomplete

// Disable warnings for obsolete MSBuild.
// Mono doesn't support the latest API.
#nowarn "0044"

open System
open System.IO
open System.Xml
open Microsoft.Build
open Microsoft.Build.Execution
open FSharp.CompilerBinding

type MonoProjectParser private (p: ProjectInstance) =

  let loadtime = DateTime.Now

  static member Load (uri : string) : Option<IProjectParser> =
    
    if File.Exists uri then
      try
        let engine = new Evaluation.ProjectCollection()
//        engine.DefaultToolsVersion <- "12.0"
        let xmlReader = XmlReader.Create(uri)
        let p = engine.LoadProject(xmlReader, "12.0", FullPath=uri)
        let p = p.CreateProjectInstance()
        System.GC.SuppressFinalize(Microsoft.Build.Execution.BuildManager.DefaultBuildManager)
        Some (new MonoProjectParser(p) :> IProjectParser)
      with :? Exceptions.InvalidProjectFileException as e ->
        //printfn "Exception while loading project:\n%A" e
        None
    else
      None

  interface IProjectParser with
    member x.FileName = p.FullPath
    member x.LoadTime = loadtime
    member x.Directory = p.Directory

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
      IO.Path.Combine((x :> IProjectParser).Directory,
                      (p.GetPropertyValue "OutDir"),
                      (p.GetPropertyValue "TargetFileName"))

    member x.GetReferences =
      // let l = new Logging.ConsoleLogger() :> Framework.ILogger
      // l.Verbosity <- Framework.LoggerVerbosity.Detailed
      let x = x :> IProjectParser
      let b = p.Build([|"ResolveAssemblyReferences"|], [])
      [| for i in p.GetItems("ReferencePath") do
           yield "-r:" + i.EvaluatedInclude
         for cp in p.GetItems("ProjectReference") do
           match MonoProjectParser.Load (cp.GetMetadataValue("FullPath")) with
           | None -> ()
           | Some p' -> yield "-r:" + p'.Output
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
