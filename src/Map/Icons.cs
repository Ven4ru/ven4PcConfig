using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PcInventory
{
    /// <summary>
    /// Значки карты — картинки с прозрачным фоном, нарисованные кодом: монитор (с кружком состояния:
    /// зелёный — замечаний нет, красный — есть) и принтер. Одни и те же картинки идут в окно, Visio и draw.io.
    /// </summary>
    static class Icons
    {
        public const int Size = 256;
        public const string PcOk = "pc-ok", PcBad = "pc-bad", Printer = "printer";
        // Bitmap нельзя рисовать из двух потоков сразу (GDI+ «объект используется в другом месте») — кэш свой у каждого потока
        [ThreadStatic] static Dictionary<string, Bitmap> cache;
        static readonly Dictionary<string, byte[]> png = new Dictionary<string, byte[]>();

        public static string KindOf(MapItem i) => !i.IsPc ? Printer : i.Grade == Collector.Bad ? PcBad : PcOk;

        public static Bitmap Get(string kind)
        {
            if (cache == null) cache = new Dictionary<string, Bitmap>();
            Bitmap b;
            if (!cache.TryGetValue(kind, out b)) cache[kind] = b = Draw(kind);
            return b;
        }

        public static byte[] Png(string kind) => Png(kind, Color.Empty);

        /// <summary>PNG значка; bg — непрозрачный фон (для Visio: LibreOffice заливает прозрачные места фигуры-картинки своим синим).</summary>
        public static byte[] Png(string kind, Color bg)
        {
            string key = kind + "|" + bg.ToArgb();
            lock (png)
            {
                byte[] p;
                if (png.TryGetValue(key, out p)) return p;
                if (bg.IsEmpty) p = MapRenderer.Png(Get(kind));
                else
                    using (var b = new Bitmap(Size, Size))
                    {
                        using (var g = Graphics.FromImage(b)) { g.Clear(bg); g.DrawImage(Get(kind), 0, 0, Size, Size); }
                        p = MapRenderer.Png(b);
                    }
                png[key] = p;
                return p;
            }
        }

        static Color C(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        static Bitmap Draw(string kind)
        {
            var bmp = new Bitmap(Size, Size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                if (kind == Printer) DrawPrinter(g); else DrawPc(g, kind == PcBad);
            }
            return bmp;
        }

        static void DrawPc(Graphics g, bool bad)
        {
            // корпус монитора
            Fill(g, new Rectangle(14, 26, 228, 156), 16, C(0x334155), C(0x1E293B), 3);
            // экран с бликом
            var screen = new Rectangle(30, 42, 196, 124);
            using (var br = new LinearGradientBrush(screen, C(0x38BDF8), C(0x075985), 55f)) g.FillRectangle(br, screen);
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[] { new Point(30, 42), new Point(150, 42), new Point(70, 166), new Point(30, 166) });
                using (var br = new SolidBrush(Color.FromArgb(46, Color.White))) g.FillPath(br, path);
            }
            // ножка и подставка
            using (var br = new SolidBrush(C(0x475569))) g.FillRectangle(br, 112, 182, 32, 26);
            Fill(g, new Rectangle(66, 204, 124, 22), 10, C(0x334155), C(0x1E293B), 3);
            // кружок состояния
            var dot = new Rectangle(176, 158, 70, 70);
            using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, dot);
            using (var br = new SolidBrush(bad ? C(0xEF4444) : C(0x22C55E))) g.FillEllipse(br, Rectangle.Inflate(dot, -9, -9));
            using (var pen = new Pen(Color.White, 9) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (bad) { g.DrawLine(pen, 211, 178, 211, 196); g.DrawLine(pen, 211, 208, 211, 209); }
                else g.DrawLines(pen, new[] { new Point(196, 194), new Point(206, 204), new Point(226, 182) });
            }
        }

        static void DrawPrinter(Graphics g)
        {
            // лист сверху
            Fill(g, new Rectangle(66, 18, 124, 84), 6, Color.White, C(0x94A3B8), 4);
            using (var pen = new Pen(C(0xCBD5E1), 6)) { g.DrawLine(pen, 84, 42, 172, 42); g.DrawLine(pen, 84, 62, 150, 62); }
            // корпус
            Fill(g, new Rectangle(16, 88, 224, 112), 20, C(0xE2E8F0), C(0x64748B), 4);
            using (var br = new SolidBrush(C(0xCBD5E1))) g.FillRectangle(br, 24, 96, 208, 24);
            // лоток и лист снизу
            using (var br = new SolidBrush(C(0x334155))) g.FillRectangle(br, 54, 156, 148, 14);
            Fill(g, new Rectangle(66, 164, 124, 76), 6, Color.White, C(0x94A3B8), 4);
            using (var pen = new Pen(C(0xCBD5E1), 6)) { g.DrawLine(pen, 84, 196, 172, 196); g.DrawLine(pen, 84, 214, 150, 214); }
            // индикатор и кнопка
            using (var br = new SolidBrush(C(0x22C55E))) g.FillEllipse(br, 198, 128, 18, 18);
            using (var br = new SolidBrush(C(0x0284C7))) g.FillEllipse(br, 170, 128, 18, 18);
        }

        static void Fill(Graphics g, Rectangle r, int radius, Color fill, Color border, float width)
        {
            using (var path = new GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (var br = new SolidBrush(fill)) g.FillPath(br, path);
                using (var pen = new Pen(border, width)) g.DrawPath(pen, path);
            }
        }
    }
}
