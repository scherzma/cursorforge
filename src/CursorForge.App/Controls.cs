using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CursorForge.Ui;

/// <summary>Swatch palette + hex box. Picking a swatch keeps the current alpha.</summary>
public sealed class ColorRow : WrapPanel
{
    static readonly string[] Palette =
        ["#FFFFFF", "#0A0A0A", "#FFD60A", "#FF3B30", "#FF3EA5", "#8B6CFF", "#22D3EE", "#B6FF3B"];

    readonly TextBox _hex = new() { Width = 100, Padding = new Thickness(8, 4, 8, 4), FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"), ToolTip = "#RRGGBB or #AARRGGBB (alpha first)" };
    readonly List<Border> _swatches = [];
    string _value = "#FFFFFFFF";

    public event Action? Changed;

    public ColorRow()
    {
        Orientation = Orientation.Horizontal;
        foreach (var c in Palette)
        {
            var b = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)),
                BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Tag = c, ToolTip = c,
                VerticalAlignment = VerticalAlignment.Center,
            };
            b.MouseLeftButtonUp += (_, _) => Set(AlphaOf(_value) + c.TrimStart('#'), raise: true);
            _swatches.Add(b);
            Children.Add(b);
        }
        _hex.LostKeyboardFocus += (_, _) => CommitHex();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitHex(); };
        Children.Add(_hex);
        Refresh();
    }

    public string Value
    {
        get => _value;
        set => Set(value, raise: false);
    }

    void CommitHex()
    {
        string t = _hex.Text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        if (Rgba.TryParse(t, out _)) Set(t.Length == 7 ? AlphaOf(_value) + t[1..] : t[1..], raise: true);
        else _hex.Text = _value;
    }

    void Set(string v, bool raise)
    {
        v = v.Trim().TrimStart('#').ToUpperInvariant();
        if (v.Length == 6) v = "FF" + v;
        if (!Rgba.TryParse(v, out _)) return;
        v = "#" + v;
        bool changed = v != _value;
        _value = v;
        Refresh();
        if (raise && changed) Changed?.Invoke();
    }

    void Refresh()
    {
        _hex.Text = _value;
        string rgb = _value.Length == 9 ? _value[3..] : _value.TrimStart('#');
        foreach (var b in _swatches)
        {
            bool sel = string.Equals(((string)b.Tag).TrimStart('#'), rgb, StringComparison.OrdinalIgnoreCase);
            b.BorderBrush = sel ? (Brush)Application.Current.FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        }
    }

    static string AlphaOf(string argb) => argb.Length == 9 ? argb[1..3] : "FF";
}

/// <summary>Click, then press a key combo. Backspace/Delete disables the hotkey.</summary>
public sealed class HotkeyBox : TextBox
{
    public uint Modifiers { get; private set; }
    public uint VirtualKey { get; private set; }
    public event Action? Changed;

    public HotkeyBox()
    {
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        Tag = "Click and press keys";
    }

    public void Set(uint modifiers, uint vk)
    {
        Modifiers = modifiers;
        VirtualKey = vk;
        Text = Format(modifiers, vk);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Back or Key.Delete)
        {
            Set(0, 0);
            Changed?.Invoke();
            return;
        }
        if (key is Key.Tab or Key.Escape) { MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin) return;

        uint mods = 0;
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= Hotkey.Alt;
        if (m.HasFlag(ModifierKeys.Control)) mods |= Hotkey.Control;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= Hotkey.Shift;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= Hotkey.Win;
        bool isFKey = key >= Key.F1 && key <= Key.F24;
        if (mods == 0 && !isFKey) return; // a bare letter would hijack normal typing

        Set(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        Changed?.Invoke();
    }

    public static string Format(uint mods, uint vk)
    {
        if (vk == 0) return "Off";
        var parts = new List<string>();
        if ((mods & Hotkey.Control) != 0) parts.Add("Ctrl");
        if ((mods & Hotkey.Alt) != 0) parts.Add("Alt");
        if ((mods & Hotkey.Shift) != 0) parts.Add("Shift");
        if ((mods & Hotkey.Win) != 0) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        string name = key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (int)(key - Key.NumPad0),
            Key.OemTilde => "`",
            _ => key.ToString(),
        };
        parts.Add(name);
        return string.Join(" + ", parts);
    }
}

/// <summary>Small dark dialog asking for a name.</summary>
public sealed class NameDialog : Window
{
    readonly TextBox _box;

    public string Value => _box.Text.Trim();

    public NameDialog(string title, string initial)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)FindResource("Bg");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        SourceInitialized += (_, _) => NativeUi.ApplyDarkChrome(this);

        _box = new TextBox { Text = initial, Tag = "Name" };
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Accept();
            else if (e.Key == Key.Escape) DialogResult = false;
        };
        var ok = new Button { Style = (Style)FindResource("BtnPrimary"), Content = "Save", Padding = new Thickness(20, 7, 20, 7) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Style = (Style)FindResource("Btn"), Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(_box);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    void Accept()
    {
        if (Value.Length > 0) DialogResult = true;
    }
}

/// <summary>Lists running apps that have a window, to add them to the overlay list.</summary>
public sealed class PickAppWindow : Window
{
    public string? Selected { get; private set; }

    public PickAppWindow()
    {
        Title = "Pick a running app";
        Width = 480;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)FindResource("Bg");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        SourceInitialized += (_, _) => NativeUi.ApplyDarkChrome(this);

        var list = new StackPanel { Margin = new Thickness(16) };
        list.Children.Add(new TextBlock
        {
            Text = "Focus-able windows that are running right now:", Style = (Style)FindResource("Hint"),
            Margin = new Thickness(2, 0, 0, 12),
        });

        int self = Environment.ProcessId;
        var apps = new List<(string Name, string Title)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == self || p.MainWindowHandle == 0) continue;
                    string title = p.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    apps.Add((p.ProcessName, title));
                }
                catch { }
            }
        }
        foreach (var (name, title) in apps.DistinctBy(a => a.Name.ToLowerInvariant()).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = name + ".exe", FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock
            {
                Text = title, Foreground = (Brush)FindResource("Muted"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            });
            var btn = new Button
            {
                Style = (Style)FindResource("Btn"), Content = content, HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6), FontWeight = FontWeights.Normal,
            };
            btn.Click += (_, _) => { Selected = name; DialogResult = true; };
            list.Children.Add(btn);
        }
        Content = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
