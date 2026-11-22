using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public static class Trend {
  static readonly DateTime Epoch = new DateTime(1970, 1, 1);
  const long EstOffset = 5 * 3600;

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
      case "Loop1.Process Value": return "Reactor temperature";
      case "Loop1.Working SetPoint": return "Reactor setpoint";
      case "Loop1.Output": return "Reactor output";
      case "Loop2.Process Value": return "Loop 2 temperature";
      case "Loop2.Working SetPoint": return "Loop 2 setpoint";
      case "Loop2.Output": return "Loop 2 output";
      case "Loop3.Process Value": return "Loop 3 temperature";
      case "Loop3.Working SetPoint": return "Loop 3 setpoint";
      case "Loop3.Output": return "Loop 3 output";
      case "Loop4.Process Value": return "Pressure";
      case "Loop4.Working SetPoint": return "Pressure setpoint";
      case "Loop4.Output": return "Pressure output";
      case "Loop6.Process Value": return "Loop 6 temperature";
      case "Tags500-.Tag 15": return "Tag 15";
      case "Tags500-.Tag 14": return "Tag 14";
      case "Tags500-.Tag 11": return "Tag 11";
      case "Tags500-.Tag 16": return "Tag 16";
      case "AIs-.Module5 Inp1": return "Flow 2";
      case "AIs-.Module5 Inp2": return "Inlet flow";
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

  public static string BuildJson() {
    string svDir = @"C:\LabDrop\SpecViewShareddata\Copy Logs";
    string logPath = Path.Combine(svDir, "26_324.LOG");
    string livePath = Path.Combine(svDir, "livelog.csv");
    string coleDir = @"C:\LabDrop\ColePalmerSharedData";
    string gcDir = @"C:\LabDrop\TotalChromSharedData\CopyLogs";

    byte[] data = ReadShare(logPath);
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

    var times = new List<long>();
    var cols = new Dictionary<string, List<double?>>();
    foreach (var kv in idName) cols[kv.Value] = new List<double?>();

    while (pos + 8 <= data.Length && BlockHeader(data, pos)) {
      uint ts = U32(data, pos + 4);
      pos += 8;
      if (pos + 4 <= data.Length && data[pos] == 0x08 && data[pos + 1] == 0x00 && data[pos + 2] == 0x04 && data[pos + 3] == 0x00) pos += 4;
      var sample = new Dictionary<string, double?>();
      while (pos < data.Length && !BlockHeader(data, pos)) {
        int length = Valid(data, pos);
        if (length == 0) { pos = data.Length; break; }
        if (data[pos] == 0x03 && length >= 12) {
          uint ident = U32(data, pos + 4);
          string name;
          if (idName.TryGetValue(ident, out name)) {
            float value = BitConverter.ToSingle(data, pos + 8);
            sample[name] = Clean(name, value);
          }
        }
        pos += length;
      }
      times.Add(ts);
      foreach (var kv in cols) {
        double? v;
        kv.Value.Add(sample.TryGetValue(kv.Key, out v) ? v : null);
      }
    }
    if (times.Count == 0) throw new Exception("no specview samples");

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
    string coleFile = null;
    if (Directory.Exists(coleDir)) {
      foreach (string f in Directory.GetFiles(coleDir, "flowlog-2026-11-21*.log")) coleFile = f;
    }
    if (coleFile != null) {
      string text;
      using (var fs = new FileStream(coleFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
      using (var sr = new StreamReader(fs, Encoding.ASCII)) text = sr.ReadToEnd();
      DateTime? t0 = null;
      int seq = 0;
      string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
      for (int li = 0; li < lines.Length; li++) {
        string line = lines[li];
        if (line.StartsWith("=~")) {
          int mark = line.IndexOf("log ", StringComparison.Ordinal);
          if (mark < 0) continue;
          string stamp = line.Substring(mark + 4);
          int cut = stamp.IndexOf(' ');
          // "2026.11.21 12:40:24"
          string[] bits = stamp.Split(new[] { ' ', '.' , ':' }, StringSplitOptions.RemoveEmptyEntries);
          if (bits.Length >= 6) {
            t0 = new DateTime(int.Parse(bits[0]), int.Parse(bits[1]), int.Parse(bits[2]), int.Parse(bits[3]), int.Parse(bits[4]), int.Parse(bits[5]));
          }
          continue;
        }
        if (!t0.HasValue) continue;
        string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) continue;
        double p, c, v, m;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out p)) continue;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out c)) continue;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out m)) continue;
        long sec = (long)(t0.Value - Epoch).TotalSeconds + seq;
        seq++;
        coleT.Add(sec);
        coleP.Add(p); coleC.Add(c); coleV.Add(v); coleM.Add(m);
      }
    }

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
    if (File.Exists(livePath)) {
      string text;
      using (var fs = new FileStream(livePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
      using (var sr = new StreamReader(fs, Encoding.ASCII)) text = sr.ReadToEnd();
      var map = new Dictionary<string, string> {
        {"L1PV", "Reactor temperature"},
        {"L1SP", "Reactor setpoint"},
        {"L1Out", "Reactor output"},
        {"Flow1", "Inlet flow"},
        {"Flow2", "Flow 2"},
        {"PressFB", "Pressure feedback"},
        {"L4PV", "Pressure"},
        {"L4SP", "Pressure setpoint"},
        {"L4Out", "Pressure output"}
      };
      long lastTs = times[times.Count - 1];
      foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
        int at = rawLine.IndexOf("Time,", StringComparison.Ordinal);
        if (at < 0) continue;
        string line = at > 0 ? rawLine.Substring(at) : rawLine;
        string[] p = line.Split(',');
        if (p.Length < 4 || p[0] != "Time") continue;
        DateTime clock;
        if (!DateTime.TryParseExact(p[1], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out clock)
            && !DateTime.TryParseExact(p[1], "H:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out clock)) continue;
        var wall = new DateTime(2026, 11, 21, clock.Hour, clock.Minute, clock.Second);
        long sec = (long)(wall - Epoch).TotalSeconds;
        if (sec <= lastTs) continue;
        var sample = new Dictionary<string, double>();
        for (int i = 2; i + 1 < p.Length; i += 2) {
          string dest;
          if (!map.TryGetValue(p[i], out dest)) continue;
          double dv;
          if (!double.TryParse(p[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) continue;
          if (dest == "Inlet flow" && (dv < -20 || dv > 5000)) continue;
          sample[dest] = dv;
        }
        liveTail.Add(new KeyValuePair<long, Dictionary<string, double>>(sec, sample));
        lastTs = sec;
      }
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
    if (Directory.Exists(gcDir)) {
      var files = new List<string>(Directory.GetFiles(gcDir, "datafile_*.rst"));
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
    return v.HasValue ? v.Value.ToString("0.00", CultureInfo.InvariantCulture) : "";
  }

  static string Mol(double? area, double? slope, double? intercept) {
    if (!area.HasValue || !slope.HasValue || slope.Value == 0) return "";
    double b = intercept.HasValue ? intercept.Value : 0;
    return ((area.Value - b) / slope.Value).ToString("0.###", CultureInfo.InvariantCulture);
  }

  class BedSample {
    public long T;
    public double? Temp;
    public double? Press;
    public double? Inlet;
  }

  // SpecView stamps are already lab wall clock. TotalChrom acquisition is real UTC.
  public static string BuildTable(string h2m, string h2b, string com, string cob, string co2m, string co2b, string ch4m, string ch4b) {
    double? mH2 = NumText(h2m), bH2 = NumText(h2b);
    double? mCo = NumText(com), bCo = NumText(cob);
    double? mCo2 = NumText(co2m), bCo2 = NumText(co2b);
    double? mCh4 = NumText(ch4m), bCh4 = NumText(ch4b);

    string svDir = @"C:\LabDrop\SpecViewShareddata\Copy Logs";
    string logPath = Path.Combine(svDir, "26_324.LOG");
    string livePath = Path.Combine(svDir, "livelog.csv");
    string coleDir = @"C:\LabDrop\ColePalmerSharedData";
    string gcDir = @"C:\LabDrop\TotalChromSharedData\CopyLogs";

    var bed = new List<BedSample>();
    if (File.Exists(logPath)) {
      byte[] data = ReadShare(logPath);
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
        double? temp = null, press = null, inlet = null;
        bool any = false;
        while (pos < data.Length && !BlockHeader(data, pos)) {
          int length = Valid(data, pos);
          if (length == 0) { pos = data.Length; break; }
          if (data[pos] == 0x03 && length >= 12) {
            uint ident = U32(data, pos + 4);
            string name;
            if (idName.TryGetValue(ident, out name)) {
              float value = BitConverter.ToSingle(data, pos + 8);
              if (!float.IsNaN(value) && !float.IsInfinity(value)) {
                if (name == "Loop1.Process Value") { temp = value; any = true; }
                else if (name == "Loop4.Process Value") { press = value; any = true; }
                else if (name == "AIs-.Module5 Inp2") { inlet = value; any = true; }
              }
            }
          }
          pos += length;
        }
        if (any) bed.Add(new BedSample { T = ts, Temp = temp, Press = press, Inlet = inlet });
      }
    }
    if (File.Exists(livePath)) {
      long lastTs = bed.Count > 0 ? bed[bed.Count - 1].T : 0;
      string text;
      using (var fs = new FileStream(livePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
      using (var sr = new StreamReader(fs, Encoding.ASCII)) text = sr.ReadToEnd();
      foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
        int at = rawLine.IndexOf("Time,", StringComparison.Ordinal);
        if (at < 0) continue;
        string line = at > 0 ? rawLine.Substring(at) : rawLine;
        string[] p = line.Split(',');
        if (p.Length < 4 || p[0] != "Time") continue;
        DateTime clock;
        if (!DateTime.TryParseExact(p[1], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out clock)
            && !DateTime.TryParseExact(p[1], "H:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out clock)) continue;
        var wall = new DateTime(2026, 11, 21, clock.Hour, clock.Minute, clock.Second);
        long sec = (long)(wall - Epoch).TotalSeconds;
        if (sec <= lastTs) continue;
        double? temp = null, press = null, inlet = null;
        for (int i = 2; i + 1 < p.Length; i += 2) {
          double dv;
          if (!double.TryParse(p[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) continue;
          if (p[i] == "L1PV") temp = dv;
          else if (p[i] == "L4PV") press = dv;
          else if (p[i] == "Flow1") inlet = dv;
        }
        bed.Add(new BedSample { T = sec, Temp = temp, Press = press, Inlet = inlet });
        lastTs = sec;
      }
    }

    var coleT = new List<long>();
    var coleP = new List<double>();
    var coleC = new List<double>();
    var coleV = new List<double>();
    var coleM = new List<double>();
    string coleFile = null;
    if (Directory.Exists(coleDir)) {
      foreach (string f in Directory.GetFiles(coleDir, "flowlog-2026-11-21*.log")) coleFile = f;
    }
    if (coleFile != null) {
      string text;
      using (var fs = new FileStream(coleFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
      using (var sr = new StreamReader(fs, Encoding.ASCII)) text = sr.ReadToEnd();
      DateTime? t0 = null;
      int seq = 0;
      foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
        if (line.StartsWith("=~")) {
          int mark = line.IndexOf("log ", StringComparison.Ordinal);
          if (mark < 0) continue;
          string[] bits = line.Substring(mark + 4).Split(new[] { ' ', '.', ':', '=' }, StringSplitOptions.RemoveEmptyEntries);
          if (bits.Length >= 6) {
            int y, mo, d, h, mi, s;
            if (int.TryParse(bits[0], out y) && int.TryParse(bits[1], out mo) && int.TryParse(bits[2], out d)
                && int.TryParse(bits[3], out h) && int.TryParse(bits[4], out mi) && int.TryParse(bits[5], out s)) {
              t0 = new DateTime(y, mo, d, h, mi, s);
            }
          }
          continue;
        }
        if (!t0.HasValue) continue;
        string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) continue;
        if (!parts[parts.Length - 1].Equals("N2", StringComparison.OrdinalIgnoreCase)) continue;
        double p, c, v, m;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out p)) continue;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out c)) continue;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out m)) continue;
        coleT.Add((long)(t0.Value - Epoch).TotalSeconds + seq);
        seq++;
        coleP.Add(p); coleC.Add(c); coleV.Add(v); coleM.Add(m);
      }
    }

    var rows = new List<string[]>();
    if (Directory.Exists(gcDir)) {
      var files = new List<string>(Directory.GetFiles(gcDir, "datafile_*.rst"));
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

        double? temp = null, press = null, inlet = null;
        long bestGap = long.MaxValue;
        long bestT = 0;
        bool have = false;
        for (int j = 0; j < bed.Count; j++) {
          long gap = Math.Abs(bed[j].T - wall);
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
          mH2.HasValue ? Mol(h2, mH2, bH2) : "",
          mCo.HasValue ? Mol(co, mCo, bCo) : "",
          mCo2.HasValue ? Mol(co2, mCo2, bCo2) : "",
          mCh4.HasValue ? Mol(ch4, mCh4, bCh4) : ""
        });
      }
    }

    var head = new List<string> {
      "Injection Time [EST]", "file",
      "h2_area (counts)", "co_area (counts)", "co2_area (counts)", "ch4_area (counts)",
      "Product gas meter pressure (psia)", "Product gas meter temperature (°C)",
      "Product gas volumetric flow (LPM)", "Product gas mass flow (SLPM, N2)",
      "Product gas meter samples (count)",
      "Reactor temperature (°C)", "Reactor pressure (psi)", "Inlet flow 1 (SCCM)"
    };
    head.Add(mH2.HasValue ? "H2 (mol%)" : "H2 area (counts)");
    head.Add(mCo.HasValue ? "CO (mol%)" : "CO area (counts)");
    head.Add(mCo2.HasValue ? "CO2 (mol%)" : "CO2 area (counts)");
    head.Add(mCh4.HasValue ? "CH4 (mol%)" : "CH4 area (counts)");

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
        if (!mH2.HasValue && i == 14) cell = row[2];
        if (!mCo.HasValue && i == 15) cell = row[3];
        if (!mCo2.HasValue && i == 16) cell = row[4];
        if (!mCh4.HasValue && i == 17) cell = row[5];
        sb.Append(Csv(cell));
      }
      sb.Append("\r\n");
    }
    return sb.ToString();
  }
}
