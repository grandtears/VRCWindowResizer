using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VRChatWindowSizer;

/// <summary>
/// ユーザーがメモ帳などで追記できるプリセット一覧です。
/// アプリはファイルが無いときだけ初期内容を作り、以後はユーザーの編集を上書きしません。
/// </summary>
internal static class PresetFile
{
    /// <summary>ボタン1つ分の幅、高さ、任意の表示名です。</summary>
    internal readonly record struct Preset(int Width, int Height, string? Name);

    /// <summary>プリセットの保存先です。復元用の saved-window.json と同じフォルダに置きます。</summary>
    internal static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VRChatWindowSizer", "presets.txt");

    /// <summary>
    /// 初めてファイルを作るときの内容です。行末はメモ帳でも改行されるよう CR+LF にしています。
    /// 「初期」サイズは検出結果なので、このファイルには含めません。
    /// </summary>
    internal const string DefaultText =
        "# VRChat Window Sizer のプリセットです。UTF-8 で保存してください。\r\n" +
        "# 1行に1つ。幅と高さのあいだは空白、x、×、*、カンマのどれでも書けます。\r\n" +
        "# 行末に名前を書くとボタンに表示します。# で始まる行は無視します。\r\n" +
        "# 「初期」ボタンは検出したサイズで、このファイルには書きません。\r\n" +
        "# 例: 800 600 作業用\r\n" +
        "\r\n" +
        "960 540\r\n" +
        "1280 720\r\n" +
        "1600 900\r\n" +
        "1920 1080\r\n" +
        "2560 1440\r\n" +
        "1 1 負荷軽減\r\n";

    /// <summary>「1280 720 名前」「1280x720」「1280×720」「1280,720」「1280*720」を同じ形として読みます。</summary>
    private static readonly Regex LinePattern = new(
        @"^(?<w>\d+)\s*(?:[xX×,*]\s*|\s+)(?<h>\d+)(?:\s+(?<name>.+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// ファイルが無いときだけ初期内容を作ります。
    /// BOM 付き UTF-8 にして、メモ帳が日本語を別の文字コードで開き直さないようにします。
    /// </summary>
    internal static void EnsureExists()
    {
        if (File.Exists(FilePath)) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, DefaultText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>
    /// テキストを上から順にプリセットへ変換します。空行と # で始まる行は読み飛ばします。
    /// 幅または高さが 0 以下、整数として読めない行は結果に入れず、行番号だけを error へまとめます。
    /// 範囲外でも 1 以上の値はボタンにし、適用時のチェックへ任せます。
    /// </summary>
    internal static IReadOnlyList<Preset> Parse(string text, out string? error)
    {
        var presets = new List<Preset>();
        var bad = new List<int>();
        int lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var match = LinePattern.Match(line);
            if (!match.Success
                || !int.TryParse(match.Groups["w"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(match.Groups["h"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int height)
                || width <= 0 || height <= 0)
            {
                bad.Add(lineNo);
                continue;
            }
            string? name = match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : null;
            if (name?.Length == 0) name = null;
            presets.Add(new Preset(width, height, name));
        }
        error = bad.Count == 0 ? null : $"プリセットの{string.Join("、", bad.Take(5).Select(n => $"{n}行目"))}を読み込めませんでした。";
        return presets;
    }
}
