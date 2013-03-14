// --------------------------------------------------------------------------------------
// (c) Tomas Petricek, http://tomasp.net/blog
// --------------------------------------------------------------------------------------
module internal FSharp.InteractiveAutocomplete.TipFormatter

open System.Text
open System.IO
open System.Xml
open System.Xml.Linq
open Microsoft.FSharp.Compiler.SourceCodeServices

open Monodoc
open System.Diagnostics
// ---------------------------------------------------------------------------
// Formatting of tool-tip information displayed in F# IntelliSense
// ---------------------------------------------------------------------------

let tree : Ref<Option<RootTree>> = ref None

let getHelpTree () =
  match !tree with
  | None -> let t = RootTree.LoadTree()
            tree := Some t
            t
  | Some t -> t
  
let (|MemberName|_|) (name:string) = 
  let dotRight = name.LastIndexOf '.'
  if dotRight < 1 || dotRight >= name.Length - 1 then None else
    let typeName = name.[0..dotRight-1]
    let elemName = name.[dotRight+1..]
    Some (typeName,elemName)

let (|MethodKey|_|) (key:string) = 
   if key.StartsWith "M:" then 
       let key = key.[2..]
       let name,count,args = 
           if not (key.Contains "(") then key, 0, [| |] else

           let pieces = key.Split( [|'('; ')' |], System.StringSplitOptions.RemoveEmptyEntries)
           if pieces.Length < 2 then key, 0, [| |] else
           let nameAndCount = pieces.[0]
           let argsText = pieces.[1].Replace(")","")
           let args = argsText.Split(',')
           if nameAndCount.Contains "`" then 
               let ps = nameAndCount.Split( [| '`' |],System.StringSplitOptions.RemoveEmptyEntries) 
               ps.[0], (try int ps.[1] with _ -> 0) , args
           else
               nameAndCount, 0, args

       match name with 
       | MemberName(typeName,elemName) -> Some (typeName, elemName, count, args)
       | _ -> None
   else None

let (|SimpleKey|_|) (key:string) = 
  if key.StartsWith "P:" || key.StartsWith "F:" || key.StartsWith "E:" then 
    let name = key.[2..]
    // printfn "AAA name = %A" name
    match name with 
    | MemberName(typeName,elemName) -> Some (typeName, elemName)
    | _ -> None
  else None

let trySelectOverload (nodes: XmlNodeList, argsFromKey:string[]) =

    //printfn "AAA argsFromKey = %A" argsFromKey
    if (nodes.Count = 1) then Some nodes.[0] else

    let result = 
      [ for x in nodes -> x ] |> Seq.tryFind (fun curNode -> 
        let paramList = curNode.SelectNodes ("Parameters/*")

        printfn "AAA paramList = %A" [ for x in paramList -> x.OuterXml ]

        (paramList <> null) &&
        (argsFromKey.Length = paramList.Count) 
        (* &&
        (p, paramList) ||> Seq.forall2 (fun pi pmi -> 
          let idString = GetTypeString pi.Type
          (idString = pmi.Attributes ["Type"].Value)) *) )

    match result with 
    | None -> None
    | Some node -> 
        let docs = node.SelectSingleNode ("Docs") 
        if docs = null then None else Some docs

  
let tryGetDoc key = 
  let helpTree = getHelpTree ()
  if helpTree = null then None 
  else
    try 
      let helpxml = helpTree.GetHelpXml(key)
      if helpxml = null then None else Some(helpxml)
    with ex -> Debug.WriteLine (sprintf "GetHelpXml failed for key %s:\r\n\t%A" key ex)
               None  

let findMonoDocProviderForEntity (file, key) = 
  Debug.WriteLine (sprintf "key= %A, File= %A" key file) 
  let typeMemberFormatter name = "/Type/Members/Member[@MemberName='" + name + "']" 
  match key with  
  | SimpleKey (parentId, name) -> 
    Debug.WriteLine (sprintf "SimpleKey parentId= %s, name= %s" parentId name )
    match tryGetDoc ("T:" + parentId) with
    | Some doc -> let docXml = doc.SelectSingleNode (typeMemberFormatter name)
                  Debug.WriteLine (sprintf "SimpleKey xml (simple)= null" )
                  if docXml = null then None else 
                  Debug.WriteLine (sprintf "Simple xml (simple)= <<<%s>>>" docXml.OuterXml )
                  Some docXml.OuterXml
    | None -> None
  | MethodKey(parentId, name, count, args) -> 
      Debug.WriteLine (sprintf "MethodKey, parentId= %s, name= %s, count= %i args= %A" parentId name count args )
      match tryGetDoc ("T:" + parentId) with
      | Some doc -> let nodeXmls = doc.SelectNodes (typeMemberFormatter name)
                    let docXml = trySelectOverload (nodeXmls, args)
                    docXml |> Option.map (fun xml -> xml.OuterXml) 
      | None -> None
  | _ -> Debug.WriteLine (sprintf "**No match for key = %s" key)
         None

let findXmlDocProviderForEntity (file, key) =
  let root = Path.Combine ((Path.GetDirectoryName file),
                           (Path.GetFileNameWithoutExtension file))
  let x = List.map
  let xmlfile =
    let f1 = Path.ChangeExtension(file, ".xml")
    Debug.printc "XmlSig" "Looking for '%s'" f1
    let f2 = Path.ChangeExtension(file, ".XML")
    Debug.printc "XmlSig" "... or '%s'" f2
    if File.Exists f1 then Some f1
    else if File.Exists f2 then Some f2
    else None

  let xn s = XName.Get s
  Debug.printc "XmlSig" "%s, %s" file key
  match xmlfile with
  | None -> None
  | Some f ->
      Debug.printc "XmlSig" "'%s' exists!" f
      let doc = XDocument.Load f
      let node =
        doc.Element(xn "doc").Element(xn "members").Elements(xn "member")
        |> Seq.tryFind (fun xe -> xe.Attribute(xn "name").Value = key)
      Debug.printc "XmlSig" "node found: %b" (Option.isSome node)
      match node with
      | None -> None
      | Some n -> Some (n.Element(xn "summary").Value)

let findDocForEntity (file, key)  = 
  match findXmlDocProviderForEntity (file, key) with 
  | Some doc -> Some doc
  | None -> findMonoDocProviderForEntity (file, key) 


let private buildFormatComment cmt (sb:StringBuilder) =
  match cmt with
  | XmlCommentText(s) -> sb.AppendLine(s)
  // For 'XmlCommentSignature' we could get documentation from 'xml'
  // files, but I'm not sure whether these are available on Mono
  | XmlCommentSignature(file,key) ->

      match findDocForEntity (file, key) with
      | None -> sb
      | Some doc -> sb.AppendLine(doc)
    
  | _ -> sb

// If 'isSingle' is true (meaning that this is the only tip displayed)
// then we add first line "Multiple overloads" because MD prints first
// int in bold (so that no overload is highlighted)
let private buildFormatElement isSingle el (sb:StringBuilder) =
  match el with
  | DataTipElementNone -> sb
  | DataTipElement(it, comment) ->
      sb.AppendLine(it) |> buildFormatComment comment
  | DataTipElementGroup(items) ->
      let items, msg =
        if items.Length > 10 then
          (items |> Seq.take 10 |> List.ofSeq),
            sprintf "   (+%d other overloads)</i>" (items.Length - 10)
        else items, null
      if (isSingle && items.Length > 1) then
        sb.AppendLine("Multiple overloads") |> ignore
      for (it, comment) in items do
        sb.AppendLine(it) |> buildFormatComment comment |> ignore
      if msg <> null then sb.AppendFormat(msg) else sb
  | DataTipElementCompositionError(err) ->
      sb.Append("Composition error: " + err)

let private buildFormatTip tip (sb:StringBuilder) =
  match tip with
  | DataTipText([single]) -> sb |> buildFormatElement true single
  | DataTipText(its) ->
      sb.AppendLine("Multiple items") |> ignore
      its |> Seq.mapi (fun i it -> i = 0, it) |> Seq.fold (fun sb (first, item) ->
        if not first then sb.AppendLine("\n--------------------\n") |> ignore
        sb |> buildFormatElement false item) sb

/// Format tool-tip that we get from the language service as string
let formatTip tip =
  (buildFormatTip tip (new StringBuilder())).ToString().Trim('\n', '\r')
