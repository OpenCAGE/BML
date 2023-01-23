// Alien Isolation (Binary XML converter)
// Written by WRS (xentax.com)

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Xml;
using CathodeLib;
using System.Linq;

namespace CATHODE
{
    /* Handles BML config files as XML douments */
    public class BML : CathodeFile
    {
        public XmlDocument Content { get { return GetContent(); } set { SetContent(value); } }
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public BML(string path) : base(path) { }

        private Header _header;
        private Node _root = new Node();

        #region FILE_IO
        override protected bool LoadInternal()
        {
            bool valid = true;
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                valid &= _header.Read(reader);
                if (!valid)
                {
                    reader.Close();
                    return valid;
                }

                BMLString.StringPool1.Clear();
                BMLString.StringPool2.Clear();
                valid &= ReadAllNodes(reader);
            }
            return valid;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                FixupAllNodes(_root, true);

                Node[] nodearray = GetNodeArray();
                UInt32 node_size = 0;
                foreach (Node n in nodearray)
                    node_size += n.Size();

                MemoryStream p1 = BMLString.StringPool1.Export();
                MemoryStream p2 = BMLString.StringPool2.Export();

                UInt32 block1 = Header.Size() + node_size + 1;
                UInt32 block2 = block1 + 1 + (UInt32)p1.Length;
                UInt32 block3 = block2 + (UInt32)p2.Length;
                UInt32 file_size = block3 + 1;

                _header.Fixup(block1, block2, block3);
                writer.BaseStream.SetLength(file_size);

                UInt32 of1 = block1 + 1;
                UInt32 of2 = block2;

                _header.Write(writer);

                UInt32 cur_depth = 0;
                UInt32 cur_depth_offset = 0;
                foreach (Node n in nodearray)
                {
                    if (n.Flags.Children > 0)
                    {
                        if (cur_depth != n.Depth + 1)
                        {
                            cur_depth_offset = Header.Size();
                            foreach (Node nn in nodearray)
                            {
                                if (nn.Depth == n.Depth + 1) break;
                                cur_depth_offset += nn.Size();
                            }
                            cur_depth = n.Depth + 1;
                        }
                        n.Offset = cur_depth_offset;
                        foreach (Node child in n.Nodes)
                            cur_depth_offset += child.Size();
                    }
                    n.Write(writer, of1, of2);
                }

                writer.BaseStream.Seek(block1 + 1, SeekOrigin.Begin);
                p1.WriteTo(writer.BaseStream);
                p2.WriteTo(writer.BaseStream);
            }
            return true;
        }
        #endregion

        #region ACCESSORS
        /* Get the content of the BML file (as XML) */
        private XmlDocument GetContent()
        {
            FixupAllNodes(_root, true);

            XmlDocument xml = new XmlDocument();
            xml.LoadXml(DumpNode(_root));
            return xml;
        }

        /* Set the content of the BML file (as XML) */
        private bool SetContent(XmlDocument doc)
        {
            bool valid = true;
            BMLString.StringPool1.Clear();
            BMLString.StringPool2.Clear();
            _root.SetDeclaration();
            foreach (XmlNode xnode in doc.ChildNodes)
            {
                switch (xnode.NodeType)
                {
                    case XmlNodeType.XmlDeclaration:
                        XmlDeclaration decl = (xnode as XmlDeclaration);
                        if (decl.Version != null && decl.Version.Length > 0)
                        {
                            Attribute ver = new Attribute();
                            ver.ReadXML("version", decl.Version);
                            _root.Attributes.Add(ver);
                        }
                        if (decl.Encoding != null && decl.Encoding.Length > 0)
                        {
                            Attribute enc = new Attribute();
                            enc.ReadXML("encoding", decl.Encoding);
                            _root.Attributes.Add(enc);
                        }
                        if (decl.Standalone != null && decl.Standalone.Length > 0)
                        {
                            Attribute sta = new Attribute();
                            sta.ReadXML("standalone", decl.Standalone);
                            _root.Attributes.Add(sta);
                        }
                        _root.Flags.Attributes = Convert.ToByte(_root.Attributes.Count & 0xFF);
                        break;
                    case XmlNodeType.Element:
                        Node actual_root = new Node();
                        valid &= actual_root.ReadXML(xnode as XmlElement, _root.Depth + 1);
                        _root.Nodes.Add(actual_root);
                        break;
                }
            }
            return valid;
        }
        #endregion

        #region HELPERS
        private bool ReadWrapper(BinaryReader br, ref Node owner, UInt32 depth)
        {
            bool success = true;

            Node n = new Node();
            success &= n.Read(br, depth);

            // try to parse other blocks
            if( success )
            {
                if( n.Offset != 0 )
                {
                    long pos = br.BaseStream.Position;

                    // seek to child pos
                    br.BaseStream.Position = n.Offset;

                    for (UInt32 i = 0; i < n.Flags.Children; i++)
                    {
                        success &= ReadWrapper(br, ref n, depth + 1);
                    }

                    // seek back
                    br.BaseStream.Position = pos;
                }

                owner.Nodes.Add(n);
            }

            return success;
        }

        private bool ReadAllNodes(BinaryReader br)
        {
            bool success = _root.Read(br, 0);

            // this is always the initial node
            success &= (_root.Text.value == "?xml");

            // there should be at least 1 child node
            success &= (_root.Flags.Children > 0);
            
            if(!success) return false;
            
            success &= ReadWrapper(br, ref _root, _root.Depth +1);

            return success;
        }

        private string DumpNode(Node n, int depth = 0)
        {
            string d = "";
            bool ignored = (depth == 0 && n.Attributes.Count == 0 );
            
            if( !ignored )
            {
                d += String.Format("<{0}", n.Text.value);

                foreach (Attribute a in n.Attributes)
                {
                    // Now encodes XML entities (value)
                    d += String.Format(" {0}=\"{1}\"", a.Name.value, BMLString.EncodeXml(a.Value.value));
                }
            }

            if (n.Nodes.Count > 0)
            {
                if (!ignored)
                {
                    if( depth == 0 )
                    {
                        // first xml tag must end in matching <? tags ?>
                        d += "?";
                    }

                    d += ">";

                    if( n.End != null ) d += n.End.value;
                }

                if (n.Inner != null && n.Inner.value.Length != 0)
                {
                    // Now encodes XML entities
                    d += BMLString.EncodeXml(n.Inner.value);
                }

                foreach (Node node in n.Nodes)
                {
                    d += DumpNode(node, depth + 1);
                }

                // uh, the first xml tag doesn't need to close
                if (depth != 0)
                {
                    d += String.Format("</{0}>", n.Text.value);

                    if( n.End2 != null ) d += n.End2.value;
                }
            }
            else if( !ignored )
            {
                if (n.Inner != null && n.Inner.value.Length != 0)
                {
                    d += ">";
                    d += n.Inner.value;
                    d += String.Format("</{0}>", n.Text.value);
                    d += n.End2.value;
                }
                else
                {
                    // <tag /> and <tag a="b" />
                    if( n.Attributes.Count > 0 )
                    {
                        d += " ";
                    }

                    d += "/>";

                    if (n.End != null) d += n.End.value;
                    if (n.End2 != null) d += n.End2.value;
                }
            }

            return d;
        }

        private void FixupAllNodes(Node n, bool last_node)
        {
            n.Fixup(last_node);

            int last = n.Nodes.Count - 1;
            int count = 0;
            foreach (Node c in n.Nodes)
            {
                FixupAllNodes(c, count == last);
                ++count;
            }
        }

        private Node[] GetNodesAtDepth(List<Node> nodes, UInt32 depth)
        {
            List<Node> local_nodes = new List<Node>();

            foreach( Node n in nodes )
            {
                if( n.Depth == depth )
                {
                    local_nodes.Add(n);
                }
                else if( n.Depth < depth )
                {
                    Node[] ns = GetNodesAtDepth(n.Nodes, depth);
                    if( ns.Length > 0 )
                    {
                        local_nodes.AddRange(ns);
                    }
                }
            }

            return local_nodes.ToArray();
        }

        private Node[] GetNodeArray()
        {
            List<Node> nodes = new List<Node>();

            // level 0
            nodes.Add(_root);
            // level 1+
            UInt32 depth = 1;
            while ( true )
            {
                Node[] ns = GetNodesAtDepth(_root.Nodes, depth);

                if( ns.Length == 0 )
                {
                    break;
                }
                else
                {
                    nodes.AddRange(ns);
                    ++depth;
                }
            }

            return nodes.ToArray();
        }
        #endregion

        #region STRUCTURES
        struct Header
        {
            const string XML_FLAG = "xml\0";

            public UInt32 blockData { get; private set; }
            public UInt32 blockStrings { get; private set; }
            public UInt32 blockLineEndings { get; private set; }

            public bool Read(BinaryReader br)
            {
                bool valid = true;

                string magic = Encoding.Default.GetString(br.ReadBytes(XML_FLAG.Length));
                valid &= (magic == XML_FLAG);

                blockData = br.ReadUInt32();
                blockStrings = br.ReadUInt32();
                blockLineEndings = br.ReadUInt32();

                valid &= (blockLineEndings < br.BaseStream.Length);
                valid &= (blockStrings < blockLineEndings);
                valid &= (blockData < blockStrings);

                return valid;
            }

            public bool Write(BinaryWriter bw)
            {
                bool valid = true;

                bw.Write(Encoding.Default.GetBytes(XML_FLAG), 0, XML_FLAG.Length);

                valid &= blockLineEndings != 0;
                valid &= blockStrings != 0;
                valid &= blockData != 0;

                bw.Write(blockData);
                bw.Write(blockStrings);
                bw.Write(blockLineEndings);

                return valid;
            }

            public void Fixup(UInt32 of1, UInt32 of2, UInt32 of3)
            {
                blockData = of1;
                blockStrings = of2;
                blockLineEndings = of3;
            }

            static public UInt32 Size()
            {
                return 16;
            }
        }

        // complete.
        struct Attribute
        {
            public BMLString.Ref Name { get; private set; }
            public BMLString.Ref Value { get; private set; }

            public bool ReadXML(string str_name, string str_value)
            {
                Name = new BMLString.Ref(str_name, true);
                Value = new BMLString.Ref(BMLString.DecodeXml(str_value), true);

                return true;
            }

            public bool Read(BinaryReader br)
            {
                Name = new BMLString.Ref(br, true);
                Value = new BMLString.Ref(br, true);

                return true;
            }

            public bool Write(BinaryWriter bw)
            {
                bw.Write(Name.offset);
                bw.Write(Value.offset);

                return true;
            }

            static public UInt32 Size()
            {
                return 8;
            }
        }

        class NodeFlags
        {
            public Byte Attributes { get; set; }

            public Byte RawInfo { get; set; }

            public bool unknown_1
            {
                // 1 << 0
                get { return GetFlag(0x1); }
                set { SetFlag(0x1, value); }
            }

            public bool unknown_2
            {
                // 1 << 1
                get { return GetFlag(0x2); }
                set { SetFlag(0x2, value); }
            }

            public bool ContinueSequence
            {
                // 1 << 2
                get { return GetFlag(0x4); }
                set { SetFlag(0x4, value); }
            }

            bool GetFlag(Byte mask)
            {
                return (RawInfo & mask) != 0;
            }

            void SetFlag(Byte mask, bool value)
            {
                RawInfo &= Convert.ToByte((~mask) & 0xFF);
                if (value)
                {
                    RawInfo |= mask;
                }
            }

            public UInt16 Children { get; set; }

            public NodeFlags()
            {
                Attributes = 0;
                RawInfo = 0;
                Children = 0;
            }

            public bool Read(BinaryReader br)
            {
                UInt32 bytes = br.ReadUInt32();

                // bit format:
                // aaaa aaaa iiic cccc cccc cccc cccc cccc

                // 8-bits : number of attributes
                Attributes = Convert.ToByte(bytes & 0xFF);

                // 3-bits : info flags
                RawInfo = Convert.ToByte((bytes >> 8) & 0x7);

                // 21-bits : number of child nodes
                UInt32 raw_children = (bytes >> 11) & 0x1FFFFF;

                // note: we store raw_children as u16 for alignment purposes
                // aaaa aaaa iiic cccc cccc cccc ccc- ---- (- = ignored)

                Children = Convert.ToUInt16(raw_children & 0xFFFF);

                return true;
            }

            public bool Write(BinaryWriter bw)
            {
                UInt32 bytes = 0;

                UInt32 tmp = Children;
                bytes |= tmp << 11;

                tmp = RawInfo;
                bytes |= (tmp & 0x7) << 8;

                tmp = Attributes;
                bytes |= (tmp & 0xFF);

                bw.Write(bytes);

                return true;
            }

            static public UInt32 Size()
            {
                return 4;
            }
        }

        class Node
        {
            public List<Node> Nodes { get; private set; }
            public List<Attribute> Attributes { get; private set; }

            public BMLString.Ref End2 { get; private set; }
            public BMLString.Ref Text { get; private set; }
            public BMLString.Ref End { get; private set; }
            public BMLString.Ref Inner { get; private set; }

            public UInt32 Offset { get; set; } // public modifier
            public UInt32 Depth { get; private set; }

            public NodeFlags Flags { get; private set; }

            public Node()
            {
                Nodes = new List<Node>();
                Attributes = new List<Attribute>();
                Flags = new NodeFlags();
                Depth = 0;
            }

            public void SetDeclaration()
            {
                Text = new BMLString.Ref("?xml", true);
                Flags.Children = 0;
            }

            bool HasElementSibling(XmlElement ele)
            {
                XmlNode sibling = ele.NextSibling;

                while (sibling != null)
                {
                    if (sibling.NodeType == XmlNodeType.Element)
                    {
                        return true;
                    }

                    sibling = sibling.NextSibling;
                }

                return false;
            }

            public bool ReadXML(XmlElement ele, UInt32 depth)
            {
                bool valid = true;

                Depth = depth;
                Text = new BMLString.Ref(ele.Name, true);

                if (ele.HasAttributes)
                {
                    if (ele.Attributes.Count > 0xFF)
                    {
                        valid = false;
                        return valid;
                    }

                    foreach (XmlAttribute attr in ele.Attributes)
                    {
                        Attribute a = new Attribute();
                        a.ReadXML(attr.Name, attr.Value);
                        Attributes.Add(a);
                    }
                }

                if (ele.HasChildNodes)
                {
                    // inner text is treated as a special text node, so it has children.. (YIKES)

                    foreach (XmlNode xnode in ele.ChildNodes)
                    {
                        // special parser requirements

                        switch (xnode.NodeType)
                        {
                            case XmlNodeType.Element:

                                XmlElement child = (xnode as XmlElement);

                                Node nchild = new Node();

                                valid &= nchild.ReadXML(child, depth + 1);

                                if (valid)
                                {
                                    Nodes.Add(nchild);
                                }

                                break;

                            case XmlNodeType.Text:

                                Inner = new BMLString.Ref(BMLString.DecodeXml(xnode.Value), false);
                                End2 = new BMLString.Ref("\r\n", false);

                                break;

                            case XmlNodeType.Comment:
                                // Could be added as Inner/End2, but not required
                                break;

                            default:
                                break;
                        }
                    }
                }

                bool last_child = !HasElementSibling(ele);

                Fixup(last_child);

                return valid;
            }

            public bool Read(BinaryReader br, UInt32 depth)
            {
                bool valid = true;

                Depth = depth;
                Text = new BMLString.Ref(br, true);

                valid &= Flags.Read(br);

                // get attributes

                if (Flags.Attributes > 0)
                {
                    for (UInt32 attribs = 0; attribs < (UInt32)Flags.Attributes; attribs++)
                    {
                        Attribute a = new Attribute();
                        valid &= a.Read(br);

                        if (!valid)
                        {
                            return false;
                        }

                        Attributes.Add(a);
                    }
                }

                switch (Flags.RawInfo)
                {
                    case 0: // 000
                        Offset = br.ReadUInt32();

                        break;

                    case 1: // 001
                        End = new BMLString.Ref(br, false);
                        Offset = br.ReadUInt32();

                        break;

                    case 2: // 010 -> last in sequence
                    case 6: // 110 -> continued sequence
                        End = new BMLString.Ref(br, false);

                        if (Flags.Children > 0)
                        {
                            Offset = br.ReadUInt32();
                        }

                        break;

                    case 3: // 011 -> last in sequence
                    case 7: // 111 -> continued sequence

                        // note: inner text is stored in the second pool

                        Inner = new BMLString.Ref(br, false); // inner text or line diff
                        End2 = new BMLString.Ref(br, false); // line ending

                        if (Flags.Children > 0)
                        {
                            Offset = br.ReadUInt32();
                        }

                        break;

                    default:
                        // flags may need sorting out
                        break;
                }

                return valid;
            }

            public UInt32 Size()
            {
                UInt32 my_size = 0;

                // text offset
                my_size += 4;
                // flags
                my_size += NodeFlags.Size();
                // attribute entries (read from flags)
                my_size += Attribute.Size() * Flags.Attributes;
                // check against info
                switch (Flags.RawInfo)
                {
                    case 0:
                        my_size += 4;
                        break;
                    case 1:
                        my_size += 4 + 4;
                        break;
                    case 2:
                    case 6:
                        my_size += 4;
                        if (Flags.Children > 0) my_size += 4;
                        break;
                    case 3:
                    case 7:
                        my_size += 4 + 4;
                        if (Flags.Children > 0) my_size += 4;
                        break;
                    default:
                        break;
                }

                return my_size;
            }

            public void Fixup(bool last_child)
            {
                Flags.RawInfo = 0;
                Flags.Attributes = Convert.ToByte(Attributes.Count & 0xFF);
                Flags.Children = Convert.ToUInt16(Nodes.Count & 0xFFFF);

                if (Text.value == "?xml")
                {
                    if (Flags.Attributes == 0)
                    {
                        // just offsets to child

                        Flags.unknown_1 = false;
                        Flags.unknown_2 = false;
                        Flags.ContinueSequence = false;

                        // raw flags are now 000
                    }
                    else
                    {
                        if (End == null)
                        {
                            End = new BMLString.Ref("\r\n", false);
                        }

                        // declaration kept - child mandatory
                        Flags.unknown_1 = true;
                        Flags.unknown_2 = false;
                        Flags.ContinueSequence = false;

                        // raw flags are now 001
                    }
                }
                else
                {
                    if (Inner != null)
                    {
                        // has inner kept; child optional
                        Flags.unknown_1 = true;
                        Flags.unknown_2 = true;
                        Flags.ContinueSequence = !last_child;

                        // raw flags are now either 011 or 111

                        if (End2 == null)
                        {
                            End2 = new BMLString.Ref("\r\n", false);
                        }
                    }
                    else
                    {
                        // end spacing, child optional
                        Flags.unknown_1 = false;
                        Flags.unknown_2 = true;
                        Flags.ContinueSequence = !last_child;

                        // raw flags are now either 010 or 110

                        if (End == null)
                        {
                            End = new BMLString.Ref("\r\n", false);
                        }
                    }
                }
            }

            public bool Write(BinaryWriter bw, UInt32 of1, UInt32 of2)
            {
                // text offset
                Text.Fixup(of1, of2);
                bw.Write(Text.offset);
                // flags
                Flags.Write(bw);
                foreach (Attribute a in Attributes)
                {
                    a.Name.Fixup(of1, of2);
                    a.Value.Fixup(of1, of2);
                    a.Write(bw);
                }

                switch (Flags.RawInfo)
                {
                    case 0:
                        bw.Write(Offset);
                        break;
                    case 1:
                        End.Fixup(of1, of2);
                        bw.Write(End.offset);
                        bw.Write(Offset);
                        break;
                    case 2:
                    case 6:
                        End.Fixup(of1, of2);
                        bw.Write(End.offset);
                        if (Flags.Children > 0) bw.Write(Offset);
                        break;
                    case 3:
                    case 7:
                        Inner.Fixup(of1, of2);
                        End2.Fixup(of1, of2);
                        bw.Write(Inner.offset);
                        bw.Write(End2.offset);
                        if (Flags.Children > 0) bw.Write(Offset);
                        break;
                    default:
                        break;
                }

                return true;
            }
        }

        class BMLString
        {
            #region Common methods for reading strings from a BinaryReader

            static public string MakeCleanString(Byte[] bytes)
            {
                return Encoding.Default.GetString(bytes).TrimEnd('\0');
            }

            static public string ReadInlineNullTerminatedString(BinaryReader br)
            {
                List<byte> buf = new List<byte>();

                for (byte b = br.ReadByte(); b != 0x0; b = br.ReadByte())
                {
                    buf.Add(b);
                }

                return MakeCleanString(buf.ToArray());
            }

            static public string ReadNullTerminatedStringAt(BinaryReader br, UInt32 targetpos)
            {
                br.BaseStream.Position = targetpos;
                return ReadInlineNullTerminatedString(br);
            }

            static public string ReadNullTerminatedString(BinaryReader br, UInt32 targetpos)
            {
#if DEBUG
                if (targetpos >= br.BaseStream.Length)
                {
                    return "";
                }
#endif

                long pos = br.BaseStream.Position;
                string str = ReadNullTerminatedStringAt(br, targetpos);
                br.BaseStream.Position = pos;

                return str;
            }

            #endregion

            static private void FixupXmlEntityInternal(ref string str, string src, string dest)
            {
                if (str.IndexOf(src) >= 0)
                {
                    str = str.Replace(src, dest);
                }
            }

            static public string EncodeXml(string str)
            {
                string xml_str = str;

                FixupXmlEntityInternal(ref xml_str, "\"", "&quot;");
                FixupXmlEntityInternal(ref xml_str, "&", "&amp;");
                FixupXmlEntityInternal(ref xml_str, "'", "&apos;");
                FixupXmlEntityInternal(ref xml_str, "<", "&lt;");
                FixupXmlEntityInternal(ref xml_str, ">", "&gt;");

                return xml_str;
            }

            static public string DecodeXml(string xml_str)
            {
                string str = xml_str;

                FixupXmlEntityInternal(ref str, "&quot;", "\"");
                FixupXmlEntityInternal(ref str, "&amp;", "&");
                FixupXmlEntityInternal(ref str, "&apos;", "'");
                FixupXmlEntityInternal(ref str, "&lt;", "<");
                FixupXmlEntityInternal(ref str, "&gt;", ">");

                return str;
            }

            struct Inst
            {
                public string Value;
                public UInt32 Offset;

                public Inst(string str_value)
                {
                    Value = str_value;
                    Offset = 0;
                }
            }

            public class Cache
            {
                private List<Inst> Strings;

                public Cache()
                {
                    Strings = new List<Inst>();
                }

                public void Clear()
                {
                    Strings.Clear();
                }

                public void AddString(string str)
                {
                    if (!Strings.Exists(i => i.Value == str))
                    {
                        Strings.Add(new Inst(str));
                    }
                }

                public void PrepareForExport()
                {
                    // xxxxx refactor asap

                    Inst[] items = Strings.OrderBy(x => x.Value).ToArray();

                    UInt32 InternalOffset = 0;

                    for (int i = 0; i < items.Count(); i++)
                    {
                        items[i].Offset = InternalOffset;
                        InternalOffset += Convert.ToUInt32(items[i].Value.Length + 1);
                    }

                    Strings = items.ToList();
                }

                public UInt32 GetOffset(string str)
                {
                    int idx = Strings.FindIndex(i => i.Value == str);
                    if (idx != -1)
                    {
                        return Strings[idx].Offset;
                    }

                    return 0;
                }

                public MemoryStream Export()
                {
                    // Sorts cache
                    PrepareForExport();

                    MemoryStream ms = new MemoryStream();

                    foreach (Inst i in Strings)
                    {
                        Byte[] data = Encoding.Default.GetBytes(i.Value + "\0");
                        ms.Write(data, 0, data.Length);
                    }

                    return ms;
                }
            }

            public static Cache StringPool1 = new Cache(); // node and attribute names
            public static Cache StringPool2 = new Cache(); // preserved spacing, inner text

            public class Ref
            {
                public string value { get; private set; }
                public UInt32 offset { get; private set; }

                bool main_pool;

                void AddToCache()
                {
                    if (main_pool) StringPool1.AddString(value);
                    else StringPool2.AddString(value);
                }

                public Ref(string raw_string, bool coreString)
                {
                    offset = 0;
                    value = raw_string;
                    main_pool = coreString;

                    AddToCache();
                }

                public Ref(BinaryReader br, bool coreString)
                {
                    offset = 0;
                    value = ReadNullTerminatedString(br, br.ReadUInt32());
                    main_pool = coreString;

                    AddToCache();
                }

                public void Fixup(UInt32 block_1, UInt32 block_2)
                {
                    offset = main_pool ? block_1 : block_2;
                    offset += main_pool ? StringPool1.GetOffset(value) : StringPool2.GetOffset(value);
                }
            }
        }
        #endregion
    }
}
