open System
open System.Text.RegularExpressions


let isMono = Type.GetType ("Mono.Runtime") <> null

let fsi =
  if isMono
    then "fsharpi"
    else @"C:\Program Files (x86)\Microsoft SDKs\F#\3.0\Framework\v4.0\fsi.exe"

let absPathRegex =
  sprintf "%s.*?FSharp.AutoComplete%ctest%c(.*?(\"|\$))"
    (if isMono then "/" else @".:\")
    IO.Path.PathSeparator
    IO.Path.PathSeparator

let absPathReplacement =
  sprintf @"<absolute path removed>%ctest%c\1"
    IO.Path.PathSeparator
    IO.Path.PathSeparator
  
let runners =
  IO.Directory.GetFiles(Environment.CurrentDirectory,
                        "*Runner.fsx",
                        IO.SearchOption.AllDirectories)

for runner in runners do
  let p = new System.Diagnostics.Process()
  p.StartInfo.FileName  <- fsi
  p.StartInfo.Arguments <- "--exec " + runner
  p.StartInfo.UseShellExecute <- false
  printfn "Running %s %s" p.StartInfo.FileName p.StartInfo.Arguments
  p.Start () |> ignore
  p.WaitForExit()
  
let outputs =
  IO.Directory.GetFiles(Environment.CurrentDirectory,
                        "*txt",
                        IO.SearchOption.AllDirectories)

for output in outputs do
  let text = IO.File.ReadAllText(output)
  let text' = Regex.Replace(text, absPathRegex, absPathReplacement)
  IO.File.WriteAllText(output, text')
