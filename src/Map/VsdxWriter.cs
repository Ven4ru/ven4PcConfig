using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace PcInventory
{
    /// <summary>
    /// Карта в формате Visio (.vsdx, Visio 2013+): по странице на этаж, кабинеты — прямоугольники,
    /// ПК и принтеры — фигуры с «Данными фигуры» (все характеристики). Пакет OPC через ZipWriter, без библиотек.
    /// Единицы Visio — дюймы: 100 условных пикселей карты = 1 дюйм; ось Y в Visio смотрит вверх.
    /// </summary>
    static class VsdxWriter
    {
        const string NsV = "http://schemas.microsoft.com/office/visio/2012/main";
        const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string Head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
        const double Px = 100.0;

        // светлая палитра, как у PNG — Visio-файл печатают
        static readonly MapPalette P = MapPalette.Light;

        public static byte[] Build(ResolvedMap m, string mapDir)
        {
            var ms = new MemoryStream();
            var zip = new ZipWriter(ms);
            var floors = m.Doc.Floors;
            var bgs = new Dictionary<int, byte[]>();
            for (int i = 0; i < floors.Count; i++)
            {
                byte[] png = BgPng(mapDir, floors[i]);
                if (png != null) bgs[i] = png;
            }

            var ct = new StringBuilder(Head);
            ct.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            ct.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            ct.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            if (bgs.Count > 0) ct.Append("<Default Extension=\"png\" ContentType=\"image/png\"/>");
            ct.Append("<Override PartName=\"/visio/document.xml\" ContentType=\"application/vnd.ms-visio.drawing.main+xml\"/>");
            ct.Append("<Override PartName=\"/visio/pages/pages.xml\" ContentType=\"application/vnd.ms-visio.pages+xml\"/>");
            for (int i = 0; i < floors.Count; i++)
                ct.Append("<Override PartName=\"/visio/pages/page" + (i + 1) + ".xml\" ContentType=\"application/vnd.ms-visio.page+xml\"/>");
            ct.Append("<Override PartName=\"/visio/windows.xml\" ContentType=\"application/vnd.ms-visio.windows+xml\"/>");
            ct.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            ct.Append("<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>");
            ct.Append("</Types>");
            zip.Add("[Content_Types].xml", ct.ToString());

            zip.Add("_rels/.rels", Head +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.microsoft.com/visio/2010/relationships/document\" Target=\"visio/document.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
                "</Relationships>");

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            zip.Add("docProps/core.xml", Head +
                "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                "<dc:title>Карта АРМ</dc:title><dc:creator>ven4PcConfig</dc:creator>" +
                "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + now + "</dcterms:created>" +
                "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + now + "</dcterms:modified></cp:coreProperties>");
            zip.Add("docProps/app.xml", Head +
                "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>ven4PcConfig</Application><Template/></Properties>");

            zip.Add("visio/document.xml", Document());
            zip.Add("visio/_rels/document.xml.rels", Head +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.microsoft.com/visio/2010/relationships/pages\" Target=\"pages/pages.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.microsoft.com/visio/2010/relationships/windows\" Target=\"windows.xml\"/>" +
                "</Relationships>");
            zip.Add("visio/windows.xml", Windows(floors.Count > 0 ? floors[0] : new Floor()));

            var pages = new StringBuilder(Head);
            pages.Append("<Pages xmlns=\"" + NsV + "\" xmlns:r=\"" + NsR + "\" xml:space=\"preserve\">");
            var prel = new StringBuilder(Head);
            prel.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < floors.Count; i++)
            {
                var f = floors[i];
                pages.Append("<Page ID=\"" + i + "\" NameU=\"" + Esc(PageName(f, i)) + "\" Name=\"" + Esc(PageName(f, i)) + "\" ViewScale=\"-1\" ViewCenterX=\"" + D(f.W / Px / 2) + "\" ViewCenterY=\"" + D(f.H / Px / 2) + "\">");
                pages.Append("<PageSheet LineStyle=\"0\" FillStyle=\"0\" TextStyle=\"0\">");
                pages.Append(C("PageWidth", f.W / Px, "IN") + C("PageHeight", f.H / Px, "IN") + C("ShdwOffsetX", 0.125) + C("ShdwOffsetY", -0.125) +
                             C("PageScale", 1, "IN") + C("DrawingScale", 1, "IN") + C("DrawingSizeType", 0) + C("DrawingScaleType", 0) +
                             C("InhibitSnap", 0) + C("PageLockReplace", 0) + C("PageLockDuplicate", 0) + C("UIVisibility", 0) + C("ShdwType", 0) +
                             C("ShdwObliqueAngle", 0) + C("ShdwScaleFactor", 1) + C("DrawingResizeType", 1) + C("PageShapeSplit", 1) +
                             C("PrintPageOrientation", f.W >= f.H ? 2 : 1));
                pages.Append("<Section N=\"Layer\">");
                string[][] layers = { new[] { "Подложка", "Background" }, new[] { "Кабинеты", "Rooms" }, new[] { "Оборудование", "Equipment" } };
                for (int l = 0; l < layers.Length; l++)
                    pages.Append("<Row IX=\"" + l + "\">" + CS("Name", layers[l][0]) + C("Color", 255) + C("Status", 0) + C("Visible", 1) + C("Print", 1) +
                                 C("Active", 0) + C("Lock", l == 0 ? 1 : 0) + C("Snap", 1) + C("Glue", 1) + CS("NameUniv", layers[l][1]) + C("ColorTrans", 0) + "</Row>");
                pages.Append("</Section></PageSheet>");
                pages.Append("<Rel r:id=\"rId" + (i + 1) + "\"/></Page>");
                prel.Append("<Relationship Id=\"rId" + (i + 1) + "\" Type=\"http://schemas.microsoft.com/visio/2010/relationships/page\" Target=\"page" + (i + 1) + ".xml\"/>");

                byte[] bg;
                bgs.TryGetValue(i, out bg);
                zip.Add("visio/pages/page" + (i + 1) + ".xml", Page(m, f, bg != null));
                if (bg != null)
                {
                    zip.Add("visio/media/image" + (i + 1) + ".png", bg);
                    zip.Add("visio/pages/_rels/page" + (i + 1) + ".xml.rels", Head +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image" + (i + 1) + ".png\"/>" +
                        "</Relationships>");
                }
            }
            pages.Append("</Pages>");
            prel.Append("</Relationships>");
            zip.Add("visio/pages/pages.xml", pages.ToString());
            zip.Add("visio/pages/_rels/pages.xml.rels", prel.ToString());
            zip.Finish();
            return ms.ToArray();
        }

        static string PageName(Floor f, int i) => f.Name != "" ? f.Name : "Этаж " + (i + 1);

        static byte[] BgPng(string mapDir, Floor f)
        {
            using (var img = MapRenderer.LoadBg(mapDir, f))
            {
                if (img == null) return null;
                using (var bmp = new Bitmap(img)) return MapRenderer.Png(bmp);
            }
        }

        // ------------------------------------------------------------ документ: стиль «без стиля» со всеми основными ячейками
        static string Document()
        {
            var sb = new StringBuilder(Head);
            sb.Append("<VisioDocument xmlns=\"" + NsV + "\" xmlns:r=\"" + NsR + "\" xml:space=\"preserve\">");
            sb.Append("<DocumentSettings TopPage=\"0\" DefaultTextStyle=\"0\" DefaultLineStyle=\"0\" DefaultFillStyle=\"0\" DefaultGuideStyle=\"0\">");
            sb.Append("<GlueSettings>9</GlueSettings><SnapSettings>65847</SnapSettings><SnapExtensions>34</SnapExtensions><SnapAngles/>");
            sb.Append("<DynamicGridEnabled>1</DynamicGridEnabled><ProtectStyles>0</ProtectStyles><ProtectShapes>0</ProtectShapes><ProtectMasters>0</ProtectMasters><ProtectBkgnds>0</ProtectBkgnds>");
            sb.Append("</DocumentSettings>");
            sb.Append("<Colors>");
            string[] colors = { "#000000", "#FFFFFF", "#FF0000", "#00FF00", "#0000FF", "#FFFF00", "#FF00FF", "#00FFFF", "#800000", "#008000", "#000080", "#808000", "#800080", "#008080", "#C0C0C0", "#E6E6E6", "#CDCDCD", "#B3B3B3", "#9A9A9A", "#808080", "#666666", "#4D4D4D", "#333333", "#1A1A1A" };
            for (int i = 0; i < colors.Length; i++) sb.Append("<ColorEntry IX=\"" + i + "\" RGB=\"" + colors[i] + "\"/>");
            sb.Append("</Colors>");
            sb.Append("<FaceNames><FaceName NameU=\"Segoe UI\" UnicodeRanges=\"-536861953 -1073711013 9 0\" CharSets=\"536871359 0\" Panose=\"2 11 5 2 4 2 4 2 2 3\" Flags=\"325\"/>" +
                      "<FaceName NameU=\"Calibri\" UnicodeRanges=\"-469750017 -1073732485 9 0\" CharSets=\"536871327 0\" Panose=\"2 15 5 2 2 2 4 3 2 4\" Flags=\"325\"/></FaceNames>");
            sb.Append("<StyleSheets><StyleSheet ID=\"0\" NameU=\"No Style\" IsCustomNameU=\"1\" Name=\"No Style\" IsCustomName=\"1\">");
            sb.Append(C("EnableLineProps", 1) + C("EnableFillProps", 1) + C("EnableTextProps", 1) + C("HideForApply", 0));
            sb.Append(C("LineWeight", 0.01041666666666667) + C("LineColor", 0) + C("LinePattern", 1) + C("Rounding", 0) + C("EndArrowSize", 2) +
                      C("BeginArrow", 0) + C("EndArrow", 0) + C("LineCap", 0) + C("BeginArrowSize", 2) + C("LineColorTrans", 0) + C("CompoundType", 0));
            sb.Append(C("FillForegnd", 1) + C("FillBkgnd", 0) + C("FillPattern", 1) + C("ShdwForegnd", 0) + C("ShdwPattern", 0) + C("FillForegndTrans", 0) +
                      C("FillBkgndTrans", 0) + C("ShdwForegndTrans", 0) + C("ShapeShdwType", 0) + C("ShapeShdwOffsetX", 0) + C("ShapeShdwOffsetY", 0) +
                      C("ShapeShdwObliqueAngle", 0) + C("ShapeShdwScaleFactor", 1) + C("ShapeShdwBlur", 0) + C("ShapeShdwShow", 0));
            sb.Append(C("LeftMargin", 0) + C("RightMargin", 0) + C("TopMargin", 0) + C("BottomMargin", 0) + C("VerticalAlign", 1) + C("TextBkgnd", 0) +
                      C("DefaultTabStop", 0.5) + C("TextDirection", 0) + C("TextBkgndTrans", 0));
            sb.Append(C("NoObjHandles", 0) + C("NonPrinting", 0) + C("NoCtlHandles", 0) + C("NoAlignBox", 0) + C("UpdateAlignBox", 0) + C("HideText", 0) +
                      C("DynFeedback", 0) + C("GlueType", 0) + C("WalkPreference", 0) + C("BegTrigger", 0) + C("EndTrigger", 0) + C("ObjType", 0) +
                      C("Comment", 0) + C("IsDropSource", 0) + C("NoLiveDynamics", 0) + C("LocalizeMerge", 0) + C("NoProofing", 0) + C("Calendar", 0) +
                      C("LangID", 1049) + C("ShapeKeywords", 0) + C("DropOnPageScale", 1));
            sb.Append("<Section N=\"Character\"><Row IX=\"0\">" + CS("Font", "Calibri") + C("Color", 0) + C("Style", 0) + C("Case", 0) + C("Pos", 0) +
                      C("FontScale", 1) + C("Size", 0.1666666666666667) + C("DblUnderline", 0) + C("Overline", 0) + C("Strikethru", 0) +
                      C("DoubleStrikethrough", 0) + C("Letterspace", 0) + C("ColorTrans", 0) + C("AsianFont", 0) + C("ComplexScriptFont", 0) +
                      C("ComplexScriptSize", -1) + C("LangID", 1049) + "</Row></Section>");
            sb.Append("<Section N=\"Paragraph\"><Row IX=\"0\">" + C("IndFirst", 0) + C("IndLeft", 0) + C("IndRight", 0) + C("SpLine", -1.2) + C("SpBefore", 0) +
                      C("SpAfter", 0) + C("HorzAlign", 1) + C("Bullet", 0) + CS("BulletStr", "") + C("BulletFont", 0) + C("BulletFontSize", -1) +
                      C("TextPosAfterBullet", 0) + C("Flags", 0) + "</Row></Section>");
            sb.Append("</StyleSheet></StyleSheets>");
            sb.Append("</VisioDocument>");
            return sb.ToString();
        }

        static string Windows(Floor f)
        {
            return Head + "<Windows ClientWidth=\"1280\" ClientHeight=\"800\" xmlns=\"" + NsV + "\" xmlns:r=\"" + NsR + "\" xml:space=\"preserve\">" +
                   "<Window ID=\"0\" WindowType=\"Drawing\" WindowState=\"1073741824\" WindowLeft=\"-8\" WindowTop=\"-31\" WindowWidth=\"1296\" WindowHeight=\"839\" " +
                   "ContainerType=\"Page\" Page=\"0\" ViewScale=\"-1\" ViewCenterX=\"" + D(f.W / Px / 2) + "\" ViewCenterY=\"" + D(f.H / Px / 2) + "\">" +
                   "<ShowRulers>1</ShowRulers><ShowGrid>0</ShowGrid><ShowPageBreaks>0</ShowPageBreaks><ShowGuides>1</ShowGuides><ShowConnectionPoints>1</ShowConnectionPoints>" +
                   "<GlueSettings>9</GlueSettings><SnapSettings>65847</SnapSettings><SnapExtensions>34</SnapExtensions><SnapAngles/><DynamicGridEnabled>1</DynamicGridEnabled>" +
                   "<TabSplitterPos>0.5</TabSplitterPos></Window></Windows>";
        }

        // ------------------------------------------------------------ страница
        static string Page(ResolvedMap m, Floor f, bool hasBg)
        {
            var sb = new StringBuilder(Head);
            sb.Append("<PageContents xmlns=\"" + NsV + "\" xmlns:r=\"" + NsR + "\" xml:space=\"preserve\"><Shapes>");
            int id = 1;
            if (hasBg)
            {
                sb.Append("<Shape ID=\"" + id++ + "\" NameU=\"Background\" Name=\"Подложка\" Type=\"Foreign\" LineStyle=\"0\" FillStyle=\"0\" TextStyle=\"0\">");
                sb.Append(Xform(f, new Rectangle(0, 0, f.W, f.H)));
                sb.Append(C("ImgOffsetX", 0) + C("ImgOffsetY", 0) + C("ImgWidth", f.W / Px) + C("ImgHeight", f.H / Px) + C("LinePattern", 0) + C("FillPattern", 0) +
                          CS("LayerMember", "0") + C("LockSelect", 1));
                sb.Append("<ForeignData ForeignType=\"Bitmap\" CompressionType=\"PNG\"><Rel r:id=\"rId1\"/></ForeignData></Shape>");
            }
            foreach (var r in m.RoomsOn(f))
            {
                sb.Append("<Shape ID=\"" + id + "\" NameU=\"Room." + id++ + "\" Name=\"" + Esc(r.Name) + "\" Type=\"Shape\" LineStyle=\"0\" FillStyle=\"0\" TextStyle=\"0\">");
                sb.Append(Xform(f, r.Rect));
                sb.Append(CS("FillForegnd", Hex(P.Room)) + CS("LineColor", Hex(P.RoomBorder)) + C("LineWeight", 0.0138888) + C("Rounding", 0.06) +
                          C("VerticalAlign", 0) + C("TopMargin", 0.08) + C("LeftMargin", 0.1) + C("RightMargin", 0.1) + CS("LayerMember", "1"));
                sb.Append("<Section N=\"Character\"><Row IX=\"0\">" + CS("Font", "Segoe UI") + CS("Color", Hex(P.RoomText)) + C("Style", 1) + C("Size", 0.13) + "</Row></Section>");
                sb.Append("<Section N=\"Paragraph\"><Row IX=\"0\">" + C("HorzAlign", 0) + "</Row></Section>");
                sb.Append(Props(new[] { new[] { "Room", "Кабинет", r.Name }, new[] { "Dept", "Отдел", r.Dept }, new[] { "Floor", "Этаж", f.Name } }));
                sb.Append(Rect());
                sb.Append("<Text>" + Esc(r.Name + (r.Dept != "" && !string.Equals(r.Dept, r.Name, StringComparison.OrdinalIgnoreCase) ? " · " + r.Dept : "")) + "</Text>");
                sb.Append("</Shape>");
            }
            foreach (var i in m.ItemsOn(f))
            {
                Color stripe = i.IsPc ? (i.Grade == Collector.Bad ? P.Bad : P.Ok) : P.Printer;
                sb.Append("<Shape ID=\"" + id + "\" NameU=\"" + (i.IsPc ? "PC" : "Printer") + "." + id++ + "\" Name=\"" + Esc(i.Line1) + "\" Type=\"Shape\" LineStyle=\"0\" FillStyle=\"0\" TextStyle=\"0\">");
                sb.Append(Xform(f, i.Rect));
                sb.Append(CS("FillForegnd", Hex(P.Item)) + CS("LineColor", Hex(stripe)) + C("LineWeight", 0.0208333) + C("Rounding", 0.05) +
                          C("VerticalAlign", 1) + C("LeftMargin", 0.08) + C("RightMargin", 0.04) + CS("LayerMember", "2"));
                sb.Append("<Section N=\"Character\"><Row IX=\"0\">" + CS("Font", "Segoe UI") + CS("Color", Hex(P.ItemText)) + C("Size", 0.105) + "</Row></Section>");
                sb.Append("<Section N=\"Paragraph\"><Row IX=\"0\">" + C("HorzAlign", 0) + "</Row></Section>");
                sb.Append(Props(i.IsPc ? PcProps(m, i.Pc) : PrinterProps(i.Printer)));
                sb.Append(Rect());
                sb.Append("<Text>" + Esc(Util.Join("\n", new[] { i.Line1, i.Line2 })) + "</Text>");
                sb.Append("</Shape>");
            }
            sb.Append("</Shapes></PageContents>");
            return sb.ToString();
        }

        /// <summary>«Данные фигуры» ПК: все характеристики, как в реестре.</summary>
        public static string[][] PcProps(ResolvedMap m, PcRecord r)
        {
            return new[]
            {
                new[] { "ArmId", "Номер АРМ", r["ArmId"] }, new[] { "PcName", "Компьютер", r["PcName"] }, new[] { "Dept", "Отдел", r["Dept"] },
                new[] { "Room", "Кабинет", m.RoomTitle(m.RoomOfPc(r)) }, new[] { "Fio", "ФИО", r["Fio"] }, new[] { "Model", "Модель ПК", r["PcModel"] },
                new[] { "Cpu", "Процессор", r["Cpu"] }, new[] { "Ram", "ОЗУ", r["Ram"] }, new[] { "Board", "Мат. плата", r["Board"] },
                new[] { "Gpu", "Видео", r["Gpu"] }, new[] { "Disk", "Накопители", r["Disk"] }, new[] { "Monitor", "Мониторы", r["Monitor"] },
                new[] { "Os", "ОС", Util.Join(" ", new[] { r["Os"], r["OsVer"] }) }, new[] { "Ip", "IP-адрес", r["Ip"] }, new[] { "Mac", "MAC-адрес", r["Mac"] },
                new[] { "User", "Пользователь", r["User"] }, new[] { "Av", "Антивирус", r["AvAct"] != "" ? r["AvAct"] : r["Av"] },
                new[] { "WinAct", "Активация Windows", r["WinAct"] }, new[] { "OfficeAct", "Активация Office", r["OfficeAct"] },
                new[] { "DiskHealth", "Состояние дисков", r["DiskHealth"] }, new[] { "Problems", "Замечания", string.Join("; ", Report.ProblemsOf(r).ToArray()) },
                new[] { "Collected", "Дата сбора", r["Collected"] },
            };
        }

        public static string[][] PrinterProps(MapPrinter p)
        {
            var pcs = new List<string>();
            foreach (var pc in p.Pcs) pcs.Add(Report.ShortTitle(pc));
            return new[]
            {
                new[] { "Name", "Принтер", p.Name }, new[] { "Driver", "Драйвер", p.Driver }, new[] { "Port", "Порт", p.Port },
                new[] { "Network", "Сетевой", p.Network ? "да" : "нет" }, new[] { "Pcs", "Компьютеры", string.Join(", ", pcs.ToArray()) },
            };
        }

        static string Props(string[][] rows)
        {
            var sb = new StringBuilder("<Section N=\"Property\">");
            foreach (var r in rows)
                sb.Append("<Row N=\"" + r[0] + "\">" + CS("Value", (r[2] ?? "").Replace("\n", "; "), "STR") + CS("Label", r[1]) + C("Type", 0) + C("Invisible", 0) + "</Row>");
            sb.Append("</Section>");
            return sb.ToString();
        }

        static string Xform(Floor f, Rectangle r)
        {
            double w = r.Width / Px, h = r.Height / Px;
            double px = (r.X + r.Width / 2.0) / Px, py = (f.H - (r.Y + r.Height / 2.0)) / Px;
            return C("PinX", px) + C("PinY", py) + C("Width", w) + C("Height", h) +
                   "<Cell N=\"LocPinX\" V=\"" + D(w / 2) + "\" F=\"Width*0.5\"/><Cell N=\"LocPinY\" V=\"" + D(h / 2) + "\" F=\"Height*0.5\"/>" +
                   C("Angle", 0) + C("FlipX", 0) + C("FlipY", 0) + C("ResizeMode", 0);
        }

        /// <summary>Прямоугольник в относительных координатах — растягивается вместе с фигурой.</summary>
        static string Rect() =>
            "<Section N=\"Geometry\" IX=\"0\">" + C("NoFill", 0) + C("NoLine", 0) + C("NoShow", 0) + C("NoSnap", 0) + C("NoQuickDrag", 0) +
            "<Row T=\"RelMoveTo\" IX=\"1\">" + C("X", 0) + C("Y", 0) + "</Row>" +
            "<Row T=\"RelLineTo\" IX=\"2\">" + C("X", 1) + C("Y", 0) + "</Row>" +
            "<Row T=\"RelLineTo\" IX=\"3\">" + C("X", 1) + C("Y", 1) + "</Row>" +
            "<Row T=\"RelLineTo\" IX=\"4\">" + C("X", 0) + C("Y", 1) + "</Row>" +
            "<Row T=\"RelLineTo\" IX=\"5\">" + C("X", 0) + C("Y", 0) + "</Row></Section>";

        static string C(string n, double v, string unit = null) =>
            "<Cell N=\"" + n + "\" V=\"" + D(v) + "\"" + (unit != null ? " U=\"" + unit + "\"" : "") + "/>";

        static string CS(string n, string v, string unit = null) =>
            "<Cell N=\"" + n + "\" V=\"" + Esc(v ?? "") + "\"" + (unit != null ? " U=\"" + unit + "\"" : "") + "/>";

        static string D(double v) => v.ToString("0.################", CultureInfo.InvariantCulture);
        static string Hex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        static string Esc(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\n': sb.Append("&#xA;"); break;
                    default: if (c < 0x20 && c != '\t') sb.Append(' '); else sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
