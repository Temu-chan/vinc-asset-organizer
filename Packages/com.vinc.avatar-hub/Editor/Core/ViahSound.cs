using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace VInc.AvatarHub
{
    /// <summary>
    /// 時間のかかる作業が終わったとき・確認待ちで止まったときの効果音。
    /// 内蔵の音はその場で合成する（音源ファイルを持たないので、著作権の心配がない）。
    /// 設定で、音量の調整と自分の音（wav / mp3 / ogg）への入れ替えができる。
    /// </summary>
    public static class ViahSound
    {
        public static readonly (string key, string label)[] Presets =
        {
            ("pirorin", "ピロリン"),
            ("kirarin", "キラリン"),
            ("pinpon", "ピンポーン"),
            ("pon", "ポーン"),
            ("poron", "ポロン"),
        };

        /// <summary>音を鳴らす作業。設定で 1 つずつオン・オフできる。</summary>
        public static readonly (string key, string label)[] Events =
        {
            ("seq-export", "シーケンスエクスポート"),
            ("export", "単発エクスポート"),
            ("seq-import", "シーケンスインポート"),
            ("package", "UnityPackage から作る"),
            ("search", "アセットSearcher の検索"),
            ("scan", "プロファイルの自動検出"),
        };

        /// <summary>これより早く終わったときは鳴らさない（設定でオフにできる）。</summary>
        public const double ShortSeconds = 3;

        public const string FileKey = "file";

        /// <summary>作業が終わった（seconds はかかった時間）。</summary>
        public static void Done(string ev, double seconds) => Notify(ev, seconds, false);

        /// <summary>確認待ちで止まった。</summary>
        public static void Attention(string ev, double seconds) => Notify(ev, seconds, true);

        public static bool WillPlay(string ev, double seconds)
        {
            var s = ViahDatabase.Settings;
            return s.soundOn && !s.soundMuted.Contains(ev) && !(s.soundSkipShort && seconds < ShortSeconds);
        }

        static void Notify(string ev, double seconds, bool attention)
        {
            if (WillPlay(ev, seconds)) Play(attention);
        }

        /// <summary>音を鳴らしたとき（true なら確認待ちの音）。テスト用。</summary>
        public static event Action<bool> Played;

        /// <summary>いまの設定の音を鳴らす（設定画面の「試しに鳴らす」でも使う）。</summary>
        public static void Play(bool attention)
        {
            Played?.Invoke(attention);
            var s = ViahDatabase.Settings;
            var key = attention ? s.soundAttention : s.soundDone;
            var file = attention ? s.soundAttentionFile : s.soundDoneFile;
            if (key == FileKey && !string.IsNullOrEmpty(file)) PlayFile(Path.Combine(ViahStorage.SoundsDir, file), s.soundVolume, attention);
            else PlaySamples(Synth(key), 1, Rate, s.soundVolume);
        }

        public static string LabelOf(string key) => Presets.FirstOrDefault(p => p.key == key).label ?? "ピロリン";

        // ───────────── 合成 ─────────────

        public const int Rate = 44100;
        static readonly Dictionary<string, float[]> synthCache = new Dictionary<string, float[]>();

        /// <summary>内蔵の音を作る（モノラル・44.1kHz・大きさは最大 0.9 にそろえる）。</summary>
        public static float[] Synth(string key)
        {
            if (synthCache.TryGetValue(key ?? "", out var cached)) return cached;
            // 音の高さ（Hz）・鳴り始め（秒）・減衰の速さ（秒）
            (double f, double at, double decay)[] notes;
            string timbre = "chime";
            switch (key)
            {
                case "kirarin":
                    notes = new[] { (2093.0, 0.0, 0.10), (2637.0, 0.05, 0.10), (3136.0, 0.10, 0.10), (4186.0, 0.15, 0.30) };
                    break;
                case "pinpon":
                    notes = new[] { (1318.5, 0.0, 0.30), (1046.5, 0.30, 0.45) };
                    break;
                case "pon":
                    notes = new[] { (880.0, 0.0, 0.5) };
                    timbre = "bell";
                    break;
                case "poron":
                    notes = new[] { (784.0, 0.0, 0.16), (1174.7, 0.09, 0.28) };
                    timbre = "marimba";
                    break;
                default: // ピロリン
                    notes = new[] { (1318.5, 0.0, 0.12), (1661.2, 0.075, 0.12), (1975.5, 0.15, 0.30) };
                    break;
            }
            double length = notes.Max(n => n.at + n.decay * 5);
            var data = new float[(int)(length * Rate)];
            foreach (var (f, at, decay) in notes)
            {
                int start = (int)(at * Rate);
                // 高い音ほど少し小さくして、耳に痛くならないようにする
                double gain = 1.0 / (1.0 + f / 4000.0);
                for (int i = start; i < data.Length; i++)
                {
                    double t = (i - start) / (double)Rate;
                    double env = Math.Min(1.0, t / 0.004) * Math.Exp(-t / decay);
                    if (t > 0.05 && env < 1e-4) break;
                    double w = 2 * Math.PI * f * t;
                    double v = Math.Sin(w);
                    if (timbre == "bell")
                        v += 0.4 * Math.Sin(w * 2.76) * Math.Exp(-t / (decay * 0.5)) + 0.2 * Math.Sin(w * 5.4) * Math.Exp(-t / (decay * 0.25));
                    else if (timbre == "marimba")
                        v += 0.3 * Math.Sin(w * 4.0) * Math.Exp(-t / (decay * 0.3));
                    else
                        v += 0.3 * Math.Sin(w * 2.0) * Math.Exp(-t / (decay * 0.5));
                    data[i] += (float)(v * env * gain);
                }
            }
            // 最後の 30ms は小さくしていく（切れ目でプツッと鳴らないように）
            int fade = Math.Min(data.Length, (int)(0.03 * Rate));
            for (int i = 0; i < fade; i++) data[data.Length - 1 - i] *= i / (float)fade;
            float peak = data.Length == 0 ? 0 : data.Max(x => Math.Abs(x));
            if (peak > 0)
                for (int i = 0; i < data.Length; i++) data[i] *= 0.9f / peak;
            synthCache[key ?? ""] = data;
            return data;
        }

        // ───────────── 鳴らす ─────────────

        static readonly Type AudioUtil = typeof(Editor).Assembly.GetType("UnityEditor.AudioUtil");
        static AudioClip clip;

        /// <summary>最後に鳴らしたときの方法（"windows" / "unity" / 鳴らせなかった理由）。テスト・調査用。</summary>
        public static string LastResult { get; private set; } = "";

#if UNITY_EDITOR_WIN
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern bool PlaySound(string sound, IntPtr module, uint flags);

        const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndFilename = 0x00020000;
        static int fileIndex;
#endif

        /// <summary>
        /// 鳴らす。Windows では Windows の機能で鳴らす（Unity のプレビュー再生は、作った音が鳴らないことがあり、
        /// Game ビューの「Mute Audio」にも左右されるため）。それ以外は Unity のプレビュー再生で鳴らす。音量はデータに掛けて調整する。
        /// </summary>
        static void PlaySamples(float[] data, int channels, int rate, float volume)
        {
            if (data == null || data.Length == 0 || volume <= 0)
            {
                LastResult = "音量が 0";
                return;
            }
#if UNITY_EDITOR_WIN
            try
            {
                PlaySound(null, IntPtr.Zero, 0); // 鳴っている音を止める
                // 鳴らしている途中のファイルを書き換えないよう、2 つを交互に使う
                fileIndex ^= 1;
                var path = Path.Combine(Path.GetTempPath(), $"viah_sound_{fileIndex}.wav");
                WriteWav(path, data, channels, rate, volume);
                if (PlaySound(path, IntPtr.Zero, SndAsync | SndNoDefault | SndFilename))
                {
                    LastResult = "windows";
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Avatar Hub] 効果音を鳴らせませんでした: " + e.Message);
            }
#endif
            PlayInUnity(data, channels, rate, volume);
        }

        /// <summary>16bit PCM の wav を書く（音量を掛けて）。</summary>
        public static void WriteWav(string path, float[] data, int channels, int rate, float volume = 1f)
        {
            float v = Mathf.Clamp01(volume);
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = data.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                foreach (var f in data) w.Write((short)Mathf.Clamp(f * v * 32767f, -32768f, 32767f));
            }
        }

        /// <summary>Unity のプレビュー再生で鳴らす（シーンには何も置かない）。</summary>
        static void PlayInUnity(float[] data, int channels, int rate, float volume)
        {
            AudioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
            if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            var scaled = new float[data.Length];
            float v = Mathf.Clamp01(volume);
            for (int i = 0; i < data.Length; i++) scaled[i] = data[i] * v;
            clip = AudioClip.Create("VIAH Sound", data.Length / channels, channels, rate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clip.SetData(scaled, 0);
            var play = AudioUtil?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
            if (play != null)
            {
                play.Invoke(null, new object[] { clip, 0, false });
                LastResult = "unity";
            }
            else
            {
                EditorApplication.Beep();
                LastResult = "beep";
            }
        }

        // ───────────── 自分の音 ─────────────

        public struct Loaded
        {
            public float[] data;
            public int channels;
            public int rate;
        }

        static readonly Dictionary<string, Loaded> fileCache = new Dictionary<string, Loaded>();

        static void PlayFile(string path, float volume, bool attention)
        {
            if (!File.Exists(path))
            {
                // ファイルが消えていたら内蔵の音で知らせる
                PlaySamples(Synth(attention ? "pinpon" : "pirorin"), 1, Rate, volume);
                return;
            }
            var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            if (fileCache.TryGetValue(key, out var c))
            {
                PlaySamples(c.data, c.channels, c.rate, volume);
                return;
            }
            Load(path, loaded =>
            {
                if (loaded == null)
                {
                    PlaySamples(Synth(attention ? "pinpon" : "pirorin"), 1, Rate, volume);
                    return;
                }
                fileCache[key] = loaded.Value;
                PlaySamples(loaded.Value.data, loaded.Value.channels, loaded.Value.rate, volume);
            });
        }

        static AudioType TypeOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav": return AudioType.WAV;
                case ".mp3": return AudioType.MPEG;
                case ".ogg": return AudioType.OGGVORBIS;
                default: return AudioType.UNKNOWN;
            }
        }

        /// <summary>音のファイルを読む（読み終わると done。読めなければ null）。長い音は最初の 10 秒だけ使う。</summary>
        public static void Load(string path, Action<Loaded?> done)
        {
            UnityWebRequest req;
            try
            {
                req = UnityWebRequestMultimedia.GetAudioClip(new Uri(Path.GetFullPath(path)).AbsoluteUri, TypeOf(path));
                ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Avatar Hub] 効果音を読めませんでした: " + e.Message);
                done(null);
                return;
            }
            var op = req.SendWebRequest();
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (!op.isDone) return;
                EditorApplication.update -= tick;
                Loaded? result = null;
                try
                {
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        var c = DownloadHandlerAudioClip.GetContent(req);
                        if (c != null && c.samples > 0)
                        {
                            int frames = Math.Min(c.samples, c.frequency * 10);
                            var data = new float[frames * c.channels];
                            c.GetData(data, 0);
                            result = new Loaded { data = data, channels = c.channels, rate = c.frequency };
                        }
                        if (c != null) UnityEngine.Object.DestroyImmediate(c);
                    }
                    else
                    {
                        Debug.LogWarning("[Avatar Hub] 効果音を読めませんでした: " + req.error);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Avatar Hub] 効果音を読めませんでした: " + e.Message);
                }
                finally
                {
                    req.Dispose();
                }
                done(result);
            };
            EditorApplication.update += tick;
        }

        /// <summary>選んだファイルを Sounds フォルダーにコピーする（どの Unity プロジェクトでも同じ音になるように）。コピー先のファイル名を返す。</summary>
        public static string CopyIn(string source)
        {
            Directory.CreateDirectory(ViahStorage.SoundsDir);
            var name = Path.GetFileName(source);
            var dest = Path.Combine(ViahStorage.SoundsDir, name);
            if (!ViahStorage.SamePath(source, dest))
            {
                int n = 2;
                while (File.Exists(dest)) dest = Path.Combine(ViahStorage.SoundsDir, $"{Path.GetFileNameWithoutExtension(name)} ({n++}){Path.GetExtension(name)}");
                File.Copy(source, dest);
            }
            return Path.GetFileName(dest);
        }
    }
}
