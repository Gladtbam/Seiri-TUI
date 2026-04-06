using Terminal.Gui;
using SeiriTUI.ViewModels;
using SeiriTUI.Models;
using System.Linq;

namespace SeiriTUI.Views;

public class MainWindow : Window
{
    public readonly MainViewModel ViewModel;

    private ListView _leftListView;
    private ListView _rightListView;

    private TextField _detailSeasonField;
    private TextField _detailEpisodeField;
    private TextField _detailLanguageField;
    private ComboBox _detailResolutionCombo;
    private ComboBox _detailQualityCombo;

    private TextView _tvFullSource;
    private TextView _tvFullTarget;

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
            string SourcePath = txtSourcePath.Text.ToString() ?? "";
            ViewModel.LoadMediaFilesFromDirectory(SourcePath);
            ViewModel.TargetRootPath = SourcePath; // 自动将目标目录设为源目录
            txtTargetPath.Text = SourcePath;       // 同步更新 UI 输入框
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
        topFrame.Add(lblTargetPath, txtTargetPath, cbAutoSeason, cbSelectionMode, btnSelectAll, btnDeselectAll, btnInvertSel, btnPanel);


        // ======================= 中部对比列表 =======================
        var listContainer = new View() { X = 0, Y = Pos.Bottom(topFrame), Width = Dim.Fill(), Height = Dim.Fill(7) };

        var leftFrame = new FrameView("Input.Files ()")
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(50),
            Height = Dim.Fill(),
            Border = new Border() { BorderStyle = BorderStyle.Rounded }
        };
        _leftListView = new ListView() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), AllowsMarking = false };
        leftFrame.Add(_leftListView);

        var rightFrame = new FrameView("Output.Preview ()")
        {
            X = Pos.Right(leftFrame),
            Y = 0,
            Width = Dim.Percent(50),
            Height = Dim.Fill(),
            Border = new Border() { BorderStyle = BorderStyle.Rounded }
        };
        _rightListView = new ListView() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), AllowsMarking = false };
        rightFrame.Add(_rightListView);

        listContainer.Add(leftFrame, rightFrame);
        _leftListView.SelectedItemChanged += OnSelectedItemChanged;
        
        // 当多选模式下用户按空格/点击更改了选中状态，我们需要将其同步到 ViewModel (但没有官方的 MarkChanged，所以我们用鼠标事件和键盘事件捕获)
        _leftListView.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key == Key.Space && _leftListView.AllowsMarking && _leftListView.SelectedItem >= 0)
            {
                // 让它自带的处理先过，然后再把状态同步到 IsSelected
                Application.MainLoop.Invoke(() => {
                    ViewModel.MediaFiles[_leftListView.SelectedItem].IsSelected = _leftListView.Source.IsMarked(_leftListView.SelectedItem);
                });
            }
        };
        _leftListView.MouseClick += (e) =>
        {
            if (_leftListView.AllowsMarking && e.MouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked))
            {
                Application.MainLoop.Invoke(() => {
                    if (_leftListView.SelectedItem >= 0)
                        ViewModel.MediaFiles[_leftListView.SelectedItem].IsSelected = _leftListView.Source.IsMarked(_leftListView.SelectedItem);
                });
            }
        };


        // ======================= 底部单体参数修改区 (含 ComboBox) =======================
        var detailFrame = new FrameView("Item.Details ()")
        {
            X = 0,
            Y = Pos.Bottom(listContainer),
            Width = Dim.Fill(),
            Height = 6,
            Border = new Border() { BorderStyle = BorderStyle.Rounded }
        };

        var lblDetSeason = new Label("独立季:") { X = 1, Y = 0 };
        _detailSeasonField = new TextField("") { X = Pos.Right(lblDetSeason) + 1, Y = 0, Width = 5 };
        _detailSeasonField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.Season = int.TryParse(_detailSeasonField.Text.ToString(), out int val) ? val : null);

        var lblDetEp = new Label("独立集数:") { X = Pos.Right(_detailSeasonField) + 4, Y = 0 };
        _detailEpisodeField = new TextField("") { X = Pos.Right(lblDetEp) + 1, Y = 0, Width = 5 };
        _detailEpisodeField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.Episode = int.TryParse(_detailEpisodeField.Text.ToString(), out int val) ? val : null);

        var lblDetRes = new Label("分辨率:") { X = Pos.Right(_detailEpisodeField) + 4, Y = 0 };
        _detailResolutionCombo = new ComboBox()
        {
            X = Pos.Right(lblDetRes) + 1,
            Y = 0,
            Width = 12,
            Height = 6,
            ColorScheme = Colors.TopLevel
        };
        _detailResolutionCombo.SetSource(_resolutions);
        _detailResolutionCombo.SelectedItemChanged += (e) =>
        {
            string val = e.Value.ToString() ?? "";
            UpdateSelectedItemProperty(prop => prop.Resolution = val == "(无)" ? "" : val);
        };

        var lblDetQa = new Label("质量来源:") { X = Pos.Right(_detailResolutionCombo) + 4, Y = 0 };
        _detailQualityCombo = new ComboBox()
        {
            X = Pos.Right(lblDetQa) + 1,
            Y = 0,
            Width = 15,
            Height = 6,
            ColorScheme = Colors.TopLevel
        };
        _detailQualityCombo.SetSource(_qualities);
        _detailQualityCombo.SelectedItemChanged += (e) =>
        {
            string val = e.Value.ToString() ?? "";
            UpdateSelectedItemProperty(prop => prop.Quality = val == "(无)" ? "" : val);
        };

        var lblDetLang = new Label("语言后缀:") { X = Pos.Right(_detailQualityCombo) + 4, Y = 0 };
        _detailLanguageField = new TextField("") { X = Pos.Right(lblDetLang) + 1, Y = 0, Width = 8 };
        _detailLanguageField.TextChanged += (e) => UpdateSelectedItemProperty(prop => prop.Language = _detailLanguageField.Text.ToString());

        var lblFullSourceTitle = new Label("原名:") { X = 1, Y = 2 };
        _tvFullSource = new TextView() { X = Pos.Right(lblFullSourceTitle), Y = 2, Width = Dim.Fill(), Height = 2, ReadOnly = true, WordWrap = true };

        var lblFullTargetTitle = new Label("目标:") { X = 1, Y = 3 };
        _tvFullTarget = new TextView() { X = Pos.Right(lblFullTargetTitle), Y = 3, Width = Dim.Fill(), Height = 2, ReadOnly = true, WordWrap = true };

        detailFrame.Add(lblDetSeason, _detailSeasonField, lblDetEp, _detailEpisodeField, lblDetRes, _detailResolutionCombo, lblDetQa, _detailQualityCombo, lblDetLang, _detailLanguageField);
        detailFrame.Add(lblFullSourceTitle, _tvFullSource, lblFullTargetTitle, _tvFullTarget);

        // ======================= 最底部状态栏 =======================
        var statusMsgItem = new StatusItem(Key.Null, "Status: Ready", null);
        var statusPathItem = new StatusItem(Key.Null, "", null);

        var statusBar = new StatusBar(new StatusItem[] {
            new StatusItem(Key.CtrlMask | Key.Q, "~CTRL-Q~ Quit", () => Application.RequestStop()),
            statusMsgItem,
            statusPathItem
        });

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
                    string msg = ViewModel.GlobalStatusMessage ?? "";
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
        if (e.Item >= 0 && e.Item < ViewModel.MediaFiles.Count)
        {
            var item = ViewModel.MediaFiles[e.Item];
            ViewModel.SelectedItem = item;

            if (_rightListView.SelectedItem != e.Item)
            {
                _rightListView.SelectedItem = e.Item;
            }

            _detailSeasonField.Text = item.Season?.ToString() ?? "";
            _detailEpisodeField.Text = item.Episode?.ToString() ?? "";
            _detailLanguageField.Text = item.Language ?? "";

            // Sync Res Combo
            string res = string.IsNullOrEmpty(item.Resolution) ? "(无)" : item.Resolution;
            int ridx = _resolutions.IndexOf(res);
            if (ridx >= 0) _detailResolutionCombo.SelectedItem = ridx;
            else _detailResolutionCombo.Text = item.Resolution ?? ""; // 自定义值

            // Sync Quality Combo
            string qa = string.IsNullOrEmpty(item.Quality) ? "(无)" : item.Quality;
            int qidx = _qualities.IndexOf(qa);
            if (qidx >= 0) _detailQualityCombo.SelectedItem = qidx;
            else _detailQualityCombo.Text = item.Quality ?? ""; // 自定义值

            // ====== Set full multiline wrapped strings ======
            _tvFullSource.Text = item.OriginalFileName ?? "";
            _tvFullTarget.Text = item.TargetFileName ?? "";
        }
    }

    public void RefreshLists()
    {
        var leftSource = ViewModel.MediaFiles.Select(x => x.OriginalFileName).ToList();
        var rightSource = ViewModel.MediaFiles.Select(x => x.TargetFileName).ToList();

        _leftListView.SetSource(leftSource);
        _rightListView.SetSource(rightSource);

        if (_leftListView.AllowsMarking)
        {
            for (int i = 0; i < ViewModel.MediaFiles.Count; i++)
            {
                _leftListView.Source.SetMark(i, ViewModel.MediaFiles[i].IsSelected);
            }
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
                    string prefix = Path.GetFileName(currentText) ?? "";

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
