using SeiriTUI.Models;
using SeiriTUI.ViewModels;
using Terminal.Gui;

namespace SeiriTUI.Views;

public class MainWindow : Window
{
    public readonly MainViewModel ViewModel;

    private ListView _leftListView;
    private ListView _rightListView;

    // Detail fields - required
    private TextField _detailSeasonField;
    private TextField _detailEpisodeField;
    private TextField _detailLanguageField;
    private Button _detailResolutionBtn;
    private Button _detailQualityBtn;

    // Detail fields - optional (可选项参数区)
    private Button _detailVideoCodecBtn;
    private Button _detailBitDepthBtn;
    private Button _detailAudioCodecBtn;
    private Button _detailAudioChannelBtn;
    private TextField _detailReleaseGroupField;

    // Detail fields - Readonly labels for name preview
    private Label _detailOriginalNameLabel;
    private Label _detailTargetNameLabel;
    private Label _detailMatchedVideoLabel;

    // Frame references for dynamic title updates
    private FrameView _leftFrame;
    private FrameView _rightFrame;

    private readonly List<string> _resolutions = new() { "(无)", "2160p", "1080p", "720p", "480p" };
    private readonly List<string> _qualities = new() { "(无)", "WEBDL", "WEBRip", "BD", "BDRip", "HDTV", "DVD" };
    private readonly List<string> _videoCodecs = new() { "(无)", "x264", "x265", "AV1" };
    private readonly List<string> _bitDepths = new() { "(无)", "8bit", "10bit" };
    private readonly List<string> _audioCodecs = new() { "(无)", "AAC", "FLAC", "AC-3", "E-AC-3", "TrueHD", "DTS", "OPUS", "MP3" };
    private readonly List<string> _audioChannels = new() { "(无)", "2.0", "2.1", "5.1", "7.1" };

    public MainWindow() : base("SeiriTUI · Modern Edition")
    {
        var fileOpsService = new Services.FileOperationService();
        ViewModel = new MainViewModel(fileOpsService);

        // 自定义现代化暗黑主题色 (类 VSCode/JetBrains 纯黑背景)
        ColorScheme = new ColorScheme()
        {
            Normal = Application.Driver.MakeAttribute(Color.White, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.White, Color.DarkGray),
            HotNormal = Application.Driver.MakeAttribute(Color.BrightCyan, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.BrightCyan, Color.DarkGray)
        };

        BuildUi();

        // 提取系统被打包时写入的版本号 (Github Actions 中通过 -p:Version=x.y.z 注入)
        string ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
        if (ver.EndsWith(".0"))
        {
            var parts = ver.Split('.');
            if (parts.Length > 2) ver = $"{parts[0]}.{parts[1]}.{parts[2]}";
        }

        var btnAbout = new Button("关于")
        {
            X = Pos.AnchorEnd(10),
            Y = 0,
            // Button 本身自带主题，也可以单独赋色：
            ColorScheme = new ColorScheme() { Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black) }
        };

        btnAbout.Clicked += () =>
        {
            string aboutText =
$@"Seiri-TUI · 现代化终端刮削辅助工具

当前版本: v{ver}

开发者: Gladtbam
开源仓库: https://github.com/Gladtbam/Seiri-TUI";

            MessageBox.Query("关于 (About)", aboutText, "确定");
        };

        Add(btnAbout);
        UpdateFrameTitles();
    }

    private class CycleButton : Button
    {
        public Action? CycleNext { get; set; }
        public Action? CyclePrev { get; set; }

        public CycleButton(string text) : base(text) { }

        public override bool MouseEvent(MouseEvent me)
        {
            if (me.Flags.HasFlag(MouseFlags.WheeledDown))
            {
                CycleNext?.Invoke();
                return true;
            }
            if (me.Flags.HasFlag(MouseFlags.WheeledUp))
            {
                CyclePrev?.Invoke();
                return true;
            }
            return base.MouseEvent(me);
        }
    }

    private Button CreateCycleButton(List<string> options, string currentValue, Action<string> onValueChanged)
    {
        string text = string.IsNullOrEmpty(currentValue) ? "(无)" : currentValue;
        if (!options.Contains(text)) text = "(无)";
        
        var btn = new CycleButton(text);
        
        btn.CycleNext = () =>
        {
            var current = btn.Text.ToString();
            int idx = options.IndexOf(current ?? "");
            idx = (idx + 1) % options.Count;
            btn.Text = options[idx];
            onValueChanged(options[idx] == "(无)" ? "" : options[idx]);
        };

        btn.CyclePrev = () =>
        {
            var current = btn.Text.ToString();
            int idx = options.IndexOf(current ?? "");
            idx = (idx - 1 + options.Count) % options.Count;
            btn.Text = options[idx];
            onValueChanged(options[idx] == "(无)" ? "" : options[idx]);
        };

        btn.Clicked += btn.CycleNext;
        
        return btn;
    }

    private void BuildUi()
    {
        Action? updateVisibility = null;

        // ======================= 顶部全局控制区 =======================
        var topFrame = new FrameView("Global Console")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 10,
            Border = new Border() { BorderStyle = BorderStyle.Rounded }
        };

        var lblShowName = new Label("名称 (Show):") { X = 1, Y = 0 };
        var txtShowName = new TextField(ViewModel.GlobalShowName) { X = 16, Y = 0, Width = 28 };
        txtShowName.TextChanged += (e) =>
        {
            ViewModel.GlobalShowName = txtShowName.Text.ToString() ?? "";
            RefreshLists();
        };

        var lblSeason = new Label("季:") { X = 47, Y = 0 };
        var txtSeason = new TextField(ViewModel.GlobalSeason.ToString()) { X = 56, Y = 0, Width = 5 };
        txtSeason.TextChanged += (e) =>
        {
            if (int.TryParse(txtSeason.Text.ToString(), out int s)) ViewModel.GlobalSeason = s;
            RefreshLists();
        };

        var lblStartEp = new Label("起始集数:") { X = 65, Y = 0 };
        var txtStartEp = new TextField("") { X = 76, Y = 0, Width = 5 };
        txtStartEp.TextChanged += (e) =>
        {
            if (int.TryParse(txtStartEp.Text.ToString(), out int ep)) ViewModel.StartEpisode = ep; else ViewModel.StartEpisode = null;
            RefreshLists();
        };

        // ========= 新增的全局分辨率与质量 =========
        var lblGlobalRes = new Label("全局分辨率:") { X = 1, Y = 1 };
        var btnGlobalRes = CreateCycleButton(_resolutions, ViewModel.GlobalResolution, val => { ViewModel.GlobalResolution = val; RefreshLists(); });
        btnGlobalRes.X = Pos.Right(lblGlobalRes) + 1; btnGlobalRes.Y = 1;

        var lblGlobalQa = new Label("全局质量:") { X = Pos.Right(btnGlobalRes) + 2, Y = 1 };
        var btnGlobalQa = CreateCycleButton(_qualities, ViewModel.GlobalQuality, val => { ViewModel.GlobalQuality = val; RefreshLists(); });
        btnGlobalQa.X = Pos.Right(lblGlobalQa) + 1; btnGlobalQa.Y = 1;

        var lblGlobalLang = new Label("默认字幕语言:") { X = Pos.Right(btnGlobalQa) + 2, Y = 1 };
        var txtGlobalLang = new TextField(ViewModel.DefaultSubtitleLanguage) { X = Pos.Right(lblGlobalLang) + 1, Y = 1, Width = 8 };
        txtGlobalLang.TextChanged += (e) =>
        {
            ViewModel.DefaultSubtitleLanguage = txtGlobalLang.Text.ToString() ?? "";
            RefreshLists();
        };
        // ===========================================

        // ========= 新增的全局补充参数 (Y=2) =========
        var lblGlobalVC = new Label("视频编码:") { X = 1, Y = 2 };
        var btnGlobalVC = CreateCycleButton(_videoCodecs, ViewModel.GlobalVideoCodec, val => { ViewModel.GlobalVideoCodec = val; RefreshLists(); });
        btnGlobalVC.X = Pos.Right(lblGlobalVC) + 1; btnGlobalVC.Y = 2;

        var lblGlobalBD = new Label("色深:") { X = Pos.Right(btnGlobalVC) + 2, Y = 2 };
        var btnGlobalBD = CreateCycleButton(_bitDepths, ViewModel.GlobalBitDepth, val => { ViewModel.GlobalBitDepth = val; RefreshLists(); });
        btnGlobalBD.X = Pos.Right(lblGlobalBD) + 1; btnGlobalBD.Y = 2;

        var lblGlobalAC = new Label("音频编码:") { X = Pos.Right(btnGlobalBD) + 2, Y = 2 };
        
        // 我们需要先声明 btnGlobalACh，以便在 AC 发生变化时能更新它
        Button btnGlobalACh = null!;
        var btnGlobalAC = CreateCycleButton(_audioCodecs, ViewModel.GlobalAudioCodec, val => 
        { 
            ViewModel.GlobalAudioCodec = val;
            string? autoChannel = MainViewModel.GetDefaultChannelForCodec(val);
            if (autoChannel != null)
            {
                btnGlobalACh.Text = autoChannel;
                ViewModel.GlobalAudioChannel = autoChannel;
            }
            RefreshLists(); 
        });
        btnGlobalAC.X = Pos.Right(lblGlobalAC) + 1; btnGlobalAC.Y = 2;

        var lblGlobalACh = new Label("声道:") { X = Pos.Right(btnGlobalAC) + 2, Y = 2 };
        btnGlobalACh = CreateCycleButton(_audioChannels, ViewModel.GlobalAudioChannel, val => { ViewModel.GlobalAudioChannel = val; RefreshLists(); });
        btnGlobalACh.X = Pos.Right(lblGlobalACh) + 1; btnGlobalACh.Y = 2;

        var lblGlobalRG = new Label("全局发布组:") { X = Pos.Right(btnGlobalACh) + 2, Y = 2 };
        var txtGlobalRG = new TextField(ViewModel.GlobalReleaseGroup) { X = Pos.Right(lblGlobalRG) + 1, Y = 2, Width = 15 };
        txtGlobalRG.TextChanged += (e) => { ViewModel.GlobalReleaseGroup = txtGlobalRG.Text.ToString() ?? ""; RefreshLists(); };
        // ===========================================

        var lblSourcePath = new Label("扫描源目录:") { X = 1, Y = 4 };
        var txtSourcePath = new TextField(Directory.GetCurrentDirectory()) { X = 16, Y = 4, Width = 45 };
        SetupPathAutocomplete(txtSourcePath);

        var lblTargetPath = new Label("输出根路径:") { X = 1, Y = 5 };
        var txtTargetPath = new TextField(Directory.GetCurrentDirectory()) { X = 16, Y = 5, Width = 45 };
        SetupPathAutocomplete(txtTargetPath);

        var btnScan = new Button("执行扫描") { X = 65, Y = 4 };
        btnScan.Clicked += () =>
        {
            string sourcePath = txtSourcePath.Text.ToString() ?? "";
            ViewModel.LoadMediaFilesFromDirectory(sourcePath);
            ViewModel.TargetRootPath = sourcePath; // 自动将目标目录设为源目录
            txtTargetPath.Text = sourcePath;       // 同步更新 UI 输入框
            RefreshLists();
        };

        txtTargetPath.TextChanged += (e) =>
        {
            ViewModel.TargetRootPath = txtTargetPath.Text.ToString() ?? "";
            RefreshLists();
        };

        var cbAutoSeason = new CheckBox("自动创建 Season 文件夹", ViewModel.AutoCreateSeasonFolder) { X = 65, Y = 5 };
        cbAutoSeason.Toggled += (prev) =>
        {
            ViewModel.AutoCreateSeasonFolder = cbAutoSeason.Checked;
            RefreshLists();
        };

        var cbSelectionMode = new CheckBox("启用部分勾选模式", ViewModel.SelectionModeEnabled) { X = 95, Y = 5 };

        // 工作模式切换
        var lblWorkMode = new Label("工作模式:") { X = 95, Y = 4 };
        var btnWorkMode = CreateCycleButton(new List<string> { "常规", "电影", "字幕" }, "常规", val =>
        {
            ViewModel.MovieMode = val == "电影";
            ViewModel.SubtitleMatchingMode = val == "字幕";
            UpdateFrameTitles();
            RefreshLists();
            updateVisibility?.Invoke();
        });
        btnWorkMode.X = Pos.Right(lblWorkMode) + 1; btnWorkMode.Y = 4;

        var btnSelectAll = new Button("全选") { X = 65, Y = 6, Visible = false };
        var btnDeselectAll = new Button("全不选") { X = 77, Y = 6, Visible = false };
        var btnInvertSel = new Button("反选") { X = 91, Y = 6, Visible = false };

        btnSelectAll.Clicked += () => { ViewModel.SelectAll(); RefreshLists(); };
        btnDeselectAll.Clicked += () => { ViewModel.DeselectAll(); RefreshLists(); };
        btnInvertSel.Clicked += () => { ViewModel.InvertSelection(); RefreshLists(); };

        cbSelectionMode.Toggled += (prev) =>
        {
            ViewModel.SelectionModeEnabled = cbSelectionMode.Checked;
            _leftListView.AllowsMarking = cbSelectionMode.Checked;
            btnSelectAll.Visible = cbSelectionMode.Checked;
            btnDeselectAll.Visible = cbSelectionMode.Checked;
            btnInvertSel.Visible = cbSelectionMode.Checked;
            RefreshLists();
        };

        // 执行操作面板
        var btnPanel = new View() { X = 1, Y = 7, Width = Dim.Fill(), Height = 1 };
        var btnMove = new Button("重命名(移动)") { X = 0, Y = 0 };
        var btnCopy = new Button("拷贝") { X = 20, Y = 0 };
        var btnLink = new Button("硬链接") { X = 32, Y = 0 };
        var btnExit = new Button("退出") { X = 48, Y = 0 };
        btnExit.Clicked += () => Application.RequestStop();

        // 绑定实际的 IO 处理命令与刷新操作
        btnMove.Clicked += async () =>
        {
            await ViewModel.ProcessMoveCommand.ExecuteAsync(null);
            Application.MainLoop.Invoke(() => { RefreshLists(); });
        };
        btnCopy.Clicked += async () =>
        {
            await ViewModel.ProcessCopyCommand.ExecuteAsync(null);
            Application.MainLoop.Invoke(() => { RefreshLists(); });
        };
        btnLink.Clicked += async () =>
        {
            await ViewModel.ProcessHardLinkCommand.ExecuteAsync(null);
            Application.MainLoop.Invoke(() => { RefreshLists(); });
        };

        btnPanel.Add(btnMove, btnCopy, btnLink, btnExit);

        topFrame.Add(lblShowName, txtShowName, lblSeason, txtSeason, lblStartEp, txtStartEp);
        topFrame.Add(lblGlobalRes, btnGlobalRes, lblGlobalQa, btnGlobalQa, lblGlobalLang, txtGlobalLang);
        topFrame.Add(lblGlobalVC, btnGlobalVC, lblGlobalBD, btnGlobalBD, lblGlobalAC, btnGlobalAC, lblGlobalACh, btnGlobalACh, lblGlobalRG, txtGlobalRG);
        topFrame.Add(lblSourcePath, txtSourcePath, btnScan);
        topFrame.Add(lblTargetPath, txtTargetPath, cbAutoSeason, cbSelectionMode, lblWorkMode, btnWorkMode, btnSelectAll, btnDeselectAll, btnInvertSel, btnPanel);


        // ======================= 中部对比列表 =======================
        var listContainer = new View() { X = 0, Y = Pos.Bottom(topFrame), Width = Dim.Fill(), Height = Dim.Fill(10) };

        _leftFrame = new FrameView("Input.Files ()")
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(50),
            Height = Dim.Fill(),
            Border = new Border() { BorderStyle = BorderStyle.Rounded }
        };
        _leftListView = new ListView() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), AllowsMarking = false };
        _leftFrame.Add(_leftListView);

        _rightFrame = new FrameView("Output.Preview ()")
        {
            X = Pos.Right(_leftFrame),
            Y = 0,
            Width = Dim.Percent(50),
            Height = Dim.Fill(),
            Border = new Border() { BorderStyle = BorderStyle.Rounded }
        };
        _rightListView = new ListView() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), AllowsMarking = false };
        _rightFrame.Add(_rightListView);

        listContainer.Add(_leftFrame, _rightFrame);
        _leftListView.SelectedItemChanged += OnSelectedItemChanged;

        // 当多选模式下用户按空格/点击更改了选中状态，我们需要将其同步到 ViewModel (但没有官方的 MarkChanged，所以我们用鼠标事件和键盘事件捕获)
        _leftListView.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key == Key.Space && _leftListView.AllowsMarking && _leftListView.SelectedItem >= 0)
            {
                // 让它自带的处理先过，然后再把状态同步到 IsSelected
                Application.MainLoop.Invoke(() =>
                {
                    ViewModel.MediaFiles[_leftListView.SelectedItem].IsSelected = _leftListView.Source.IsMarked(_leftListView.SelectedItem);
                });
            }
        };
        _leftListView.MouseClick += (e) =>
        {
            if (_leftListView.AllowsMarking && e.MouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
            {
                Application.MainLoop.Invoke(() =>
                {
                    if (_leftListView.SelectedItem >= 0)
                        ViewModel.MediaFiles[_leftListView.SelectedItem].IsSelected = _leftListView.Source.IsMarked(_leftListView.SelectedItem);
                });
            }
        };


        // ======================= 底部单体参数修改区 (中下部参数区) =======================
        var detailFrame = new FrameView("Item.Details ()")
        {
            X = 0,
            Y = Pos.Bottom(listContainer),
            Width = Dim.Fill(),
            Height = 9,
            Border = new Border() { BorderStyle = BorderStyle.Rounded }
        };

        // ---- 必须项参数区 (第 0 行): 季 / 集 / 分辨率 / 质量 / 语言 ----
        var lblDetSeason = new Label("季:") { X = 1, Y = 0 };
        _detailSeasonField = new TextField("") { X = Pos.Right(lblDetSeason) + 1, Y = 0, Width = 4 };
        _detailSeasonField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.Season = int.TryParse(_detailSeasonField.Text.ToString(), out int val) ? val : null);

        var lblDetEp = new Label("集:") { X = Pos.Right(_detailSeasonField) + 2, Y = 0 };
        _detailEpisodeField = new TextField("") { X = Pos.Right(lblDetEp) + 1, Y = 0, Width = 4 };
        _detailEpisodeField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.Episode = int.TryParse(_detailEpisodeField.Text.ToString(), out int val) ? val : null);

        var lblDetRes = new Label("分辨率:") { X = Pos.Right(_detailEpisodeField) + 2, Y = 0 };
        _detailResolutionBtn = CreateCycleButton(_resolutions, "", val => UpdateSelectedItemProperty(prop => prop.Resolution = val));
        _detailResolutionBtn.X = Pos.Right(lblDetRes) + 1; _detailResolutionBtn.Y = 0;

        var lblDetQa = new Label("质量:") { X = Pos.Right(_detailResolutionBtn) + 2, Y = 0 };
        _detailQualityBtn = CreateCycleButton(_qualities, "", val => UpdateSelectedItemProperty(prop => prop.Quality = val));
        _detailQualityBtn.X = Pos.Right(lblDetQa) + 1; _detailQualityBtn.Y = 0;

        var lblDetLang = new Label("语言:") { X = Pos.Right(_detailQualityBtn) + 2, Y = 0 };
        _detailLanguageField = new TextField("") { X = Pos.Right(lblDetLang) + 1, Y = 0, Width = 8 };
        _detailLanguageField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.Language = _detailLanguageField.Text.ToString());

        // ---- 可选项参数区 (第 1 行): 视频编码 / 色彩深度 / 音频编码 / 音频声道 / 发布组 ----
        var lblDetVC = new Label("视频编码:") { X = 1, Y = 2 };
        _detailVideoCodecBtn = CreateCycleButton(_videoCodecs, "", val => UpdateSelectedItemProperty(prop => prop.VideoCodec = val));
        _detailVideoCodecBtn.X = Pos.Right(lblDetVC) + 1; _detailVideoCodecBtn.Y = 2;

        var lblDetBD = new Label("色深:") { X = Pos.Right(_detailVideoCodecBtn) + 2, Y = 2 };
        _detailBitDepthBtn = CreateCycleButton(_bitDepths, "", val => UpdateSelectedItemProperty(prop => prop.BitDepth = val));
        _detailBitDepthBtn.X = Pos.Right(lblDetBD) + 1; _detailBitDepthBtn.Y = 2;

        var lblDetAC = new Label("音频编码:") { X = Pos.Right(_detailBitDepthBtn) + 2, Y = 2 };
        _detailAudioCodecBtn = CreateCycleButton(_audioCodecs, "", val => 
        {
            UpdateSelectedItemProperty(prop => 
            {
                prop.AudioCodec = val;
                string? autoChannel = MainViewModel.GetDefaultChannelForCodec(val);
                if (autoChannel != null)
                {
                    prop.AudioChannel = autoChannel;
                    _detailAudioChannelBtn.Text = autoChannel;
                }
            });
        });
        _detailAudioCodecBtn.X = Pos.Right(lblDetAC) + 1; _detailAudioCodecBtn.Y = 2;

        var lblDetACh = new Label("声道:") { X = Pos.Right(_detailAudioCodecBtn) + 2, Y = 2 };
        _detailAudioChannelBtn = CreateCycleButton(_audioChannels, "", val => UpdateSelectedItemProperty(prop => prop.AudioChannel = val));
        _detailAudioChannelBtn.X = Pos.Right(lblDetACh) + 1; _detailAudioChannelBtn.Y = 2;

        var lblDetRG = new Label("发布组:") { X = Pos.Right(_detailAudioChannelBtn) + 2, Y = 2 };
        _detailReleaseGroupField = new TextField("") { X = Pos.Right(lblDetRG) + 1, Y = 2, Width = 15 };
        _detailReleaseGroupField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.ReleaseGroup = _detailReleaseGroupField.Text.ToString());

        _detailOriginalNameLabel = new Label("") { X = 1, Y = 4, Width = Dim.Fill() - 1 };
        _detailMatchedVideoLabel = new Label("") { X = 1, Y = 5, Width = Dim.Fill() - 1, Visible = false };
        _detailTargetNameLabel = new Label("") { X = 1, Y = 6, Width = Dim.Fill() - 1 };

        detailFrame.Add(lblDetSeason, _detailSeasonField, lblDetEp, _detailEpisodeField, lblDetRes, _detailResolutionBtn, lblDetQa, _detailQualityBtn, lblDetLang, _detailLanguageField);
        detailFrame.Add(lblDetVC, _detailVideoCodecBtn, lblDetBD, _detailBitDepthBtn, lblDetAC, _detailAudioCodecBtn, lblDetACh, _detailAudioChannelBtn, lblDetRG, _detailReleaseGroupField);
        detailFrame.Add(_detailOriginalNameLabel, _detailMatchedVideoLabel, _detailTargetNameLabel);

        // ======================= 最底部状态栏 =======================
        var statusMsgItem = new StatusItem(Key.Null, "Status: Ready", null);
        var statusPathItem = new StatusItem(Key.Null, "", null);

        var statusBar = new StatusBar([
            new StatusItem(Key.CtrlMask | Key.Q, "~CTRL-Q~ Quit", () => Application.RequestStop()),
            statusMsgItem,
            statusPathItem
        ]);

        var errorTheme = new ColorScheme()
        {
            Normal = Application.Driver.MakeAttribute(Color.Red, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.BrightRed, Color.Black)
        };

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.GlobalStatusMessage))
            {
                Application.MainLoop.Invoke(() =>
                {
                    string msg = ViewModel.GlobalStatusMessage;
                    statusMsgItem.Title = msg;

                    if (msg.Contains("Error", StringComparison.OrdinalIgnoreCase) || msg.Contains("失败") || msg.Contains("异常"))
                        statusBar.ColorScheme = errorTheme;
                    else
                        statusBar.ColorScheme = ColorScheme; // 恢复默认暗黑配色

                    statusBar.SetNeedsDisplay();
                });
            }
            else if (e.PropertyName == nameof(MainViewModel.SelectedItem))
            {
                Application.MainLoop.Invoke(() =>
                {
                    var curItem = ViewModel.SelectedItem;
                    if (curItem != null)
                    {
                        if (curItem.HasError)
                        {
                            // 选中的文件包含出错信息，立即投射到底部大红框
                            ViewModel.GlobalStatusMessage = $"[选定单项错误] {curItem.StatusMessage}";
                        }
                        statusPathItem.Title = $"| Path: {curItem.OriginalPath}";
                    }
                    else
                    {
                        statusPathItem.Title = "";
                    }
                    statusBar.SetNeedsDisplay();
                });
            }
        };
        updateVisibility = () =>
        {
            bool isMovie = ViewModel.MovieMode;
            bool isSub = ViewModel.SubtitleMatchingMode;

            bool showSE = !isMovie;
            lblSeason.Visible = showSE;
            txtSeason.Visible = showSE;
            lblStartEp.Visible = showSE;
            txtStartEp.Visible = showSE;
            lblDetSeason.Visible = showSE;
            _detailSeasonField.Visible = showSE;
            lblDetEp.Visible = showSE;
            _detailEpisodeField.Visible = showSE;

            cbAutoSeason.Visible = (!isMovie && !isSub);

            bool showTech = !isSub;
            lblGlobalRes.Visible = showTech;
            btnGlobalRes.Visible = showTech;
            lblGlobalQa.Visible = showTech;
            btnGlobalQa.Visible = showTech;
            lblGlobalVC.Visible = showTech;
            btnGlobalVC.Visible = showTech;
            lblGlobalBD.Visible = showTech;
            btnGlobalBD.Visible = showTech;
            lblGlobalAC.Visible = showTech;
            btnGlobalAC.Visible = showTech;
            lblGlobalACh.Visible = showTech;
            btnGlobalACh.Visible = showTech;

            lblDetRes.Visible = showTech;
            _detailResolutionBtn.Visible = showTech;
            lblDetQa.Visible = showTech;
            _detailQualityBtn.Visible = showTech;
            lblDetVC.Visible = showTech;
            _detailVideoCodecBtn.Visible = showTech;
            lblDetBD.Visible = showTech;
            _detailBitDepthBtn.Visible = showTech;
            lblDetAC.Visible = showTech;
            _detailAudioCodecBtn.Visible = showTech;
            lblDetACh.Visible = showTech;
            _detailAudioChannelBtn.Visible = showTech;
        };
        updateVisibility.Invoke();

        Add(topFrame, listContainer, detailFrame, statusBar);
    }

    private void UpdateSelectedItemProperty(Action<MediaFileItem> updateAction)
    {
        if (ViewModel.SelectedItem != null && !_leftListView.HasFocus && !_rightListView.HasFocus)
        {
            updateAction(ViewModel.SelectedItem);
            ViewModel.RecalculateTargetFileName(ViewModel.SelectedItem);
            RefreshLists();
        }
    }

    private void OnSelectedItemChanged(ListViewItemEventArgs e)
    {
        // 字幕模式下左侧列表可能只显示部分文件，需要映射回 MediaFiles 索引
        var displayItems = GetDisplayItems();
        if (e.Item >= 0 && e.Item < displayItems.Count)
        {
            var item = displayItems[e.Item];
            ViewModel.SelectedItem = item;

            // 同步右侧列表选中
            if (_rightListView.SelectedItem != e.Item)
                _rightListView.SelectedItem = e.Item;

            // 必须项
            _detailSeasonField.Text = item.Season?.ToString() ?? "";
            _detailEpisodeField.Text = item.Episode?.ToString() ?? "";
            _detailLanguageField.Text = item.Language ?? "";

            // Sync Res Button
            _detailResolutionBtn.Text = string.IsNullOrEmpty(item.Resolution) ? "(无)" : item.Resolution;

            // Sync Quality Button
            _detailQualityBtn.Text = string.IsNullOrEmpty(item.Quality) ? "(无)" : item.Quality;

            // 可选项同步
            // 可选项同步
            _detailVideoCodecBtn.Text = string.IsNullOrEmpty(item.VideoCodec) ? "(无)" : item.VideoCodec;
            _detailBitDepthBtn.Text = string.IsNullOrEmpty(item.BitDepth) ? "(无)" : item.BitDepth;
            _detailAudioCodecBtn.Text = string.IsNullOrEmpty(item.AudioCodec) ? "(无)" : item.AudioCodec;
            _detailAudioChannelBtn.Text = string.IsNullOrEmpty(item.AudioChannel) ? "(无)" : item.AudioChannel;

            _detailReleaseGroupField.Text = item.ReleaseGroup ?? "";

            UpdateDetailLabels();
        }
    }

    private void UpdateDetailLabels()
    {
        var item = ViewModel.SelectedItem;
        if (item == null)
        {
            _detailOriginalNameLabel.Text = "";
            _detailTargetNameLabel.Text = "";
            _detailMatchedVideoLabel.Text = "";
            return;
        }

        _detailOriginalNameLabel.Text = $"原名: {item.OriginalFileName}";

        if (ViewModel.SubtitleMatchingMode)
        {
            string videoName = item.AssociatedVideoItem != null ? Path.GetFileName(item.AssociatedVideoItem.TargetFileName) : "(未匹配视频)";
            _detailMatchedVideoLabel.Text = $"匹配视频: {videoName}";
            _detailMatchedVideoLabel.Visible = true;
            _detailTargetNameLabel.Text = $"目标字幕: {Path.GetFileName(item.TargetFileName)}";
            _detailMatchedVideoLabel.Y = 5;
            _detailTargetNameLabel.Y = 6;
        }
        else
        {
            _detailMatchedVideoLabel.Visible = false;
            _detailTargetNameLabel.Text = $"目标名称: {Path.GetFileName(item.TargetFileName)}";
            _detailTargetNameLabel.Y = 5;
        }
    }

    public void RefreshLists()
    {
        var displayItems = GetDisplayItems();

        if (ViewModel.SubtitleMatchingMode)
        {
            // 字幕匹配模式：左侧显示字幕文件原名，右侧显示"匹配的视频名 + 目标字幕名"
            var leftSource = displayItems.Select(x => x.OriginalFileName + "\n").ToList();
            var rightSource = displayItems.Select(x =>
            {
                string videoLine = x.AssociatedVideoItem != null
                    ? $"→ {Path.GetFileName(x.AssociatedVideoItem.TargetFileName)}"
                    : "→ (未匹配视频)";
                string subtitleLine = $"↓ {Path.GetFileName(x.TargetFileName)}";
                return $"{videoLine}\n{subtitleLine}";
            }).ToList();

            _leftListView.SetSource(leftSource);
            _rightListView.SetSource(rightSource);
        }
        else
        {
            // 常规模式：左侧显示原文件名，右侧显示重命名预览
            var leftSource = displayItems.Select(x => x.OriginalFileName).ToList();
            var rightSource = displayItems.Select(x => x.TargetFileName).ToList();

            _leftListView.SetSource(leftSource);
            _rightListView.SetSource(rightSource);
        }

        // 同步勾选状态
        if (_leftListView.AllowsMarking)
        {
            for (int i = 0; i < displayItems.Count; i++)
            {
                _leftListView.Source.SetMark(i, displayItems[i].IsSelected);
            }
        }

        // 更新标题栏文件数
        UpdateFrameTitles();
        UpdateDetailLabels();
    }

    /// <summary>
    /// 根据当前模式返回显示在左侧列表中的文件子集
    /// </summary>
    private List<MediaFileItem> GetDisplayItems()
    {
        if (ViewModel.SubtitleMatchingMode)
        {
            // 字幕模式：仅显示字幕文件
            return ViewModel.MediaFiles
                .Where(f => f.FileType == MediaFileType.Subtitle)
                .ToList();
        }
        return ViewModel.MediaFiles.ToList();
    }

    /// <summary>
    /// 根据工作模式动态更新左右栏标题
    /// </summary>
    private void UpdateFrameTitles()
    {
        var displayItems = GetDisplayItems();
        int count = displayItems.Count;

        if (ViewModel.SubtitleMatchingMode)
        {
            _leftFrame.Title = $"Subtitle.Files ({count})";
            _rightFrame.Title = $"Match.Preview ({count})";
        }
        else
        {
            _leftFrame.Title = $"Input.Files ({count})";
            _rightFrame.Title = $"Output.Preview ({count})";
        }
    }

    /// <summary>
    /// 为 TextField 绑定文件路径补全能力 (支持键盘和点击选取)
    /// </summary>
    private void SetupPathAutocomplete(TextField textField)
    {
        // 确保使用弹窗样式的自动补全 (默认包含)
        if (textField.Autocomplete != null)
        {
            textField.Autocomplete.AllSuggestions = new List<string>();

            // 监听输入动态生成候选项
            textField.TextChanged += (e) =>
            {
                try
                {
                    string currentText = textField.Text.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(currentText)) return;

                    string dir = Path.GetDirectoryName(currentText) ?? "";
                    string prefix = Path.GetFileName(currentText);

                    if (string.IsNullOrEmpty(dir))
                    {
                        // 处理当前位于根目录或无路径符的情况
                        if (!currentText.Contains(Path.DirectorySeparatorChar) && !currentText.Contains(Path.AltDirectorySeparatorChar))
                        {
                            dir = Directory.GetCurrentDirectory();
                            prefix = currentText;
                        }
                        else
                        {
                            dir = currentText; // 例如输入了 "D:\" 此时 prefix==""
                        }
                    }

                    if (Directory.Exists(dir))
                    {
                        // 防止出现带有无效字符（如 ":"）引发 ArgumentException 的情况
                        // 即用户输入 "D:" 时，不作为前缀去当前目录搜索
                        if (prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        {
                            return;
                        }

                        var directories = Directory.GetDirectories(dir, prefix + "*")
                            .Select(d => Path.GetFileName(d) + Path.DirectorySeparatorChar)
                            .ToList();

                        textField.Autocomplete.AllSuggestions = directories;
                    }
                }
                catch
                {
                    // 忽略驱动器未就绪或无权限异常
                }
            };
        }
    }
}
