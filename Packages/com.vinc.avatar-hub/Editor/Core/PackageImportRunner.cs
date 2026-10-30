using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VInc.AvatarHub
{
    /// <summary>キューの 1 行（インポートする unitypackage 1 つ）。</summary>
    [Serializable]
    public class ImportJob
    {
        public string id;
        public string file;
        public bool makeProfile = true;
        /// <summary>waiting / importing / imported / done / conflict / profile-conflict / skipped / failed / cancelled</summary>
        public string status = "waiting";
        public string message = "";
        public int conflictCount;
        /// <summary>黄色の「!」が付くファイル（先頭の一部）。</summary>
        public List<string> conflicts = new List<string>();
        /// <summary>衝突していてもそのまま入れる（ユーザーが選んだ）。</summary>
        public bool force;
        /// <summary>Unity のインポート画面で入れる（ユーザーが選んだ）。</summary>
        public bool interactive;
        public string profileId = "";
        /// <summary>中身がまったく同じプロファイルが既にあったので作らなかった（profileId はそのプロファイル）。</summary>
        public bool alreadyExists;
        /// <summary>同じ名前のプロファイルがあったときの選択："" / "overwrite" / "rename" / "skip"</summary>
        public string profileChoice = "";
        /// <summary>同じ名前で中身が違うプロファイル（止まったとき）。</summary>
        public string existingProfileId = "";
        /// <summary>そのプロファイルと比べて何が変わるか（止まったとき）。</summary>
        public List<string> differences = new List<string>();
        public List<string> notes = new List<string>();
        /// <summary>確認してほしいこと（入れ忘れの可能性など）。</summary>
        public List<string> warnings = new List<string>();

        public bool HasWarning => warnings.Count > 0;

        public string Name => Path.GetFileNameWithoutExtension(file);
        public bool Finished => status == "done" || status == "skipped" || status == "failed";
    }

    [Serializable]
    public class ImportSession
    {
        public string kind = "AvatarHub.ImportQueue";
        /// <summary>"sequence"（シーケンスインポート）/ "single"（UnityPackage から作る）</summary>
        public string name;
        public List<ImportJob> jobs = new List<ImportJob>();
        public bool running;
        /// <summary>始めた（再開した）時刻（EditorApplication.timeSinceStartup）。効果音を鳴らすか決めるため。</summary>
        public double startedAt;
        /// <summary>衝突（黄色の「!」）があったら止めて確認する。false なら無視して入れる。</summary>
        public bool stopOnConflict = true;

        public int DoneCount => jobs.Count(j => j.Finished);
        public bool Complete => jobs.Count > 0 && jobs.All(j => j.Finished);
        public ImportJob Find(string id) => jobs.Find(j => j.id == id);
    }

    /// <summary>
    /// unitypackage を順番にインポートし、必要ならプロファイルも作る。
    /// スクリプト入りのパッケージではドメインリロードが起きるので、状態は Library/VIAH に保存して続きから再開する。
    /// インポート自体は Unity の機能なので Undo では戻せない（作ったプロファイルは戻せる）。
    /// </summary>
    [InitializeOnLoad]
    public static class PackageImportRunner
    {
        static readonly Dictionary<string, ImportSession> sessions = new Dictionary<string, ImportSession>();
        static bool ticking;
        static string waitingFor;
        static double waitStarted;
        /// <summary>Unity のインポート画面を最後に見た時刻（まだ見ていなければ 0）。</summary>
        static double dialogSeenAt;

        /// <summary>キューの状態が変わったとき（画面の描き直し用）。</summary>
        public static event Action Changed;

        static PackageImportRunner()
        {
            AssetDatabase.importPackageCompleted += name => OnImportEnded(name, "imported", null);
            AssetDatabase.importPackageCancelled += name => OnImportEnded(name, "cancelled", null);
            AssetDatabase.importPackageFailed += (name, error) => OnImportEnded(name, "failed", error);
            // リロード前に実行中だったら続きから
            EditorApplication.delayCall += () =>
            {
                if (Active != null) StartTicking();
            };
        }

        /// <summary>開発用：キューの保存先を差し替える（ガイド画像の撮影などで、実際のキューを汚さないように）。</summary>
        public static string DirOverride;

        static string FileOf(string name) => Path.Combine(DirOverride ?? Path.Combine(ViahStorage.ProjectRoot, "Library", "VIAH"), "import-" + name + ".json");

        /// <summary>読み込み済みのキューを捨てる（次に使うときにファイルから読み直す）。</summary>
        public static void ResetCache() => sessions.Clear();

        public static ImportSession Get(string name)
        {
            if (sessions.TryGetValue(name, out var s)) return s;
            var path = FileOf(name);
            s = File.Exists(path) ? ViahStorage.Read<ImportSession>(path) : null;
            s ??= new ImportSession();
            s.name = name;
            s.jobs ??= new List<ImportJob>();
            foreach (var j in s.jobs)
            {
                j.conflicts ??= new List<string>();
                j.notes ??= new List<string>();
                j.warnings ??= new List<string>();
                j.differences ??= new List<string>();
            }
            sessions[name] = s;
            return s;
        }

        public static ImportSession Sequence => Get("sequence");
        public static ImportSession Single => Get("single");

        /// <summary>実行中のキュー（無ければ null）。同時に動かせるのは 1 つだけ。</summary>
        public static ImportSession Active => Sequence.running ? Sequence : Single.running ? Single : null;

        public static void Save(ImportSession s)
        {
            try { ViahStorage.Write(FileOf(s.name), s); }
            catch (Exception e) { Debug.LogWarning("[Avatar Hub] インポートのキューを保存できませんでした: " + e.Message); }
            Changed?.Invoke();
        }

        public static ImportJob NewJob(string file, bool makeProfile) =>
            new ImportJob { id = ViahStorage.NewId(), file = file, makeProfile = makeProfile };

        /// <summary>始められない理由（始められるなら null）。</summary>
        public static string CannotStart(ImportSession s)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "再生中はインポートできません。再生を止めてください。";
            var active = Active;
            if (active != null && active != s) return active.name == "sequence" ? "シーケンスインポートの実行中です。" : "UnityPackage からの作成を実行中です。";
            if (!s.jobs.Any(j => j.status == "waiting" || j.status == "imported")) return "インポートを待っているものがありません。";
            return null;
        }

        public static void Start(ImportSession s)
        {
            if (CannotStart(s) != null) return;
            s.running = true;
            s.startedAt = EditorApplication.timeSinceStartup;
            Save(s);
            StartTicking();
        }

        /// <summary>いまのパッケージが終わったところで止める。</summary>
        public static void Pause(ImportSession s)
        {
            s.running = false;
            Save(s);
        }

        /// <summary>衝突で止まった行をどうするか。"unity" / "force" / "skip"</summary>
        public static void Resolve(ImportSession s, ImportJob job, string choice)
        {
            job.interactive = choice == "unity";
            job.force = choice == "force";
            job.message = "";
            job.status = choice == "skip" ? "skipped" : "waiting";
            if (choice == "skip") job.message = "衝突があるため飛ばしました";
            Start(s);
            if (!s.running) Save(s);
        }

        /// <summary>同じ名前のプロファイルがあって止まった行をどうするか。"overwrite" / "rename" / "skip"</summary>
        public static void ResolveProfile(ImportSession s, ImportJob job, string choice)
        {
            job.profileChoice = choice;
            job.message = "";
            job.status = "imported";
            Start(s);
            if (!s.running) Save(s);
        }

        // ───────────── 実行 ─────────────

        static string EventOf(ImportSession s) => s.name == "single" ? "package" : "seq-import";

        static double Elapsed(ImportSession s) => s.startedAt > 0 ? EditorApplication.timeSinceStartup - s.startedAt : 0;

        /// <summary>確認待ちで止まった（離れていても気づけるように音で知らせる）。</summary>
        static void Stopped(ImportSession s) => ViahSound.Attention(EventOf(s), Elapsed(s));

        static void StartTicking()
        {
            if (ticking) return;
            ticking = true;
            EditorApplication.update += Tick;
        }

        static void StopTicking()
        {
            ticking = false;
            EditorApplication.update -= Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var s = Active;
            if (s == null)
            {
                StopTicking();
                return;
            }

            var importing = s.jobs.Find(j => j.status == "importing");
            if (importing != null)
            {
                // リロードのあとは、もう一度待ち始める（Unity のインポート画面が開いたままのことがある）
                if (waitingFor != importing.id)
                {
                    waitingFor = importing.id;
                    waitStarted = EditorApplication.timeSinceStartup;
                    dialogSeenAt = 0;
                }
                if (!LostImport(importing)) return;
                // 結果の通知を受け取れなかった：実際に入ったかで判断する
                waitingFor = null;
                var a = SafeAnalyze(importing.file);
                if (importing.interactive || (a != null && a.Count(PackageItemState.Missing) == 0)) importing.status = "imported";
                else Fail(importing, "インポートが完了したか確認できませんでした。もう一度試してください。");
                Save(s);
                return;
            }

            var imported = s.jobs.Find(j => j.status == "imported");
            if (imported != null)
            {
                Finish(s, imported);
                return;
            }

            var next = s.jobs.Find(j => j.status == "waiting");
            if (next == null)
            {
                s.running = false;
                Save(s);
                StopTicking();
                ViahSound.Done(EventOf(s), Elapsed(s));
                return;
            }
            Process(s, next);
        }

        static bool LostImport(ImportJob job)
        {
            var now = EditorApplication.timeSinceStartup;
            if (!job.interactive) return now - waitStarted > 5;
            // Unity のインポート画面は、パッケージを展開してから少し遅れて開く。開いている間は待ち、
            // 閉じたのに結果の通知が来なかったときだけ諦める（開かないまま 60 秒たったときも）
            bool open = Resources.FindObjectsOfTypeAll<EditorWindow>().Any(w => w != null && w.GetType().FullName == "UnityEditor.PackageImport");
            if (open)
            {
                dialogSeenAt = now;
                return false;
            }
            return dialogSeenAt > 0 ? now - dialogSeenAt > 3 : now - waitStarted > 60;
        }

        static void Process(ImportSession s, ImportJob job)
        {
            job.notes.Clear();
            job.warnings.Clear();
            job.alreadyExists = false;
            job.profileChoice = "";
            job.existingProfileId = "";
            job.differences.Clear();
            if (!File.Exists(job.file))
            {
                Fail(job, "ファイルが見つかりません");
                Save(s);
                return;
            }
            if (EditorUtility.scriptCompilationFailed)
            {
                // エラーのまま次を入れると、さらに壊れることがある
                s.running = false;
                job.message = "スクリプトのエラーがあるため止めました。Console のエラーを直してから再開してください。";
                Save(s);
                Stopped(s);
                return;
            }

            var a = SafeAnalyze(job.file, true);
            if (a == null)
            {
                Fail(job, "unitypackage として読めませんでした");
                Save(s);
                return;
            }
            var conflicts = a.Conflicts;
            job.conflictCount = conflicts.Count;
            job.conflicts = conflicts.Take(50).Select(c => c.entry.path).ToList();

            if (conflicts.Count > 0 && s.stopOnConflict && !job.force && !job.interactive)
            {
                job.status = "conflict";
                job.message = $"黄色の「!」（同じ名前の別のファイル）が {conflicts.Count} 件あるため止めました";
                s.running = false;
                Save(s);
                Stopped(s);
                return;
            }
            if (!a.NeedsImport && !job.interactive)
            {
                job.notes.Add("中身はすべてプロジェクトに入っていたので、インポートは省きました");
                job.status = "imported";
                Save(s);
                return;
            }

            job.status = "importing";
            job.message = "";
            waitingFor = job.id;
            waitStarted = EditorApplication.timeSinceStartup;
            dialogSeenAt = 0;
            Save(s);
            try
            {
                AssetDatabase.ImportPackage(job.file, job.interactive);
            }
            catch (Exception e)
            {
                waitingFor = null;
                Fail(job, e.Message);
                Save(s);
            }
        }

        static void OnImportEnded(string packageName, string result, string error)
        {
            var s = Active;
            var job = s?.jobs.Find(j => j.status == "importing");
            // 自分で入れたもの以外（手動のインポートなど）は無視する
            if (job == null || !SamePackage(job, packageName)) return;
            waitingFor = null;
            switch (result)
            {
                case "imported":
                    job.status = "imported";
                    if (job.conflictCount > 0 && !job.interactive) job.notes.Add($"黄色の「!」の {job.conflictCount} 件は上書きしました");
                    break;
                case "cancelled":
                    job.status = "cancelled";
                    job.message = "インポートをキャンセルしたので止めました";
                    s.running = false;
                    break;
                default:
                    Fail(job, string.IsNullOrEmpty(error) ? "インポートに失敗しました" : error);
                    break;
            }
            Save(s);
        }

        /// <summary>通知のパッケージ名は「拡張子を除いたフルパス」。名前に「.」が入ることがあるので拡張子の判定はしない。</summary>
        static bool SamePackage(ImportJob job, string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            var name = packageName.Replace('\\', '/');
            var full = job.file.Replace('\\', '/');
            if (full.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) full = full.Substring(0, full.Length - ".unitypackage".Length);
            return string.Equals(name, full, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name.Substring(name.LastIndexOf('/') + 1), job.Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>インポートのあと：必要ならプロファイルを作る。</summary>
        static void Finish(ImportSession s, ImportJob job)
        {
            if (job.makeProfile && string.IsNullOrEmpty(job.profileId))
            {
                try
                {
                    var r = PackageProfileBuilder.Build(UnityPackageReader.Read(job.file));
                    if (r.profile.includeFiles.Count == 0)
                        job.warnings.Add("プロジェクトに入っているファイルが無いため、プロファイルは作りませんでした");
                    else if (!SaveProfile(s, job, r))
                        return; // 同じ名前のプロファイルがあるので、どうするか選んでもらう
                }
                catch (Exception e)
                {
                    job.warnings.Add("プロファイルを作れませんでした：" + e.Message);
                }
            }
            job.status = "done";
            Save(s);
        }

        /// <summary>
        /// 作ったプロファイルを保存する。中身がまったく同じものが既にあれば作らない。
        /// 同じ名前で中身が違うものがあれば、上書きか別名かを選んでもらうために止めて false を返す。
        /// </summary>
        static bool SaveProfile(ImportSession s, ImportJob job, PackageProfileBuilder.Result r)
        {
            var p = r.profile;
            var same = ViahDatabase.Profiles.FirstOrDefault(x => ProfileCompare.SameContent(x, p));
            if (same != null)
            {
                job.profileId = same.id;
                job.alreadyExists = true;
                job.notes.Add($"中身がまったく同じプロファイル「{same.name}」が既にあるので、新しくは作りませんでした");
                return true;
            }

            var named = ViahDatabase.Profiles.FirstOrDefault(x => string.Equals(x.name.Trim(), p.name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (named != null)
            {
                switch (job.profileChoice)
                {
                    case "overwrite":
                        ProfileCompare.CopyContent(p, named);
                        ViahDatabase.SaveProfile(named, $"「{named.name}」を UnityPackage の中身で上書き");
                        job.notes.Add($"プロファイル「{named.name}」を上書きしました");
                        p = named;
                        break;
                    case "rename":
                        ViahDatabase.RenameProfile(p, ViahDatabase.UniqueProfileName(p.name));
                        ViahDatabase.SaveProfile(p, $"「{p.name}」を UnityPackage から作成");
                        job.notes.Add($"同じ名前のプロファイルがあるので「{p.name}」として作りました");
                        break;
                    case "skip":
                        job.notes.Add($"同じ名前のプロファイル「{named.name}」があるので、作りませんでした");
                        return true;
                    default:
                        job.status = "profile-conflict";
                        job.existingProfileId = named.id;
                        job.message = $"同じ名前のプロファイル「{named.name}」が既にあります（中身が違います）";
                        job.differences = ProfileCompare.Describe(named, p);
                        s.running = false;
                        Save(s);
                        Stopped(s);
                        return false;
                }
            }
            else
            {
                ViahDatabase.SaveProfile(p, $"「{p.name}」を UnityPackage から作成");
            }

            if (r.version != null) PackageProfileBuilder.RecordVersion(p, r.version.Value, job.file);
            job.profileId = p.id;
            if (r.notFound.Count > 0) job.warnings.Add($"パッケージにあってプロジェクトに無いファイル {r.notFound.Count} 件は入れていません");
            if (r.otherFile.Count > 0) job.warnings.Add($"同じ名前の別のファイル {r.otherFile.Count} 件は、中身が違う可能性があるので入れていません");
            if (r.uncovered.Count > 0) job.warnings.Add($"プレハブが使っているのに入っていないファイルが {r.uncovered.Count} 件あります（入れ忘れの可能性）");
            return true;
        }

        static void Fail(ImportJob job, string message)
        {
            job.status = "failed";
            job.message = message;
        }

        static PackageAnalysis SafeAnalyze(string file, bool progress = false)
        {
            try { return PackageAnalysis.Read(file, progress); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Avatar Hub] unitypackage を読めませんでした: {file}\n{e.Message}");
                return null;
            }
        }
    }
}
