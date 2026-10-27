using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VInc.AvatarHub
{
    /// <summary>0.0.1 ～ 9.9.9 のバージョン。各位は 0～9。</summary>
    [Serializable]
    public struct PkgVersion : IEquatable<PkgVersion>
    {
        public int major;
        public int minor;
        public int patch;

        public static readonly PkgVersion First = new PkgVersion(1, 0, 0);

        public PkgVersion(int major, int minor, int patch)
        {
            this.major = major;
            this.minor = minor;
            this.patch = patch;
            Clamp();
        }

        public int this[int digit] => digit == 0 ? major : digit == 1 ? minor : patch;

        void Clamp()
        {
            major = Math.Max(0, Math.Min(9, major));
            minor = Math.Max(0, Math.Min(9, minor));
            patch = Math.Max(0, Math.Min(9, patch));
            if (major == 0 && minor == 0 && patch == 0) patch = 1;
        }

        /// <summary>
        /// 位ごとの上げ下げ。上げるときは繰り上げ＋下の位を 0 に（0.0.9 → 0.1.0、1.2.5 の真ん中 → 1.3.0）。
        /// 下げるときは繰り下げ（0.1.0 → 0.0.9）。範囲は 0.0.1 ～ 9.9.9。
        /// </summary>
        public PkgVersion Step(int digit, int dir)
        {
            int a = major, b = minor, c = patch;
            if (dir > 0)
            {
                if (a == 9 && b == 9 && c == 9) return this;
                if (digit == 2)
                {
                    if (c < 9) c++;
                    else if (b < 9) { b++; c = 0; }
                    else { a++; b = 0; c = 0; }
                }
                else if (digit == 1)
                {
                    if (b < 9) { b++; c = 0; }
                    else if (a < 9) { a++; b = 0; c = 0; }
                }
                else if (a < 9)
                {
                    a++; b = 0; c = 0;
                }
            }
            else
            {
                if (digit == 2)
                {
                    if (c > 0) c--;
                    else if (b > 0) { b--; c = 9; }
                    else if (a > 0) { a--; b = 9; c = 9; }
                }
                else if (digit == 1)
                {
                    if (b > 0) b--;
                    else if (a > 0) { a--; b = 9; }
                }
                else if (a > 0)
                {
                    a--;
                }
            }
            return new PkgVersion(a, b, c);
        }

        public PkgVersion WithDigit(int digit, int value)
        {
            int a = major, b = minor, c = patch;
            if (digit == 0) a = value;
            else if (digit == 1) b = value;
            else c = value;
            return new PkgVersion(a, b, c);
        }

        static readonly Regex Pattern = new Regex(@"^\s*(?:ver\.?|v)?\s*(\d)\s*\.\s*(\d)\s*\.\s*(\d)\s*$", RegexOptions.IgnoreCase);

        public static bool TryParse(string s, out PkgVersion v)
        {
            v = First;
            if (string.IsNullOrEmpty(s)) return false;
            var m = Pattern.Match(s);
            if (!m.Success) return false;
            v = new PkgVersion(m.Groups[1].Value[0] - '0', m.Groups[2].Value[0] - '0', m.Groups[3].Value[0] - '0');
            return true;
        }

        public override string ToString() => $"{major}.{minor}.{patch}";
        public bool Equals(PkgVersion o) => major == o.major && minor == o.minor && patch == o.patch;
        public override bool Equals(object obj) => obj is PkgVersion o && Equals(o);
        public override int GetHashCode() => major * 100 + minor * 10 + patch;
    }

    /// <summary>エクスポート時に名前へ付ける要素。</summary>
    public struct NameOptions
    {
        public bool useDebug;
        public string debugLabel;
        public bool useVersion;
        public PkgVersion version;
        public bool useDate;
        public bool useTime;
    }

    public static class PackageNaming
    {
        public static string DateText(DateTime t) => t.ToString("yyyy_MM_dd", CultureInfo.InvariantCulture);
        public static string TimeText(DateTime t) => t.ToString("HH_mm", CultureInfo.InvariantCulture);
        public static string VersionText(PkgVersion v) => "Ver" + v;

        /// <summary>
        /// 例: productC / productC_Ver0.0.1 / productC_Ver0.0.1-2026_10_25-18_51 / productC_2026_10_25-18_51
        /// 名前の後ろにつなぐときは必ず「_」、バージョン・日付・時刻のあいだは「-」。
        /// </summary>
        public static string Build(string baseName, NameOptions o, DateTime t)
        {
            var name = (baseName ?? "").Trim();
            if (o.useDebug && !string.IsNullOrWhiteSpace(o.debugLabel)) name += "_" + o.debugLabel.Trim();

            var parts = new List<string>();
            if (o.useVersion) parts.Add(VersionText(o.version));
            if (o.useDate) parts.Add(DateText(t));
            if (o.useTime) parts.Add(TimeText(t));
            if (parts.Count > 0) name += "_" + string.Join("-", parts);
            return ViahStorage.SafeFileName(name);
        }
    }
}
