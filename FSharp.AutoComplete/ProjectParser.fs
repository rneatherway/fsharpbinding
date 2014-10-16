// --------------------------------------------------------------------------------------
// (c) Robin Neatherway
// --------------------------------------------------------------------------------------
namespace FSharp.InteractiveAutocomplete

open System

module ProjectParser =

  let load file =
    if true then//Type.GetType ("Mono.Runtime") <> null then
      MonoProjectParser.Load file
    else
      DotNetProjectParser.Load file

