using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VInc.AvatarHub.Tests
{
    /// <summary>
    /// 開発用：unitypackage の読み取り・照らし合わせ・自動インポート・プロファイル作成を通す。
    /// スクリプト入りのパッケージでドメインリロードが起きても続きから進むので、状態は Temp に保存する。
    /// Temp/viah_pkgtest.request を置いてスクリプトを再読み込みすると実行し、Temp/viah_pkgtest.log に結果を書く。
    /// データ（プロファイル・キュー）は Temp の中の別の場所に保存し、実際のデータには触らない。
    /// </summary>
    [InitializeOnLoad]
    static class PackageImportTest
    {
        const string Request = "Temp/viah_pkgtest.request";
        const string StatePath = "Temp/viah_pkgtest.state.json";
        const string LogPath = "Temp/viah_pkgtest.log";
        const string Root = "Assets/VIAH_PkgTest";
        const string Gimmick = Root + "/Gimmick";
        const string Moved = Root + "/GimmickMoved";
        const string ScriptDir = Root + "/Script";
        static readonly string Work = Path.GetFullPath("Temp/viah_pkgtest");
        static string PkgGimmick => Path.Combine(Work, "Gimmick_Ver1.2.3-2026_10_28.unitypackage");
        static string PkgScript => Path.Combine(Work, "ScriptPack.unitypackage");

        [Serializable]
        class State
        {
            public int step;
            /// <summary>Unity のインポート画面を開いたまま待つ（「Import」は人が押す）。</summary>
            public bool interactive;
            public int pass, fail;
            public int count;
            public List<string> log = new List<string>();
            public double waitStart;
        }

        static State st;
        static bool driving;

        static PackageImportTest()
        {
            if (File.Exists(Request))
            {
                var mode = File.ReadAllText(Request).Trim();
                File.Delete(Request);
                if (File.Exists(StatePath)) File.Delete(StatePath);
                st = new State { interactive = mode == "interactive" };
                Save();
            }
            if (!File.Exists(StatePath)) return;
            st = JsonUtility.FromJson<State>(File.ReadAllText(StatePath));
            // リロードのあとも、テスト用の保存先を使う
            ViahStorage.DataDirOverride = Path.Combine(Work, "data");
            PackageImportRunner.DirOverride = Path.Combine(Work, "queue");
            if (!driving)
            {
                driving = true;
                EditorApplication.update += Drive;
            }
        }

        static void Save()
        {
            // ほかのプロセス（ウイルス対策など）が一瞬つかんでいることがあるので、少し待って書き直す
            for (int i = 0; ; i++)
            {
                try
                {
                    File.WriteAllText(StatePath, JsonUtility.ToJson(st, true));
                    return;
                }
                catch (IOException) when (i < 5)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        static void Log(string s)
        {
            st.log.Add(s);
            Save();
        }

        static void Check(bool ok, string what)
        {
            if (ok) st.pass++;
            else st.fail++;
            Log((ok ? "OK   " : "NG   ") + what);
        }

        static void Next()
        {
            st.step++;
            st.waitStart = EditorApplication.timeSinceStartup;
            Save();
        }

        static bool TimedOut(double seconds) => EditorApplication.timeSinceStartup - st.waitStart > seconds;

        static void Drive()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (st.interactive)
                {
                    switch (st.step)
                    {
                        case 0: Setup(); Next(); break;
                        case 1: StartInteractive(); Next(); break;
                        case 2: if (WaitInteractive()) Next(); break;
                        default: Finish(); break;
                    }
                    return;
                }
                switch (st.step)
                {
                    case 0: Setup(); Next(); break;
                    case 1: MovedCase(); Next(); break;
                    case 2: StartSingle(); Next(); break;
                    case 3: if (WaitSingle()) Next(); break;
                    case 4: StartSequence(); Next(); break;
                    case 5: if (WaitConflict()) Next(); break;
                    case 6: if (WaitSequence()) Next(); break;
                    case 7: SameAgain(); Next(); break;
                    case 8: if (SingleIdle()) { CheckAlreadyExists(); Next(); } break;
                    case 9: if (SingleIdle()) { CheckProfileConflict("rename"); Next(); } break;
                    case 10: if (SingleIdle()) { CheckRenamed(); Next(); } break;
                    case 11: if (SingleIdle()) { CheckProfileConflict("overwrite"); Next(); } break;
                    case 12: if (SingleIdle()) { CheckOverwritten(); NamesCase(); Next(); } break;
                    default: Finish(); break;
                }
            }
            catch (Exception e)
            {
                st.fail++;
                Log("EXCEPTION " + e);
                Finish();
            }
        }

        // ───────────── 準備：サンプルを作って unitypackage に書き出す ─────────────

        static void Setup()
        {
            Log("== 準備");
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            if (Directory.Exists(Work)) Directory.Delete(Work, true);
            Directory.CreateDirectory(Work);
            ViahDatabase.Reload();

            AssetDatabase.CreateFolder("Assets", "VIAH_PkgTest");
            AssetDatabase.CreateFolder(Root, "Gimmick");
            AssetDatabase.CreateFolder(Root, "Shared");
            AssetDatabase.CreateFolder(Root, "Script");

            var tex = WriteTexture(Gimmick + "/Tex.png");
            var tex2 = WriteTexture(Gimmick + "/Tex2.png");
            var mat = new Material(Shader.Find("Standard")) { mainTexture = tex };
            AssetDatabase.CreateAsset(mat, Gimmick + "/Mat.mat");
            var mat2 = new Material(Shader.Find("Standard")) { mainTexture = tex2 };
            AssetDatabase.CreateAsset(mat2, Gimmick + "/Mat2.mat");
            var shared = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(shared, Root + "/Shared/SharedMat.mat");
            AssetDatabase.CreateAsset(new AnimationClip(), Gimmick + "/Extra.anim");

            // Sub は Gimmick の中に入れる（起点にはならない）
            var subGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            subGo.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var sub = PrefabUtility.SaveAsPrefabAsset(subGo, Gimmick + "/Sub.prefab");
            UnityEngine.Object.DestroyImmediate(subGo);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.GetComponent<MeshRenderer>().sharedMaterials = new[] { mat2, shared };
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(sub);
            inst.transform.SetParent(go.transform);
            PrefabUtility.SaveAsPrefabAsset(go, Gimmick + "/Gimmick.prefab");
            UnityEngine.Object.DestroyImmediate(go);

            File.WriteAllText(ScriptDir + "/ViahPkgTestBehaviour.cs", "using UnityEngine;\npublic class ViahPkgTestBehaviour : MonoBehaviour { }\n");
            AssetDatabase.ImportAsset(ScriptDir + "/ViahPkgTestBehaviour.cs");
            AssetDatabase.SaveAssets();

            // Tex2 は入れ忘れ（基準フォルダーの中で使われているのに入っていない）、SharedMat は外部として書き出さない
            var files = new[] { Root, Gimmick, Gimmick + "/Tex.png", Gimmick + "/Mat.mat", Gimmick + "/Mat2.mat", Gimmick + "/Extra.anim", Gimmick + "/Sub.prefab", Gimmick + "/Gimmick.prefab" };
            AssetDatabase.ExportPackage(files, PkgGimmick, ExportPackageOptions.Default);
            AssetDatabase.ExportPackage(new[] { Root, ScriptDir, ScriptDir + "/ViahPkgTestBehaviour.cs" }, PkgScript, ExportPackageOptions.Default);
            Check(File.Exists(PkgGimmick) && File.Exists(PkgScript), "サンプルの unitypackage を書き出した");

            var c = UnityPackageReader.Read(PkgGimmick);
            Check(c.entries.Count == files.Length, $"中身を読めた（{c.entries.Count} / {files.Length} 件）");
            Check(c.entries.Count(e => e.isFolder) == 2, "フォルダーを見分けた");
            var tguid = AssetDatabase.AssetPathToGUID(Gimmick + "/Tex.png");
            Check(c.entries.Any(e => e.guid == tguid && e.path == Gimmick + "/Tex.png" && e.size > 0), "GUID・パス・サイズが合っている");
            var a = PackageAnalysis.Analyze(c);
            Check(a.Count(PackageItemState.Same) == 6 && !a.NeedsImport, $"インポート済み：同じ場所 {a.Count(PackageItemState.Same)} 件");
        }

        static Texture2D WriteTexture(string path)
        {
            var t = new Texture2D(8, 8);
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ───────────── インポート後に移動したもの ─────────────

        static void MovedCase()
        {
            Log("== 移動・名前変更したあと");
            var err = AssetDatabase.MoveAsset(Gimmick, Moved);
            Check(string.IsNullOrEmpty(err), "フォルダーを移動した " + err);
            var a = PackageAnalysis.Read(PkgGimmick, false);
            Check(a.Count(PackageItemState.Moved) == 6 && a.Count(PackageItemState.Missing) == 0, $"GUID で追いかけた（移動 {a.Count(PackageItemState.Moved)} 件）");

            var r = PackageProfileBuilder.Build(a.contents);
            var p = r.profile;
            Check(p.name == "Gimmick", "名前：" + p.name);
            Check(r.version != null && r.version.Value.ToString() == "1.2.3", "バージョン：" + r.version);
            Check(p.includeFiles.Count == 6 && p.includeFiles.All(f => f.path.StartsWith(Moved + "/")), "移動先のパスで登録（" + p.includeFiles.Count + " 件）");
            Check(p.includeFolders.Count == 0, "フォルダーごとにはしない");
            Check(ProfileResolver.BaseFolderOf(p) == Moved, "基準フォルダー：" + ProfileResolver.BaseFolderOf(p));
            Check(p.mode == "auto" && p.seeds.Count == 1 && p.seeds[0].path == Moved + "/Gimmick.prefab", "起点は外側のプレハブだけ：" + string.Join(",", p.seeds.Select(s => s.path)));
            Check(r.uncovered.Contains(Moved + "/Tex2.png"), "入れ忘れ（Tex2）を見つけた：" + string.Join(",", r.uncovered));
            Check(p.ignoredDependencies.Any(i => i.path == Root + "/Shared/SharedMat.mat"), "外部（SharedMat）は警告しない");

            Check(PackageProfileBuilder.ParseName("Ring_Debug_Ver1.0.3-2026_10_28-18_51", out var v1) == "Ring" && v1?.ToString() == "1.0.3", "名前の解析：Debug・日付・時刻を外す");
            Check(PackageProfileBuilder.ParseName("VRCMeshDeformer_v0.1.0", out var v2) == "VRCMeshDeformer" && v2?.ToString() == "0.1.0", "名前の解析：v0.1.0");
            Check(PackageProfileBuilder.ParseName("Tool_v1.12.0", out var v3) == "Tool" && v3 == null, "名前の解析：範囲外のバージョンは引き継がない");
            Check(PackageProfileBuilder.ParseName("TalkCards_22-0034cards", out var v4) == "TalkCards_22-0034cards" && v4 == null, "名前の解析：バージョンが無い名前はそのまま");
            Check(PackageProfileBuilder.ParseName("productC_Ver1.0.0-18_51", out var v5) == "productC" && v5?.ToString() == "1.0.0", "名前の解析：バージョン＋時刻");
            Check(PackageProfileBuilder.ParseName("productC_2026_10_25-18_51", out var v6) == "productC" && v6 == null, "名前の解析：日付＋時刻だけ");
            Check(PackageProfileBuilder.ParseName("Gimmick_10_28", out _) == "Gimmick_10_28", "名前の解析：日付でない数字は残す");
        }

        // ───────────── まだ入っていないもの：自動でインポートしてプロファイルを作る ─────────────

        static void StartSingle()
        {
            Log("== 未インポート → 自動インポート → プロファイル作成");
            AssetDatabase.DeleteAsset(Moved);
            var a = PackageAnalysis.Read(PkgGimmick, false);
            Check(a.Count(PackageItemState.Missing) == 6 && a.NeedsImport, $"まだ入っていない：{a.Count(PackageItemState.Missing)} 件");
            var s = PackageImportRunner.Single;
            s.jobs.Clear();
            s.jobs.Add(PackageImportRunner.NewJob(PkgGimmick, true));
            PackageImportRunner.Start(s);
            Check(s.running, "実行を始めた");
        }

        static bool WaitSingle()
        {
            var s = PackageImportRunner.Single;
            var j = s.jobs[0];
            if (s.running && !TimedOut(60)) return false;
            Check(j.status == "done", "完了：" + j.status + " " + j.message);
            Check(AssetRef.Exists(Gimmick + "/Gimmick.prefab"), "元の場所にインポートされた");
            var p = ViahDatabase.FindProfile(j.profileId);
            Check(p != null, "プロファイルを保存した");
            if (p == null) return true;
            Check(p.FilePath != null && p.FilePath.StartsWith(Path.Combine(Work, "data")), "テスト用の保存先に保存");
            Check(SelectionModel.FromProfile(p).ResolveFiles().Count == 6, "登録したファイルがすべて見つかる");
            Check(PackageExporter.LastVersion(p)?.ToString() == "1.2.3", "前回のバージョン 1.2.3 を記録");
            Check(!j.warnings.Any(w => w.Contains("無いファイル")), "パッケージのファイルはすべて登録した");
            return true;
        }

        // ───────────── シーケンス：衝突で止まる → 上書きで続ける → スクリプト（リロード）→ 完了 ─────────────

        static void StartSequence()
        {
            Log("== シーケンス：衝突で停止・再開、スクリプト入りでリロード");
            ReplaceWithNewGuid(Gimmick + "/Extra.anim");
            AssetDatabase.DeleteAsset(Gimmick + "/Tex.png");
            AssetDatabase.DeleteAsset(ScriptDir);
            var a = PackageAnalysis.Read(PkgGimmick, false);
            Check(a.Conflicts.Count == 1 && a.Conflicts[0].entry.path == Gimmick + "/Extra.anim", "黄色の「!」を見つけた：" + string.Join(",", a.Conflicts.Select(x => x.entry.path)));

            var s = PackageImportRunner.Sequence;
            s.jobs.Clear();
            s.stopOnConflict = true;
            s.jobs.Add(PackageImportRunner.NewJob(PkgGimmick, false));
            s.jobs.Add(PackageImportRunner.NewJob(PkgScript, true));
            PackageImportRunner.Start(s);
        }

        /// <summary>
        /// 同じパスに別の GUID のファイルを置く（黄色の「!」）。
        /// 消して作り直すだけだと Unity が同じ GUID を使い回すので、新しい GUID の .meta を書いて読み込ませる。
        /// </summary>
        static void ReplaceWithNewGuid(string path)
        {
            var bytes = File.ReadAllBytes(path);
            AssetDatabase.DeleteAsset(path);
            File.WriteAllBytes(path, bytes);
            File.WriteAllText(path + ".meta", "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") +
                "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 7400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
            AssetDatabase.ImportAsset(path);
        }

        static bool WaitConflict()
        {
            var s = PackageImportRunner.Sequence;
            if (s.running && !TimedOut(30)) return false;
            var j = s.jobs[0];
            Check(j.status == "conflict" && !s.running, "衝突で止まった：" + j.status);
            Check(j.conflictCount == 1, "衝突の件数：" + j.conflictCount);
            Check(s.jobs[1].status == "waiting", "次の行は待機のまま");
            PackageImportRunner.Resolve(s, j, "force");
            Check(s.running, "「このまま入れる」で再開した");
            return true;
        }

        static bool WaitSequence()
        {
            var s = PackageImportRunner.Sequence;
            if (s.running && !TimedOut(120)) return false;
            Check(s.Complete, "すべて完了：" + string.Join(",", s.jobs.Select(x => x.status + " " + x.message)));
            var j0 = s.jobs[0];
            Check(j0.status == "done" && string.IsNullOrEmpty(j0.profileId), "1 行目：インポートだけ（プロファイルは作らない）");
            Check(AssetRef.Exists(Gimmick + "/Tex.png"), "消したファイルが入った");
            Check(j0.notes.Any(n => n.Contains("上書き")), "上書きしたことを記録");
            var pkgGuid = UnityPackageReader.Read(PkgGimmick).entries.First(e => e.path == Gimmick + "/Extra.anim").guid;
            Log("情報：上書きしたあとの Extra.anim の GUID は " + (AssetDatabase.AssetPathToGUID(Gimmick + "/Extra.anim") == pkgGuid ? "パッケージ側" : "プロジェクト側"));
            var after = PackageProfileBuilder.Build(UnityPackageReader.Read(PkgGimmick));
            Check(after.notFound.Count == 0, "上書きしたあとも、パッケージのファイルはすべて見つかる");
            var j1 = s.jobs[1];
            Check(j1.status == "done" && AssetRef.Exists(ScriptDir + "/ViahPkgTestBehaviour.cs"), "2 行目：スクリプトが入った（リロードをまたいで完了）");
            var p = ViahDatabase.FindProfile(j1.profileId);
            Check(p != null && p.mode == "manual" && p.includeFiles.Count == 1 && p.name == "ScriptPack", "プレハブが無いので手動登録で作った");
            return true;
        }

        // ───────────── 同じパッケージからもう一度作る ─────────────

        static void RunSingle()
        {
            var s = PackageImportRunner.Single;
            s.jobs.Clear();
            s.jobs.Add(PackageImportRunner.NewJob(PkgGimmick, true));
            st.count = ViahDatabase.Profiles.Count;
            Save();
            PackageImportRunner.Start(s);
        }

        static bool SingleIdle() => !PackageImportRunner.Single.running || TimedOut(60);

        static ImportJob SingleJob => PackageImportRunner.Single.jobs[0];

        static PackageProfile Gimmick1 => ViahDatabase.Profiles.First(x => x.name == "Gimmick");

        static void SameAgain()
        {
            Log("== 同じパッケージからもう一度作る");
            RunSingle();
        }

        static void CheckAlreadyExists()
        {
            var j = SingleJob;
            Check(j.status == "done" && j.alreadyExists, "まったく同じなら「既にあります」：" + j.status + " " + string.Join(" / ", j.notes));
            Check(ViahDatabase.Profiles.Count == st.count, "新しく作っていない");
            Check(j.profileId == Gimmick1.id, "既にあるプロファイルを指している");
            // 中身を変えて、同じ名前・違う中身にする
            var g = Gimmick1;
            g.includeFiles.RemoveAt(0);
            ViahDatabase.SaveProfile(g, "テスト：中身を変える");
            RunSingle();
        }

        static void CheckProfileConflict(string choice)
        {
            var s = PackageImportRunner.Single;
            var j = SingleJob;
            Check(j.status == "profile-conflict" && !s.running, $"同じ名前・違う中身で止まった（{choice} を選ぶ）：" + j.status);
            Check(j.existingProfileId == Gimmick1.id, "今のプロファイルを指している");
            Check(j.differences.Any(d => d.Contains("増える 1 件")), "違いを説明している：" + string.Join(" / ", j.differences));
            Check(ViahDatabase.Profiles.Count == st.count, "選ぶまでは保存しない");
            PackageImportRunner.ResolveProfile(s, j, choice);
        }

        static void CheckRenamed()
        {
            var j = SingleJob;
            var p = ViahDatabase.FindProfile(j.profileId);
            Check(j.status == "done" && p != null, "別名で作った：" + j.status);
            Check(p != null && p.name == "Gimmick (2)" && p.PackageBaseName == "Gimmick", $"名前は「Gimmick (2)」、unitypackage の名前は Gimmick のまま：{p?.name} / {p?.PackageBaseName}");
            Check(p != null && p.FilePath.EndsWith("Gimmick (2).profile.json"), "ファイル名と名前がそろっている：" + Path.GetFileName(p?.FilePath ?? ""));
            Check(ViahDatabase.Profiles.Count == st.count + 1, "1 件増えた");
            // 上書きを試すため、別名のほうは消しておく（残っていると「まったく同じ」が見つかる）
            if (p != null) ViahDatabase.Delete(p, "テスト：消す");
            RunSingle();
        }

        static void CheckOverwritten()
        {
            var j = SingleJob;
            var g = Gimmick1;
            Check(j.status == "done" && j.profileId == g.id && j.profileChoice == "overwrite", "上書きした：" + j.status);
            var fresh = PackageProfileBuilder.Build(UnityPackageReader.Read(PkgGimmick)).profile;
            Check(ProfileCompare.SameContent(g, fresh), "中身がパッケージと同じになった");
            Check(ViahDatabase.Profiles.Count == st.count, "数は変わらない");
        }

        // ───────────── 名前の重なり ─────────────

        static void NamesCase()
        {
            Log("== 名前の重なり");
            var dir = ViahStorage.ProfilesDir;
            PackageProfile Make(string name)
            {
                var p = new PackageProfile { id = ViahStorage.NewId(), name = name };
                p.Sanitize();
                return p;
            }
            // 以前のバージョンで作られた「同じ名前・ファイル名だけ (2)」の状態
            ViahStorage.Write(Path.Combine(dir, "Dup.profile.json"), Make("Dup"));
            ViahStorage.Write(Path.Combine(dir, "Dup (2).profile.json"), Make("Dup"));
            ViahDatabase.Reload();
            var names = ViahDatabase.Profiles.Where(x => x.name.StartsWith("Dup")).Select(x => x.name).OrderBy(x => x).ToList();
            Check(names.SequenceEqual(new[] { "Dup", "Dup (2)" }), "読み込むと 2 つ目が「Dup (2)」になる：" + string.Join(",", names));
            var second = ViahDatabase.Profiles.FirstOrDefault(x => x.name == "Dup (2)");
            Check(second != null && second.PackageBaseName == "Dup", "unitypackage の名前は Dup のまま");
            Check(second != null && File.ReadAllText(second.FilePath).Contains("\"Dup (2)\""), "ファイルにも保存した");
            Check(ViahDatabase.UniqueProfileName("Dup") == "Dup (3)", "次は Dup (3)：" + ViahDatabase.UniqueProfileName("Dup"));
            Check(ViahDatabase.UniqueProfileName("Dup (2)") == "Dup (3)", "「Dup (2)」から作っても Dup (3)");
            Check(ViahDatabase.UniqueProfileName("dup") == "dup (3)", "大文字・小文字は同じ名前として扱う");
            Check(ViahDatabase.UniqueProfileName("Ｄｕｐ") == "Ｄｕｐ", "全角の名前もそのまま使える");
            Check(ViahDatabase.UniqueProfileName("Dup", second?.id) == "Dup (2)" && ViahDatabase.UniqueProfileName("Dup (2)", second?.id) == "Dup (2)", "自分自身とは重ならない（Dup (2) を Dup にしようとしても Dup (2) のまま）");
        }

        // ───────────── Unity のインポート画面で入れる（衝突あり） ─────────────

        static void StartInteractive()
        {
            Log("== Unity のインポート画面で入れる（人が「Import」を押す）");
            AssetDatabase.DeleteAsset(Gimmick + "/Tex.png");
            ReplaceWithNewGuid(Gimmick + "/Extra.anim");
            var s = PackageImportRunner.Single;
            s.jobs.Clear();
            var job = PackageImportRunner.NewJob(PkgGimmick, true);
            job.interactive = true;
            s.jobs.Add(job);
            PackageImportRunner.Start(s);
            Check(s.running, "実行を始めた（インポート画面が開くはず）");
        }

        static bool WaitInteractive()
        {
            var s = PackageImportRunner.Single;
            if (s.running && !TimedOut(180)) return false;
            var j = s.jobs[0];
            Check(j.status == "done", "完了：" + j.status + " " + j.message);
            Check(AssetRef.Exists(Gimmick + "/Tex.png"), "消したファイルが入った");
            var p = ViahDatabase.FindProfile(j.profileId);
            Check(p != null, "プロファイルを作った：" + string.Join(" / ", j.warnings.Concat(j.notes)));
            return true;
        }

        static void Finish()
        {
            EditorApplication.update -= Drive;
            driving = false;
            Log($"== 結果：OK {st.pass} / NG {st.fail}");
            var text = string.Join("\n", st.log);
            // 片付け
            try
            {
                if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
                PackageImportRunner.Sequence.jobs.Clear();
                PackageImportRunner.Single.jobs.Clear();
                PackageImportRunner.DirOverride = null;
                PackageImportRunner.ResetCache();
                ViahStorage.DataDirOverride = null;
                ViahDatabase.Reload();
                if (Directory.Exists(Work)) Directory.Delete(Work, true);
            }
            catch (Exception e)
            {
                text += "\n片付けで失敗: " + e.Message;
            }
            File.Delete(StatePath);
            File.WriteAllText(LogPath, text);
        }
    }
}
