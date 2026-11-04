using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VRChatWindowSizer;

/// <summary>
/// VRChat のクライアント領域をプリセットまたは手入力のサイズへ変更する画面です。
/// ゲームのメモリや設定ファイルには触れず、ウィンドウの位置と大きさだけを扱います。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>サイズと位置の復元データです。プリセット一覧とは別ファイルです。</summary>
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRChatWindowSizer", "saved-window.json");

    /// <summary>VRChat の有無と presets.txt の更新を、1秒ごとに確認します。</summary>
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    /// <summary>「保存した状態に戻す」で使うサイズと位置です。未保存のときは null です。</summary>
    private WindowState? saved;

    /// <summary>変更直後に期待するサイズです。反映確認が終わると null に戻します。</summary>
    private WindowState? expected;

    /// <summary>この時刻以降に、指定サイズが実際のクライアント領域と一致するか確認します。</summary>
    private DateTime verifyAfter;

    /// <summary>
    /// 最初に確定したゲーム画面の幅と高さです。0 のあいだは未確定です。
    /// 一度入れると、プリセットや「現在の状態を保存」では上書きしません。
    /// </summary>
    private int initialWidth;
    private int initialHeight;

    /// <summary>初期サイズを確定する前に、同じサイズが続いているかを数えます。</summary>
    private int seenWidth;
    private int seenHeight;
    private int seenCount;

    /// <summary>前回ボタンへ反映した presets.txt の全文です。同じ内容ならボタンを作り直しません。</summary>
    private string? loadedPresetText;

    /// <summary>
    /// saved-window.json の形です。古い版は Width、Height、X、Y だけでした。
    /// InitialWidth が 0 のファイルは旧形式とみなし、その幅と高さを初期サイズにも使います。
    /// RestoreSaved が false のときは初期サイズだけを控えており、位置の復元データはまだありません。
    /// </summary>
    private record Persisted(int Width, int Height, int X, int Y, int InitialWidth = 0, int InitialHeight = 0, bool RestoreSaved = false);

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            if (File.Exists(settingsPath)) Load();
        }
        catch (Exception e) { StatusText.Text = $"保存データを読み込めません: {e.Message}"; }
        UpdateSaved();
        ShowInitialInInputs();
        EditPresetsButton.ToolTip = PresetFile.FilePath;
        ReloadPresets();
        timer.Tick += (_, _) =>
        {
            ReloadPresets();
            Refresh();
        };
        // エディタから戻った直後にも、保存済みの追記を一覧へ反映します。
        Activated += (_, _) => ReloadPresets();
        Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh();
    }

    /// <summary>
    /// presets.txt を読み、内容が変わったときだけボタンを作り直します。
    /// 読めない行があっても、読めた行のボタンは表示します。
    /// 読み取りに失敗したときは今のボタンを残し、次の確認でやり直します。
    /// </summary>
    private void ReloadPresets()
    {
        try
        {
            PresetFile.EnsureExists();
            string text = File.ReadAllText(PresetFile.FilePath);
            if (text == loadedPresetText) return;
            var presets = PresetFile.Parse(text, out var error);
            ShowPresets(presets);
            loadedPresetText = text;
            if (error == null)
            {
                // サイズ変更の結果表示は消さず、プリセットのエラー表示だけを片付けます。
                if (StatusText.Text?.StartsWith("プリセット") == true) StatusText.Text = "";
            }
            else StatusText.Text = error;
        }
        catch (Exception e)
        {
            string message = $"プリセットを読み込めません: {e.Message}";
            if (StatusText.Text != message) StatusText.Text = message;
        }
    }

    /// <summary>
    /// ファイル由来のプリセットボタンを並べます。
    /// 「初期」ボタンは XAML で作ってあり、検出サイズを示すので、ここでは残します。
    /// </summary>
    private void ShowPresets(IReadOnlyList<PresetFile.Preset> presets)
    {
        for (int i = PresetPanel.Children.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(PresetPanel.Children[i], InitialPresetButton))
                PresetPanel.Children.RemoveAt(i);
        foreach (var preset in presets)
        {
            var button = new Button
            {
                Content = preset.Name == null ? $"{preset.Width} × {preset.Height}" : $"{preset.Name}（{preset.Width} × {preset.Height}）",
                Tag = $"{preset.Width},{preset.Height}",
                ToolTip = $"{preset.Width} × {preset.Height} px"
            };
            button.Click += PresetClick;
            PresetPanel.Children.Add(button);
        }
    }

    /// <summary>関連付けられたエディタで presets.txt を開きます。</summary>
    private void OpenPresetFile(object sender, RoutedEventArgs e) => Run(() =>
    {
        PresetFile.EnsureExists();
        Process.Start(new ProcessStartInfo(PresetFile.FilePath) { UseShellExecute = true });
    });

    /// <summary>保存済みの復元データと初期サイズを読みます。</summary>
    private void Load()
    {
        var stored = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(settingsPath));
        if (stored == null) return;
        initialWidth = stored.InitialWidth > 0 ? stored.InitialWidth : stored.Width;
        initialHeight = stored.InitialHeight > 0 ? stored.InitialHeight : stored.Height;
        // 旧形式には RestoreSaved が無いので、初期サイズが無いこと自体を復元データありとみなします。
        if (stored.RestoreSaved || stored.InitialWidth <= 0)
            saved = new WindowState(stored.Width, stored.Height, stored.X, stored.Y);
    }

    /// <summary>VRChat の現在のクライアントサイズを表示し、変更結果の確認と初期サイズの検出を行います。</summary>
    private void Refresh()
    {
        try
        {
            var handle = WindowApi.Find();
            if (handle == 0) { CurrentText.Text = "VRChatが見つかりません"; expected = null; seenCount = 0; return; }
            var state = WindowApi.Read(handle);
            CurrentText.Text = $"{state.Width} × {state.Height} px";
            RememberInitial(handle, state);
            if (expected != null && DateTime.UtcNow >= verifyAfter)
            {
                StatusText.Text = state.Width == expected.Width && state.Height == expected.Height
                    ? $"{state.Width} × {state.Height} に変更しました。"
                    : "指定サイズが反映されませんでした。VRChatの画面モードや権限を確認してください。";
                expected = null;
            }
        }
        catch (Exception e) { CurrentText.Text = "取得できません"; StatusText.Text = e.Message; }
    }

    /// <summary>
    /// ウィンドウモードのサイズが2回連続で同じだったとき、それを初期サイズとして保存します。
    /// 起動直後の一瞬だけ違うサイズを、初期サイズとして固定しないためです。
    /// 最小化、最大化、枠なしのあいだはカウントをやり直します。
    /// </summary>
    private void RememberInitial(nint handle, WindowState state)
    {
        if (initialWidth > 0) return;
        try { WindowApi.Check(handle); }
        catch (InvalidOperationException) { seenCount = 0; return; }
        if (state.Width == seenWidth && state.Height == seenHeight) seenCount++;
        else { seenWidth = state.Width; seenHeight = state.Height; seenCount = 1; }
        if (seenCount < 2) return;
        initialWidth = state.Width;
        initialHeight = state.Height;
        try { Persist(); }
        catch
        {
            // 保存に失敗した値を確定させず、次の確認で最初からやり直します。
            initialWidth = 0;
            initialHeight = 0;
            seenCount = 0;
            throw;
        }
        ShowInitialInInputs();
        UpdateSaved();
    }

    /// <summary>
    /// 幅と高さの入力欄が画面初期値の 1280 と 720 のままなら、検出した初期サイズへ置き換えます。
    /// ユーザーがすでに入力した値は残します。
    /// </summary>
    private void ShowInitialInInputs()
    {
        if (initialWidth <= 0) return;
        if (WidthInput.Text == "1280" && HeightInput.Text == "720")
        {
            WidthInput.Text = initialWidth.ToString();
            HeightInput.Text = initialHeight.ToString();
        }
    }

    /// <summary>復元データの説明と、「初期」ボタンの表示を現在の状態へ合わせます。</summary>
    private void UpdateSaved()
    {
        SavedText.Text = saved == null ? "未保存（初回変更時に自動保存）" : $"{saved.Width} × {saved.Height} px / 位置 {saved.X}, {saved.Y}";
        if (initialWidth > 0)
        {
            InitialPresetButton.Content = $"初期 {initialWidth} × {initialHeight}";
            InitialPresetButton.Tag = $"{initialWidth},{initialHeight}";
            InitialPresetButton.IsEnabled = true;
        }
        else
        {
            InitialPresetButton.Content = "初期サイズ";
            InitialPresetButton.IsEnabled = false;
        }
    }

    /// <summary>
    /// 戻すためのサイズと位置を保存します。
    /// 初期サイズがまだ無いときだけ、この時点の画面サイズを初期サイズにします。
    /// </summary>
    private void Save(WindowState state)
    {
        saved = state;
        if (initialWidth <= 0)
        {
            initialWidth = state.Width;
            initialHeight = state.Height;
        }
        Persist();
        UpdateSaved();
    }

    /// <summary>一時ファイルへ書いてから置き換え、保存の途中でアプリが止まっても JSON が欠けないようにします。</summary>
    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var stored = new Persisted(
            saved?.Width ?? initialWidth,
            saved?.Height ?? initialHeight,
            saved?.X ?? 0,
            saved?.Y ?? 0,
            initialWidth,
            initialHeight,
            saved != null);
        string temp = settingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(stored, jsonOptions));
        File.Move(temp, settingsPath, true);
    }

    /// <summary>ボタン操作の失敗を、状態表示へそのまま出します。</summary>
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception e) { StatusText.Text = e.Message; }
    }

    /// <summary>
    /// クライアント領域を指定サイズへ変えます。位置は現在の場所のままです。
    /// 初めて変更する直前のサイズと位置は、復元用に自動で保存します。
    /// </summary>
    private void Resize(int width, int height)
    {
        WindowApi.EnsureSize(width, height);
        var handle = WindowApi.Find();
        WindowApi.Check(handle);
        var current = WindowApi.Read(handle);
        if (saved == null) Save(current);
        Apply(handle, current with { Width = width, Height = height });
    }

    /// <summary>変更を依頼したあと、ゲーム側がサイズを戻していないか、少し待ってから確認します。</summary>
    private void Apply(nint handle, WindowState state)
    {
        WindowApi.Apply(handle, state);
        expected = state;
        verifyAfter = DateTime.UtcNow.AddMilliseconds(800);
        StatusText.Text = "サイズを変更しています…";
        Refresh();
    }

    /// <summary>ボタンの Tag に入れた「幅,高さ」を、クライアント領域へ適用します。</summary>
    private void PresetClick(object sender, RoutedEventArgs e) => Run(() =>
    {
        var values = ((string)((Button)sender).Tag).Split(',');
        WidthInput.Text = values[0]; HeightInput.Text = values[1];
        Resize(int.Parse(values[0]), int.Parse(values[1]));
    });
    private void CustomClick(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!int.TryParse(WidthInput.Text, out int width) || !int.TryParse(HeightInput.Text, out int height))
            throw new InvalidOperationException("幅と高さを整数で入力してください。");
        Resize(width, height);
    });
    private void SaveClick(object sender, RoutedEventArgs e) => Run(() =>
    {
        var handle = WindowApi.Find(); WindowApi.Check(handle);
        Save(WindowApi.Read(handle)); StatusText.Text = "現在のサイズと位置を保存しました。";
    });
    private void RestoreClick(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (saved == null) throw new InvalidOperationException("保存したサイズがありません。");
        Apply(WindowApi.Find(), saved);
    });
}
