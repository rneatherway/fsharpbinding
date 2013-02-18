#r "System.Xml.dll"
#r "System.Xml.Linq.dll"
open System
open System.IO
open System.Xml.Linq

let getElemName name = XName.Get(name, "http://schemas.microsoft.com/developer/msbuild/2003")

let getElemValue name (parent:XElement) =
    let elem = parent.Element(getElemName name)
    if elem = null || String.IsNullOrEmpty elem.Value then None else Some(elem.Value)
    
let getAttrValue name (elem:XElement) =
    let attr = elem.Attribute(XName.Get name)
    if attr = null || String.IsNullOrEmpty attr.Value then None else Some(attr.Value)

let text = """
<?xml version="1.0" encoding="utf-8"?>
<doc>
<assembly><name>FSharp.Core</name></assembly>
<members>
<member name="P:Microsoft.FSharp.Collections.FSharpList`1.Tail">
 <summary>Gets the tail of the list, which is a list containing all the elements of the list, excluding the first element </summary>
</member>
<member name="P:Microsoft.FSharp.Collections.FSharpList`1.Length">
 <summary>Gets the number of items contained in the list</summary>
</member>
</members>
</doc>
"""

let doc = XDocument.Load "xmltest.xml"

let xn s = XName.Get s

let props = doc.Element(xn "doc").Element(xn "members").Elements(xn "member")
            |> Seq.tryFind (fun xe -> xe.Attribute(xn "name").Value = "P:Microsoft.FSharp.Collections.FSharpList`1.Length")
            |> Option.get |> (fun xe -> xe.Element(xn "summary").Value)
              

doc.Element(xn "doc").Elements(xn "member")
doc.Element(xn "doc").Elements(xn "assembly")
doc.Descendants(xn "doc").Where(fun (p: XElement) -> p.Attribute(xn "name").Value = "P:Microsoft.FSharp.Collections.FSharpList`1.Tail").Single()

Path.Combine(
  Path.GetDirectoryName ("/home/scratch/local_mono/lib/mono/4.0/FSharp.Core.dll"),
  Path.GetFileNameWithoutExtension ("/home/scratch/local_mono/lib/mono/4.0/FSharp.Core.dll"))
