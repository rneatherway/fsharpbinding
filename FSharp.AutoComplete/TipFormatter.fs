// --------------------------------------------------------------------------------------
// (c) Tomas Petricek, http://tomasp.net/blog
// --------------------------------------------------------------------------------------
module internal FSharp.InteractiveAutocomplete.TipFormatter

open System.Text
open System.IO
open System.Xml.Linq
open Microsoft.FSharp.Compiler.SourceCodeServices

// --------------------------------------------------------------------------------------
// Formatting of tool-tip information displayed in F# IntelliSense
// --------------------------------------------------------------------------------------

let private buildFormatComment cmt (sb:StringBuilder) =
  match cmt with
  | XmlCommentText(s) -> sb.AppendLine(s)
  // For 'XmlCommentSignature' we could get documentation from 'xml'
  // files, but I'm not sure whether these are available on Mono
  | XmlCommentSignature(s1,s2) ->
    
      let root = Path.Combine ((Path.GetDirectoryName s1),
                               (Path.GetFileNameWithoutExtension s1))
      let x = List.map
      let xmlfile =
        let f1 = Path.ChangeExtension(s1, ".xml")
        Debug.printc "XmlSig" "Looking for '%s'" f1
        let f2 = Path.ChangeExtension(s1, ".XML")
        Debug.printc "XmlSig" "... or '%s'" f2
        if File.Exists f1 then Some f1
        else if File.Exists f2 then Some f2
        else None

      let xn s = XName.Get s
      Debug.printc "XmlSig" "%s, %s" s1 s2
      match xmlfile with
      | None -> sb
      | Some f ->
          Debug.printc "XmlSig" "'%s' exists!" f
          let doc = XDocument.Load f
          let node =
            doc.Element(xn "doc").Element(xn "members").Elements(xn "member")
            |> Seq.tryFind (fun xe -> xe.Attribute(xn "name").Value = s2)
          Debug.printc "XmlSig" "node found: %b" (Option.isSome node)
          match node with
          | None -> sb
          | Some n -> sb.AppendLine(n.Element(xn "summary").Value)

  | _ -> sb

let newfunction s =
  List.map ((+) 1) [1..3]

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
