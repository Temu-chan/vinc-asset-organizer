# V.inc Asset Organizer

VRChat ギミック制作向けの Unity エディター拡張です。
**案件フォルダーの自動作成 → 「何を含めるか」のプロファイル登録 → UnityPackage の書き出し（単発・シーケンス）** を 1 つのウィンドウで行います。

> ⚠ **社内の許可された人のみ使用できます。** 配信 URL を社外に共有しないでください。詳しくは [利用規約](LICENSE.md) を参照してください。

## インストール（VCC）

**かんたん：** 次の URL を開くと VCC が起動し、そのまま追加できます（ブラウザの確認で「開く」を押してください）。

https://temu-chan.github.io/vinc-asset-organizer/add/

**手動で追加する場合：**

1. VRChat Creator Companion（VCC）を開き、**Settings → Packages → Add Repository** を押します。
2. 次の URL を貼り付けて **Add** を押します。
   ```
   https://temu-chan.github.io/vinc-asset-organizer/index.json
   ```
3. プロジェクトの **Manage Project** を開き、一覧の **V.inc Asset Organizer** の ＋ を押して追加します。
4. Unity のメニュー **V.inc → Asset Organizer** から開きます。

## アップデート

新しいバージョンが公開されると、VCC の Manage Project に更新の印が付きます。バージョンを選んで更新してください。

## 使い方

ウィンドウのホーム画面に、3 ステップ（プロジェクト作成 → プロファイル登録 → エクスポート）の案内があります。
詳しい仕様は [パッケージの README](Packages/com.vinc.asset-organizer/README.md) を参照してください。

---

## 開発者向け：新しいバージョンを公開する手順

1. `Packages/com.vinc.asset-organizer/package.json` の `version` を上げてコミット・プッシュします（例: `0.1.0` → `0.1.1`）。
2. GitHub の **Actions → Build Release → Run workflow** を実行します。
   - zip と unitypackage を作ってリリースを作成し、続けて **Build Repo Listing** が自動で走り、VCC 用の一覧（GitHub Pages）が更新されます。

このリポジトリは VRChat 公式の [template-package](https://github.com/vrchat-community/template-package) をもとにしています。
リポジトリ変数 `PACKAGE_NAME` = `com.vinc.asset-organizer`、Pages のソース = GitHub Actions が必要です。
