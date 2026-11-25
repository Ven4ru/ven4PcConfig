using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PcInventory
{
    /// <summary>
    /// Автообновление из выпусков GitHub (Ven4ru/ven4PcConfig): последняя метка vX.Y.Z → архив ven4PcConfig.zip
    /// и SHA256SUMS.txt → сверка контрольных сумм архива и exe → замена exe, exe.config и README.txt рядом с программой.
    /// Папка с данными не трогается. Запущенный exe переименовывается в ven4PcConfig.old.exe (Windows это позволяет)
    /// и удаляется при следующем запуске.
    /// </summary>
    static class Updater
    {
        public const string Repo = "Ven4ru/ven4PcConfig";
        public const string ReleasesUrl = "https://github.com/" + Repo + "/releases";
        public const string OldExe = "ven4PcConfig.old.exe";
        static readonly string[] Files = { "ven4PcConfig.exe", "ven4PcConfig.exe.config", "README.txt" };

        /// <summary>Текущая версия; для проверки обновления «как со старой версии» её можно подменить
        /// переменной окружения V4PC_FAKE_VERSION.</summary>
        public static string CurrentVersion
        {
            get
            {
                string fake = Environment.GetEnvironmentVariable("V4PC_FAKE_VERSION");
                return string.IsNullOrEmpty(fake) ? Program.Version : fake;
            }
        }

        /// <summary>a новее b? Версии вида 1.4.0, допускается «v» впереди и «+хеш» сзади.</summary>
        public static bool Newer(string a, string b)
        {
            int[] x = Parse(a), y = Parse(b);
            for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i] > y[i];
            return false;
        }

        static int[] Parse(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int plus = v.IndexOfAny(new[] { '+', '-', ' ' });
            if (plus >= 0) v = v.Substring(0, plus);
            var res = new int[3];
            var parts = v.Split('.');
            for (int i = 0; i < 3 && i < parts.Length; i++) int.TryParse(parts[i], out res[i]);
            return res;
        }

        static void Tls()
        {
            // TLS 1.2 (3072) и 1.1 (768): в .NET 2.0 без обновлений их нет — тогда GitHub недоступен, проверка молча пропускается
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls; }
            catch { try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { } }
        }

        static HttpWebRequest Req(string url, int timeoutMs)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "ven4PcConfig/" + Program.Version;
            r.Timeout = timeoutMs;
            r.ReadWriteTimeout = timeoutMs;
            return r;
        }

        /// <summary>Метка последнего выпуска («v1.4.0») — по перенаправлению /releases/latest, без API и JSON.
        /// null — нет сети, нет TLS 1.2 или GitHub не ответил.</summary>
        public static string LatestTag()
        {
            try
            {
                Tls();
                var r = Req(ReleasesUrl + "/latest", 10000);
                r.AllowAutoRedirect = false;
                r.Method = "HEAD";
                using (var resp = (HttpWebResponse)r.GetResponse())
                {
                    string loc = resp.Headers["Location"] ?? "";
                    int i = loc.LastIndexOf("/tag/", StringComparison.Ordinal);
                    return i >= 0 ? Uri.UnescapeDataString(loc.Substring(i + 5)) : null;
                }
            }
            catch (WebException ex)
            {
                // часть серверов отдаёт 302 как «ошибку» при AllowAutoRedirect=false
                var resp = ex.Response as HttpWebResponse;
                if (resp == null) return null;
                string loc = resp.Headers["Location"] ?? "";
                int i = loc.LastIndexOf("/tag/", StringComparison.Ordinal);
                return i >= 0 ? Uri.UnescapeDataString(loc.Substring(i + 5)) : null;
            }
            catch { return null; }
        }

        static byte[] Download(string url)
        {
            Tls();
            var r = Req(url, 60000);
            using (var resp = r.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[65536];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return ms.ToArray();
            }
        }

        /// <summary>Скачать выпуск tag и установить рядом с программой. Возвращает путь нового exe.</summary>
        public static string DownloadAndInstall(string tag, string dir)
        {
            string baseUrl = ReleasesUrl + "/download/" + tag + "/";
            byte[] zip = Download(baseUrl + "ven4PcConfig.zip");
            string sums = Encoding.UTF8.GetString(Download(baseUrl + "SHA256SUMS.txt"));
            return Install(zip, sums, dir);
        }

        /// <summary>Проверить архив по SHA256SUMS и заменить файлы программы в dir.</summary>
        public static string Install(byte[] zip, string sums, string dir)
        {
            var expected = ParseSums(sums);
            string zh;
            if (!expected.TryGetValue("ven4PcConfig.zip", out zh)) throw new Exception("в SHA256SUMS.txt нет суммы архива");
            if (Sha256(zip) != zh) throw new Exception("контрольная сумма архива не совпала — файл повреждён при скачивании");

            var entries = ZipRead(zip);
            var files = new Dictionary<string, byte[]>();
            foreach (var kv in entries)
            {
                string name = kv.Key.Replace('\\', '/');
                name = name.Substring(name.LastIndexOf('/') + 1);
                if (Array.IndexOf(Files, name) >= 0) files[name] = kv.Value;
            }
            if (!files.ContainsKey("ven4PcConfig.exe")) throw new Exception("в архиве нет ven4PcConfig.exe");
            string eh;
            if (expected.TryGetValue("ven4PcConfig.exe", out eh) && Sha256(files["ven4PcConfig.exe"]) != eh)
                throw new Exception("контрольная сумма exe не совпала");

            // сначала всё пишем рядом как *.new — если места или прав нет, старая версия остаётся целой
            foreach (var kv in files) File.WriteAllBytes(Path.Combine(dir, kv.Key + ".new"), kv.Value);
            string exe = Path.Combine(dir, "ven4PcConfig.exe"), old = Path.Combine(dir, OldExe);
            if (File.Exists(old)) File.Delete(old); // остаток прошлого обновления
            if (File.Exists(exe)) File.Move(exe, old); // запущенный exe можно переименовать, но не перезаписать
            foreach (var kv in files)
            {
                string target = Path.Combine(dir, kv.Key);
                if (kv.Key != "ven4PcConfig.exe" && File.Exists(target)) File.Delete(target);
                File.Move(target + ".new", target);
            }
            return exe;
        }

        /// <summary>Убрать exe прошлой версии после обновления (при запуске новой).</summary>
        public static void CleanupOld(string dir)
        {
            try { string old = Path.Combine(dir, OldExe); if (File.Exists(old)) File.Delete(old); } catch { }
        }

        public static Dictionary<string, string> ParseSums(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim().TrimStart('﻿');
                int sp = line.IndexOf(' ');
                if (sp != 64) continue;
                d[line.Substring(sp).Trim().TrimStart('*')] = line.Substring(0, 64).ToLowerInvariant();
            }
            return d;
        }

        public static string Sha256(byte[] data)
        {
            using (var h = new SHA256Managed())
            {
                var sb = new StringBuilder(64);
                foreach (byte b in h.ComputeHash(data)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Чтение ZIP (deflate или без сжатия) по центральному каталогу — в .NET 2.0 нет ZipArchive.</summary>
        public static Dictionary<string, byte[]> ZipRead(byte[] z)
        {
            int eocd = -1;
            for (int i = z.Length - 22; i >= Math.Max(0, z.Length - 65557); i--)
                if (U32(z, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) throw new Exception("архив повреждён");
            int count = U16(z, eocd + 10), pos = (int)U32(z, eocd + 16);
            var res = new Dictionary<string, byte[]>();
            for (int k = 0; k < count; k++)
            {
                if (U32(z, pos) != 0x02014b50) throw new Exception("архив повреждён");
                int method = U16(z, pos + 10), flags = U16(z, pos + 8);
                int csize = (int)U32(z, pos + 20), usize = (int)U32(z, pos + 24);
                int nlen = U16(z, pos + 28), xlen = U16(z, pos + 30), clen = U16(z, pos + 32);
                int local = (int)U32(z, pos + 42);
                var enc = (flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(866);
                string name = enc.GetString(z, pos + 46, nlen);
                pos += 46 + nlen + xlen + clen;
                if (name.EndsWith("/")) continue;
                int data = local + 30 + U16(z, local + 26) + U16(z, local + 28);
                byte[] raw = new byte[usize];
                if (method == 0) Array.Copy(z, data, raw, 0, usize);
                else if (method == 8)
                    using (var ds = new DeflateStream(new MemoryStream(z, data, csize), CompressionMode.Decompress))
                    {
                        int off = 0, n;
                        while (off < usize && (n = ds.Read(raw, off, usize - off)) > 0) off += n;
                    }
                else throw new Exception("неизвестный способ сжатия в архиве");
                res[name] = raw;
            }
            return res;
        }

        static int U16(byte[] b, int i) => b[i] | (b[i + 1] << 8);
        static uint U32(byte[] b, int i) => (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));
    }
}
