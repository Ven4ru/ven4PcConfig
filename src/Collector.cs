using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace PcInventory
{
    /// <summary>Сбор сведений о компьютере. Каждый раздел изолирован: ошибка в одном не мешает остальным.</summary>
    partial class Collector
    {
        public readonly List<string> Errors = new List<string>();
        PcRecord r;
        string uuid = "", biosSerial = "", boardSerial = "";

        public PcRecord Collect(Action<string> progress)
        {
            r = new PcRecord();
            Step(progress, "Система", System_);
            Step(progress, "Процессор", Cpu);
            Step(progress, "Память", Ram);
            Step(progress, "Материнская плата", Board);
            Step(progress, "Видеоадаптеры", Gpu);
            Step(progress, "Накопители", Disks);
            Step(progress, "Мониторы", Monitors);
            Step(progress, "Операционная система", Os);
            Step(progress, "Антивирус", Antivirus);
            Step(progress, "Сеть", Network);
            Step(progress, "Принтеры", Printers);
            Step(progress, "Программы", Software);
            Step(progress, "Активация Windows", WindowsActivation);
            Step(progress, "Активация Office", OfficeActivation);
            r["Collected"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            string id = !Util.Junk(uuid) ? uuid : biosSerial + "|" + boardSerial;
            r.Key = Util.SafeFileName(r["PcName"]) + "_" + Util.Hash8(id.ToUpperInvariant());
            return r;
        }

        void Step(Action<string> progress, string name, Action0 a)
        {
            if (progress != null) progress(name);
            try { a(); }
            catch (Exception ex) { Errors.Add(name + ": " + ex.Message); }
        }

        delegate void Action0();

        void Part(string type, string name, string details, string serial) =>
            r.Parts.Add(new[] { type, Util.Clean(name), Util.Clean(details), Util.Clean(serial) });

        // ---------------------------------------------------------------- система
        void System_()
        {
            r["PcName"] = Environment.MachineName;
            var cs = Wmi.First("SELECT * FROM Win32_ComputerSystem");
            var csp = Wmi.First("SELECT * FROM Win32_ComputerSystemProduct");
            var bios = Wmi.First("SELECT * FROM Win32_BIOS");

            uuid = Wmi.S(csp, "UUID");
            biosSerial = Util.NoJunk(Wmi.S(bios, "SerialNumber"));
            if (biosSerial == "") biosSerial = Util.NoJunk(Wmi.S(csp, "IdentifyingNumber"));

            string vendor = Util.Vendor(Wmi.S(cs, "Manufacturer"));
            string model = Util.NoJunk(Wmi.S(cs, "Model"));
            string family = "";
            if (vendor == "Lenovo") family = Util.NoJunk(Wmi.S(csp, "Version")); // у Lenovo модель — в Version
            r["PcModel"] = Util.WithVendor(vendor, family != "" ? family + " (" + model + ")" : model);

            string user = Wmi.S(cs, "UserName");
            if (user == "") user = Environment.UserDomainName + "\\" + Environment.UserName;
            r["User"] = user;
            r["UserFullName"] = UserInfo.DisplayName();

            if (Wmi.B(cs, "PartOfDomain")) r["Domain"] = "Домен: " + Wmi.S(cs, "Domain");
            else
            {
                string wg = Wmi.S(cs, "Workgroup");
                if (wg == "") wg = Wmi.S(cs, "Domain");
                r["Domain"] = "Раб. группа: " + wg;
            }

            string bdate = Wmi.S(bios, "ReleaseDate");
            string bd = "";
            if (bdate.Length >= 8)
                try { bd = ManagementDateTimeConverter.ToDateTime(bdate).ToString("dd.MM.yyyy"); } catch { }
            r["Bios"] = Util.Join(" ", new[] { Util.Vendor(Wmi.S(bios, "Manufacturer")), Wmi.S(bios, "SMBIOSBIOSVersion"), bd != "" ? "(" + bd + ")" : "" });
            r["Uuid"] = uuid;

            Part("Компьютер", r["PcModel"], Util.Join("; ", new[] { "UUID " + uuid, r["Bios"] != "" ? "BIOS " + r["Bios"] : "" }), biosSerial);
        }

        // ---------------------------------------------------------------- процессор
        void Cpu()
        {
            var procs = Wmi.Cim("SELECT * FROM Win32_Processor");
            if (procs.Count == 0) return;
            string name = CleanCpu(Wmi.S(procs[0], "Name"));
            r["Cpu"] = procs.Count > 1 ? procs.Count + " x " + name : name;
            foreach (var p in procs)
            {
                long cores = Wmi.L(p, "NumberOfCores"), threads = Wmi.L(p, "NumberOfLogicalProcessors");
                Part("Процессор", CleanCpu(Wmi.S(p, "Name")), Util.Join(", ", new[] {
                    cores > 0 ? "ядер: " + cores : "",
                    threads > 0 ? "потоков: " + threads : "",
                    Wmi.L(p, "MaxClockSpeed") > 0 ? Wmi.L(p, "MaxClockSpeed") + " МГц" : "",
                    Wmi.S(p, "SocketDesignation") != "" ? "сокет " + Wmi.S(p, "SocketDesignation") : "" }),
                    Util.NoJunk(Wmi.S(p, "ProcessorId")));
            }
        }

        static string CleanCpu(string s)
        {
            foreach (var t in new[] { "(R)", "(r)", "(TM)", "(tm)", "®", "™" }) s = s.Replace(t, "");
            return Util.Clean(s.Replace(" CPU @", " CPU @"));
        }

        // ---------------------------------------------------------------- память
        static string SmbiosMemType(long t)
        {
            switch (t)
            {
                case 0x0F: return "SDRAM";
                case 0x12: return "DDR";
                case 0x13: return "DDR2";
                case 0x14: return "DDR2";
                case 0x18: return "DDR3";
                case 0x1A: return "DDR4";
                case 0x1B: return "LPDDR";
                case 0x1C: return "LPDDR2";
                case 0x1D: return "LPDDR3";
                case 0x1E: return "LPDDR4";
                case 0x22: return "DDR5";
                case 0x23: return "LPDDR5";
                default: return "";
            }
        }

        static string WmiMemType(long t)
        {
            switch (t)
            {
                case 17: return "SDRAM";
                case 20: return "DDR";
                case 21: return "DDR2";
                case 22: return "DDR2";
                case 24: return "DDR3";
                case 26: return "DDR4";
                default: return "";
            }
        }

        static string RamVendor(string m)
        {
            string u = m.ToUpperInvariant();
            string[][] map =
            {
                new[] { "80CE", "Samsung" }, new[] { "CE00", "Samsung" }, new[] { "80AD", "SK Hynix" }, new[] { "AD00", "SK Hynix" },
                new[] { "802C", "Micron" }, new[] { "2C00", "Micron" }, new[] { "859B", "Crucial" }, new[] { "04CD", "G.Skill" },
                new[] { "0198", "Kingston" }, new[] { "9801", "Kingston" }, new[] { "029E", "Corsair" }, new[] { "9E02", "Corsair" },
                new[] { "04EF", "Team Group" }, new[] { "0443", "Ramaxel" }, new[] { "8551", "Qimonda" }, new[] { "5105", "Qimonda" },
                new[] { "0B03", "Nanya" }, new[] { "830B", "Nanya" }, new[] { "8394", "Mushkin" }, new[] { "0CF8", "Apacer" }, new[] { "017A", "Apacer" },
            };
            foreach (var x in map) if (u.StartsWith(x[0]) && u.Replace("0", "").Length <= 6) return x[1];
            return Util.Junk(m) || u.StartsWith("MANUFACTURER") ? "" : m;
        }

        void Ram()
        {
            var mods = Wmi.Cim("SELECT * FROM Win32_PhysicalMemory");
            var smb = Smbios.Memory();
            long total = 0;
            string type = "";
            long speed = 0, confSpeed = 0;
            var sizes = new List<long>();
            foreach (var m in mods)
            {
                long cap = Wmi.L(m, "Capacity");
                if (cap <= 0) continue;
                total += cap;
                sizes.Add(cap);
                string t = SmbiosMemType(Wmi.L(m, "SMBIOSMemoryType"));
                if (t == "") t = WmiMemType(Wmi.L(m, "MemoryType"));
                if (type == "") type = t;
                speed = Math.Max(speed, Wmi.L(m, "Speed"));
                confSpeed = Math.Max(confSpeed, Wmi.L(m, "ConfiguredClockSpeed"));
            }
            foreach (var d in smb)
            {
                if (type == "") type = SmbiosMemType(d.Type);
                if (speed == 0) speed = d.Speed;
                if (confSpeed == 0) confSpeed = d.ConfSpeed;
            }
            if (sizes.Count == 0)
                foreach (var d in smb) if (d.SizeMb > 0) { sizes.Add(d.SizeMb * 1048576L); total += d.SizeMb * 1048576L; }
            if (total == 0)
                total = Wmi.L(Wmi.First("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"), "TotalPhysicalMemory");

            long sp = confSpeed > 0 ? confSpeed : speed;
            string cfg = "";
            if (sizes.Count > 1)
            {
                bool same = true;
                foreach (var s in sizes) if (s != sizes[0]) same = false;
                if (same) cfg = sizes.Count + "x" + Util.Gb(sizes[0] / 1073741824.0) + " ГБ";
                else
                {
                    var parts = new List<string>();
                    foreach (var s in sizes) parts.Add(Util.Gb(s / 1073741824.0));
                    cfg = string.Join("+", parts.ToArray()) + " ГБ";
                }
            }
            double gb = total / 1073741824.0;
            if (total > 0) r["RamGb"] = gb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            r["Ram"] =Util.Join(" ", new[] {
                total > 0 ? Util.Gb(gb) + " ГБ" : "", type, sp > 0 ? "@ " + sp + "MHz" : "", cfg != "" ? "(" + cfg + ")" : "" });

            foreach (var m in mods)
            {
                long cap = Wmi.L(m, "Capacity");
                if (cap <= 0) continue;
                string t = SmbiosMemType(Wmi.L(m, "SMBIOSMemoryType"));
                if (t == "") t = WmiMemType(Wmi.L(m, "MemoryType"));
                if (t == "") t = type;
                long s1 = Wmi.L(m, "ConfiguredClockSpeed");
                if (s1 == 0) s1 = Wmi.L(m, "Speed");
                string name = Util.Join(" ", new[] { RamVendor(Wmi.S(m, "Manufacturer")), Util.NoJunk(Wmi.S(m, "PartNumber")) });
                Part("ОЗУ", name == "" ? "Модуль памяти" : name,
                    Util.Join(", ", new[] { Util.Size(cap) + " " + t, s1 > 0 ? s1 + " МГц" : "",
                        Util.Join(" / ", new[] { Wmi.S(m, "DeviceLocator"), Wmi.S(m, "BankLabel") }) }),
                    Util.NoJunk(Wmi.S(m, "SerialNumber")));
            }
        }

        // ---------------------------------------------------------------- плата
        void Board()
        {
            var bb = Wmi.First("SELECT * FROM Win32_BaseBoard");
            boardSerial = Util.NoJunk(Wmi.S(bb, "SerialNumber"));
            string vendor = Util.Vendor(Wmi.S(bb, "Manufacturer"));
            string product = Util.NoJunk(Wmi.S(bb, "Product"));
            r["Board"] = Util.WithVendor(vendor, product);
            r["Serial"] = biosSerial != "" ? biosSerial : boardSerial;
            Part("Мат. плата", r["Board"], Util.NoJunk(Wmi.S(bb, "Version")) != "" ? "ревизия " + Wmi.S(bb, "Version") : "", boardSerial);
        }

        // ---------------------------------------------------------------- видео
        static readonly string[] VirtualGpu = { "remote display", "mirror", "dameware", "rdpdd", "rdp encoder", "radmin", "parsec",
            "virtual display", "idd", "citrix", "spacedesk", "duet", "splashtop", "anydesk", "teamviewer", "usb mobile monitor" };

        void Gpu()
        {
            var vram = VramFromRegistry();
            var names = new List<string>();
            var all = new List<string>();
            foreach (var v in Wmi.Cim("SELECT * FROM Win32_VideoController"))
            {
                string name = Wmi.S(v, "Name");
                if (name == "") continue;
                long ram;
                if (!vram.TryGetValue(name, out ram) || ram <= 0) ram = Wmi.L(v, "AdapterRAM");
                name = CleanCpu(name);
                long w = Wmi.L(v, "CurrentHorizontalResolution"), h = Wmi.L(v, "CurrentVerticalResolution");
                Part("Видеоадаптер", name, Util.Join(", ", new[] {
                    ram > 0 ? "память " + Util.Size(ram) : "",
                    Wmi.S(v, "DriverVersion") != "" ? "драйвер " + Wmi.S(v, "DriverVersion") : "",
                    w > 0 ? "режим " + w + "x" + h : "" }), "");
                if (!all.Contains(name)) all.Add(name);
                bool virt = false;
                string l = name.ToLowerInvariant();
                foreach (var x in VirtualGpu) if (l.Contains(x)) virt = true;
                if (!virt && !names.Contains(name)) names.Add(name);
            }
            if (names.Count == 0) names = all;
            if (names.Count > 1) names.Remove("Microsoft Basic Display Adapter");
            r["Gpu"] = string.Join("\n", names.ToArray());
        }

        static Dictionary<string, long> VramFromRegistry()
        {
            var d = new Dictionary<string, long>();
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (k == null) return d;
                    foreach (var sub in k.GetSubKeyNames())
                    {
                        try
                        {
                            using (var s = k.OpenSubKey(sub))
                            {
                                if (s == null) continue;
                                string desc = s.GetValue("DriverDesc") as string;
                                if (string.IsNullOrEmpty(desc)) continue;
                                long size = 0;
                                object q = s.GetValue("HardwareInformation.qwMemorySize");
                                if (q is long ql) size = ql;
                                else if (q is byte[] qb && qb.Length >= 8) size = BitConverter.ToInt64(qb, 0);
                                if (size <= 0)
                                {
                                    object m = s.GetValue("HardwareInformation.MemorySize");
                                    if (m is int mi) size = (uint)mi;
                                    else if (m is byte[] mb && mb.Length >= 4) size = BitConverter.ToUInt32(mb, 0);
                                }
                                if (size > 0 && (!d.ContainsKey(desc) || d[desc] < size)) d[desc] = size;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return d;
        }

        // ---------------------------------------------------------------- диски
        void Disks()
        {
            var msft = new Dictionary<string, ManagementBaseObject>();
            foreach (var o in Wmi.Query(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk", true))
                msft[Wmi.S(o, "DeviceId")] = o;

            var drives = Wmi.Cim("SELECT * FROM Win32_DiskDrive");
            drives.Sort((a, b) => Wmi.L(a, "Index").CompareTo(Wmi.L(b, "Index")));
            var lines = new List<string>();
            var healthLines = new List<string>();
            var diskKind = new Dictionary<long, string>();
            var predictFail = PredictFailures();
            bool anyBad = false, allOk = true, smartRead = false, smartDenied = false;
            foreach (var d in drives)
            {
                ManagementBaseObject m;
                msft.TryGetValue(Wmi.S(d, "Index"), out m);
                long bus = m != null ? Wmi.L(m, "BusType") : -1;
                string iface = Wmi.S(d, "InterfaceType").ToUpperInvariant();
                string media = Wmi.S(d, "MediaType").ToLowerInvariant();
                string pnp = Wmi.S(d, "PNPDeviceID").ToUpperInvariant();
                bool internalBus = bus == 11 || bus == 17 || bus == 3 || bus == 10 || bus == 8;
                bool usb = bus == 7 || iface == "USB" || pnp.StartsWith("USBSTOR") ||
                           (!internalBus && (media.Contains("removable") || media.Contains("external")));
                if (usb) continue; // флешки и внешние диски (в т.ч. саму флешку с программой) не учитываем

                string model = m != null ? Wmi.S(m, "Model") : "";
                if (model == "" && m != null) model = Wmi.S(m, "FriendlyName");
                if (model == "" || model.StartsWith("PhysicalDisk")) model = Wmi.S(d, "Model");
                foreach (var suf in new[] { " ATA Device", " SCSI Disk Device", " SATA Disk Device", " NVMe Device", " USB Device", " Disk Device" })
                    if (model.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) model = model.Substring(0, model.Length - suf.Length);
                model = Util.Clean(model);

                long size = m != null ? Wmi.L(m, "Size") : 0;
                if (size <= 0) size = Wmi.L(d, "Size");
                long gib = size / 1073741824L;

                string busName;
                switch (bus)
                {
                    case 11: busName = "SATA"; break;
                    case 17: busName = "NVMe"; break;
                    case 3: busName = "ATA"; break;
                    case 10: busName = "SAS"; break;
                    case 8: busName = "RAID"; break;
                    case 1: busName = "SCSI"; break;
                    case 12: busName = "SD"; break;
                    case 13: busName = "eMMC"; break;
                    case 9: busName = "iSCSI"; break;
                    case 15: busName = "VHD"; break;
                    case 16: busName = "Storage Spaces"; break;
                    default: busName = iface == "IDE" ? "SATA/IDE" : iface; break;
                }

                string kind = "";
                long mt = m != null ? Wmi.L(m, "MediaType") : 0;
                string ml = model.ToLowerInvariant();
                if (mt == 4 || bus == 17 || ml.Contains("ssd") || ml.Contains("nvme") || ml.Contains("solid state")) kind = "SSD";
                else if (mt == 3) kind = "HDD";
                else if (mt == 5) kind = "SCM";
                else if (m == null && (ml.StartsWith("wdc ") || ml.StartsWith("st") || ml.Contains("hitachi") || ml.Contains("hgst") || ml.StartsWith("toshiba dt") || ml.StartsWith("maxtor")))
                    kind = "HDD";

                string health;
                bool predFail = predictFail.ContainsKey(pnp);
                if (m != null)
                {
                    long hs = Wmi.L(m, "HealthStatus");
                    health = hs == 0 ? "исправен" : hs == 1 ? "ПРЕДУПРЕЖДЕНИЕ" : hs == 2 ? "НЕИСПРАВЕН" : "";
                }
                else
                {
                    string st = Wmi.S(d, "Status");
                    health = st == "OK" ? "исправен" : st == "Pred Fail" ? "ПРОГНОЗ ОТКАЗА" : st;
                }
                if (predFail) health = "ПРОГНОЗ ОТКАЗА";

                // Счётчики SMART (Windows 8+; читаются только с правами администратора)
                long wear = -1, temp = 0, hours = 0, errs = 0;
                var mo = m as ManagementObject;
                if (mo != null)
                {
                    try
                    {
                        foreach (ManagementBaseObject c in mo.GetRelated("MSFT_StorageReliabilityCounter"))
                        {
                            object w = Wmi.Raw(c, "Wear");
                            if (w != null) wear = Convert.ToInt64(w);
                            temp = Wmi.L(c, "Temperature");
                            hours = Wmi.L(c, "PowerOnHours");
                            errs = Wmi.L(c, "ReadErrorsUncorrected") + Wmi.L(c, "WriteErrorsUncorrected");
                            smartRead = true;
                        }
                    }
                    catch { smartDenied = true; }
                }

                string tag = Util.Join(" ", new[] { busName, kind });
                lines.Add(Util.Join(" ", new[] { gib > 0 ? gib + "GB" : "", model, tag != "" ? "(" + tag + ")" : "" }));
                if (kind != "") diskKind[Wmi.L(d, "Index")] = kind;

                // Оценка: «плохо» — неисправность, прогноз отказа, износ ≥ 90 %, неисправимые ошибки, перегрев ≥ 70 °C
                var notes = new List<string>();
                bool bad = health != "" && health != "исправен";
                if (wear >= 90) { bad = true; notes.Add("ресурс почти исчерпан"); }
                if (errs > 0) { bad = true; notes.Add("неисправимых ошибок: " + errs); }
                if (temp >= 70) { bad = true; notes.Add("перегрев"); }
                string smart = Util.Join(", ", new[] {
                    wear > 0 ? "износ " + wear + "%" : "",
                    temp > 0 && temp < 150 ? temp + " °C" : "",
                    hours > 0 ? "наработка " + hours + " ч" : "" }); // неразрывные пробелы: «41 °C» не рвётся переносом
                string state = Util.Join(", ", new[] { health != "" ? health : "состояние неизвестно", smart, Util.Join(", ", notes) });
                healthLines.Add((model != "" ? model : "Диск " + Wmi.S(d, "Index")) + " — " + state);
                if (bad) anyBad = true;
                if (health != "исправен") allOk = false;

                Part("Накопитель", model, Util.Join(", ", new[] {
                    gib > 0 ? gib + " ГиБ (" + (size / 1000000000L) + " ГБ)" : "", tag, "состояние: " + state }),
                    Wmi.S(m ?? d, "SerialNumber") != "" ? Wmi.S(m ?? d, "SerialNumber") : Wmi.S(d, "SerialNumber"));
            }
            r["Disk"] = string.Join("\n", lines.ToArray());
            r["DiskHealth"] = string.Join("\n", healthLines.ToArray());
            r["DiskHealthSt"] = anyBad ? Bad : allOk && healthLines.Count > 0 ? Ok : "";
            if (smartDenied && !smartRead) r["DiskHealth"] += "\n(износ и температура — только при запуске от имени администратора)";

            // Системный раздел: на каком диске (SSD или HDD) и сколько на нём свободно
            string sysDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\').ToUpperInvariant();
            try
            {
                foreach (var p in Wmi.Query(@"root\cimv2", "ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + sysDrive + "'} WHERE AssocClass=Win32_LogicalDiskToPartition", true))
                {
                    string k;
                    if (diskKind.TryGetValue(Wmi.L(p, "DiskIndex"), out k)) r["SysDiskKind"] = k;
                }
            }
            catch { }

            foreach (var ld in Wmi.Cim("SELECT * FROM Win32_LogicalDisk WHERE DriveType=3"))
            {
                long sz = Wmi.L(ld, "Size"), free = Wmi.L(ld, "FreeSpace");
                string label = Wmi.S(ld, "VolumeName");
                if (Wmi.S(ld, "DeviceID").ToUpperInvariant() == sysDrive && sz > 0)
                {
                    r["SysFreePct"] = (free * 100 / sz).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    r["SysFreeGb"] = (free / 1073741824L).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                Part("Раздел", Wmi.S(ld, "DeviceID") + (label != "" ? " (" + label + ")" : ""), Util.Join(", ", new[] {
                    Wmi.S(ld, "FileSystem"), sz > 0 ? "всего " + Util.Size(sz) : "", sz > 0 ? "свободно " + Util.Size(free) + " (" + (free * 100 / sz) + "%)" : "" }),
                    Wmi.S(ld, "VolumeSerialNumber"));
            }
        }

        /// <summary>PNPDeviceID дисков, для которых драйвер сообщает прогноз отказа SMART (нужны права администратора; работает и на Windows 7/XP).</summary>
        static Dictionary<string, bool> PredictFailures()
        {
            var res = new Dictionary<string, bool>();
            foreach (var o in Wmi.Query(@"root\wmi", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", true))
            {
                if (!Wmi.B(o, "PredictFailure")) continue;
                string inst = Wmi.S(o, "InstanceName").ToUpperInvariant();
                if (inst.EndsWith("_0")) inst = inst.Substring(0, inst.Length - 2);
                res[inst] = true;
            }
            return res;
        }

        // ---------------------------------------------------------------- мониторы
        static readonly Dictionary<string, string> PnpBrands = MakeBrands();

        static Dictionary<string, string> MakeBrands()
        {
            var d = new Dictionary<string, string>();
            string[] pairs = {
                "ACR","Acer", "ACI","ASUS", "AUS","ASUS", "AOC","AOC", "AUO","AU Optronics", "BNQ","BenQ", "BOE","BOE",
                "CMN","Innolux", "CMO","Chimei", "DEL","Dell", "DELL","Dell", "EIZ","EIZO", "ENC","EIZO", "GSM","LG", "LGD","LG Display",
                "HWP","HP", "HPN","HP", "HPQ","HP", "HSD","HannStar", "HKC","HKC", "IVM","iiyama", "IVO","InfoVision", "LEN","Lenovo",
                "NEC","NEC", "PHL","Philips", "SAM","Samsung", "SEC","Samsung", "SDC","Samsung", "SNY","Sony", "VSC","ViewSonic",
                "MSI","MSI", "GBT","Gigabyte", "SHP","Sharp", "TSB","Toshiba", "FUS","Fujitsu", "MEI","Panasonic", "XMI","Xiaomi",
                "APP","Apple", "MAX","Belinea", "HIQ","Hyundai", "HYO","Hyundai", "PRI","Prima", "KTC","KTC", "TCL","TCL",
                "HUA","Huawei", "HWV","Huawei", "MZI","Digma", "DNS","DNS", "GWD","GreenWorld", "SKY","Skyworth", "CHI","Chimei",
                "LPL","LG Philips", "QDS","Quanta", "BBY","Insignia", "VIZ","Vizio", "NOK","Nokia", "MED","Medion", "ELO","Elo Touch",
                "HRE","Haier", "RTK","Realtek", "PGS","Princeton", "ASB","AOpen", "CPQ","Compaq", "ADA","Asus", "TOS","Toshiba",
            };
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
            return d;
        }

        static string Brand(string code)
        {
            string b;
            if (code != null && PnpBrands.TryGetValue(code.ToUpperInvariant(), out b)) return b;
            return code ?? "";
        }

        static string TechName(long t)
        {
            switch (t)
            {
                case 0: return "VGA";
                case 4: return "DVI";
                case 5: return "HDMI";
                case 6: return "LVDS";
                case 10: return "DisplayPort";
                case 11: return "eDP";
                case 12: return "DisplayPort";
                case 15: return "Miracast";
                case 2147483648: return "встроенный";
                default: return "";
            }
        }

        void Monitors()
        {
            var modes = Display.Get();
            var conn = new Dictionary<string, long>();
            foreach (var c in Wmi.Query(@"root\wmi", "SELECT * FROM WmiMonitorConnectionParams", true))
                conn[Wmi.S(c, "InstanceName").ToUpperInvariant()] = Wmi.L(c, "VideoOutputTechnology");
            var basic = new Dictionary<string, double>();
            foreach (var c in Wmi.Query(@"root\wmi", "SELECT * FROM WmiMonitorBasicDisplayParams", true))
            {
                long w = Wmi.L(c, "MaxHorizontalImageSize"), h = Wmi.L(c, "MaxVerticalImageSize");
                if (w > 0 && h > 0) basic[Wmi.S(c, "InstanceName").ToUpperInvariant()] = Math.Sqrt(w * w + h * h) / 2.54;
            }

            var lines = new List<string>();
            var ids = Wmi.Query(@"root\wmi", "SELECT * FROM WmiMonitorID WHERE Active=TRUE", true);
            foreach (var id in ids)
            {
                string inst = Wmi.S(id, "InstanceName").ToUpperInvariant();
                Display.Mode mode = null;
                foreach (var m in modes)
                    if (!m.Used && m.MonitorPath != "" && (inst == m.MonitorPath || inst.StartsWith(m.MonitorPath + "_"))) { mode = m; break; }
                if (mode == null)
                {
                    // запасной вариант: совпадение по аппаратному ID (DISPLAY\PHL08E5\...)
                    string[] seg = inst.Split('\\');
                    if (seg.Length > 1)
                        foreach (var m in modes)
                            if (!m.Used && m.MonitorPath.Contains("\\" + seg[1] + "\\")) { mode = m; break; }
                }
                if (mode != null) mode.Used = true;

                long tech;
                conn.TryGetValue(inst, out tech);
                double diag;
                basic.TryGetValue(inst, out diag);
                AddMonitor(lines, Wmi.U16(id, "ManufacturerName"), Wmi.U16(id, "UserFriendlyName"), Wmi.U16(id, "ProductCodeID"),
                    Wmi.U16(id, "SerialNumberID"), (int)Wmi.L(id, "YearOfManufacture"), diag, conn.ContainsKey(inst) ? tech : -1, mode);
            }

            if (ids.Count == 0)
            {
                // Windows XP / нет WMI-классов мониторов: EDID из реестра
                foreach (var m in modes)
                {
                    var e = Edid.FromRegistryByDriver(m.DriverKey);
                    if (e != null)
                    {
                        double diag = e.WidthCm > 0 ? Math.Sqrt(e.WidthCm * e.WidthCm + e.HeightCm * e.HeightCm) / 2.54 : 0;
                        AddMonitor(lines, e.Mfr, e.Name, e.Product, e.Serial, e.Year, diag, -1, m);
                    }
                    else if (m.W > 0)
                    {
                        string n = m.MonitorName != "" && !m.MonitorName.StartsWith("Generic") && !m.MonitorName.StartsWith("Универсальный")
                            ? m.MonitorName : "Монитор";
                        lines.Add(n + " (" + m.Res + ")");
                        Part("Монитор", n, "разрешение " + m.Res, "");
                    }
                }
            }
            r["Monitor"] = string.Join("\n", lines.ToArray());
        }

        void AddMonitor(List<string> lines, string mfr, string friendly, string product, string serial, int year, double diag, long tech, Display.Mode mode)
        {
            // Название модели есть только в EDID-дескрипторе; без него показываем PnP-код монитора как есть (напр. PHL08E5)
            string full = friendly != "" ? Util.WithVendor(Brand(mfr), friendly)
                        : mfr + product != "" ? "Монитор " + mfr + product : "";
            bool builtIn = tech == 0x80000000L || tech == 6 || tech == 11 || tech == 13;
            if (builtIn) full = "Встроенный дисплей" + (full != "" ? " " + full : "");
            if (full == "") full = "Монитор";
            string res = mode != null ? mode.Res : "";
            lines.Add(full + (res != "" ? " (" + res + ")" : ""));
            Part("Монитор", full, Util.Join("; ", new[] {
                res != "" ? "режим " + res : "",
                diag > 5 ? "диагональ ≈" + diag.ToString("0.#", Util.Ru) + "\"" : "",
                TechName(tech) != "" ? "подключение " + TechName(tech) : "",
                year > 1990 && year < 2100 ? "выпуск " + year : "",
                product != "" ? "код " + mfr + product : "" }), serial);
        }

        // ---------------------------------------------------------------- ОС
        static bool Is64() => IntPtr.Size == 8 || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432"));

        void Os()
        {
            var os = Wmi.First("SELECT * FROM Win32_OperatingSystem");
            string cap = Wmi.S(os, "Caption");
            foreach (var p in new[] { "Microsoft ", "Майкрософт ", "Microsoft® ", "Microsoft(R) " })
                if (cap.StartsWith(p)) cap = cap.Substring(p.Length);
            cap = Util.Clean(cap.Replace("(R)", "").Replace("®", "").TrimEnd(','));
            if (cap == "") cap = Environment.OSVersion.VersionString;
            r["Os"] = cap + " " + (Is64() ? "64-bit" : "32-bit");

            string disp = "", rel = "", build = Wmi.S(os, "BuildNumber");
            long ubr = 0;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (k != null)
                    {
                        disp = Convert.ToString(k.GetValue("DisplayVersion")) ?? "";
                        rel = Convert.ToString(k.GetValue("ReleaseId")) ?? "";
                        object u = k.GetValue("UBR");
                        if (u is int ui) ubr = ui;
                        if (build == "") build = Convert.ToString(k.GetValue("CurrentBuild")) ?? "";
                    }
            }
            catch { }
            string sp = Wmi.S(os, "CSDVersion");
            string spNum = "";
            foreach (char c in sp) if (char.IsDigit(c)) spNum += c;
            r["OsVer"] = disp != "" ? disp : rel != "" ? rel : spNum != "" ? "SP" + spNum : "";
            string ver = Wmi.S(os, "Version");
            r["OsBuild"] = ver != "" ? ver + (ubr > 0 ? "." + ubr : "") : build;

            string inst = Wmi.S(os, "InstallDate");
            if (inst.Length >= 8)
                try { r["InstallDate"] = ManagementDateTimeConverter.ToDateTime(inst).ToString("yyyy-MM-dd"); } catch { }

            string lb = Wmi.S(os, "LastBootUpTime");
            string boot = "";
            if (lb.Length >= 8) try { boot = ManagementDateTimeConverter.ToDateTime(lb).ToString("dd.MM.yyyy HH:mm"); } catch { }
            Part("ОС", r["Os"], Util.Join("; ", new[] { r["OsVer"], "сборка " + r["OsBuild"], boot != "" ? "загружена " + boot : "",
                Wmi.S(os, "RegisteredUser") != "" ? "владелец " + Wmi.S(os, "RegisteredUser") : "" }), Wmi.S(os, "SerialNumber"));
        }

        // ---------------------------------------------------------------- антивирус
        void Antivirus()
        {
            var on = new List<string>();
            var off = new List<string>();
            var avs = new List<object[]>();
            var list = Wmi.Query(@"root\SecurityCenter2", "SELECT * FROM AntiVirusProduct", true);
            if (list.Count > 0)
            {
                foreach (var a in list)
                {
                    string n = Wmi.S(a, "displayName");
                    long st = Wmi.L(a, "productState");
                    // productState: биты 12–15 — 0 выкл, 1 вкл, 2 приостановлен, 3 лицензия истекла; биты 4–7 — 0 базы актуальны
                    int state = (int)((st >> 12) & 0xF);
                    int sig = ((st >> 4) & 0xF) == 0 ? 0 : 1;
                    avs.Add(new object[] { n, state, sig });
                    bool enabled = state == 1 || state == 3;
                    var target = enabled ? on : off;
                    if (n != "" && !target.Contains(n)) target.Add(n);
                }
            }
            else
            {
                // Windows XP SP2/SP3
                foreach (var a in Wmi.Query(@"root\SecurityCenter", "SELECT * FROM AntiVirusProduct", true))
                {
                    string n = Wmi.S(a, "displayName");
                    bool enabled = Wmi.B(a, "onAccessScanningEnabled");
                    object upd = Wmi.Raw(a, "productUptoDate");
                    avs.Add(new object[] { n, enabled ? 1 : 0, upd is bool ? ((bool)upd ? 0 : 1) : -1 });
                    var target = enabled ? on : off;
                    if (n != "" && !target.Contains(n)) target.Add(n);
                }
            }
            // Сторонний антивирус выводим первым, Defender — после
            on.Sort((a, b) => IsDefender(a).CompareTo(IsDefender(b)));
            if (on.Count > 1) on.RemoveAll(IsDefenderPred);
            var res = new List<string>(on);
            foreach (var o in off) if (!(IsDefender(o) == 1 && on.Count > 0)) res.Add(o + " (выключен)");
            r["Av"] = string.Join(", ", res.ToArray());
            foreach (var x in on) Part("Антивирус", x, "включен", "");
            foreach (var x in off) Part("Антивирус", x, "выключен", "");
            AntivirusLicense(avs);
        }

        static int IsDefender(string s) => s.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("Защитник", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0;
        static bool IsDefenderPred(string s) => IsDefender(s) == 1;

        // ---------------------------------------------------------------- сеть
        void Network()
        {
            var tiers = new List<string[]>[5];
            for (int i = 0; i < tiers.Length; i++) tiers[i] = new List<string[]>();
            foreach (var n in Wmi.Cim("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE"))
            {
                string desc = Wmi.S(n, "Description");
                string mac = Wmi.S(n, "MACAddress").Replace(':', '-');
                var v4 = new List<string>();
                foreach (var ip in Wmi.Arr(n, "IPAddress")) if (ip.IndexOf('.') > 0 && ip != "0.0.0.0") v4.Add(ip);
                string gw = Util.Join(", ", Wmi.Arr(n, "DefaultIPGateway"));
                string dns = Util.Join(", ", Wmi.Arr(n, "DNSServerSearchOrder"));
                Part("Сеть", desc, Util.Join("; ", new[] {
                    v4.Count > 0 ? "IP " + string.Join(", ", v4.ToArray()) : "",
                    gw != "" ? "шлюз " + gw : "", dns != "" ? "DNS " + dns : "",
                    Wmi.B(n, "DHCPEnabled") ? "DHCP" : "статический" }), mac);
                if (v4.Count == 0) continue;
                string l = desc.ToLowerInvariant();
                bool virt = false, vpn = mac == "";
                foreach (var x in new[] { "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "loopback", "npcap", "bluetooth" })
                    if (l.Contains(x)) virt = true;
                foreach (var x in new[] { "tap-", "tap ", "tunnel", "vpn", "wan miniport", "wireguard", "wintun", "openvpn", "zerotier",
                    "tailscale", "radmin", "hamachi", "kaspersky", "fortinet", "cisco anyconnect", "pangp" })
                    if (l.Contains(x)) vpn = true;
                // Приоритет: физический со шлюзом → физический → виртуальный (Hyper-V switch) со шлюзом → прочие (VPN/туннели)
                int tier = vpn ? 4 : virt ? (gw != "" ? 3 : 4) : (gw != "" ? 1 : 2);
                tiers[tier].Add(new[] { string.Join(", ", v4.ToArray()), mac });
            }
            var good = new List<string[]>();
            for (int t = 1; t <= 4 && good.Count == 0; t++) good = tiers[t];
            var ips = new List<string>();
            var macs = new List<string>();
            foreach (var g in good) { ips.Add(g[0]); macs.Add(g[1]); }
            r["Ip"] = string.Join("\n", ips.ToArray());
            r["Mac"] = string.Join("\n", macs.ToArray());
        }

        // ---------------------------------------------------------------- принтеры
        void Printers()
        {
            foreach (var p in Wmi.Cim("SELECT * FROM Win32_Printer"))
            {
                string n = Wmi.S(p, "Name");
                if (n == "" || PcInventory.Printers.IsVirtual(n)) continue;
                Part("Принтер", n, Util.Join("; ", new[] {
                    Wmi.S(p, "DriverName") != "" ? "драйвер " + Wmi.S(p, "DriverName") : "",
                    Wmi.S(p, "PortName") != "" ? "порт " + Wmi.S(p, "PortName") : "",
                    Wmi.B(p, "Network") ? "сетевой" : "",
                    Wmi.B(p, "Default") ? "по умолчанию" : "" }), "");
            }
        }

        // ---------------------------------------------------------------- программы
        public const string AllUsers = "все пользователи";
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <summary>Индекс «название|версия» → строка r.Soft, чтобы одна программа у нескольких пользователей была одной строкой.</summary>
        Dictionary<string, string[]> softIndex;

        void ReadUninstall(RegistryKey k, string who)
        {
            foreach (var sub in k.GetSubKeyNames())
            {
                try
                {
                    using (var s = k.OpenSubKey(sub))
                    {
                        if (s == null) continue;
                        string name = Util.Clean(s.GetValue("DisplayName") as string);
                        if (name == "") continue;
                        if (s.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (!Util.Blank(s.GetValue("ParentKeyName") as string)) continue;
                        string rt = s.GetValue("ReleaseType") as string ?? "";
                        if (rt == "Update" || rt == "Hotfix" || rt == "Security Update" || rt == "Update Rollup") continue;
                        string ver = Util.Clean(Convert.ToString(s.GetValue("DisplayVersion")));
                        string key = (name + "|" + ver).ToLowerInvariant();
                        string[] row;
                        if (softIndex.TryGetValue(key, out row))
                        {
                            if (row[4] == AllUsers) continue;
                            if (who == AllUsers) row[4] = AllUsers;
                            else if (Array.IndexOf(row[4].Split(new[] { ", " }, StringSplitOptions.None), who) < 0) row[4] += ", " + who;
                            continue;
                        }
                        string date = Util.Clean(Convert.ToString(s.GetValue("InstallDate")));
                        if (date.Length == 8)
                        {
                            DateTime dt;
                            if (DateTime.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out dt))
                                date = dt.ToString("yyyy-MM-dd");
                        }
                        row = new[] { name, ver, Util.Clean(s.GetValue("Publisher") as string), date, who };
                        softIndex[key] = row;
                        r.Soft.Add(row);
                    }
                }
                catch { }
            }
        }

        void ReadUninstall(RegistryKey root, string path, string who)
        {
            try
            {
                using (var k = root.OpenSubKey(path))
                    if (k != null) ReadUninstall(k, who);
            }
            catch { }
        }

        void Software()
        {
            softIndex = new Dictionary<string, string[]>();
            ReadUninstall(Registry.LocalMachine, UninstallKey, AllUsers);
            ReadUninstall(Registry.LocalMachine, @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall", AllUsers);
            // программы, установленные только для пользователя (Chrome, Zoom, Telegram, VS Code и т. п.)
            ReadUninstall(Registry.CurrentUser, UninstallKey, Environment.UserName);
            var skipped = new List<string>();
            try { UserHives.ForEachOther(UninstallKey, ReadUninstall, skipped); }
            catch (Exception ex) { Errors.Add("Программы других пользователей: " + ex.Message); }
            r["SoftSkipped"] = string.Join(", ", skipped.ToArray()); // предупреждение выводит Program, это не ошибка сбора
            r.Soft.Sort((a, b) => string.Compare(a[0], b[0], StringComparison.CurrentCultureIgnoreCase));

            // Офисный пакет
            var office = new List<string>();
            foreach (var s in r.Soft)
            {
                string n = s[0], l = n.ToLowerInvariant();
                bool isOffice = l.StartsWith("microsoft office") || l.StartsWith("microsoft 365") || l.StartsWith("libreoffice") ||
                                l.StartsWith("openoffice") || l.StartsWith("apache openoffice") || l.StartsWith("wps office") ||
                                l.StartsWith("мойофис") || l.StartsWith("myoffice") || l.StartsWith("р7-офис") || l.StartsWith("r7-office") ||
                                l.StartsWith("onlyoffice") || l.StartsWith("microsoft office 365");
                if (!isOffice) continue;
                foreach (var bad in new[] { "language", "proofing", "mui", "shared", "click-to-run", "средства", "языков", "проверки", "help pack", "web components", "file validation", "компоненты", "validation", "viewer", "compatibility", "совместимости", "outlook connector" })
                    if (l.Contains(bad)) { isOffice = false; break; }
                if (!isOffice) continue;
                int dash = n.LastIndexOf(" - ");
                if (dash > 0 && n.Length - dash <= 9) n = n.Substring(0, dash); // " - ru-ru"
                if (s[1] != "" && !l.StartsWith("microsoft") && n.IndexOf(s[1], StringComparison.Ordinal) < 0) n += " " + s[1];
                if (!office.Contains(n)) office.Add(n);
            }
            r["Office"] = string.Join("\n", office.ToArray());
        }
    }
}
