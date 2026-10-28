using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VInc.AvatarHub
{
    /// <summary>unitypackage に入っている 1 つのアセット（またはフォルダー）。</summary>
    public class PackageEntry
    {
        public string guid;
        /// <summary>書き出したときのパス（例: Assets/Ring/Ring.prefab）。</summary>
        public string path;
        public bool isFolder;
        public long size;
    }

    public class PackageContents
    {
        /// <summary>unitypackage のフルパス。</summary>
        public string file;
        public readonly List<PackageEntry> entries = new List<PackageEntry>();

        public string Name => Path.GetFileNameWithoutExtension(file);
    }

    /// <summary>
    /// unitypackage（tar.gz：GUID ごとのフォルダーに pathname・asset・asset.meta が入っている）の中身の一覧を、
    /// インポートせずに読む。アセット本体は読み飛ばすので、メモリにもディスクにも展開しない。
    /// </summary>
    public static class UnityPackageReader
    {
        const int Block = 512;

        /// <param name="progress">0〜1（圧縮されたファイルのどこまで読んだか）</param>
        public static PackageContents Read(string file, Action<float> progress = null)
        {
            var result = new PackageContents { file = file };
            var byGuid = new Dictionary<string, PackageEntry>(StringComparer.Ordinal);
            var header = new byte[Block];
            var skip = new byte[64 * 1024];

            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                string longName = null;
                long lastReport = 0;
                while (true)
                {
                    if (!ReadFull(gz, header, Block)) break;
                    if (IsZero(header)) break; // 終わりの印

                    var name = longName ?? HeaderName(header);
                    longName = null;
                    long size = ParseOctal(header, 124, 12);
                    char type = (char)header[156];
                    long padded = (size + Block - 1) / Block * Block;

                    if (type == 'L' || type == 'x')
                    {
                        // 長い名前（GNU）・拡張ヘッダー（pax）：次のエントリーの名前
                        var data = ReadBytes(gz, padded);
                        var text = Encoding.UTF8.GetString(data, 0, (int)Math.Min(size, data.Length));
                        longName = type == 'L' ? text.TrimEnd('\0') : PaxPath(text);
                        continue;
                    }

                    var parts = Normalize(name).Split(new[] { '/' }, 2);
                    if (parts.Length == 2 && parts[0].Length > 0)
                    {
                        if (!byGuid.TryGetValue(parts[0], out var e))
                        {
                            e = new PackageEntry { guid = parts[0], isFolder = true };
                            byGuid[e.guid] = e;
                        }
                        if (parts[1] == "pathname")
                        {
                            var data = ReadBytes(gz, padded);
                            var text = Encoding.UTF8.GetString(data, 0, (int)Math.Min(size, data.Length));
                            // 古い形式では 2 行目に余計な値が入っていることがある
                            e.path = text.Split('\n')[0].Trim().TrimEnd('\0').Replace('\\', '/');
                            padded = 0;
                        }
                        else if (parts[1] == "asset")
                        {
                            e.isFolder = false;
                            e.size = size;
                        }
                    }
                    Skip(gz, padded, skip);

                    if (progress != null && fs.Position - lastReport > 256 * 1024)
                    {
                        lastReport = fs.Position;
                        progress((float)fs.Position / Math.Max(1, fs.Length));
                    }
                }
            }

            foreach (var e in byGuid.Values)
                if (!string.IsNullOrEmpty(e.path)) result.entries.Add(e);
            result.entries.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            return result;
        }

        static string Normalize(string name)
        {
            name = name.Replace('\\', '/');
            while (name.StartsWith("./", StringComparison.Ordinal)) name = name.Substring(2);
            return name;
        }

        static string HeaderName(byte[] h)
        {
            var name = CString(h, 0, 100);
            // ustar 形式なら前半（prefix）がある
            if (h[257] == 'u' && h[258] == 's' && h[259] == 't' && h[260] == 'a' && h[261] == 'r')
            {
                var prefix = CString(h, 345, 155);
                if (prefix.Length > 0) name = prefix + "/" + name;
            }
            return name;
        }

        static string PaxPath(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                int i = line.IndexOf(" path=", StringComparison.Ordinal);
                if (i >= 0) return line.Substring(i + 6);
            }
            return null;
        }

        static string CString(byte[] b, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, offset, end - offset);
        }

        static long ParseOctal(byte[] b, int offset, int length)
        {
            // 大きなファイルはバイナリ表記（先頭ビットが立つ）
            if ((b[offset] & 0x80) != 0)
            {
                long v = 0;
                for (int i = offset + 1; i < offset + length; i++) v = (v << 8) | b[i];
                return v;
            }
            long r = 0;
            for (int i = offset; i < offset + length; i++)
            {
                var c = b[i];
                if (c == 0 || c == ' ') { if (r > 0) break; continue; }
                if (c < '0' || c > '7') break;
                r = r * 8 + (c - '0');
            }
            return r;
        }

        static bool IsZero(byte[] b)
        {
            foreach (var x in b)
                if (x != 0) return false;
            return true;
        }

        static bool ReadFull(Stream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        static byte[] ReadBytes(Stream s, long count)
        {
            var buf = new byte[count];
            if (!ReadFull(s, buf, (int)count)) throw new InvalidDataException("unitypackage の途中で終わっています。");
            return buf;
        }

        static void Skip(Stream s, long count, byte[] buf)
        {
            while (count > 0)
            {
                int n = s.Read(buf, 0, (int)Math.Min(buf.Length, count));
                if (n <= 0) throw new InvalidDataException("unitypackage の途中で終わっています。");
                count -= n;
            }
        }
    }
}
