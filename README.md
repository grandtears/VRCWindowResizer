# VRChat Window Sizer

VRChatのゲーム画面部分（クライアント領域）をプリセットまたは任意のピクセル数に変更するWPFツールです。

## 起動

Windows 10/11（64ビット）で動作します。このフォルダの `VRChatWindowSizer.exe` だけで起動できます。使い方は同じ場所の `使い方.txt` です。VRChatはウィンドウモードにしてください。

- プリセットは `%LOCALAPPDATA%/VRChatWindowSizer/presets.txt` に保存します。1行に `幅 高さ`（`1280x720` や `1280×720` も可）と書け、行末の名前はボタンに表示します。`#` で始まる行はコメントです。追記して保存すると、起動中でも一覧に反映されます。
- 初回起動時の内容: 960×540 / 1280×720 / 1600×900 / 1920×1080 / 2560×1440 / 1×1（負荷軽減）
- ウィンドウモードのVRChatを検出すると、そのサイズを「初期」プリセットとして保存します。このサイズは presets.txt には書かず、プリセットや「現在の状態を保存」では上書きしません。
- 初めてサイズを変更する直前のサイズと画面上の位置を自動保存します。
- 保存した位置とサイズはプリセットを繰り返し押しても上書きしません。アプリ再起動後も復元できます。
- 1×1は描画負荷を下げるためのサイズです。幅と高さの通常の指定範囲は320～16384、200～16384です。
- 「現在の状態を保存」で保存内容を更新できます。
- 「保存した状態に戻す」でサイズと位置を復元します。
- 保存先: `%LOCALAPPDATA%/VRChatWindowSizer/saved-window.json`

最大化・最小化・フルスクリーン中の変更は受け付けません。画面より大きいプリセットは画面外にはみ出す場合があります。複数のVRChatウィンドウがある場合は最も広い表示領域を対象にします。VRChatが管理者権限で起動されている場合、権限差により操作できないことがあります。

WindowsのウィンドウAPIだけを使用し、VRChatのプロセスメモリや設定ファイルは変更しません。ゲーム側がサイズを戻した場合は反映失敗を表示します。アプリ終了時の自動復元は行いません。

## ビルド

```powershell
Set-Location VRChatWindowSizer
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ..
```
