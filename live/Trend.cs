using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public static class Trend {
  static readonly DateTime Epoch = new DateTime(1970, 1, 1);
  const long EstOffset = 5 * 3600;

  static string LabRoot() {
    string env = Environment.GetEnvironmentVariable("LABDROP");
    if (!string.IsNullOrEmpty(env)) return env;
    return @"C:\LabDrop";
  }

  static string specViewDir;
  static string coleDir;
  static string gcDir;

  public static void UseLocations(string specView, string cole, string gc) {
    specViewDir = specView;
    coleDir = cole;
    gcDir = gc;
  }

  static string SpecViewDir() {
    if (!string.IsNullOrEmpty(specViewDir)) return specViewDir;
    return Path.Combine(LabRoot(), "SpecViewShareddata", "Copy Logs");
  }
  static string ColeDir() {
    if (!string.IsNullOrEmpty(coleDir)) return coleDir;
    return Path.Combine(LabRoot(), "ColePalmerSharedData");
  }
  static string GcDir() {
    if (!string.IsNullOrEmpty(gcDir)) return gcDir;
    return Path.Combine(LabRoot(), "TotalChromSharedData", "CopyLogs");
  }

  static uint U32(byte[] d, int p) {
    return (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24));
  }
  static int Valid(byte[] d, int pos) {
    if (pos + 4 > d.Length) return 0;
    byte tag = d[pos];
    if (tag != 0x03 && tag != 0x06 && tag != 0x09) return 0;
    int length = d[pos + 2] | (d[pos + 3] << 8);
    if (length < 6 || pos + length > d.Length) return 0;
    return length;
  }
  static bool BlockHeader(byte[] d, int pos) {
    if (pos + 8 > d.Length || d[pos] != 0x01 || d[pos + 2] != 0x08 || d[pos + 3] != 0x00) return false;
    uint ts = U32(d, pos + 4);
    return ts >= 946684800 && ts <= 4102444800;
  }
  static byte[] ReadShare(string path) {
    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
      byte[] b = new byte[fs.Length];
      int o = 0;
      while (o < b.Length) {
        int n = fs.Read(b, o, b.Length - o);
        if (n == 0) break;
        o += n;
      }
      if (o != b.Length) {
        byte[] c = new byte[o];
        Buffer.BlockCopy(b, 0, c, 0, o);
        return c;
      }
      return b;
    }
  }

  class Series {
    public string Name;
    public string Group;
    public string Unit;
    public bool Dashed;
    public List<double?> Y = new List<double?>();
  }

  class Rst {
    public byte[] data;
    public int i;
    public byte[] Need(int n) {
      if (i + n > data.Length) throw new Exception("end");
      byte[] c = new byte[n];
      Buffer.BlockCopy(data, i, c, 0, n);
      i += n;
      return c;
    }
    public uint U32b() {
      var b = Need(4);
      return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
    }
    public string Str() {
      uint length = U32b();
      if (length > 1000000) throw new Exception("strlen");
      int padded = (int)length + (int)((4 - length % 4) % 4);
      byte[] raw = Need(padded);
      int cut = 0;
      while (cut < length && raw[cut] != 0) cut++;
      return Encoding.GetEncoding(28591).GetString(raw, 0, cut).Trim();
    }
    public uint Ts() { uint s = U32b(); Need(8); return s; }
  }

  static double? BeDouble(byte[] d, int p) {
    if (p < 0 || p + 8 > d.Length) return null;
    byte[] rev = new byte[8];
    for (int k = 0; k < 8; k++) rev[k] = d[p + 7 - k];
    double v = BitConverter.ToDouble(rev, 0);
    if (double.IsNaN(v) || double.IsInfinity(v)) return null;
    return v;
  }

  static double? PeakArea(byte[] data, string name) {
    byte[] pat = Encoding.ASCII.GetBytes(name);
    for (int i = 0; i + pat.Length + 88 <= data.Length; i++) {
      bool ok = true;
      for (int j = 0; j < pat.Length; j++) if (data[i + j] != pat[j]) { ok = false; break; }
      if (!ok) continue;
      int pad = (4 - (pat.Length % 4)) % 4;
      double? area = BeDouble(data, i + pat.Length + pad + 80);
      if (!area.HasValue || area.Value < 100 || area.Value > 1e9) return null;
      return area;
    }
    return null;
  }

  static uint Acquired(byte[] data) {
    var r = new Rst();
    r.data = data;
    r.i = 0;
    var mag = r.Need(4);
    if (mag[0] != (byte)'P') throw new Exception("not rst");
    for (int k = 0; k < 6; k++) r.U32b();
    r.Str(); r.Str(); r.Str(); r.Str();
    r.Ts();
    r.Str(); r.Str();
    r.Ts();
    r.Str();
    r.U32b(); r.U32b();
    r.Str(); r.Str();
    r.Need(8); r.Need(8); r.Need(8); r.Need(8);
    r.U32b();
    r.Str(); r.Str();
    r.U32b();
    return r.Ts();
  }

  static int RunNo(string name) {
    int us = name.IndexOf('_');
    if (us < 0) return 0;
    int end = us + 1;
    while (end < name.Length && char.IsDigit(name[end])) end++;
    int n;
    return int.TryParse(name.Substring(us + 1, end - us - 1), out n) ? n : 0;
  }

  static string Friendly(string name) {
    switch (name) {
      case "Loop1.Process Value": return "Reactor";
      case "Loop1.Working SetPoint": return "Reactor setpoint";
      case "Loop1.Output": return "Reactor output";
      case "Loop2.Process Value": return "Loop 2 temperature";
      case "Loop2.Working SetPoint": return "Loop 2 setpoint";
      case "Loop2.Output": return "Loop 2 output";
      case "Loop3.Process Value": return "Loop 3 temperature";
      case "Loop3.Working SetPoint": return "Loop 3 setpoint";
      case "Loop3.Output": return "Loop 3 output";
      case "Loop4.Process Value": return "reactor pressure";
      case "Loop4.Working SetPoint": return "Pressure setpoint";
      case "Loop4.Output": return "Pressure output";
      case "Loop6.Process Value": return "Loop 6 temperature";
      case "Tags500-.Tag 15": return "Tag 15";
      case "Tags500-.Tag 14": return "Tag 14";
      case "Tags500-.Tag 11": return "Tag 11";
      case "Tags500-.Tag 16": return "Tag 16";
      case "AIs-.Module5 Inp1": return "Flow 2";
      case "AIs-.Module5 Inp2": return "inlet flow";
      case "AIs-.Module5 Inp3": return "Pressure feedback";
      case "AIs-.Module5 Inp4": return "Module 5 input 4";
      case "AIs-.Module5 Inp5": return "Module 5 input 5";
      case "AIs-.Module5 Inp6": return "Module 5 input 6";
      case "AIs-.Module5 Inp7": return "Module 5 input 7";
      case "AIs-.Module5 Inp8": return "Module 5 input 8";
      case "AIs-.Module4 Inp3": return "Module 4 input 3";
      default: return name;
    }
  }

  static void Classify(Series s) {
    string n = s.Name;
    s.Dashed = n.IndexOf("setpoint", StringComparison.OrdinalIgnoreCase) >= 0;
    if (n.IndexOf("setpoint", StringComparison.OrdinalIgnoreCase) >= 0) {
      if (n.IndexOf("ressure", StringComparison.OrdinalIgnoreCase) >= 0) { s.Group = "pressure"; s.Unit = ""; }
      else { s.Group = "temp"; s.Unit = "°C"; }
      return;
    }
    if (n.IndexOf("output", StringComparison.OrdinalIgnoreCase) >= 0) {
      s.Group = "output"; s.Unit = "%"; return;
    }
    if (n.StartsWith("Module 5 input", StringComparison.Ordinal)) {
      s.Group = "temp"; s.Unit = "°C"; return;
    }
    if (n.Equals("Reactor", StringComparison.OrdinalIgnoreCase)) {
      s.Group = "temp"; s.Unit = "°C"; return;
    }
    if (n.IndexOf("Inlet", StringComparison.OrdinalIgnoreCase) >= 0) {
      s.Group = "inlet"; s.Unit = "SCCM"; return;
    }
    if (n.IndexOf("Flow 2", StringComparison.OrdinalIgnoreCase) >= 0 ||
        n.IndexOf("Outlet mass", StringComparison.OrdinalIgnoreCase) >= 0 ||
        n.IndexOf("Outlet volumetric", StringComparison.OrdinalIgnoreCase) >= 0) {
      s.Group = "flow"; s.Unit = "SLPM"; return;
    }
    if (n.IndexOf("Outlet meter pressure", StringComparison.OrdinalIgnoreCase) >= 0) {
      s.Group = "meter"; s.Unit = "psia"; return;
    }
    if (n.IndexOf("Outlet meter temperature", StringComparison.OrdinalIgnoreCase) >= 0 ||
        n.IndexOf("temperature", StringComparison.OrdinalIgnoreCase) >= 0 ||
        n.StartsWith("Tag 15") || n.StartsWith("Tag 14") || n.StartsWith("Tag 11")) {
      s.Group = "temp"; s.Unit = "°C"; return;
    }
    if (n.IndexOf("ressure", StringComparison.OrdinalIgnoreCase) >= 0) {
      s.Group = "pressure"; s.Unit = ""; return;
    }
    s.Group = "other"; s.Unit = "";
  }

  static bool Same(List<double?> a, List<double?> b) {
    if (a.Count != b.Count) return false;
    for (int i = 0; i < a.Count; i++) {
      if (a[i].HasValue != b[i].HasValue) return false;
      if (a[i].HasValue && Math.Abs(a[i].Value - b[i].Value) > 1.0) return false;
    }
    return true;
  }

  static double? Clean(string name, float value) {
    if (float.IsNaN(value) || float.IsInfinity(value)) return null;
    if (name == "AIs-.Module5 Inp2" && (value < -20 || value > 5000)) return null;
    return value;
  }

  class SvSample {
    public long T;
    public int Ord;
    public Dictionary<string, double?> Values = new Dictionary<string, double?>();
  }

  static List<string> SpecViewLogs(string dir) {
    var files = new List<string>();
    if (!Directory.Exists(dir)) return files;
    foreach (string f in Directory.GetFiles(dir)) {
      // Run names change. Match *.LOG, never one file name.
      if (Path.GetExtension(f).Equals(".LOG", StringComparison.OrdinalIgnoreCase)) files.Add(f);
    }
    files.Sort(StringComparer.OrdinalIgnoreCase);
    return files;
  }

  static List<SvSample> ReadSpecViewLog(byte[] data, bool clean, int ordBase) {
    var samples = new List<SvSample>();
    int pos = 0;
    while (pos + 8 <= data.Length && !(data[pos] == 0x09 && Valid(data, pos) > 0)) pos++;
    var idName = new Dictionary<uint, string>();
    while (pos + 8 <= data.Length && !BlockHeader(data, pos)) {
      int length = Valid(data, pos);
      if (length == 0) { pos++; continue; }
      if (data[pos] == 0x09 && length >= 13) {
        uint ident = U32(data, pos + 4);
        string name = Encoding.GetEncoding(28591).GetString(data, pos + 12, length - 12).Split('\0')[0].Trim(' ', ';');
        if (name.Length > 0 && !idName.ContainsKey(ident)) idName[ident] = name;
      }
      pos += length;
    }
    while (pos + 8 <= data.Length && BlockHeader(data, pos)) {
      uint ts = U32(data, pos + 4);
      pos += 8;
      if (pos + 4 <= data.Length && data[pos] == 0x08 && data[pos + 1] == 0x00 && data[pos + 2] == 0x04 && data[pos + 3] == 0x00) pos += 4;
      var sample = new SvSample();
      sample.T = ts;
      sample.Ord = ordBase + samples.Count;
      while (pos < data.Length && !BlockHeader(data, pos)) {
        int length = Valid(data, pos);
        if (length == 0) { pos = data.Length; break; }
        if (data[pos] == 0x03 && length >= 12) {
          uint ident = U32(data, pos + 4);
          string name;
          if (idName.TryGetValue(ident, out name)) {
            float raw = BitConverter.ToSingle(data, pos + 8);
            if (float.IsNaN(raw) || float.IsInfinity(raw)) sample.Values[name] = null;
            else sample.Values[name] = clean ? Clean(name, raw) : (double)raw;
          }
        }
        pos += length;
      }
      samples.Add(sample);
    }
    return samples;
  }

  static List<SvSample> ReadAllSpecViewLogs(string dir, bool clean) {
    var all = new List<SvSample>();
    int ord = 0;
    foreach (string f in SpecViewLogs(dir)) {
      try {
        var part = ReadSpecViewLog(ReadShare(f), clean, ord);
        ord += part.Count + 1;
        all.AddRange(part);
      } catch { }
    }
    all.Sort((a, b) => {
      int c = a.T.CompareTo(b.T);
      return c != 0 ? c : a.Ord.CompareTo(b.Ord);
    });
    return all;
  }

  class TimedValues {
    public long T;
    public Dictionary<string, double> Values = new Dictionary<string, double>();
  }

  class ColePoint {
    public long T;
    public double P, C, V, M;
  }

  // Names change every run. Match the prefix and extension, never one date.
  static List<string> FilesByPrefix(string dir, string prefix, string extension) {
    var files = new List<string>();
    if (!Directory.Exists(dir)) return files;
    foreach (string f in Directory.GetFiles(dir)) {
      string name = Path.GetFileName(f);
      if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
          && Path.GetExtension(name).Equals(extension, StringComparison.OrdinalIgnoreCase))
        files.Add(f);
    }
    files.Sort(StringComparer.OrdinalIgnoreCase);
    return files;
  }

  // "PuTTY log 2026.11.21 12:40:24" — the numbers come from the file.
  static bool TryLogClock(string line, out DateTime t0) {
    t0 = default(DateTime);
    if (line == null || !line.StartsWith("=~", StringComparison.Ordinal)) return false;
    int mark = line.IndexOf("log ", StringComparison.Ordinal);
    if (mark < 0) return false;
    string[] bits = line.Substring(mark + 4).Split(new[] { ' ', '.', ':', '=' }, StringSplitOptions.RemoveEmptyEntries);
    if (bits.Length < 6) return false;
    int y, mo, d, h, mi, s;
    if (!int.TryParse(bits[0], out y) || !int.TryParse(bits[1], out mo) || !int.TryParse(bits[2], out d)
        || !int.TryParse(bits[3], out h) || !int.TryParse(bits[4], out mi) || !int.TryParse(bits[5], out s)) return false;
    try { t0 = new DateTime(y, mo, d, h, mi, s); }
    catch (ArgumentOutOfRangeException) { return false; }
    return true;
  }

  static void ReadColeLogs(string folder, List<long> t, List<double> p, List<double> c, List<double> v, List<double> m) {
    var pts = new List<ColePoint>();
    foreach (string f in FilesByPrefix(folder, "flowlog-", ".log")) {
      string text;
      try {
        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var sr = new StreamReader(fs, Encoding.ASCII)) text = sr.ReadToEnd();
      } catch { continue; }
      DateTime? t0 = null;
      int seq = 0;
      foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
        DateTime clock;
        if (TryLogClock(line, out clock)) { t0 = clock; seq = 0; continue; }
        if (!t0.HasValue) continue;
        string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) continue;
        if (!parts[parts.Length - 1].Equals("N2", StringComparison.OrdinalIgnoreCase)) continue;
        double pv, cv, vv, mv;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out pv)) continue;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out cv)) continue;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vv)) continue;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out mv)) continue;
        pts.Add(new ColePoint {
          T = (long)(t0.Value - Epoch).TotalSeconds + seq,
          P = pv, C = cv, V = vv, M = mv
        });
        seq++;
      }
    }
    pts.Sort((a, b) => a.T.CompareTo(b.T));
    foreach (var pt in pts) {
      t.Add(pt.T); p.Add(pt.P); c.Add(pt.C); v.Add(pt.V); m.Add(pt.M);
    }
  }

  // livelog.csv lines are "Time,HH:mm:ss" with no date. The date is the last
  // *.LOG sample. A clock that jumps backwards has crossed midnight.
  static List<TimedValues> ReadLiveTail(string path, long lastTs) {
    var rows = new List<TimedValues>();
    if (!File.Exists(path)) return rows;
    DateTime day;
    long cursor;
    if (lastTs > 0) {
      day = Epoch.AddSeconds(lastTs);
      cursor = lastTs;
    } else {
      DateTime wrote = File.GetLastWriteTime(path);
      day = new DateTime(wrote.Year, wrote.Month, wrote.Day);
      cursor = long.MinValue;
    }
    TimeSpan prevClock = TimeSpan.Zero;
    bool haveClock = false;
    string text;
    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    using (var sr = new StreamReader(fs, Encoding.ASCII)) text = sr.ReadToEnd();
    foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
      int at = rawLine.IndexOf("Time,", StringComparison.Ordinal);
      if (at < 0) continue;
      string line = at > 0 ? rawLine.Substring(at) : rawLine;
      string[] parts = line.Split(',');
      if (parts.Length < 4 || parts[0] != "Time") continue;
      DateTime clock;
      if (!DateTime.TryParseExact(parts[1], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out clock)
          && !DateTime.TryParseExact(parts[1], "H:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out clock)) continue;
      if (haveClock && clock.TimeOfDay < prevClock) day = day.AddDays(1);
      prevClock = clock.TimeOfDay;
      haveClock = true;
      var wall = new DateTime(day.Year, day.Month, day.Day, clock.Hour, clock.Minute, clock.Second);
      long sec = (long)(wall - Epoch).TotalSeconds;
      if (sec <= cursor) continue;
      var sample = new Dictionary<string, double>();
      for (int i = 2; i + 1 < parts.Length; i += 2) {
        double dv;
        if (!double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) continue;
        sample[parts[i]] = dv;
      }
      rows.Add(new TimedValues { T = sec, Values = sample });
      cursor = sec;
      day = wall;
    }
    return rows;
  }

  public static string BuildJson() {
    string svDir = SpecViewDir();
    string livePath = Path.Combine(svDir, "livelog.csv");
    string coleFolder = ColeDir();
    string gcFolder = GcDir();

    var samples = ReadAllSpecViewLogs(svDir, true);
    if (samples.Count == 0) throw new Exception("no specview samples");

    var times = new List<long>();
    var names = new List<string>();
    var seen = new Dictionary<string, bool>();
    foreach (var sample in samples) {
      foreach (var kv in sample.Values) {
        if (!seen.ContainsKey(kv.Key)) { seen[kv.Key] = true; names.Add(kv.Key); }
      }
    }
    var cols = new Dictionary<string, List<double?>>();
    foreach (string name in names) cols[name] = new List<double?>();
    foreach (var sample in samples) {
      times.Add(sample.T);
      foreach (string name in names) {
        double? v;
        cols[name].Add(sample.Values.TryGetValue(name, out v) ? v : null);
      }
    }

    // Drop channels that never move.
    var keep = new List<string>();
    foreach (var kv in cols) {
      double? lo = null, hi = null;
      int n = 0;
      foreach (var v in kv.Value) {
        if (!v.HasValue) continue;
        n++;
        if (!lo.HasValue || v.Value < lo.Value) lo = v.Value;
        if (!hi.HasValue || v.Value > hi.Value) hi = v.Value;
      }
      if (n > 10 && hi.HasValue && lo.HasValue && (hi.Value - lo.Value) > 0.2) keep.Add(kv.Key);
    }

    // Cole-Parmer / Alicat serial log, one line per second.
    var coleT = new List<long>();
    var coleP = new List<double>();
    var coleC = new List<double>();
    var coleV = new List<double>();
    var coleM = new List<double>();
    ReadColeLogs(coleFolder, coleT, coleP, coleC, coleV, coleM);

    Func<List<double>, List<double?>> sampleCole = (src) => {
      var y = new List<double?>(times.Count);
      int j = 0;
      for (int i = 0; i < times.Count; i++) {
        while (j + 1 < coleT.Count && coleT[j + 1] <= times[i]) j++;
        if (coleT.Count == 0 || coleT[j] > times[i] || times[i] - coleT[j] > 30) y.Add(null);
        else y.Add(src[j]);
      }
      return y;
    };

    var series = new List<Series>();
    foreach (string key in keep) {
      var s = new Series();
      s.Name = Friendly(key);
      s.Y = cols[key];
      Classify(s);
      if (s.Name == "Pressure feedback" || s.Name == "Loop 6 temperature" || s.Name == "Module 5 input 4" || s.Name == "Module 5 input 5" || s.Name == "Tag 16" || s.Name == "Module 4 input 3") continue;
      if (s.Group == "temp") {
        bool over = false;
        foreach (var v in s.Y) {
          if (v.HasValue && v.Value > 400) { over = true; break; }
        }
        if (over) continue;
      }
      series.Add(s);
    }
    // Live tail is applied after duplicate channels are merged.
    var liveTail = new List<KeyValuePair<long, Dictionary<string, double>>>();
    var map = new Dictionary<string, string> {
      {"L1PV", "Reactor"},
      {"L1SP", "Reactor setpoint"},
      {"L1Out", "Reactor output"},
      {"Flow1", "inlet flow"},
      {"Flow2", "Flow 2"},
      {"PressFB", "Pressure feedback"},
      {"L4PV", "reactor pressure"},
      {"L4SP", "Pressure setpoint"},
      {"L4Out", "Pressure output"}
    };
    foreach (var row in ReadLiveTail(livePath, times[times.Count - 1])) {
      var sample = new Dictionary<string, double>();
      foreach (var kv in row.Values) {
        string dest;
        if (!map.TryGetValue(kv.Key, out dest)) continue;
        if (dest == "inlet flow" && (kv.Value < -20 || kv.Value > 5000)) continue;
        sample[dest] = kv.Value;
      }
      liveTail.Add(new KeyValuePair<long, Dictionary<string, double>>(row.T, sample));
    }

    // Collapse channels that are the same numbers under two SpecView names.
    var merged = new List<Series>();
    var used = new bool[series.Count];
    for (int i = 0; i < series.Count; i++) {
      if (used[i]) continue;
      used[i] = true;
      string name = series[i].Name;
      for (int k = i + 1; k < series.Count; k++) {
        if (used[k]) continue;
        if (!Same(series[i].Y, series[k].Y)) continue;
        used[k] = true;
        if (name.IndexOf(series[k].Name, StringComparison.Ordinal) < 0)
          name = name + " / " + series[k].Name;
      }
      series[i].Name = name;
      merged.Add(series[i]);
    }

    foreach (var tail in liveTail) {
      times.Add(tail.Key);
      foreach (var ser in merged) {
        bool hit = false;
        foreach (var kv in tail.Value) {
          if (ser.Name == kv.Key || ser.Name.StartsWith(kv.Key + " /", StringComparison.Ordinal)) {
            ser.Y.Add(kv.Value);
            hit = true;
            break;
          }
        }
        if (!hit) ser.Y.Add(null);
      }
    }
    if (coleT.Count > 0) {
      var defs = new[] {
        new Series { Name = "Outlet volumetric flow", Y = sampleCole(coleV) },
        new Series { Name = "Outlet mass flow", Y = sampleCole(coleM) }
      };
      foreach (var s in defs) { Classify(s); merged.Add(s); }
    }

    var injections = new List<long>();
    var injNo = new List<int>();
    var injCo = new List<double?>();
    var injCo2 = new List<double?>();
    var injCh4 = new List<double?>();
    if (Directory.Exists(gcFolder)) {
      var files = new List<string>(Directory.GetFiles(gcFolder, "datafile_*.rst"));
      files.Sort((a, b) => RunNo(Path.GetFileName(a)).CompareTo(RunNo(Path.GetFileName(b))));
      foreach (string f in files) {
        try {
          byte[] rst = ReadShare(f);
          uint acq = Acquired(rst);
          long wall = (long)acq - EstOffset;
          if (wall < times[0] - 3600 || wall > times[times.Count - 1] + 3600) continue;
          injections.Add(wall);
          injNo.Add(RunNo(Path.GetFileName(f)));
          injCo.Add(PeakArea(rst, "Carbon Monoxide"));
          injCo2.Add(PeakArea(rst, "Carbon Dioxide"));
          injCh4.Add(PeakArea(rst, "Methane"));
        } catch { }
      }
    }

    long origin = times[0];
    var sb = new StringBuilder(times.Count * merged.Count * 4);
    sb.Append("{\"start\":\"").Append(Epoch.AddSeconds(origin).ToString("yyyy-MM-ddTHH:mm:ss")).Append("\"");
    sb.Append(",\"updated\":\"").Append(Epoch.AddSeconds(times[times.Count - 1]).ToString("yyyy-MM-ddTHH:mm:ss")).Append("\"");
    sb.Append(",\"samples\":").Append(times.Count);
    sb.Append(",\"coleSamples\":").Append(coleT.Count);
    sb.Append(",\"times\":[");
    for (int i = 0; i < times.Count; i++) {
      if (i > 0) sb.Append(',');
      sb.Append(times[i] - origin);
    }
    sb.Append("],\"injections\":[");
    for (int i = 0; i < injections.Count; i++) {
      if (i > 0) sb.Append(',');
      sb.Append("{\"n\":").Append(injNo[i]).Append(",\"t\":").Append(injections[i] - origin);
      sb.Append(",\"co\":").Append(injCo[i].HasValue ? injCo[i].Value.ToString("0", CultureInfo.InvariantCulture) : "null");
      sb.Append(",\"co2\":").Append(injCo2[i].HasValue ? injCo2[i].Value.ToString("0", CultureInfo.InvariantCulture) : "null");
      sb.Append(",\"ch4\":").Append(injCh4[i].HasValue ? injCh4[i].Value.ToString("0", CultureInfo.InvariantCulture) : "null");
      sb.Append("}");
    }
    sb.Append("],\"series\":[");
    for (int s = 0; s < merged.Count; s++) {
      if (s > 0) sb.Append(',');
      var ser = merged[s];
      sb.Append("{\"name\":\"").Append(ser.Name.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"");
      sb.Append(",\"group\":\"").Append(ser.Group).Append("\"");
      sb.Append(",\"unit\":\"").Append(ser.Unit).Append("\"");
      sb.Append(",\"dash\":").Append(ser.Dashed ? "true" : "false");
      sb.Append(",\"y\":[");
      for (int i = 0; i < ser.Y.Count; i++) {
        if (i > 0) sb.Append(',');
        if (!ser.Y[i].HasValue) sb.Append("null");
        else sb.Append(ser.Y[i].Value.ToString("0.###", CultureInfo.InvariantCulture));
      }
      sb.Append("]}");
    }
    sb.Append("]}");
    return sb.ToString();
  }

  static double? NumText(string s) {
    if (string.IsNullOrWhiteSpace(s)) return null;
    double v;
    if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return null;
    if (double.IsNaN(v) || double.IsInfinity(v)) return null;
    return v;
  }

  static double? PeakAreaBest(byte[] data, string name) {
    byte[] pat = Encoding.ASCII.GetBytes(name);
    double? best = null;
    for (int i = 0; i + pat.Length + 88 <= data.Length; i++) {
      bool ok = true;
      for (int j = 0; j < pat.Length; j++) if (data[i + j] != pat[j]) { ok = false; break; }
      if (!ok) continue;
      int pad = (4 - (pat.Length % 4)) % 4;
      double? area = BeDouble(data, i + pat.Length + pad + 80);
      if (!area.HasValue || area.Value < 100 || area.Value > 1e9) continue;
      if (!best.HasValue || area.Value > best.Value) best = area;
    }
    return best;
  }

  static double Median(List<double> xs) {
    xs.Sort();
    int n = xs.Count;
    if ((n % 2) == 1) return xs[n / 2];
    return (xs[n / 2 - 1] + xs[n / 2]) / 2.0;
  }

  static string Csv(string s) {
    if (s == null) return "";
    if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
    return s;
  }

  static string Num2(double? v) {
    if (!v.HasValue) return "";
    if (Math.Abs(v.Value) < 0.005) return "0.00";
    return v.Value.ToString("0.00", CultureInfo.InvariantCulture);
  }

  static string Mol(double? area, double? slope, double? intercept) {
    if (!area.HasValue || !slope.HasValue || slope.Value == 0) return "";
    double b = intercept.HasValue ? intercept.Value : 0;
    return ((area.Value - b) / slope.Value).ToString("0.###", CultureInfo.InvariantCulture);
  }

  // Nearest SpecView sample. Blank beyond 30 seconds. Inlet flow is blank when it is negative or 9000 or higher.
  static void BedAt(List<BedSample> bed, long t, out double? temp, out double? press, out double? inlet) {
    temp = null;
    press = null;
    inlet = null;
    long bestGap = long.MaxValue;
    long bestT = 0;
    bool have = false;
    for (int j = 0; j < bed.Count; j++) {
      long gap = Math.Abs(bed[j].T - t);
      if (gap < bestGap || (gap == bestGap && bed[j].T < bestT)) {
        bestGap = gap;
        bestT = bed[j].T;
        temp = bed[j].Temp;
        press = bed[j].Press;
        inlet = bed[j].Inlet;
        have = true;
      }
    }
    if (!have || bestGap > 30) { temp = null; press = null; inlet = null; }
    else if (inlet.HasValue && (inlet.Value >= 9000 || inlet.Value < 0)) inlet = null;
  }

  class BedSample {
    public long T;
    public double? Temp;
    public double? Press;
    public double? Inlet;
  }

  static double FeedPct(string s) {
    double v;
    if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return 0;
    return v;
  }

  static double MolValue(string cell, ref bool any) {
    double v;
    if (!double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return 0;
    any = true;
    return v;
  }

  // SpecView stamps are already lab wall clock. TotalChrom acquisition is real UTC.
  // Feed percents are CO, CO2, and CH4 on the calibration sheet. Inlet K is 1.
  static List<string[]> TableRows(string h2m, string h2b, string com, string cob, string co2m, string co2b, string ch4m, string ch4b, string feedCo, string feedCo2, string feedCh4, out List<string> head) {
    double? mH2 = NumText(h2m), bH2 = NumText(h2b);
    double yFeed = (FeedPct(feedCo) + FeedPct(feedCo2) + FeedPct(feedCh4)) / 100.0;
    double? mCo = NumText(com), bCo = NumText(cob);
    double? mCo2 = NumText(co2m), bCo2 = NumText(co2b);
    double? mCh4 = NumText(ch4m), bCh4 = NumText(ch4b);

    string svDir = SpecViewDir();
    string livePath = Path.Combine(svDir, "livelog.csv");
    string coleFolder = ColeDir();
    string gcFolder = GcDir();

    var bed = new List<BedSample>();
    foreach (var sample in ReadAllSpecViewLogs(svDir, false)) {
      double? temp = null, press = null, inlet = null;
      double? v;
      bool any = false;
      if (sample.Values.TryGetValue("Loop1.Process Value", out v) && v.HasValue) { temp = v; any = true; }
      if (sample.Values.TryGetValue("Loop4.Process Value", out v) && v.HasValue) { press = v; any = true; }
      if (sample.Values.TryGetValue("AIs-.Module5 Inp2", out v) && v.HasValue) { inlet = v; any = true; }
      if (any) bed.Add(new BedSample { T = sample.T, Temp = temp, Press = press, Inlet = inlet });
    }
    foreach (var row in ReadLiveTail(livePath, bed.Count > 0 ? bed[bed.Count - 1].T : 0)) {
      double? temp = null, press = null, inlet = null;
      double dv;
      if (row.Values.TryGetValue("L1PV", out dv)) temp = dv;
      if (row.Values.TryGetValue("L4PV", out dv)) press = dv;
      if (row.Values.TryGetValue("Flow1", out dv)) inlet = dv;
      bed.Add(new BedSample { T = row.T, Temp = temp, Press = press, Inlet = inlet });
    }

    var coleT = new List<long>();
    var coleP = new List<double>();
    var coleC = new List<double>();
    var coleV = new List<double>();
    var coleM = new List<double>();
    ReadColeLogs(coleFolder, coleT, coleP, coleC, coleV, coleM);

    var rows = new List<string[]>();
    if (Directory.Exists(gcFolder)) {
      var files = new List<string>(Directory.GetFiles(gcFolder, "datafile_*.rst"));
      files.Sort((a, b) => RunNo(Path.GetFileName(a)).CompareTo(RunNo(Path.GetFileName(b))));
      foreach (string f in files) {
        FileInfo info = new FileInfo(f);
        if (info.Length < 1000) continue;
        byte[] rst;
        uint acq;
        try {
          rst = ReadShare(f);
          acq = Acquired(rst);
        } catch { continue; }
        long wall = (long)acq - EstOffset;
        double? h2 = PeakAreaBest(rst, "Hydrogen");
        double? co = PeakAreaBest(rst, "Carbon Monoxide");
        double? co2 = PeakAreaBest(rst, "Carbon Dioxide");
        double? ch4 = PeakAreaBest(rst, "Methane");

        int nCole = 0;
        var winP = new List<double>();
        var winC = new List<double>();
        var winV = new List<double>();
        var winM = new List<double>();
        for (int j = 0; j < coleT.Count; j++) {
          if (coleT[j] < wall - 30) continue;
          if (coleT[j] > wall + 30) break;
          nCole++;
          winP.Add(coleP[j]); winC.Add(coleC[j]); winV.Add(coleV[j]); winM.Add(coleM[j]);
        }
        string pCell = "", cCell = "", vCell = "", mCell = "";
        if (nCole >= 10) {
          pCell = Num2(Median(winP));
          cCell = Num2(Median(winC));
          vCell = Num2(Median(winV));
          mCell = Num2(Median(winM));
        }

        double? temp, press, inlet;
        BedAt(bed, wall, out temp, out press, out inlet);

        // 1 L from the reactor outlet to the GC detector. Transit time uses the volumetric flow written on this row.
        string deadCell = "", transitCell = "", leftCell = "";
        string tempExit = "", pressExit = "", inletExit = "";
        double qShown;
        if (double.TryParse(vCell, NumberStyles.Float, CultureInfo.InvariantCulture, out qShown) && qShown > 0) {
          double transitMin = 1.0 / qShown;
          long transitSec = (long)Math.Round(transitMin * 60.0, MidpointRounding.AwayFromZero);
          long left = wall - transitSec;
          double? tempX, pressX, inletX;
          BedAt(bed, left, out tempX, out pressX, out inletX);
          deadCell = "1";
          transitCell = transitMin.ToString("0.000", CultureInfo.InvariantCulture);
          leftCell = Epoch.AddSeconds(left).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
          tempExit = Num2(tempX);
          pressExit = Num2(pressX);
          inletExit = Num2(inletX);
        }

        string molH2 = mH2.HasValue ? Mol(h2, mH2, bH2) : "";
        string molCo = mCo.HasValue ? Mol(co, mCo, bCo) : "";
        string molCo2 = mCo2.HasValue ? Mol(co2, mCo2, bCo2) : "";
        string molCh4 = mCh4.HasValue ? Mol(ch4, mCh4, bCh4) : "";
        bool anyCarbon = false;
        double yCarbon = MolValue(molCo, ref anyCarbon) + MolValue(molCo2, ref anyCarbon) + MolValue(molCh4, ref anyCarbon);
        string carbonOut = anyCarbon ? yCarbon.ToString("0.###", CultureInfo.InvariantCulture) : "";
        string xCell = "";
        double fInSccm, fOut;
        if (anyCarbon && yFeed > 0
            && double.TryParse(inletExit, NumberStyles.Float, CultureInfo.InvariantCulture, out fInSccm) && fInSccm > 0
            && double.TryParse(mCell, NumberStyles.Float, CultureInfo.InvariantCulture, out fOut)) {
          double fIn = fInSccm / 1000.0;
          double x = 100.0 * (fIn * yFeed - fOut * yCarbon / 100.0) / (fIn * yFeed);
          xCell = x.ToString("0.###", CultureInfo.InvariantCulture);
        }

        string when = Epoch.AddSeconds(wall).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string AreaH2 = h2.HasValue ? h2.Value.ToString("0", CultureInfo.InvariantCulture) : "0";
        string AreaCo = co.HasValue ? co.Value.ToString("0", CultureInfo.InvariantCulture) : "0";
        string AreaCo2 = co2.HasValue ? co2.Value.ToString("0", CultureInfo.InvariantCulture) : "0";
        string AreaCh4 = ch4.HasValue ? ch4.Value.ToString("0", CultureInfo.InvariantCulture) : "0";
        rows.Add(new[] {
          when,
          Path.GetFileName(f),
          AreaH2, AreaCo, AreaCo2, AreaCh4,
          pCell, cCell, vCell, mCell, nCole.ToString(CultureInfo.InvariantCulture),
          Num2(temp), Num2(press), Num2(inlet),
          deadCell, transitCell, leftCell, tempExit, pressExit, inletExit,
          molH2, molCo, molCo2, molCh4,
          carbonOut, xCell
        });
      }
    }

    head = new List<string> {
      "Injection Time [EST]", "file",
      "h2_area (counts)", "co_area (counts)", "co2_area (counts)", "ch4_area (counts)",
      "Product gas meter pressure (psia)", "Product gas meter temperature (°C)",
      "Product gas volumetric flow (LPM)", "Product gas mass flow (SLPM, N2)",
      "Product gas meter samples (count)",
      "Reactor (°C)", "reactor pressure (psi)", "inlet flow (SCCM)",
      "Dead volume (L)", "Transit time (min)", "Gas left reactor [EST]",
      "Reactor at exit (°C)", "reactor pressure at exit (psi)", "inlet flow at exit (SCCM)"
    };
    head.Add(mH2.HasValue ? "H2 (mol%)" : "H2 area (counts)");
    head.Add(mCo.HasValue ? "CO (mol%)" : "CO area (counts)");
    head.Add(mCo2.HasValue ? "CO2 (mol%)" : "CO2 area (counts)");
    head.Add(mCh4.HasValue ? "CH4 (mol%)" : "CH4 area (counts)");
    head.Add("Carbon out (mol%)");
    head.Add("Carbon conversion (%)");
    return rows;
  }

  public static string BuildTable(string h2m, string h2b, string com, string cob, string co2m, string co2b, string ch4m, string ch4b, string feedCo, string feedCo2, string feedCh4) {
    List<string> head;
    var rows = TableRows(h2m, h2b, com, cob, co2m, co2b, ch4m, ch4b, feedCo, feedCo2, feedCh4, out head);
    return FormatTable(rows, head, NumText(h2m).HasValue, NumText(com).HasValue, NumText(co2m).HasValue, NumText(ch4m).HasValue);
  }

  static string FormatTable(List<string[]> rows, List<string> head, bool hasH2, bool hasCo, bool hasCo2, bool hasCh4) {
    var sb = new StringBuilder();
    for (int i = 0; i < head.Count; i++) {
      if (i > 0) sb.Append(',');
      sb.Append(Csv(head[i]));
    }
    sb.Append("\r\n");
    foreach (var row in rows) {
      for (int i = 0; i < row.Length; i++) {
        if (i > 0) sb.Append(',');
        string cell = row[i];
        if (!hasH2 && i == 20) cell = row[2];
        if (!hasCo && i == 21) cell = row[3];
        if (!hasCo2 && i == 22) cell = row[4];
        if (!hasCh4 && i == 23) cell = row[5];
        sb.Append(Csv(cell));
      }
      sb.Append("\r\n");
    }
    return sb.ToString();
  }

  public static string BuildPoints(string h2m, string h2b, string com, string cob, string co2m, string co2b, string ch4m, string ch4b, string feedCo, string feedCo2, string feedCh4) {
    List<string> head;
    var rows = TableRows(h2m, h2b, com, cob, co2m, co2b, ch4m, ch4b, feedCo, feedCo2, feedCh4, out head);
    int iWhen = head.IndexOf("Injection Time [EST]");
    int iFile = head.IndexOf("file");
    int iTemp = head.IndexOf("Reactor at exit (°C)");
    int iPress = head.IndexOf("reactor pressure at exit (psi)");
    int iFeed = head.IndexOf("inlet flow at exit (SCCM)");
    int iH2 = head.IndexOf("H2 (mol%)");
    int iCo = head.IndexOf("CO (mol%)");
    int iCo2 = head.IndexOf("CO2 (mol%)");
    int iCh4 = head.IndexOf("CH4 (mol%)");
    int iX = head.IndexOf("Carbon conversion (%)");
    int iMass = head.IndexOf("Product gas mass flow (SLPM, N2)");
    int iCarbon = head.IndexOf("Carbon out (mol%)");
    int iInletNow = head.IndexOf("inlet flow (SCCM)");
    double yFeed = (FeedPct(feedCo) + FeedPct(feedCo2) + FeedPct(feedCh4)) / 100.0;
    var sb = new StringBuilder();
    sb.Append('[');
    bool first = true;
    foreach (var row in rows) {
      if (iWhen < 0 || iFile < 0) continue;
      if (!first) sb.Append(',');
      first = false;
      string inletCell = iFeed >= 0 ? row[iFeed] : "";
      if (string.IsNullOrEmpty(inletCell) && iInletNow >= 0) inletCell = row[iInletNow];
      string cin = "null";
      string cout = "null";
      double fInSccm, fOut, yCarbon;
      if (yFeed > 0 && double.TryParse(inletCell, NumberStyles.Float, CultureInfo.InvariantCulture, out fInSccm) && fInSccm > 0)
        cin = (fInSccm * yFeed).ToString("0.###", CultureInfo.InvariantCulture);
      if (iMass >= 0 && iCarbon >= 0
          && double.TryParse(row[iMass], NumberStyles.Float, CultureInfo.InvariantCulture, out fOut)
          && double.TryParse(row[iCarbon], NumberStyles.Float, CultureInfo.InvariantCulture, out yCarbon))
        cout = (fOut * 10.0 * yCarbon).ToString("0.###", CultureInfo.InvariantCulture);
      string when = row[iWhen].Replace(" ", "T");
      sb.Append("{\"when\":\"").Append(when).Append("\"");
      sb.Append(",\"file\":\"").Append(row[iFile]).Append("\"");
      sb.Append(",\"temp\":").Append(iTemp < 0 || string.IsNullOrEmpty(row[iTemp]) ? "null" : row[iTemp]);
      sb.Append(",\"press\":").Append(iPress < 0 || string.IsNullOrEmpty(row[iPress]) ? "null" : row[iPress]);
      sb.Append(",\"feed\":").Append(iFeed < 0 || string.IsNullOrEmpty(row[iFeed]) ? "null" : row[iFeed]);
      sb.Append(",\"x\":").Append(iX < 0 || string.IsNullOrEmpty(row[iX]) ? "null" : row[iX]);
      sb.Append(",\"h2\":").Append(iH2 < 0 || string.IsNullOrEmpty(row[iH2]) ? "null" : row[iH2]);
      sb.Append(",\"co\":").Append(iCo < 0 || string.IsNullOrEmpty(row[iCo]) ? "null" : row[iCo]);
      sb.Append(",\"co2\":").Append(iCo2 < 0 || string.IsNullOrEmpty(row[iCo2]) ? "null" : row[iCo2]);
      sb.Append(",\"ch4\":").Append(iCh4 < 0 || string.IsNullOrEmpty(row[iCh4]) ? "null" : row[iCh4]);
      sb.Append(",\"cin\":").Append(cin);
      sb.Append(",\"cout\":").Append(cout);
      sb.Append('}');
    }
    sb.Append(']');
    return sb.ToString();
  }
}
