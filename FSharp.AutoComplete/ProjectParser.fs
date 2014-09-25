//#r @"C:\Program Files (x86)\MSBuild\12.0\bin\Microsoft.Build.dll"
//#r @"C:\Program Files (x86)\MSBuild\12.0\bin\Microsoft.Build.Framework.dll"
namespace FSharp.InteractiveAutocomplete

open System

module ProjectParser =

  let load file =
    if Type.GetType ("Mono.Runtime") <> null then
      MonoProjectParser.Load file
    else
      DotNetProjectParser.Load file

