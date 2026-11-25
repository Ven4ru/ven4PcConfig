using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PcInventory
{
    class XCell
    {
        public object V;
        public int S;
        public XCell(object v, int s) { V = v; S = s; }
    }

    class XSheet
    {
        public string Name;
        public double[] Widths;
        public readonly List<XCell[]> Rows = new List<XCell[]>();
        public bool Filter = true;
        /// <summary>Закрепить первую строку (шапку таблицы).</summary>
        public bool Freeze = true;
        public readonly List<XImage> Images = new List<XImage>();
    }

    /// <summary>Картинка на листе: PNG, левый верхний угол в ячейке (Row, Col — с нуля), размер в пикселях.</summary>
    class XImage
    {
        public byte[] Png;
        public int Row, Col, W, H;
    }

    /// <summary>Минимальный генератор .xlsx (Office Open XML) без внешних библиотек.</summary>
    static class Xlsx
    {
        public const int Header = 1, Text = 2, Date = 3, Group = 4, Center = 5, DateTimeSt = 6, GroupCenter = 7, Bad = 8, Good = 9;

        const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public static byte[] Build(List<XSheet> sheets)
        {
            var ms = new MemoryStream();
            var zip = new ZipWriter(ms);
            var ct = new StringBuilder();
            ct.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            ct.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            ct.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            ct.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            bool anyImages = false;
            foreach (var s in sheets) if (s.Images.Count > 0) anyImages = true;
            if (anyImages) ct.Append("<Default Extension=\"png\" ContentType=\"image/png\"/>");
            for (int i = 0, d = 0; i < sheets.Count; i++)
                if (sheets[i].Images.Count > 0)
                    ct.Append("<Override PartName=\"/xl/drawings/drawing" + (++d) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
            ct.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            ct.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 0; i < sheets.Count; i++)
                ct.Append("<Override PartName=\"/xl/worksheets/sheet" + (i + 1) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            ct.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            ct.Append("<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>");
            ct.Append("</Types>");
            zip.Add("[Content_Types].xml", ct.ToString());

            zip.Add("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
                "</Relationships>");

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            zip.Add("docProps/core.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                "<dc:title>Реестр АРМ</dc:title><dc:creator>ven4PcConfig</dc:creator>" +
                "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + now + "</dcterms:created>" +
                "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + now + "</dcterms:modified></cp:coreProperties>");
            zip.Add("docProps/app.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>ven4PcConfig</Application></Properties>");

            var wb = new StringBuilder();
            wb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            wb.Append("<workbook xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\">");
            wb.Append("<bookViews><workbookView activeTab=\"0\"/></bookViews><sheets>");
            for (int i = 0; i < sheets.Count; i++)
                wb.Append("<sheet name=\"" + Esc(SheetName(sheets[i].Name)) + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>");
            wb.Append("</sheets><definedNames>");
            for (int i = 0; i < sheets.Count; i++)
            {
                var s = sheets[i];
                if (!s.Filter || s.Rows.Count == 0) continue;
                wb.Append("<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"" + i + "\" hidden=\"1\">'" +
                          Esc(SheetName(s.Name).Replace("'", "''")) + "'!$A$1:$" + Col(s.Widths.Length - 1) + "$" + s.Rows.Count + "</definedName>");
            }
            wb.Append("</definedNames></workbook>");
            zip.Add("xl/workbook.xml", wb.ToString().Replace("<definedNames></definedNames>", ""));

            var rels = new StringBuilder();
            rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < sheets.Count; i++)
                rels.Append("<Relationship Id=\"rId" + (i + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + (i + 1) + ".xml\"/>");
            rels.Append("<Relationship Id=\"rId" + (sheets.Count + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            rels.Append("</Relationships>");
            zip.Add("xl/_rels/workbook.xml.rels", rels.ToString());

            zip.Add("xl/styles.xml", Styles);
            int drawing = 0, media = 0;
            for (int i = 0; i < sheets.Count; i++)
            {
                var s = sheets[i];
                bool hasDr = s.Images.Count > 0;
                zip.Add("xl/worksheets/sheet" + (i + 1) + ".xml", SheetXml(s, i == 0, hasDr));
                if (!hasDr) continue;
                drawing++;
                zip.Add("xl/worksheets/_rels/sheet" + (i + 1) + ".xml.rels", RelsHead +
                    "<Relationship Id=\"rId1\" Type=\"" + NsRel + "/drawing\" Target=\"../drawings/drawing" + drawing + ".xml\"/></Relationships>");
                var dr = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                dr.Append("<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"" + NsRel + "\">");
                var drRels = new StringBuilder(RelsHead);
                for (int k = 0; k < s.Images.Count; k++)
                {
                    var im = s.Images[k];
                    media++;
                    zip.Add("xl/media/image" + media + ".png", im.Png);
                    drRels.Append("<Relationship Id=\"rId" + (k + 1) + "\" Type=\"" + NsRel + "/image\" Target=\"../media/image" + media + ".png\"/>");
                    long cx = im.W * 9525L, cy = im.H * 9525L; // EMU
                    dr.Append("<xdr:oneCellAnchor><xdr:from><xdr:col>" + im.Col + "</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>" + im.Row + "</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>");
                    dr.Append("<xdr:ext cx=\"" + cx + "\" cy=\"" + cy + "\"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"" + (k + 2) + "\" name=\"Карта " + (k + 1) + "\"/>");
                    dr.Append("<xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr><xdr:blipFill><a:blip r:embed=\"rId" + (k + 1) + "\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>");
                    dr.Append("<xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"" + cx + "\" cy=\"" + cy + "\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor>");
                }
                dr.Append("</xdr:wsDr>");
                drRels.Append("</Relationships>");
                zip.Add("xl/drawings/drawing" + drawing + ".xml", dr.ToString());
                zip.Add("xl/drawings/_rels/drawing" + drawing + ".xml.rels", drRels.ToString());
            }
            zip.Finish();
            return ms.ToArray();
        }

        const string RelsHead = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">";

        const string Border = "<left style=\"thin\"><color rgb=\"FFA6A6A6\"/></left><right style=\"thin\"><color rgb=\"FFA6A6A6\"/></right><top style=\"thin\"><color rgb=\"FFA6A6A6\"/></top><bottom style=\"thin\"><color rgb=\"FFA6A6A6\"/></bottom><diagonal/>";

        static readonly string Styles =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"" + NsMain + "\">" +
            "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"dd\\.mm\\.yyyy\"/><numFmt numFmtId=\"165\" formatCode=\"dd\\.mm\\.yyyy\\ hh:mm\"/></numFmts>" +
            "<fonts count=\"4\"><font><sz val=\"10\"/><name val=\"Calibri\"/><family val=\"2\"/><charset val=\"204\"/></font>" +
            "<font><b/><sz val=\"10\"/><name val=\"Calibri\"/><family val=\"2\"/><charset val=\"204\"/></font>" +
            "<font><sz val=\"10\"/><color rgb=\"FF9C0006\"/><name val=\"Calibri\"/><family val=\"2\"/><charset val=\"204\"/></font>" +
            "<font><sz val=\"10\"/><color rgb=\"FF006100\"/><name val=\"Calibri\"/><family val=\"2\"/><charset val=\"204\"/></font></fonts>" +
            "<fills count=\"6\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9E1F2\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFF2CC\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFC7CE\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFC6EFCE\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
            "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border>" + Border + "</border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"10\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"top\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"top\"/></xf>" +
            "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"top\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"5\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";

        static string SheetXml(XSheet s, bool selected, bool hasDrawing)
        {
            var sb = new StringBuilder(1 << 16);
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"" + (selected ? " tabSelected=\"1\"" : "") + (s.Freeze ? ">" : "/>"));
            if (s.Freeze)
                sb.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/><selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/></sheetView>");
            sb.Append("</sheetViews><sheetFormatPr defaultRowHeight=\"13.5\"/><cols>");
            for (int i = 0; i < s.Widths.Length; i++)
                sb.Append("<col min=\"" + (i + 1) + "\" max=\"" + (i + 1) + "\" width=\"" + s.Widths[i].ToString(CultureInfo.InvariantCulture) + "\" customWidth=\"1\"/>");
            sb.Append("</cols><sheetData>");
            for (int r = 0; r < s.Rows.Count; r++)
            {
                var row = s.Rows[r];
                sb.Append("<row r=\"" + (r + 1) + "\"" + (r == 0 ? " ht=\"30\" customHeight=\"1\"" : "") + ">");
                for (int c = 0; c < row.Length; c++)
                {
                    var cell = row[c];
                    if (cell == null) continue;
                    string refc = Col(c) + (r + 1);
                    string st = cell.S != 0 ? " s=\"" + cell.S + "\"" : "";
                    object v = cell.V;
                    if (v == null || (v is string vs0 && vs0.Length == 0))
                    {
                        if (cell.S != 0) sb.Append("<c r=\"" + refc + "\"" + st + "/>");
                    }
                    else if (v is DateTime dt)
                        sb.Append("<c r=\"" + refc + "\"" + st + "><v>" + dt.ToOADate().ToString(CultureInfo.InvariantCulture) + "</v></c>");
                    else if (v is int || v is long || v is double)
                        sb.Append("<c r=\"" + refc + "\"" + st + "><v>" + Convert.ToDouble(v).ToString(CultureInfo.InvariantCulture) + "</v></c>");
                    else
                    {
                        string t = v.ToString();
                        if (t.Length > 32000) t = t.Substring(0, 32000);
                        sb.Append("<c r=\"" + refc + "\"" + st + " t=\"inlineStr\"><is><t xml:space=\"preserve\">" + Esc(t) + "</t></is></c>");
                    }
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            if (s.Filter && s.Rows.Count > 0)
                sb.Append("<autoFilter ref=\"A1:" + Col(s.Widths.Length - 1) + s.Rows.Count + "\"/>");
            sb.Append("<pageMargins left=\"0.4\" right=\"0.4\" top=\"0.5\" bottom=\"0.5\" header=\"0.3\" footer=\"0.3\"/>");
            sb.Append("<pageSetup orientation=\"landscape\" paperSize=\"9\" fitToHeight=\"0\"/>");
            if (hasDrawing) sb.Append("<drawing r:id=\"rId1\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        public static string Col(int i)
        {
            string s = "";
            i++;
            while (i > 0) { int m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; }
            return s;
        }

        static string SheetName(string n)
        {
            var sb = new StringBuilder();
            foreach (char c in n) if ("[]:*?/\\".IndexOf(c) < 0) sb.Append(c);
            string r = sb.ToString();
            return r.Length > 31 ? r.Substring(0, 31) : r;
        }

        static string Esc(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default:
                        if (c < 0x20 && c != '\n' && c != '\t') sb.Append(' ');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Простейший ZIP-архиватор (Deflate) — в .NET 2.0 нет ZipArchive.</summary>
    class ZipWriter
    {
        readonly Stream outS;
        readonly MemoryStream central = new MemoryStream();
        int count;
        readonly ushort dosTime, dosDate;

        static readonly uint[] CrcTable = MakeCrc();

        public ZipWriter(Stream s)
        {
            outS = s;
            var n = DateTime.Now;
            dosTime = (ushort)((n.Hour << 11) | (n.Minute << 5) | (n.Second / 2));
            dosDate = (ushort)(((n.Year - 1980) << 9) | (n.Month << 5) | n.Day);
        }

        static uint[] MakeCrc()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        static uint Crc(byte[] d)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte b in d) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFF;
        }

        public void Add(string name, string text) => Add(name, new UTF8Encoding(false).GetBytes(text));

        public void Add(string name, byte[] data)
        {
            byte[] comp;
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionMode.Compress, true)) ds.Write(data, 0, data.Length);
                comp = ms.ToArray();
            }
            ushort method = 8;
            if (comp.Length >= data.Length) { comp = data; method = 0; }
            uint crc = Crc(data);
            byte[] nameB = Encoding.UTF8.GetBytes(name);
            uint offset = (uint)outS.Position;

            var w = new BinaryWriter(outS);
            w.Write(0x04034b50u); w.Write((ushort)20); w.Write((ushort)0x0800); w.Write(method);
            w.Write(dosTime); w.Write(dosDate); w.Write(crc); w.Write((uint)comp.Length); w.Write((uint)data.Length);
            w.Write((ushort)nameB.Length); w.Write((ushort)0); w.Write(nameB); w.Write(comp);
            w.Flush();

            var c = new BinaryWriter(central);
            c.Write(0x02014b50u); c.Write((ushort)20); c.Write((ushort)20); c.Write((ushort)0x0800); c.Write(method);
            c.Write(dosTime); c.Write(dosDate); c.Write(crc); c.Write((uint)comp.Length); c.Write((uint)data.Length);
            c.Write((ushort)nameB.Length); c.Write((ushort)0); c.Write((ushort)0); c.Write((ushort)0); c.Write((ushort)0);
            c.Write(0u); c.Write(offset); c.Write(nameB);
            c.Flush();
            count++;
        }

        public void Finish()
        {
            uint cdOffset = (uint)outS.Position;
            byte[] cd = central.ToArray();
            outS.Write(cd, 0, cd.Length);
            var w = new BinaryWriter(outS);
            w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)count); w.Write((ushort)count);
            w.Write((uint)cd.Length); w.Write(cdOffset); w.Write((ushort)0);
            w.Flush();
        }
    }
}
