# V.inc Avatar Hub（VIAH）

VRChat 制作向けの Unity エディター拡張です。
**案件フォルダーの自動作成 → 「何を含めるか」のプロファイル登録 → UnityPackage の書き出し（単発・シーケンス）** を 1 つのウィンドウで行います。
複数の unitypackage をまとめて入れる **シーケンスインポート** や、手で作った unitypackage からのプロファイル作成、
よく使うフォルダーへすぐ飛べる **プロジェクトブックマーカー**、アセットを使っているものを探す **アセットSearcher** もあります。
今後、ほかのワークフローもこのハブに追加していきます。

> ⚠ **社内の許可された人のみ使用できます。** 配信 URL を社外に共有しないでください。詳しくは [利用規約](LICENSE.md) を参照してください。

## インストール（VCC）

**かんたん：** 次の URL を開くと VCC が起動し、そのまま追加できます（ブラウザの確認で「開く」を押してください）。

https://temu-chan.github.io/vinc-avatar-hub/add/

**手動で追加する場合：**

1. VRChat Creator Companion（VCC）を開き、**Settings → Packages → Add Repository** を押します。
2. 次の URL を貼り付けて **Add** を押します。
   ```
   https://temu-chan.github.io/vinc-avatar-hub/index.json
   ```
3. プロジェクトの **Manage Project** を開き、一覧の **V.inc Avatar Hub** の ＋ を押して追加します。
4. Unity のメニュー **V.inc → Avatar Hub** から開きます。

> 旧名「V.inc Asset Organizer」（0.1.x）を入れていた場合は、VCC でそれを削除してから上の手順で追加し直してください。
> データ（プリセット・プロジェクト記録・プロファイル）は初回起動時に自動で引き継がれます。

## アップデート

新しいバージョンが公開されると、VCC の Manage Project に更新の印が付きます。バージョンを選んで更新してください。

## 使い方

ホーム画面の「何をしたいですか？」から、やりたいことを選ぶと画面の画像つきで手順を説明します。
詳しい仕様は [パッケージの README](Packages/com.vinc.avatar-hub/README.md) を参照してください。

---

## 開発者向け：新しいバージョンを公開する手順

1. `Packages/com.vinc.avatar-hub/package.json` の `version` を上げてコミット・プッシュします（例: `0.2.0` → `0.2.1`）。
2. GitHub の **Actions → Build Release → Run workflow** を実行します。
   - zip と unitypackage を作ってリリースを作成し、続けて **Build Repo Listing** が自動で走り、VCC 用の一覧（GitHub Pages）が更新されます。

このリポジトリは VRChat 公式の [template-package](https://github.com/vrchat-community/template-package) をもとにしています。
リポジトリ変数 `PACKAGE_NAME` = `com.vinc.avatar-hub`、Pages のソース = GitHub Actions が必要です。

ガイドのスクリーンショットは、`VINC_VIAH_DEV` を定義した状態でメニュー **V.inc → 開発 → ガイド画像を撮り直す** から作り直せます。
