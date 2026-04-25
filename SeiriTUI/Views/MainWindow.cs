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
    private ComboBox _detailResolutionCombo;
    private ComboBox _detailQualityCombo;

    // Detail fields - optional (可选项参数区)
    private TextField _detailVideoCodecField;
    private TextField _detailBitDepthField;
    private TextField _detailAudioCodecField;
    private TextField _detailAudioChannelField;
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
    }

    private void BuildUi()
    {
        // ======================= 顶部全局控制区 =======================
        var topFrame = new FrameView("Global Console")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 9,
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
        var cbGlobalRes = new ComboBox() { X = 16, Y = 1, Width = 15, Height = 6, ColorScheme = Colors.TopLevel };
        cbGlobalRes.SetSource(_resolutions);
        cbGlobalRes.SelectedItemChanged += (e) =>
        {
            string val = e.Value.ToString() ?? "";
            ViewModel.GlobalResolution = val == "(无)" ? "" : val;
            RefreshLists();
        };

        var lblGlobalQa = new Label("全局质量:") { X = 35, Y = 1 };
        var cbGlobalQa = new ComboBox() { X = 46, Y = 1, Width = 15, Height = 6, ColorScheme = Colors.TopLevel };
        cbGlobalQa.SetSource(_qualities);
        cbGlobalQa.SelectedItemChanged += (e) =>
        {
            string val = e.Value.ToString() ?? "";
            ViewModel.GlobalQuality = val == "(无)" ? "" : val;
            RefreshLists();
        };

        var lblGlobalLang = new Label("默认字幕语言:") { X = 65, Y = 1 };
        var txtGlobalLang = new TextField(ViewModel.DefaultSubtitleLanguage) { X = 77, Y = 1, Width = 8 };
        txtGlobalLang.TextChanged += (e) =>
        {
            ViewModel.DefaultSubtitleLanguage = txtGlobalLang.Text.ToString() ?? "";
            RefreshLists();
        };
        // ===========================================

        var lblSourcePath = new Label("扫描源目录:") { X = 1, Y = 3 };
        var txtSourcePath = new TextField(Directory.GetCurrentDirectory()) { X = 16, Y = 3, Width = 45 };
        SetupPathAutocomplete(txtSourcePath);

        var lblTargetPath = new Label("输出根路径:") { X = 1, Y = 4 };
        var txtTargetPath = new TextField(Directory.GetCurrentDirectory()) { X = 16, Y = 4, Width = 45 };
        SetupPathAutocomplete(txtTargetPath);

        var btnScan = new Button("执行扫描") { X = 65, Y = 3 };
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

        var cbAutoSeason = new CheckBox("自动创建 Season 文件夹", ViewModel.AutoCreateSeasonFolder) { X = 65, Y = 4 };
        cbAutoSeason.Toggled += (prev) =>
        {
            ViewModel.AutoCreateSeasonFolder = cbAutoSeason.Checked;
            RefreshLists();
        };

        var cbSelectionMode = new CheckBox("启用部分勾选模式", ViewModel.SelectionModeEnabled) { X = 95, Y = 4 };

        // 工作模式切换复选框
        var cbSubtitleMode = new CheckBox("字幕匹配模式", ViewModel.SubtitleMatchingMode) { X = 95, Y = 3 };
        cbSubtitleMode.Toggled += (prev) =>
        {
            ViewModel.SubtitleMatchingMode = cbSubtitleMode.Checked;
            UpdateFrameTitles();
            RefreshLists();
        };

        var btnSelectAll = new Button("全选") { X = 65, Y = 5, Visible = false };
        var btnDeselectAll = new Button("全不选") { X = 77, Y = 5, Visible = false };
        var btnInvertSel = new Button("反选") { X = 91, Y = 5, Visible = false };

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
        var btnPanel = new View() { X = 1, Y = 6, Width = Dim.Fill(), Height = 1 };
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
        topFrame.Add(lblGlobalRes, cbGlobalRes, lblGlobalQa, cbGlobalQa, lblGlobalLang, txtGlobalLang);
        topFrame.Add(lblSourcePath, txtSourcePath, btnScan);
        topFrame.Add(lblTargetPath, txtTargetPath, cbAutoSeason, cbSelectionMode, cbSubtitleMode, btnSelectAll, btnDeselectAll, btnInvertSel, btnPanel);


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
        _detailResolutionCombo = new ComboBox()
        {
            X = Pos.Right(lblDetRes) + 1,
            Y = 0,
            Width = 10,
            Height = 6,
            ColorScheme = Colors.TopLevel
        };
        _detailResolutionCombo.SetSource(_resolutions);
        _detailResolutionCombo.SelectedItemChanged += (e) =>
        {
            string val = e.Value.ToString() ?? "";
            UpdateSelectedItemProperty(prop => prop.Resolution = val == "(无)" ? "" : val);
        };

        var lblDetQa = new Label("质量:") { X = Pos.Right(_detailResolutionCombo) + 2, Y = 0 };
        _detailQualityCombo = new ComboBox()
        {
            X = Pos.Right(lblDetQa) + 1,
            Y = 0,
            Width = 10,
            Height = 6,
            ColorScheme = Colors.TopLevel
        };
        _detailQualityCombo.SetSource(_qualities);
        _detailQualityCombo.SelectedItemChanged += (e) =>
        {
            string val = e.Value.ToString() ?? "";
            UpdateSelectedItemProperty(prop => prop.Quality = val == "(无)" ? "" : val);
        };

        var lblDetLang = new Label("语言:") { X = Pos.Right(_detailQualityCombo) + 2, Y = 0 };
        _detailLanguageField = new TextField("") { X = Pos.Right(lblDetLang) + 1, Y = 0, Width = 8 };
        _detailLanguageField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.Language = _detailLanguageField.Text.ToString());

        // ---- 可选项参数区 (第 1 行): 视频编码 / 色彩深度 / 音频编码 / 音频声道 / 发布组 ----
        var lblDetVC = new Label("视频编码:") { X = 1, Y = 2 };
        _detailVideoCodecField = new TextField("") { X = Pos.Right(lblDetVC) + 1, Y = 2, Width = 7 };
        _detailVideoCodecField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.VideoCodec = _detailVideoCodecField.Text.ToString());

        var lblDetBD = new Label("色深:") { X = Pos.Right(_detailVideoCodecField) + 2, Y = 2 };
        _detailBitDepthField = new TextField("") { X = Pos.Right(lblDetBD) + 1, Y = 2, Width = 6 };
        _detailBitDepthField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.BitDepth = _detailBitDepthField.Text.ToString());

        var lblDetAC = new Label("音频编码:") { X = Pos.Right(_detailBitDepthField) + 2, Y = 2 };
        _detailAudioCodecField = new TextField("") { X = Pos.Right(lblDetAC) + 1, Y = 2, Width = 7 };
        _detailAudioCodecField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.AudioCodec = _detailAudioCodecField.Text.ToString());

        var lblDetACh = new Label("声道:") { X = Pos.Right(_detailAudioCodecField) + 2, Y = 2 };
        _detailAudioChannelField = new TextField("") { X = Pos.Right(lblDetACh) + 1, Y = 2, Width = 5 };
        _detailAudioChannelField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.AudioChannel = _detailAudioChannelField.Text.ToString());

        var lblDetRG = new Label("发布组:") { X = Pos.Right(_detailAudioChannelField) + 2, Y = 2 };
        _detailReleaseGroupField = new TextField("") { X = Pos.Right(lblDetRG) + 1, Y = 2, Width = 15 };
        _detailReleaseGroupField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.ReleaseGroup = _detailReleaseGroupField.Text.ToString());

        _detailOriginalNameLabel = new Label("") { X = 1, Y = 4, Width = Dim.Fill() - 1 };
        _detailMatchedVideoLabel = new Label("") { X = 1, Y = 5, Width = Dim.Fill() - 1, Visible = false };
        _detailTargetNameLabel = new Label("") { X = 1, Y = 6, Width = Dim.Fill() - 1 };

        detailFrame.Add(lblDetSeason, _detailSeasonField, lblDetEp, _detailEpisodeField, lblDetRes, _detailResolutionCombo, lblDetQa, _detailQualityCombo, lblDetLang, _detailLanguageField);
        detailFrame.Add(lblDetVC, _detailVideoCodecField, lblDetBD, _detailBitDepthField, lblDetAC, _detailAudioCodecField, lblDetACh, _detailAudioChannelField, lblDetRG, _detailReleaseGroupField);
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

                    if (msg.Contains("错误") || msg.Contains("失败"))
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

            // Sync Res Combo
            string res = string.IsNullOrEmpty(item.Resolution) ? "(无)" : item.Resolution;
            int ridx = _resolutions.IndexOf(res);
            if (ridx >= 0) _detailResolutionCombo.SelectedItem = ridx;
            else _detailResolutionCombo.Text = item.Resolution ?? "";

            // Sync Quality Combo
            string qa = string.IsNullOrEmpty(item.Quality) ? "(无)" : item.Quality;
            int qidx = _qualities.IndexOf(qa);
            if (qidx >= 0) _detailQualityCombo.SelectedItem = qidx;
            else _detailQualityCombo.Text = item.Quality ?? "";

            // 可选项
            _detailVideoCodecField.Text = item.VideoCodec ?? "";
            _detailBitDepthField.Text = item.BitDepth ?? "";
            _detailAudioCodecField.Text = item.AudioCodec ?? "";
            _detailAudioChannelField.Text = item.AudioChannel ?? "";
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
