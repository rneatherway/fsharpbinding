namespace FSharp.InteractiveAutocomplete

open System
open System.IO

open System.XML
open System.XML.Linq

module ProjectFile =

  let load (fn: string) : XDocument =
    XDocument.Load(fn)

  let setTargetFSharpCore (xd: XDocument) =
    ()
