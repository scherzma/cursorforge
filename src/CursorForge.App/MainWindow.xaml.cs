using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CursorForge.Ui;

public partial class MainWindow : Window
{
    AppConfig _cfg;
    bool _loading = true, _ready;
    double _ppd = 1;
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly ColorRow _fill = new(), _outline = new(), _glow = new(), _flashLeft = new(), _flashRight = new(), _hotDot = new();
    readonly List<(RadioButton Button, string Id)> _presetButtons = [];
    string _savedSignature = "";
    readonly Dictionary<CursorShape, (RadioButton Button, Image? Icon)> _shapeButtons = [];
    readonly Dictionary<CursorRole, Image> _stateImages = [];
    readonly Dictionary<CursorRole, BitmapSource[]> _stateFrames = [];
    readonly DispatcherTimer _spinTimer = new() { Interval = TimeSpan.FromMilliseconds(CursorRenderer.SpinnerJiffies * 1000 / 60.0) };
    int _spinFrame;

    static readonly (CursorRole Role, string Name)[] StateNames =
    [
        (CursorRole.Pointer, "Normal"), (CursorRole.Link, "Link"), (CursorRole.Text, "Text"),
        (CursorRole.Busy, "Busy"), (CursorRole.Working, "Working in background"), (CursorRole.Unavailable, "Unavailable"),
        (CursorRole.Precision, "Precision"), (CursorRole.Move, "Move"), (CursorRole.SizeNS, "Resize vertical"),
        (CursorRole.SizeWE, "Resize horizontal"), (CursorRole.SizeNWSE, "Resize diagonal"), (CursorRole.SizeNESW, "Resize diagonal"),
        (CursorRole.Up, "Alternate select"), (CursorRole.Help, "Help / pin / person"),
    ];

    public MainWindow(AppConfig cfg)
    {
        _cfg = cfg;
        InitializeComponent();

        FillHost.Content = _fill;
        OutlineHost.Content = _outline;
        GlowHost.Content = _glow;
        HotDotHost.Content = _hotDot;
        _hotDot.Changed += OnInput;
        FlashLeftHost.Content = _flashLeft;
        FlashRightHost.Content = _flashRight;
        _flashLeft.Changed += OnInput;
        _flashRight.Changed += OnInput;
        _fill.Changed += OnInput;
        _outline.Changed += OnInput;
        _glow.Changed += OnInput;
        ToggleHotkeyBox.Changed += OnInput;
        OverlayHotkeyBox.Changed += OnInput;

        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Commit(); };
        _statusTimer.Tick += (_, _) => UpdateAgentStatus();
        _spinTimer.Tick += (_, _) =>
        {
            _spinFrame = (_spinFrame + 1) % CursorRenderer.SpinnerFrames;
            foreach (var (role, frames) in _stateFrames)
                if (frames.Length > 1 && _stateImages.TryGetValue(role, out var img)) img.Source = frames[_spinFrame % frames.Length];
        };

        SourceInitialized += (_, _) => NativeUi.ApplyDarkChrome(this);
        Loaded += (_, _) =>
        {
            _ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            BuildPresetTiles();
            BuildShapeButtons();
            BuildStateTiles();
            _ready = true;
            LoadIntoControls();
            UpdateAgentStatus();
            _statusTimer.Start();
            _spinTimer.Start();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { PresetScroll.ScrollToTop(); SideScroll.ScrollToTop(); });
        };
        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            _spinTimer.Stop();
            if (_saveTimer.IsEnabled) { _saveTimer.Stop(); Commit(); }
        };
    }

    // Only the window's own DPI change (moved to another monitor). The routed DpiChanged event also
    // bubbles up from every Image, so handling that would rebuild the tiles in an endless loop.
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _ppd = newDpi.PixelsPerDip;
        if (!_ready) return;
        BuildPresetTiles();
        BuildShapeButtons();
        BuildStateTiles();
        LoadIntoControls();
    }

    // ------------------------------------------------------------------ building

    BitmapSource ToBitmap(RenderedCursor rc)
    {
        var bmp = BitmapSource.Create(rc.Width, rc.Height, 96 * _ppd, 96 * _ppd, PixelFormats.Bgra32, null, rc.Pixels, rc.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    void BuildPresetTiles()
    {
        PresetPanel.Children.Clear();
        MyPresetPanel.Children.Clear();
        _presetButtons.Clear();

        foreach (var saved in _cfg.SavedPresets)
        {
            var rb = MakeTile(saved.Name, SavedPreview(saved));
            rb.Click += (_, _) => ApplySaved(saved);
            rb.ContextMenu = SavedMenu(saved);
            MyPresetPanel.Children.Add(rb);
            _presetButtons.Add((rb, saved.Key));
        }
        MyPresetsSection.Visibility = _cfg.SavedPresets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _savedSignature = SavedSignature();

        foreach (var preset in Presets.All)
        {
            var rb = MakeTile(preset.Name, preset.Style);
            rb.Click += (_, _) => ApplyPreset(preset);
            PresetPanel.Children.Add(rb);
            _presetButtons.Add((rb, preset.Id));
        }
    }

    RadioButton MakeTile(string name, CursorStyle style)
    {
        var img = new Image
        {
            Source = ToBitmap(CursorRenderer.Render(style)),
            Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            MaxWidth = 76, MaxHeight = 76,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var content = new DockPanel();
        var label = new TextBlock
        {
            Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 4, 4),
        };
        DockPanel.SetDock(label, Dock.Bottom);
        content.Children.Add(label);
        content.Children.Add(img);
        return new RadioButton { Style = (Style)FindResource("Tile"), Content = content, GroupName = "presets", ToolTip = name };
    }

    /// <summary>
    /// Tile preview for a saved preset. The renderer draws custom images from the currently imported file,
    /// so a custom-image preset only previews correctly while its own image is the imported one.
    /// </summary>
    static CursorStyle SavedPreview(SavedPreset p)
    {
        if (p.Style.Shape != CursorShape.Custom) return p.Style;
        var own = CustomImage.TryLoad(p.ImagePath);
        var current = CustomImage.TryLoad(ConfigStore.CustomImagePath);
        bool same = own != null && current != null && own.Pixels.AsSpan().SequenceEqual(current.Pixels);
        if (same) return p.Style;
        var s = p.Style.Clone();
        s.Shape = CursorShape.Arrow; // stand-in so the tile still shows the colours
        return s;
    }

    /// <summary>Changes whenever the saved presets change, so tiles are rebuilt only when needed.</summary>
    string SavedSignature() => string.Join("|", _cfg.SavedPresets.Select(p =>
        p.Id + ":" + p.Name + ":" + System.Text.Json.JsonSerializer.Serialize(
            new[] { p.Style.Shape.ToString(), p.Style.Size.ToString(), p.Style.Fill, p.Style.Outline, p.Style.OutlineWidth.ToString(),
                    p.Style.Glow.ToString(), p.Style.GlowColor, p.Style.Shadow.ToString(), p.Style.Opacity.ToString(),
                    p.Style.HotspotDot.ToString(), p.Style.HotspotDotColor, p.Style.HotspotDotSize.ToString() })));

    ContextMenu SavedMenu(SavedPreset saved)
    {
        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Apply", () => ApplySaved(saved));
        Add("Update with current settings", () => UpdateSaved(saved));
        Add("Rename…", () => RenameSaved(saved));
        menu.Items.Add(new Separator());
        Add("Delete", () => DeleteSaved(saved));
        return menu;
    }

    void BuildShapeButtons()
    {
        ShapePanel.Children.Clear();
        _shapeButtons.Clear();
        foreach (var shape in Enum.GetValues<CursorShape>())
        {
            Image? icon = null;
            object content;
            if (shape == CursorShape.Custom)
            {
                content = new TextBlock
                {
                    Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16,
                };
            }
            else
            {
                icon = new Image { Width = 24, Height = 24 };
                content = icon;
            }
            var rb = new RadioButton
            {
                Style = (Style)FindResource("ShapeBtn"), Content = content, GroupName = "shape",
                ToolTip = shape == CursorShape.Custom ? "Your own image" : shape.ToString(),
            };
            rb.Click += (_, _) => OnShapeClick(shape);
            ShapePanel.Children.Add(rb);
            _shapeButtons[shape] = (rb, icon);
        }
    }

    void BuildStateTiles()
    {
        StatesPanel.Children.Clear();
        _stateImages.Clear();
        foreach (var (role, name) in StateNames)
        {
            var img = new Image
            {
                Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = 34, MaxHeight = 34,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            _stateImages[role] = img;
            StatesPanel.Children.Add(new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 6, 6),
                Background = (Brush)FindResource("Surface2"), Child = img, ToolTip = name,
            });
        }
    }

    void RefreshStates(CursorStyle s)
    {
        _stateFrames.Clear();
        bool same = _cfg.StateCursors == StateCursorMode.Same;
        foreach (var (role, _) in StateNames)
        {
            int frames = !same && CursorRenderer.IsAnimated(role) ? CursorRenderer.SpinnerFrames : 1;
            var bmps = new BitmapSource[frames];
            for (int f = 0; f < frames; f++)
                bmps[f] = ToBitmap(same ? CursorRenderer.Render(s) : CursorRenderer.RenderRole(s, role, f));
            _stateFrames[role] = bmps;
            if (_stateImages.TryGetValue(role, out var img)) img.Source = bmps[_spinFrame % frames];
        }
    }

    void OnStatesClick(object sender, RoutedEventArgs e)
    {
        _cfg.StateCursors = StatesSame.IsChecked == true ? StateCursorMode.Same : StateCursorMode.Matching;
        RefreshVisuals();
        ScheduleSave();
    }

    // ------------------------------------------------------------------ config <-> controls

    void LoadIntoControls()
    {
        if (!_ready) return;
        _loading = true;
        try
        {
            var s = _cfg.Style;
            EnabledSwitch.IsChecked = _cfg.Enabled;
            SizeSlider.Value = s.Size;
            OutlineSlider.Value = s.OutlineWidth;
            GlowSwitch.IsChecked = s.Glow;
            GlowSizeSlider.Value = s.GlowRadius;
            GlowStrengthSlider.Value = Math.Round(s.GlowStrength * 100);
            ShadowSwitch.IsChecked = s.Shadow;
            OpacitySlider.Value = Math.Round(s.Opacity * 100);
            _fill.Value = s.Fill;
            _outline.Value = s.Outline;
            _glow.Value = s.GlowColor;
            HotDotSwitch.IsChecked = s.HotspotDot;
            _hotDot.Value = s.HotspotDotColor;
            HotDotSizeSlider.Value = s.HotspotDotSize;
            FlashSwitch.IsChecked = _cfg.ClickFlash.Enabled;
            _flashLeft.Value = _cfg.ClickFlash.LeftColor;
            _flashRight.Value = _cfg.ClickFlash.RightColor;
            FlashDurationSlider.Value = _cfg.ClickFlash.DurationMs;
            bool topLeft = s.CustomHotX < 0.25f && s.CustomHotY < 0.25f;
            HotTopLeft.IsChecked = topLeft;
            HotCenter.IsChecked = !topLeft;
            StatesMatching.IsChecked = _cfg.StateCursors == StateCursorMode.Matching;
            StatesSame.IsChecked = _cfg.StateCursors == StateCursorMode.Same;

            foreach (var (shape, (btn, _)) in _shapeButtons) btn.IsChecked = shape == s.Shape;
            if (SavedSignature() != _savedSignature) BuildPresetTiles(); // saved presets changed (e.g. reloaded)
            foreach (var (btn, id) in _presetButtons) btn.IsChecked = id == _cfg.PresetId;

            OverlaySwitch.IsChecked = _cfg.OverlayEnabled;
            AutostartSwitch.IsChecked = _cfg.StartWithWindows;
            RoleArrow.IsChecked = _cfg.ReplaceArrow;
            RoleHand.IsChecked = _cfg.ReplaceHand;
            RoleBusy.IsChecked = _cfg.ReplaceBusy;
            RoleText.IsChecked = _cfg.ReplaceText;
            RoleResize.IsChecked = _cfg.ReplaceResize;
            RoleOther.IsChecked = _cfg.ReplaceOther;
            ToggleHotkeyBox.Set(_cfg.ToggleHotkey.Modifiers, _cfg.ToggleHotkey.Key);
            OverlayHotkeyBox.Set(_cfg.OverlayHotkey.Modifiers, _cfg.OverlayHotkey.Key);
            RebuildApps();
        }
        finally
        {
            _loading = false;
        }
        RefreshVisuals();
    }

    void ReadControls()
    {
        var s = _cfg.Style;
        s.Size = (int)SizeSlider.Value;
        s.OutlineWidth = (float)OutlineSlider.Value;
        s.Glow = GlowSwitch.IsChecked == true;
        s.GlowRadius = (float)GlowSizeSlider.Value;
        s.GlowStrength = (float)(GlowStrengthSlider.Value / 100);
        s.Shadow = ShadowSwitch.IsChecked == true;
        s.Opacity = (float)(OpacitySlider.Value / 100);
        s.Fill = _fill.Value;
        s.Outline = _outline.Value;
        s.GlowColor = _glow.Value;
        s.HotspotDot = HotDotSwitch.IsChecked == true;
        s.HotspotDotColor = _hotDot.Value;
        s.HotspotDotSize = (float)HotDotSizeSlider.Value;
        _cfg.ClickFlash.Enabled = FlashSwitch.IsChecked == true;
        _cfg.ClickFlash.LeftColor = _flashLeft.Value;
        _cfg.ClickFlash.RightColor = _flashRight.Value;
        _cfg.ClickFlash.DurationMs = (int)FlashDurationSlider.Value;

        _cfg.OverlayEnabled = OverlaySwitch.IsChecked == true;
        _cfg.ReplaceArrow = RoleArrow.IsChecked == true;
        _cfg.ReplaceHand = RoleHand.IsChecked == true;
        _cfg.ReplaceBusy = RoleBusy.IsChecked == true;
        _cfg.ReplaceText = RoleText.IsChecked == true;
        _cfg.ReplaceResize = RoleResize.IsChecked == true;
        _cfg.ReplaceOther = RoleOther.IsChecked == true;
        _cfg.ToggleHotkey = new Hotkey { Modifiers = ToggleHotkeyBox.Modifiers, Key = ToggleHotkeyBox.VirtualKey };
        _cfg.OverlayHotkey = new Hotkey { Modifiers = OverlayHotkeyBox.Modifiers, Key = OverlayHotkeyBox.VirtualKey };
    }

    /// <summary>Everything derived from the config: labels, visibility, previews, icons.</summary>
    void RefreshVisuals()
    {
        var s = _cfg.Style;
        // Windows shows scheme cursors on a canvas of its pointer size; bigger cursors raise it (up to 256 px,
        // the hardware-cursor limit) while CursorForge runs.
        int baseSize = WindowsPointer.BaseSize();
        int maxSharp = Math.Clamp(CursorRenderer.MaxSizeFor(s, CursorRenderer.MaxCanvas), (int)SizeSlider.Minimum + 1, 200);
        bool wasLoading = _loading;
        _loading = true;
        SizeSlider.Maximum = maxSharp;
        SizeSlider.Value = Math.Min(s.Size, maxSharp);
        _loading = wasLoading;
        var shown = CursorRenderer.Effective(s, CursorRenderer.MaxCanvas);
        int canvas = WindowsPointer.CanvasFor(shown, baseSize);
        SizeLimitText.Text = canvas > baseSize
            ? $"Pixel-sharp. Windows' pointer size is raised to {canvas} px while CursorForge runs (yours: {baseSize} px)."
            : $"Pixel-sharp. Fits your Windows pointer size ({baseSize} px).";
        SizeValue.Text = $"{shown.Size} px";
        OutlineValue.Text = s.OutlineWidth == 0 ? "off" : $"{s.OutlineWidth:0.#} px";
        GlowSizeValue.Text = $"{s.GlowRadius:0} px";
        GlowStrengthValue.Text = $"{s.GlowStrength * 100:0}%";
        OpacityValue.Text = $"{s.Opacity * 100:0}%";
        GlowPanel.Visibility = s.Glow ? Visibility.Visible : Visibility.Collapsed;
        HotDotPanel.Visibility = s.HotspotDot ? Visibility.Visible : Visibility.Collapsed;
        HotDotSizeValue.Text = $"{s.HotspotDotSize:0} px";
        bool custom = s.Shape == CursorShape.Custom;
        CustomPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        VectorOptions.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;

        var preset = Presets.Find(_cfg.PresetId);
        var savedPreset = _cfg.SavedPresets.FirstOrDefault(p => p.Key == _cfg.PresetId);
        PresetHint.Text = preset != null ? $"Based on {preset.Name}"
            : savedPreset != null ? $"Based on {savedPreset.Name}" : "Custom style";

        var rc = CursorRenderer.Render(shown);
        var bmp = ToBitmap(rc);
        PreviewLight.Source = bmp;
        PreviewDark.Source = bmp;
        LogoImage.Source = ToBitmap(CursorRenderer.RenderIcon(s, (int)Math.Round(28 * _ppd)));

        RefreshStates(shown);

        FlashPanel.Visibility = _cfg.ClickFlash.Enabled ? Visibility.Visible : Visibility.Collapsed;
        FlashDurationValue.Text = $"{_cfg.ClickFlash.DurationMs} ms";
        int flashPx = (int)Math.Round(28 * _ppd);
        FlashLeftPreview.Source = ToBitmap(CursorRenderer.RenderIcon(_cfg.ClickFlash.Apply(shown, right: false), flashPx));
        FlashRightPreview.Source = ToBitmap(CursorRenderer.RenderIcon(_cfg.ClickFlash.Apply(shown, right: true), flashPx));

        int iconPx = (int)Math.Round(24 * _ppd);
        foreach (var (shape, (_, icon)) in _shapeButtons)
        {
            if (icon == null) continue;
            var st = s.Clone();
            st.Shape = shape;
            st.OutlineWidth = Math.Min(st.OutlineWidth, 1.5f);
            icon.Source = ToBitmap(CursorRenderer.RenderIcon(st, iconPx));
        }
    }

    // ------------------------------------------------------------------ change handling

    void OnInput()
    {
        if (_loading || !_ready) return;
        ReadControls();
        RefreshVisuals();
        ScheduleSave();
    }

    void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OnInput();

    void OnControlClick(object sender, RoutedEventArgs e) => OnInput();

    void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void Commit()
    {
        try { ConfigStore.Save(_cfg); }
        catch (Exception ex) { MessageBox.Show(this, "Couldn't save settings:\n" + ex.Message, "CursorForge"); return; }
        AgentClient.Reload();
        UpdateAgentStatus();
    }

    void OnActivated(object? sender, EventArgs e)
    {
        // The tray menu / hotkeys may have changed the config while we were in the background.
        if (!_ready || _saveTimer.IsEnabled) return;
        var fresh = ConfigStore.Load();
        if (ConfigStore.Serialize(fresh) == ConfigStore.Serialize(_cfg))
        {
            RefreshVisuals(); // the Windows pointer size may have changed while we were in the background
            return;
        }
        _cfg = fresh;
        LoadIntoControls();
        UpdateAgentStatus();
    }

    // ------------------------------------------------------------------ saved presets

    void OnSavePreset(object sender, RoutedEventArgs e)
    {
        int n = _cfg.SavedPresets.Count + 1;
        string suggestion = "My cursor " + n;
        while (_cfg.SavedPresets.Any(p => p.Name == suggestion)) suggestion = "My cursor " + ++n;
        var dlg = new NameDialog("Save preset", suggestion) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var saved = new SavedPreset { Name = dlg.Value };
        CaptureInto(saved);
        _cfg.SavedPresets.Add(saved);
        _cfg.PresetId = saved.Key;
        BuildPresetTiles();
        LoadIntoControls();
        ScheduleSave();
    }

    /// <summary>Copies the current look into a saved preset, with its own copy of an imported image.</summary>
    void CaptureInto(SavedPreset saved)
    {
        saved.Style = _cfg.Style.Clone();
        saved.ClickFlash = _cfg.ClickFlash.Clone();
        saved.StateCursors = _cfg.StateCursors;
        try
        {
            if (saved.Style.Shape == CursorShape.Custom && File.Exists(ConfigStore.CustomImagePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(saved.ImagePath)!);
                File.Copy(ConfigStore.CustomImagePath, saved.ImagePath, overwrite: true);
            }
            else if (File.Exists(saved.ImagePath)) File.Delete(saved.ImagePath);
        }
        catch { }
    }

    void ApplySaved(SavedPreset saved)
    {
        _cfg.Style = saved.Style.Clone();
        _cfg.ClickFlash = saved.ClickFlash.Clone();
        _cfg.StateCursors = saved.StateCursors;
        _cfg.PresetId = saved.Key;
        if (saved.Style.Shape == CursorShape.Custom && File.Exists(saved.ImagePath))
        {
            try { File.Copy(saved.ImagePath, ConfigStore.CustomImagePath, overwrite: true); }
            catch { }
        }
        BuildPresetTiles(); // custom-image previews depend on which image is imported
        LoadIntoControls();
        ScheduleSave();
    }

    void UpdateSaved(SavedPreset saved)
    {
        CaptureInto(saved);
        _cfg.PresetId = saved.Key;
        BuildPresetTiles();
        LoadIntoControls();
        ScheduleSave();
    }

    void RenameSaved(SavedPreset saved)
    {
        var dlg = new NameDialog("Rename preset", saved.Name) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        saved.Name = dlg.Value;
        BuildPresetTiles();
        LoadIntoControls();
        ScheduleSave();
    }

    void DeleteSaved(SavedPreset saved)
    {
        var answer = MessageBox.Show(this, $"Delete the preset \u201C{saved.Name}\u201D?", "CursorForge",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        _cfg.SavedPresets.Remove(saved);
        try { if (File.Exists(saved.ImagePath)) File.Delete(saved.ImagePath); }
        catch { }
        if (_cfg.PresetId == saved.Key) _cfg.PresetId = "";
        BuildPresetTiles();
        LoadIntoControls();
        ScheduleSave();
    }

    void ApplyPreset(Preset preset)
    {
        _cfg.PresetId = preset.Id;
        _cfg.Style = preset.Style.Clone();
        LoadIntoControls();
        ScheduleSave();
    }

    void OnShapeClick(CursorShape shape)
    {
        if (_loading) return;
        if (shape == CursorShape.Custom && CustomImage.TryLoad(ConfigStore.CustomImagePath) == null)
        {
            if (!ImportImage()) LoadIntoControls(); // cancelled: restore the previous selection
            return;
        }
        _cfg.Style.Shape = shape;
        LoadIntoControls();
        ScheduleSave();
    }

    void OnImportImage(object sender, RoutedEventArgs e) => ImportImage();

    void OnOpenPointerSettings(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:easeofaccess-mousepointer") { UseShellExecute = true })?.Dispose(); }
        catch { }
    }

    bool ImportImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a cursor image",
            Filter = "Images and cursors|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.cur;*.webp|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return false;
        try
        {
            var (img, hx, hy) = ImageImport.Load(dlg.FileName);
            img.Save(ConfigStore.CustomImagePath);
            var s = _cfg.Style;
            s.Shape = CursorShape.Custom;
            s.CustomHotX = hx;
            s.CustomHotY = hy;
            s.Size = Math.Clamp(Math.Max(img.Width, img.Height), 24, 96);
            BuildPresetTiles(); // saved custom-image presets preview from the imported image
            LoadIntoControls();
            ScheduleSave();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't load that image:\n" + ex.Message, "CursorForge", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    void OnHotspotClick(object sender, RoutedEventArgs e)
    {
        float v = HotCenter.IsChecked == true ? 0.5f : 0f;
        _cfg.Style.CustomHotX = v;
        _cfg.Style.CustomHotY = v;
        RefreshVisuals();
        ScheduleSave();
    }

    void OnEnabledClick(object sender, RoutedEventArgs e)
    {
        _cfg.Enabled = EnabledSwitch.IsChecked == true;
        _saveTimer.Stop();
        Commit();
    }

    void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (PageCursor == null || PageApps == null || PageSettings == null) return;
        PageCursor.Visibility = TabCursor.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageApps.Visibility = TabApps.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = TabSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ overlay apps

    void RebuildApps()
    {
        AppsList.Children.Clear();
        foreach (var app in _cfg.OverlayApps.ToList())
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = app.Process + ".exe", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            string group = "mode-" + Guid.NewGuid().ToString("N");
            var modes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var mode in new[] { OverlayMode.Auto, OverlayMode.Always })
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("Seg"), Content = mode.ToString(), GroupName = group, IsChecked = app.Mode == mode,
                };
                rb.Click += (_, _) => { app.Mode = mode; ScheduleSave(); };
                modes.Children.Add(rb);
            }
            Grid.SetColumn(modes, 1);
            grid.Children.Add(modes);

            var remove = new Button { Style = (Style)FindResource("BtnGhost"), Content = "Remove", Margin = new Thickness(10, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                _cfg.OverlayApps.Remove(app);
                RebuildApps();
                ScheduleSave();
            };
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);

            AppsList.Children.Add(new Border
            {
                Background = (Brush)FindResource("Surface2"), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 8, 8, 8), Margin = new Thickness(0, 0, 0, 8), Child = grid,
            });
        }
        AppsEmpty.Visibility = _cfg.OverlayApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void AddApp(string raw)
    {
        string name = OverlayApp.Normalize(raw);
        if (name.Length == 0) return;
        if (!_cfg.OverlayApps.Any(a => a.Matches(name)))
            _cfg.OverlayApps.Add(new OverlayApp { Process = name });
        RebuildApps();
        ScheduleSave();
    }

    void OnAddApp(object sender, RoutedEventArgs e)
    {
        AddApp(AddAppBox.Text);
        AddAppBox.Clear();
    }

    void OnAddAppKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnAddApp(sender, e);
        e.Handled = true;
    }

    void OnPickApp(object sender, RoutedEventArgs e)
    {
        var picker = new PickAppWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.Selected is { } name) AddApp(name);
    }

    // ------------------------------------------------------------------ settings / agent

    void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        _cfg.StartWithWindows = AutostartSwitch.IsChecked == true;
        Autostart.Set(_cfg.StartWithWindows);
        ScheduleSave();
    }

    void UpdateAgentStatus()
    {
        bool running = AgentClient.IsRunning;
        StatusDot.Fill = (Brush)FindResource(!running ? "Bad" : _cfg.Enabled ? "Good" : "Warn");
        StatusText.Text = !running ? "Agent stopped" : _cfg.Enabled ? "Active" : "Paused";
        AgentStartStop.Content = running ? "Stop agent" : "Start agent";
        AgentInfo.Text = !AgentClient.IsInstalled
            ? $"{Ipc.AgentExe} was not found next to this app. Build with build.ps1 so both files end up in the same folder."
            : running
                ? $"Running  ·  {AgentClient.AgentPath}"
                : $"Not running  ·  {AgentClient.AgentPath}";
    }

    async void OnAgentStartStop(object sender, RoutedEventArgs e)
    {
        if (AgentClient.IsRunning) AgentClient.Stop();
        else AgentClient.Start();
        await Task.Delay(400);
        UpdateAgentStatus();
    }

    async void OnAgentRestart(object sender, RoutedEventArgs e)
    {
        AgentClient.Stop();
        for (int i = 0; i < 30 && AgentClient.IsRunning; i++) await Task.Delay(100);
        AgentClient.Start();
        await Task.Delay(400);
        UpdateAgentStatus();
    }
}
