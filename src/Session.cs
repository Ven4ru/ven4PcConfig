using System;
using System.Collections.Generic;
using System.IO;

namespace PcInventory
{
    /// <summary>
    /// Общая логика консольного режима и окна: чтение сохранённых ПК, сбор сведений об этом ПК,
    /// поиск его прежней записи, сохранение и пересборка Excel.
    /// </summary>
    class Session
    {
        public readonly string BaseDir;
        public readonly Store Store;
        public List<PcRecord> All = new List<PcRecord>();
        public readonly List<string> LoadErrors = new List<string>();

        /// <summary>Только что собранная запись этого ПК и его прежняя запись в реестре (если была).</summary>
        public PcRecord Current, Prev;
        /// <summary>Ошибки сбора: разделы и отдельные запросы WMI.</summary>
        public readonly List<string> CollectErrors = new List<string>();

        public Session(string baseDir)
        {
            BaseDir = baseDir;
            Store = new Store(Path.Combine(baseDir, Program.DataDirName));
        }

        public string XlsxPath => Path.Combine(BaseDir, Program.XlsxName);
        public string DataDir => Path.Combine(BaseDir, Program.DataDirName);

        public void Load()
        {
            LoadErrors.Clear();
            All = Store.LoadAll(LoadErrors);
        }

        /// <summary>Перечитать папку данных, сохранив собранную запись этого ПК.</summary>
        public void Reload()
        {
            Load();
            if (Current == null) return;
            Prev = null;
            foreach (var p in All) if (p.Key == Current.Key) Prev = p;
        }

        /// <summary>Все записи, кроме прежней записи этого ПК.</summary>
        public List<PcRecord> Others()
        {
            var others = new List<PcRecord>();
            foreach (var p in All) if (p != Prev) others.Add(p);
            return others;
        }

        /// <summary>Собирает сведения об этом ПК и подставляет отдел, номер, ФИО и примечание из его прежней записи.</summary>
        public PcRecord Collect(Action<string> progress)
        {
            Wmi.Errors.Clear();
            var col = new Collector();
            var rec = col.Collect(progress);

            PcRecord prev = null;
            foreach (var p in All) if (p.Key == rec.Key) prev = p;
            // ПК могли переименовать — ищем ту же машину. UUID у дешёвых плат бывает одинаковым у всех
            // (03000200-0400-0500-0006-000700080009 и т. п.), поэтому одного UUID мало: нужен ещё общий MAC
            // сетевой карты или одинаковый серийный номер.
            if (prev == null)
                foreach (var p in All)
                {
                    bool sameUuid = !Util.Junk(rec["Uuid"]) && string.Equals(p["Uuid"], rec["Uuid"], StringComparison.OrdinalIgnoreCase);
                    bool sameSerial = rec["Serial"] != "" && p["Serial"] == rec["Serial"];
                    bool sameMac = SharesMac(p, rec);
                    if ((sameUuid && (sameMac || sameSerial)) || (sameSerial && sameMac)) { prev = p; break; }
                }
            // запись переименованного ПК пишется в его прежний файл — иначе в реестре появится вторая строка
            if (prev != null && prev.Key != "") rec.Key = prev.Key;
            foreach (var k in new[] { "Dept", "ArmId", "Fio", "Note" })
                rec[k] = prev != null ? prev[k] : "";
            if (prev == null && rec["Fio"] == "") rec["Fio"] = rec["UserFullName"];

            // Ошибки сбора: разделы (col.Errors) и отдельные запросы WMI (Wmi.Errors) — иначе пустые ячейки без объяснения
            CollectErrors.Clear();
            CollectErrors.AddRange(col.Errors);
            foreach (var e in Wmi.Errors) CollectErrors.Add("WMI: " + e);
            rec["CollectErrors"] = string.Join("\n", CollectErrors.ToArray());

            Current = rec;
            Prev = prev;
            return rec;
        }

        /// <summary>Есть ли у двух записей общий MAC-адрес сетевой карты (лист «Комплектующие», тип «Сеть»).</summary>
        static bool SharesMac(PcRecord a, PcRecord b)
        {
            var macs = new Dictionary<string, bool>();
            foreach (var p in a.Parts) if (p[0] == "Сеть" && p[3] != "") macs[p[3].ToUpperInvariant()] = true;
            foreach (var p in b.Parts) if (p[0] == "Сеть" && p[3] != "" && macs.ContainsKey(p[3].ToUpperInvariant())) return true;
            return false;
        }

        /// <summary>Сохраняет запись этого ПК; в реестре она заменяет прежнюю.</summary>
        public string SaveCurrent()
        {
            string saved = Store.Save(Current);
            var others = Others();
            others.Add(Current);
            All = others;
            Prev = Current;
            return saved;
        }

        /// <summary>Пересобирает Excel. Если файл открыт в Excel — пишет рядом файл с датой (busy = true).</summary>
        public string BuildXlsx(out bool busy)
        {
            busy = false;
            byte[] xlsx = Report.Build(All);
            string path = XlsxPath;
            try { File.WriteAllBytes(path, xlsx); }
            catch (IOException)
            {
                busy = true;
                path = Path.Combine(BaseDir, "Реестр_АРМ_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
                File.WriteAllBytes(path, xlsx);
            }
            return path;
        }
    }
}
