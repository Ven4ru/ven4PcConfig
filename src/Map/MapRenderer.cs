using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace PcInventory
{
    /// <summary>Цвета карты: тёмные — для окна (тема «Бирюзовая»), светлые — для PNG и Excel (печать).</summary>
    class MapPalette
    {
        public Color Floor, FloorBorder, Room, RoomHead, RoomBorder, RoomText, RoomSub, Item, ItemBorder, ItemText, ItemSub, Icon,
            Ok, Bad, Warn, Accent, Printer;
        public float BgAlpha;

        static Color C(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        public static readonly MapPalette Dark = new MapPalette
        {
            Floor = C(0x0D1018), FloorBorder = C(0x1E2A38), Room = C(0x11161F), RoomHead = C(0x151C27), RoomBorder = C(0x2A3A4E),
            RoomText = C(0xE2E8F0), RoomSub = C(0x7A8FA8), Item = C(0x151C27), ItemBorder = C(0x2A3A4E), ItemText = C(0xE2E8F0),
            ItemSub = C(0xAAB8CE), Icon = C(0xAAB8CE), Ok = C(0x4ADE80), Bad = C(0xF87171), Warn = C(0xFBBF24), Accent = C(0x00BCD4),
            Printer = C(0x38BDF8), BgAlpha = 0.28f,
        };

        public static readonly MapPalette Light = new MapPalette
        {
            Floor = C(0xFFFFFF), FloorBorder = C(0xC8D0DA), Room = C(0xF7F9FB), RoomHead = C(0xE6F7FA), RoomBorder = C(0x7A8FA8),
            RoomText = C(0x0F172A), RoomSub = C(0x52606D), Item = C(0xFFFFFF), ItemBorder = C(0xB4C0CC), ItemText = C(0x0F172A),
            ItemSub = C(0x52606D), Icon = C(0x52606D), Ok = C(0x16A34A), Bad = C(0xDC2626), Warn = C(0xD97706), Accent = C(0x0097A7),
            Printer = C(0x0284C7), BgAlpha = 0.55f,
        };
    }

    /// <summary>Рисует этаж: подложку, кабинеты и значки. Координаты — условные пиксели карты;
    /// масштаб и сдвиг задаёт вызывающий через g.Transform (поэтому текст — DrawString, он учитывает масштаб).</summary>
    static class MapRenderer
    {
        static readonly string Family = HasFont("Segoe UI") ? "Segoe UI" : "Tahoma";
        static readonly Font FRoom = new Font(Family, 13f, FontStyle.Bold, GraphicsUnit.Pixel);
        static readonly Font FRoomSub = new Font(Family, 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        static readonly Font FItem = new Font(Family, 11f, FontStyle.Bold, GraphicsUnit.Pixel);
        static readonly Font FItemSub = new Font(Family, 10.5f, FontStyle.Regular, GraphicsUnit.Pixel);

        static bool HasFont(string name)
        {
            foreach (var f in FontFamily.Families) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static Image LoadBg(string mapDir, Floor f)
        {
            if (f == null || string.IsNullOrEmpty(f.Bg) || mapDir == null) return null;
            string path = Path.Combine(mapDir, f.Bg);
            if (!File.Exists(path)) return null;
            try { using (var img = Image.FromFile(path)) return new Bitmap(img); } catch { return null; }
        }

        /// <summary>Картинка этажа (PNG, лист Excel). Без подложки обрезается по кабинетам — иначе на листе
        /// остаётся большое пустое поле; с подложкой — весь этаж, чтобы план совпадал с картой.</summary>
        public static Bitmap Render(ResolvedMap m, Floor f, string mapDir, MapPalette p, bool crop = false)
        {
            using (var bg = LoadBg(mapDir, f))
            {
                var area = new Rectangle(0, 0, f.W, f.H);
                bool cut = crop && bg == null && !Content(m, f).IsEmpty;
                var frame = Frame(m, f, cut);
                if (cut) area = Rectangle.Intersect(Rectangle.Inflate(frame, 12, 12), area);
                var bmp = new Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.TranslateTransform(-area.X, -area.Y);
                    DrawFloor(g, m, f, p, bg, null, null, null, frame);
                }
                return bmp;
            }
        }

        /// <summary>Прямоугольник, занятый кабинетами и значками этажа.</summary>
        public static Rectangle Content(ResolvedMap m, Floor f)
        {
            var c = Rectangle.Empty;
            foreach (var r in m.RoomsOn(f)) c = c.IsEmpty ? r.Rect : Rectangle.Union(c, r.Rect);
            foreach (var i in m.ItemsOn(f)) c = c.IsEmpty ? i.Rect : Rectangle.Union(c, i.Rect);
            return c;
        }

        /// <summary>Рамка здания: на всё поле этажа; для обрезанной картинки (PNG, Excel) — вокруг кабинетов.</summary>
        public static Rectangle Frame(ResolvedMap m, Floor f, bool aroundContent)
        {
            if (aroundContent)
            {
                var c = Content(m, f);
                if (!c.IsEmpty) return new Rectangle(c.X - 20, c.Y - FrameHead - 12, c.Width + 40, c.Height + FrameHead + 32);
            }
            return new Rectangle(FrameInset, FrameInset, f.W - FrameInset * 2, f.H - FrameInset * 2);
        }

        public const int FrameInset = 10, FrameHead = 24;

        public static void DrawFloor(Graphics g, ResolvedMap m, Floor f, MapPalette p, Image bg, string currentKey, string selectedKey, string hoverKey)
            => DrawFloor(g, m, f, p, bg, currentKey, selectedKey, hoverKey, Frame(m, f, false));

        public static void DrawFloor(Graphics g, ResolvedMap m, Floor f, MapPalette p, Image bg, string currentKey, string selectedKey, string hoverKey, Rectangle frame)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var area = new Rectangle(0, 0, f.W, f.H);
            using (var b = new SolidBrush(p.Floor)) g.FillRectangle(b, area);
            if (bg != null) DrawFaded(g, bg, area, p.BgAlpha);
            using (var pen = new Pen(p.FloorBorder)) g.DrawRectangle(pen, 0, 0, f.W - 1, f.H - 1);
            DrawBuilding(g, m, f, p, frame);

            foreach (var r in m.RoomsOn(f)) DrawRoom(g, m, r, p, selectedKey == "room:" + r.Id);
            foreach (var i in m.ItemsOn(f)) DrawItem(g, i, p, i.Key == currentKey, i.Key == selectedKey, i.Key == hoverKey);
        }

        /// <summary>Здание — рамка на всё поле с названием и этажом сверху; кабинеты стоят внутри.</summary>
        static void DrawBuilding(Graphics g, ResolvedMap m, Floor f, MapPalette p, Rectangle frame)
        {
            var b = m.Doc.BuildingById(f.BuildingId);
            string title = (b != null ? b.Name : "") + (f.Name != "" ? " · " + f.Name : "");
            using (var path = Round(frame, 8))
            using (var pen = new Pen(Color.FromArgb(200, p.Accent), 1.5f) { DashStyle = DashStyle.Dash })
                g.DrawPath(pen, path);
            var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
            using (var br = new SolidBrush(p.Accent))
                g.DrawString(title, FRoom, br, new RectangleF(frame.X + 12, frame.Y + 2, frame.Width - 24, FrameHead), sf);
        }

        static void DrawFaded(Graphics g, Image img, Rectangle dest, float alpha)
        {
            var cm = new ColorMatrix { Matrix33 = alpha };
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(img, dest, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
            }
        }

        static void DrawRoom(Graphics g, ResolvedMap m, Room r, MapPalette p, bool selected)
        {
            var rect = r.Rect;
            bool over = m.Overflow.ContainsKey(r.Id);
            Color border = selected ? p.Accent : over ? p.Warn : p.RoomBorder;
            using (var path = Round(rect, 6))
            {
                using (var b = new SolidBrush(Color.FromArgb(235, p.Room))) g.FillPath(b, path);
                var head = new Rectangle(rect.X, rect.Y, rect.Width, MapLayout.Head);
                var old = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var b = new SolidBrush(p.RoomHead)) g.FillRectangle(b, head);
                g.Clip = old;
                using (var pen = new Pen(border, selected ? 2f : 1f)) g.DrawPath(pen, path);
            }
            using (var pen = new Pen(p.RoomBorder)) g.DrawLine(pen, rect.X, rect.Y + MapLayout.Head, rect.Right, rect.Y + MapLayout.Head);
            string sub = r.Dept != "" && !string.Equals(r.Dept, r.Name, StringComparison.OrdinalIgnoreCase) ? r.Dept : "";
            var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
            float subW = sub != "" ? Math.Min(g.MeasureString(sub, FRoomSub).Width + 4, rect.Width * 0.45f) : 0;
            using (var b = new SolidBrush(p.RoomText))
                g.DrawString(r.Name, FRoom, b, new RectangleF(rect.X + 10, rect.Y, rect.Width - 20 - subW, MapLayout.Head), sf);
            if (sub != "")
                using (var b = new SolidBrush(p.RoomSub))
                    g.DrawString(sub, FRoomSub, b, new RectangleF(rect.Right - 10 - subW, rect.Y, subW, MapLayout.Head), new StringFormat(sf) { Alignment = StringAlignment.Far });
            if (selected)
            {
                // ручка изменения размера
                var h = Handle(r);
                using (var b = new SolidBrush(p.Accent)) g.FillRectangle(b, h);
            }
        }

        /// <summary>Ручка изменения размера кабинета (правый нижний угол).</summary>
        public static Rectangle Handle(Room r) => new Rectangle(r.X + r.W - 12, r.Y + r.H - 12, 10, 10);

        static void DrawItem(Graphics g, MapItem i, MapPalette p, bool current, bool selected, bool hover)
        {
            var rect = i.Rect;
            Color stripe = i.IsPc ? (i.Grade == Collector.Bad ? p.Bad : p.Ok) : p.Printer;
            Color border = selected || current ? p.Accent : hover ? p.RoomSub : p.ItemBorder;
            using (var path = Round(rect, 5))
            {
                using (var b = new SolidBrush(p.Item)) g.FillPath(b, path);
                var old = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var b = new SolidBrush(stripe)) g.FillRectangle(b, rect.X, rect.Y, 4, rect.Height);
                g.Clip = old;
                using (var pen = new Pen(border, selected || current ? 2f : 1f)) g.DrawPath(pen, path);
            }
            g.DrawImage(Icons.Get(Icons.KindOf(i)), new Rectangle(rect.X + 9, rect.Y + 8, 30, 30));
            var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
            using (var b = new SolidBrush(p.ItemText))
                g.DrawString(i.Line1, FItem, b, new RectangleF(rect.X + 44, rect.Y + 9, rect.Width - 50, 16), sf);
            using (var b = new SolidBrush(p.ItemSub))
                g.DrawString(i.Line2, FItemSub, b, new RectangleF(rect.X + 44, rect.Y + 27, rect.Width - 50, 16), sf);
            if (i.IsPc && i.Pc["Dept"] != "" && i.Room != null && !string.Equals(i.Room.Dept, i.Pc["Dept"], StringComparison.OrdinalIgnoreCase))
                using (var b = new SolidBrush(p.ItemSub))
                    g.DrawString(i.Pc["Dept"], FItemSub, b, new RectangleF(rect.X + 10, rect.Y + 44, rect.Width - 16, 16), sf);
        }

        static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static byte[] Png(Bitmap bmp)
        {
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }
    }
}
