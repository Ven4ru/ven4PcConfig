using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using PcInventory;
using Xunit;
using static PcInventory.Tests.TestData;

namespace PcInventory.Tests
{
    /// <summary>1.4.0: история изменений, контроль программ, автообновление.</summary>
    public class V14Tests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "v4pc-v14-" + Guid.NewGuid().ToString("N"));
        public V14Tests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        static PcRecord Rec(params string[] soft)
        {
            var r = Pc("a", "Склад");
            r["Os"] = "Windows 11 Pro"; r["OsVer"] = "24H2";
            r.Parts.Add(new[] { "ОЗУ", "Kingston 8 ГБ", "DDR4", "SN1" });
            r.Parts.Add(new[] { "Накопитель", "Samsung SSD 980", "500 ГБ, состояние: исправен, 41 °C", "S1" });
            r.Parts.Add(new[] { "Раздел", "C:", "свободно 50 ГБ", "" });
            foreach (var s in soft) r.Soft.Add(new[] { s, "1.0", "", "", Collector.AllUsers });
            return r;
        }

        // ------------------------------------------------------------ история
        [Fact]
        public void Diff_PartsSoftAndFields()
        {
            var old = Rec("7-Zip", "AnyDesk");
            var cur = Rec("7-Zip", "Telegram");
            cur.Parts[0] = new[] { "ОЗУ", "Kingston 8 ГБ", "DDR4", "SN2" };                       // заменили модуль
            cur.Parts[1] = new[] { "Накопитель", "Samsung SSD 980", "500 ГБ, состояние: исправен, 45 °C", "S1" }; // температура — не изменение
            cur.Parts[2] = new[] { "Раздел", "C:", "свободно 40 ГБ", "" };                          // свободное место — не изменение
            cur["OsVer"] = "25H2";
            var d = History.Diff(old, cur, "2026-11-25 15:00");
            Assert.Contains(d, c => c[1] == History.Added && c[2] == "ОЗУ" && c[3].Contains("SN2"));
            Assert.Contains(d, c => c[1] == History.Removed && c[2] == "ОЗУ" && c[3].Contains("SN1"));
            Assert.Contains(d, c => c[1] == History.Changed && c[2] == "ОС" && c[3].Contains("24H2 → Windows 11 Pro 25H2"));
            Assert.Contains(d, c => c[1] == History.Added && c[2] == "Программа" && c[3].StartsWith("Telegram"));
            Assert.Contains(d, c => c[1] == History.Removed && c[2] == "Программа" && c[3].StartsWith("AnyDesk"));
            Assert.DoesNotContain(d, c => c[2] == "Накопитель" || c[2] == "Раздел");
            Assert.Equal(5, d.Count);
            Assert.Empty(History.Diff(null, cur, "x"));
        }

        [Fact]
        public void Session_SavesHistory_AndKeepsItAcrossCollections()
        {
            var s = new Session(dir + Path.DirectorySeparatorChar);
            var old = Rec("AnyDesk"); old["Collected"] = "2026-11-20 10:00";
            s.Store.Save(old);
            s.Load();
            var cur = Rec("Telegram"); cur["Collected"] = "2026-11-25 15:00";
            cur.Changes.AddRange(s.All[0].Changes);
            s.Current = cur; s.Prev = s.All[0];
            Assert.Equal(2, s.PendingChanges().Count);
            s.SaveCurrent();
            Assert.Empty(s.PendingChanges()); // повторное сохранение не дублирует
            var again = new Session(s.BaseDir); again.Load();
            Assert.Equal(2, again.All[0].Changes.Count);
            Assert.Equal("2026-11-25 15:00", again.All[0].Changes[0][0]);
        }

        // ------------------------------------------------------------ контроль программ
        [Fact]
        public void Programs_DefaultsAndChecks()
        {
            var st = new AppSettings();
            var pc = Rec("µTorrent", "qBittorrent 4.6", "AnyDesk", "7-Zip");
            Assert.Equal(new[] { "µTorrent", "qBittorrent 4.6" }, st.FoundForbidden(pc).ToArray()); // AnyDesk в примерах выключен
            st.Forbidden.Find(r => r.Name == "AnyDesk").On = true;
            Assert.Contains("AnyDesk", st.FoundForbidden(pc));
            st.Required.Add(new ProgRule("Kaspersky", true));
            Assert.Equal(new[] { "Kaspersky" }, st.MissingRequired(pc).ToArray());

            var saved = Report.Rules;
            try
            {
                Report.Rules = st;
                var probs = Report.ProblemsOf(pc);
                Assert.Contains(probs, p => p.StartsWith("запрещённые программы: ") && p.Contains("AnyDesk"));
                Assert.Contains("нет программ: Kaspersky", probs);
            }
            finally { Report.Rules = saved; }
        }

        [Fact]
        public void Programs_RoundTrip_UserListReplacesExamples()
        {
            string path = Path.Combine(dir, "настройки.xml");
            var st = new AppSettings { AutoUpdate = false };
            st.Forbidden = new List<ProgRule> { new ProgRule("Steam", true) };
            st.Required = new List<ProgRule> { new ProgRule("1С", false) };
            st.Save(path);
            var r = AppSettings.Load(path);
            Assert.False(r.AutoUpdate);
            Assert.Single(r.Forbidden);
            Assert.Equal("Steam", r.Forbidden[0].Name);
            Assert.False(r.Required[0].On);
            Assert.True(AppSettings.Load(Path.Combine(dir, "нет.xml")).AutoUpdate);
        }

        // ------------------------------------------------------------ автообновление
        [Fact]
        public void Versions()
        {
            Assert.True(Updater.Newer("v1.4.0", "1.3.0"));
            Assert.True(Updater.Newer("v1.10.0", "1.9.5"));
            Assert.False(Updater.Newer("v1.3.0", "1.3.0+17e61f0"));
            Assert.False(Updater.Newer("v1.2.1", "1.3.0"));
        }

        static byte[] ReleaseZip(byte[] exe)
        {
            var ms = new MemoryStream();
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var kv in new Dictionary<string, byte[]> { { "ven4PcConfig/ven4PcConfig.exe", exe },
                                                                     { "ven4PcConfig/ven4PcConfig.exe.config", Encoding.UTF8.GetBytes("<configuration/>") },
                                                                     { "ven4PcConfig/README.txt", Encoding.UTF8.GetBytes("новая инструкция") } })
                    using (var s = z.CreateEntry(kv.Key, CompressionLevel.Optimal).Open()) s.Write(kv.Value, 0, kv.Value.Length);
            }
            return ms.ToArray();
        }

        [Fact]
        public void Install_VerifiesAndReplaces_KeepsData()
        {
            var exe = Encoding.ASCII.GetBytes("NEW EXE " + new string('x', 5000));
            byte[] zip = ReleaseZip(exe);
            string sums = Updater.Sha256(zip) + "  ven4PcConfig.zip\n" + Updater.Sha256(exe) + "  ven4PcConfig.exe\n";
            File.WriteAllText(Path.Combine(dir, "ven4PcConfig.exe"), "OLD EXE");
            File.WriteAllText(Path.Combine(dir, "README.txt"), "старая");
            Directory.CreateDirectory(Path.Combine(dir, "ven4PcConfig_данные"));
            File.WriteAllText(Path.Combine(dir, "ven4PcConfig_данные", "pc.xml"), "<pc/>");

            Updater.Install(zip, sums, dir);
            Assert.Equal(exe, File.ReadAllBytes(Path.Combine(dir, "ven4PcConfig.exe")));
            Assert.Equal("OLD EXE", File.ReadAllText(Path.Combine(dir, Updater.OldExe)));
            Assert.Equal("новая инструкция", File.ReadAllText(Path.Combine(dir, "README.txt")));
            Assert.Equal("<pc/>", File.ReadAllText(Path.Combine(dir, "ven4PcConfig_данные", "pc.xml")));
            Updater.CleanupOld(dir);
            Assert.False(File.Exists(Path.Combine(dir, Updater.OldExe)));
        }

        [Fact]
        public void Install_RejectsBadChecksum_LeavesOldVersion()
        {
            byte[] zip = ReleaseZip(Encoding.ASCII.GetBytes("NEW"));
            File.WriteAllText(Path.Combine(dir, "ven4PcConfig.exe"), "OLD EXE");
            string sums = new string('0', 64) + "  ven4PcConfig.zip\n";
            Assert.Throws<Exception>(() => Updater.Install(zip, sums, dir));
            Assert.Equal("OLD EXE", File.ReadAllText(Path.Combine(dir, "ven4PcConfig.exe")));
        }

        [Fact]
        public void Search_FieldsAndPrograms()
        {
            var r = Rec("AnyDesk", "7-Zip");
            r["Fio"] = "Смирнова Ольга"; r["Ip"] = "10.0.12.43";
            string via;
            Assert.True(Report.Matches(r, "смирн", "", out via)); Assert.Null(via);
            Assert.True(Report.Matches(r, "12.43", "", out via));
            Assert.True(Report.Matches(r, "каб. 2", "Каб. 204", out via));
            Assert.True(Report.Matches(r, "anydesk", "", out via)); Assert.StartsWith("AnyDesk", via);
            Assert.False(Report.Matches(r, "teamviewer", "", out via));
        }

        /// <summary>Живая проверка обновления с GitHub (ручная): только если задана V4PC_LIVE_UPDATE.
        /// Скачивает последний выпуск во временную папку, сверяет суммы и ставит.</summary>
        [Fact]
        public void LiveUpdate_WhenRequested()
        {
            if (Environment.GetEnvironmentVariable("V4PC_LIVE_UPDATE") != "1") return;
            string tag = Updater.LatestTag();
            Assert.NotNull(tag);
            File.WriteAllText(Path.Combine(dir, "ven4PcConfig.exe"), "OLD");
            string exe = Updater.DownloadAndInstall(tag, dir);
            var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductVersion;
            Assert.StartsWith(tag.TrimStart('v'), ver);
            Assert.True(File.Exists(Path.Combine(dir, Updater.OldExe)));
        }

        [Fact]
        public void ZipRead_ReadsOwnZipWriterToo()
        {
            var ms = new MemoryStream();
            var w = new ZipWriter(ms);
            w.Add("a/б.txt", "привет");
            w.Add("c.bin", new byte[3000]);
            w.Finish();
            var r = Updater.ZipRead(ms.ToArray());
            Assert.Equal("привет", Encoding.UTF8.GetString(r["a/б.txt"]));
            Assert.Equal(3000, r["c.bin"].Length);
        }
    }
}
