using System;
using System.Collections.Generic;

namespace PcInventory
{
    /// <summary>
    /// История изменений ПК: что поменялось между прошлым и новым сбором — комплектующие (по названию
    /// и серийному номеру), ОС, антивирус, офис, имя ПК, пользователь, установленные программы.
    /// Строка изменения: дата, вид («добавлено», «удалено», «изменено»), что, подробности.
    /// </summary>
    static class History
    {
        public const string Added = "добавлено", Removed = "удалено", Changed = "изменено";
        /// <summary>Сколько записей истории хранить у одного ПК (старые отбрасываются).</summary>
        public const int Keep = 500;

        // комплектующие, которые меняют руками; «Раздел» (свободное место) и «Сеть» (виртуальные адаптеры) — шум
        static readonly string[] PartTypes = { "Процессор", "ОЗУ", "Мат. плата", "Видеоадаптер", "Накопитель", "Монитор", "Принтер" };

        static readonly string[][] Fields =
        {
            new[] { "PcName", "Имя ПК" }, new[] { "OsFull", "ОС" }, new[] { "Av", "Антивирус" }, new[] { "Office", "Офисный пакет" },
            new[] { "User", "Пользователь" },
        };

        static string Field(PcRecord r, string key) =>
            key == "OsFull" ? Util.Join(" ", new[] { r["Os"], r["OsVer"] }) : r[key];

        /// <summary>Изменения от old к cur (old == null — первый сбор, изменений нет).</summary>
        public static List<string[]> Diff(PcRecord old, PcRecord cur, string date)
        {
            var res = new List<string[]>();
            if (old == null) return res;

            foreach (var f in Fields)
            {
                string a = Field(old, f[0]), b = Field(cur, f[0]);
                if (a != b && b != "" && a != "") res.Add(new[] { date, Changed, f[1], a.Replace("\n", "; ") + " → " + b.Replace("\n", "; ") });
            }

            var oldParts = Parts(old);
            var newParts = Parts(cur);
            foreach (var kv in newParts) if (!oldParts.ContainsKey(kv.Key)) res.Add(new[] { date, Added, kv.Value[0], PartText(kv.Value) });
            foreach (var kv in oldParts) if (!newParts.ContainsKey(kv.Key)) res.Add(new[] { date, Removed, kv.Value[0], PartText(kv.Value) });

            // программы: старые данные без списка программ не сравниваются — иначе «добавлено» всё
            if (old.Soft.Count > 0)
            {
                var oldSoft = Soft(old);
                var newSoft = Soft(cur);
                foreach (var kv in newSoft) if (!oldSoft.ContainsKey(kv.Key)) res.Add(new[] { date, Added, "Программа", kv.Value });
                foreach (var kv in oldSoft) if (!newSoft.ContainsKey(kv.Key)) res.Add(new[] { date, Removed, "Программа", kv.Value });
            }
            return res;
        }

        /// <summary>Добавить новые изменения к истории записи (не больше Keep последних).</summary>
        public static void Append(PcRecord r, List<string[]> changes)
        {
            r.Changes.AddRange(changes);
            if (r.Changes.Count > Keep) r.Changes.RemoveRange(0, r.Changes.Count - Keep);
        }

        static Dictionary<string, string[]> Parts(PcRecord r)
        {
            var d = new Dictionary<string, string[]>();
            foreach (var p in r.Parts)
            {
                if (p.Length < 4 || Array.IndexOf(PartTypes, p[0]) < 0 || p[1] == "") continue;
                string key = p[0] + "|" + p[1].ToLowerInvariant() + "|" + p[3].ToLowerInvariant();
                // два одинаковых модуля без серийного номера — разные строки
                string k = key;
                for (int n = 2; d.ContainsKey(k); n++) k = key + "#" + n;
                d[k] = p;
            }
            return d;
        }

        static string PartText(string[] p) => p[1] + (p[3] != "" ? " (с/н " + p[3] + ")" : "");

        static Dictionary<string, string> Soft(PcRecord r)
        {
            var d = new Dictionary<string, string>();
            foreach (var s in r.Soft)
            {
                string k = s[0].ToLowerInvariant();
                if (!d.ContainsKey(k)) d[k] = s[0] + (s[1] != "" ? " " + s[1] : "");
            }
            return d;
        }

        /// <summary>Короткая строка изменения для окна: «добавлено · Накопитель · Samsung SSD 980 (с/н …)».</summary>
        public static string Line(string[] c) => c[1] + " · " + c[2] + ": " + c[3];
    }
}
