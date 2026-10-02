# TaikoNauts ModLoader Installer

[English](README.md) | 日本語

[TaikoNauts ModLoader](https://github.com/aightallthing/taikonauts-mod-loader) と
[NULM Background](https://github.com/36536pu4jt98g3ju095jk/taikonauts-nulm-background-mod) MOD を、自動で導入する Windows 用のインストーラーです。`TaikoNauts.exe` を選んで **インストール** を押すだけです。

## 使い方

1. TaikoNauts と Mod Manager を終了します。
2. `TaikoNautsModloaderInstaller.exe` を実行します。
3. **参照...** で `TaikoNauts.exe` を選びます(ウィンドウへのドラッグ&ドロップも可)。前回の選択は覚えています。
4. **インストール** を押します。
5. 作成された `Lumens` フォルダに NULM のパックを置いて、ゲームを起動します。

ウィンドウには、すでに導入済みのものが表示されます。ModLoader、MOD、`Lumens` フォルダは、それぞれチェックを外して省くこともできます。

## やること

1. **ModLoader**: 最新のリリースをダウンロードして、ゲームのフォルダに展開し、ModLoader 自身の `install.ps1` を実行します。そのため、元の `raylib.dll` の確認など、ModLoader 側のチェックがそのまま働きます。すでに最新の場合は、ダウンロードし直しません。
2. **NULM Background**: 最新のリリースを `mods\nulm-background` に導入します。すでにある `config.json` とパックはそのまま残します。
3. **Lumens**: ゲームが使っているスキン(または選んだスキン)に `Skins\<スキン>\Lumens` を作り、パックの置き場所を書いたメモを入れます。

NULM のデータや画像は導入しません。パックは各自で用意してください。

## 安全面

- ダウンロードするのは、上の 2 つの公開 GitHub リポジトリだけです。
- すべてのダウンロードを、GitHub が公開しているリリースファイルの SHA-256 と照合します。一致しなければ、そのファイルは破棄します。
- 展開先のフォルダの外に出てしまうエントリを含むパッケージは、拒否します。
- `TaikoNauts.exe` は書き換えません。未対応または書き換え済みの `raylib.dll` は、ModLoader のインストーラーが置き換えを拒否し、そのメッセージを表示します。
- 現在のユーザー権限で動き、管理者権限は要求しません。ゲームが保護されたフォルダにある場合は、管理者として実行してください。
- 実行ファイルにコード署名はないので、Windows SmartScreen の警告が出ることがあります。

## コマンドライン

```text
TaikoNautsModloaderInstaller.exe --game <TaikoNauts.exe のパス> [オプション]
```

| オプション | 意味 |
| --- | --- |
| `--no-loader` / `--no-mod` / `--no-lumens` | その手順を省きます。 |
| `--skin <名前>` | `Lumens` フォルダを作るスキン(初期値は使用中のスキン)。 |
| `--force` | ModLoader が最新でも、入れ直します。 |
| `--loader-zip <ファイル>` / `--mod-zip <ファイル>` | ダウンロードの代わりに、手元のパッケージを使います(オフライン用)。 |
| `--log <ファイル>` | ログをファイルにも書き出します。 |
| `--lang ja\|en` | メッセージの言語。 |

終了コード: `0` 成功、`1` インストールに失敗、`2` `TaikoNauts.exe` が見つからない。

## アンインストール

ゲームのフォルダの `uninstall.bat` を実行すると、元の `raylib.dll` に戻せます。MOD を外すには、`mods\nulm-background` を削除します。

## ビルド

Windows と .NET 8 SDK が必要です。

```powershell
.\scripts\build.ps1
```

`dist\TaikoNautsModloaderInstaller.exe`(自己完結の単一ファイル、約 68 MB)ができます。`tests\integration.ps1` は、手元のパッケージを使って使い捨てのフォルダに導入し、ネットワークを必要としません。

```powershell
.\tests\integration.ps1 -Exe .\dist\TaikoNautsModloaderInstaller.exe `
    -LoaderZip <ModLoader の zip> -ModZip <MOD の zip> -OriginalRaylib <元の raylib.dll>
```

## ライセンス

MIT。[LICENSE](LICENSE) を参照してください。
