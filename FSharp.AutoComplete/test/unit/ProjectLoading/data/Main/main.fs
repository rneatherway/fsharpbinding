module Main

open Samples.FSharp.RegexTypeProvider

type rp = RegexTyped<"ASDF">

[<EntryPoint>]
let main args =
    printfn "%A" (rp.IsMatch("ASDF"))
    0
