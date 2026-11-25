using System;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace PcInventory
{
    /// <summary>
    /// Карта для draw.io (diagrams.net): несжатый mxfile, по диаграмме на этаж. Кабинеты — контейнеры,
    /// значки — их дочерние элементы; характеристики ПК — атрибуты object («Edit Data» в draw.io).
    /// </summary>
    static class DrawioWriter
    {
        static readonly MapPalette P = MapPalette.Light;

        public static string Build(ResolvedMap m, string mapDir)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<mxfile host=\"ven4PcConfig\" type=\"device\">\n");
            for (int fi = 0; fi < m.Doc.Floors.Count; fi++)
            {
                var f = m.Doc.Floors[fi];
                sb.Append("<diagram id=\"floor" + (fi + 1) + "\" name=\"" + Esc(f.Name != "" ? m.Doc.FloorTitle(f) : "Этаж " + (fi + 1)) + "\">");
                sb.Append("<mxGraphModel dx=\"" + f.W + "\" dy=\"" + f.H + "\" grid=\"0\" gridSize=\"10\" guides=\"1\" tooltips=\"1\" connect=\"0\" arrows=\"0\" fold=\"1\" page=\"1\" pageScale=\"1\" " +
                          "pageWidth=\"" + f.W + "\" pageHeight=\"" + f.H + "\" math=\"0\" shadow=\"0\"><root>");
                sb.Append("<mxCell id=\"0\"/><mxCell id=\"1\" parent=\"0\"/>");

                using (var bg = MapRenderer.LoadBg(mapDir, f))
                    if (bg != null)
                        using (var bmp = new Bitmap(bg))
                        {
                            string b64 = Convert.ToBase64String(MapRenderer.Png(bmp));
                            // draw.io хранит картинку в стиле как «data:image/png,<base64>» — «;base64» не пишется, «;» разделяет стиль
                            sb.Append("<mxCell id=\"bg" + (fi + 1) + "\" value=\"\" style=\"shape=image;image=data:image/png," + b64 +
                                      ";imageAspect=0;opacity=55;locked=1;editable=0;movable=0;resizable=0;deletable=0;\" vertex=\"1\" parent=\"1\">" +
                                      Geo(0, 0, f.W, f.H) + "</mxCell>");
                        }

                foreach (var r in m.RoomsOn(f))
                {
                    sb.Append("<object id=\"" + Esc(r.Id) + "\" label=\"" + Esc(r.Name) + "\"" + Attr("Кабинет", r.Name) + Attr("Отдел", r.Dept) + Attr("Этаж", f.Name) + ">");
                    sb.Append("<mxCell style=\"swimlane;startSize=34;rounded=1;arcSize=4;fontStyle=1;fontSize=13;align=left;spacingLeft=10;" +
                              "fillColor=" + Hex(P.RoomHead) + ";swimlaneFillColor=" + Hex(P.Room) + ";strokeColor=" + Hex(P.RoomBorder) + ";fontColor=" + Hex(P.RoomText) + ";collapsible=0;\" vertex=\"1\" parent=\"1\">");
                    sb.Append(Geo(r.X, r.Y, r.W, r.H) + "</mxCell></object>");
                }

                int n = 0;
                foreach (var i in m.ItemsOn(f))
                {
                    var rows = i.IsPc ? VsdxWriter.PcProps(m, i.Pc) : VsdxWriter.PrinterProps(i.Printer);
                    Color stripe = i.IsPc ? (i.Grade == Collector.Bad ? P.Bad : P.Ok) : P.Printer;
                    string label = Html(i.Line1) + (i.Line2 != "" ? "<br><span style=\"color:" + Hex(P.ItemSub) + "\">" + Html(i.Line2) + "</span>" : "");
                    sb.Append("<object id=\"item" + (fi + 1) + "_" + (++n) + "\" label=\"" + Esc(label) + "\" tooltip=\"" + Esc(Tooltip(rows)) + "\"");
                    foreach (var row in rows) sb.Append(Attr(row[1], row[2]));
                    // shape=label — фигура draw.io со значком слева (как в программе)
                    sb.Append("><mxCell style=\"shape=label;rounded=1;arcSize=8;whiteSpace=wrap;html=1;align=left;verticalAlign=middle;spacingLeft=34;fontSize=11;" +
                              "image=data:image/svg+xml," + Icon(i.IsPc, stripe) + ";imageWidth=24;imageHeight=22;imageAlign=left;imageVerticalAlign=middle;" +
                              "fillColor=" + Hex(P.Item) + ";strokeColor=" + Hex(stripe) + ";strokeWidth=2;fontColor=" + Hex(P.ItemText) + ";\" vertex=\"1\" parent=\"" + Esc(i.Room.Id) + "\">");
                    sb.Append(Geo(i.Rect.X - i.Room.X, i.Rect.Y - i.Room.Y, i.Rect.Width, i.Rect.Height) + "</mxCell></object>");
                }
                sb.Append("</root></mxGraphModel></diagram>\n");
            }
            sb.Append("</mxfile>\n");
            return sb.ToString();
        }

        /// <summary>Значок ПК или принтера — SVG в base64 (draw.io пишет «data:image/svg+xml,&lt;base64&gt;»).</summary>
        static string Icon(bool pc, Color c)
        {
            string st = "fill='none' stroke='" + Hex(c) + "' stroke-width='1.6'";
            string body = pc
                ? "<rect x='1' y='1' width='20' height='13' " + st + "/><path d='M11 14v4M5 19h12' " + st + "/>"
                : "<rect x='5' y='1' width='12' height='5' " + st + "/><rect x='1' y='6' width='20' height='9' " + st + "/><rect x='5' y='13' width='12' height='6' " + st + "/>";
            string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='22' height='20' viewBox='0 0 22 20'>" + body + "</svg>";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        }

        static string Tooltip(string[][] rows)
        {
            var sb = new StringBuilder();
            foreach (var r in rows) if (r[2] != "") sb.Append(r[1] + ": " + r[2].Replace("\n", "; ") + "\n");
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>Атрибут object: имя — подпись без пробелов и точек («Номер_АРМ»), значение — в одну строку.</summary>
        static string Attr(string name, string value)
        {
            var nb = new StringBuilder();
            foreach (char c in name) nb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return " " + nb.ToString().Trim('_') + "=\"" + Esc((value ?? "").Replace("\n", "; ")) + "\"";
        }

        static string Geo(int x, int y, int w, int h) =>
            "<mxGeometry x=\"" + x.ToString(CultureInfo.InvariantCulture) + "\" y=\"" + y.ToString(CultureInfo.InvariantCulture) +
            "\" width=\"" + w.ToString(CultureInfo.InvariantCulture) + "\" height=\"" + h.ToString(CultureInfo.InvariantCulture) + "\" as=\"geometry\"/>";

        static string Hex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        static string Html(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        static string Esc(string s)
        {
            var sb = new StringBuilder((s ?? "").Length + 8);
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\n': sb.Append("&#xa;"); break;
                    default: if (c < 0x20 && c != '\t') sb.Append(' '); else sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
