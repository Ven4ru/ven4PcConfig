using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PcInventory
{
    /// <summary>Кнопка «Выгрузить»: Visio всегда, PNG, draw.io и лист Excel — по настройкам. Файлы — рядом с exe.</summary>
    static class MapExport
    {
        public const string BaseName = "Карта_АРМ";

        /// <summary>Пишет файлы и возвращает их пути. Ошибки отдельных форматов — в errors, остальные форматы пишутся.</summary>
        public static List<string> Export(Session s, List<string> errors)
        {
            var done = new List<string>();
            var m = s.ResolveMap();
            Try(errors, "Visio", () => done.Add(Write(Path.Combine(s.BaseDir, BaseName + ".vsdx"), VsdxWriter.Build(m, s.MapDir))));
            if (s.Settings.Png)
                foreach (var f in m.Doc.Floors)
                {
                    var floor = f;
                    Try(errors, "PNG «" + f.Name + "»", () =>
                    {
                        using (var bmp = MapRenderer.Render(m, floor, s.MapDir, MapPalette.Light, true))
                            done.Add(Write(Path.Combine(s.BaseDir, BaseName + "_" + Safe(m.Doc.FloorTitle(floor).Replace(" · ", " - ")) + ".png"), MapRenderer.Png(bmp)));
                    });
                }
            if (s.Settings.Drawio)
                Try(errors, "draw.io", () => done.Add(Write(Path.Combine(s.BaseDir, BaseName + ".drawio"), new UTF8Encoding(false).GetBytes(DrawioWriter.Build(m, s.MapDir)))));
            if (s.Settings.XlsxSheet && s.All.Count > 0)
                Try(errors, "Excel", () => { bool busy; done.Add(s.BuildXlsx(out busy)); });
            return done;
        }

        delegate void Step(); // в .NET 2.0 нет Action без параметров

        static void Try(List<string> errors, string what, Step a)
        {
            try { a(); }
            catch (Exception ex) { errors.Add(what + ": " + ex.Message); }
        }

        /// <summary>Файл открыт в другой программе (Visio, просмотрщик) — пишем рядом копию с меткой времени.</summary>
        static string Write(string path, byte[] data)
        {
            try { File.WriteAllBytes(path, data); return path; }
            catch (IOException)
            {
                string alt = Path.Combine(Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + Path.GetExtension(path));
                File.WriteAllBytes(alt, data);
                return alt;
            }
        }

        static string Safe(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString().Trim();
        }
    }
}
