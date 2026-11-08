using System.Runtime.InteropServices;

namespace PBZModManager;

/// <summary>
/// The app's dark look. WinForms has no dark mode on .NET 8 and native controls ignore most colors,
/// so every control the app shows comes from here to keep sizes, borders and scrollbars the same.
/// </summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(22, 23, 27);
    public static readonly Color Surface = Color.FromArgb(30, 32, 38);
    public static readonly Color Raised = Color.FromArgb(40, 43, 51);
    public static readonly Color Border = Color.FromArgb(58, 62, 74);
    public static readonly Color ScrollThumb = Color.FromArgb(78, 83, 97);
    public static readonly Color Text = Color.FromArgb(230, 231, 235);
    public static readonly Color Muted = Color.FromArgb(154, 159, 171);
    public static readonly Color Accent = Color.FromArgb(179, 38, 46);
    public static readonly Color AccentHover = Color.FromArgb(204, 56, 64);
    public static readonly Color Selection = Color.FromArgb(112, 32, 40);
    public static readonly Color Warning = Color.FromArgb(240, 163, 75);
    public static readonly Color Info = Color.FromArgb(127, 182, 240);
    public static readonly Color Danger = Color.FromArgb(255, 123, 123);
    public static readonly Color Dim = Color.FromArgb(124, 129, 141);
    public static readonly Color Disabled = Color.FromArgb(134, 139, 151);

    // Each language in a font that has all its letters; made on first use, after the language is chosen.
    static Font? baseFont, titleFont;
    static string FontName => L.Current switch { "zh" => "Microsoft YaHei UI", "ja" => "Yu Gothic UI", "en" => "Segoe UI", _ => "Malgun Gothic" };
    public static Font BaseFont => baseFont ??= new(FontName, 9f);
    public static Font TitleFont => titleFont ??= new(FontName, 9f, FontStyle.Bold);

    /// <summary>The height of every single-line control, so buttons, fields and dropdowns line up.</summary>
    public static int ControlHeight => BaseFont.Height + 13;

    static readonly Lazy<Icon?> AppIcon = new(() =>
    {
        try { return Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch (Exception) { return null; }
    });

    /// <summary>Asks Windows to draw system-owned parts (such as a text box's right-click menu) dark.</summary>
    public static void UseDarkSystemMenus()
    {
        try
        {
            SetPreferredAppMode(ForceDarkMode);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // An undocumented API: older Windows simply keeps light menus.
        }
    }

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = BaseFont;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        if (AppIcon.Value is { } icon) form.Icon = icon;
        form.HandleCreated += (_, _) =>
        {
            int on = 1;
            DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref on, sizeof(int));
        };
    }

    public static Button Button(string text, bool primary = false) => new DarkButton(text, primary);

    /// <summary>A borderless text box. Put it in <see cref="Field"/> or <see cref="MemoField"/> for the frame.</summary>
    public static TextBox TextBox(bool multiline = false)
    {
        var box = new TextBox
        {
            BackColor = Surface,
            ForeColor = Text,
            BorderStyle = BorderStyle.None,
            Multiline = multiline,
            Dock = DockStyle.Fill,
        };
        if (multiline)
        {
            box.ScrollBars = ScrollBars.Vertical;
            box.AcceptsReturn = true;
        }
        // The input method can come to these fields in full-width mode, typing "２Ｂ" and full-width spaces; it is
        // put back to half-width each time a field is entered. Hangul or Latin input stays as the user had it.
        box.GotFocus += (_, _) => { if (box.IsHandleCreated) box.BeginInvoke(() => Ime.UseHalfWidth(box)); };
        return box;
    }

    /// <summary>The frame of a single-line text box, matching the height and border of dropdowns.</summary>
    public static Panel Field(TextBox box)
    {
        var inner = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(6, (ControlHeight - 2 - box.PreferredHeight) / 2, 6, 0),
        };
        inner.Controls.Add(box);
        var frame = new Panel { Height = ControlHeight, BackColor = Border, Padding = new Padding(1) };
        frame.Controls.Add(inner);
        return frame;
    }

    /// <summary>The frame of a multi-line text box with the app's scrollbar.</summary>
    public static Panel MemoField(TextBox box)
    {
        var frame = new Panel { BackColor = Border, Padding = new Padding(1) };
        frame.Controls.Add(new ScrollHost(box, new Padding(6, 4, 0, 4)) { Dock = DockStyle.Fill });
        return frame;
    }

    /// <summary>A titled section: heading, bordered content, and an optional row of buttons underneath.</summary>
    public static Panel Group(string title, Control content, params Control[] buttons) => Group(title, content, buttons, Array.Empty<Control>());

    /// <param name="right">Controls kept at the right end of the button row, always shown.</param>
    public static Panel Group(string title, Control content, Control[] buttons, Control[] right)
    {
        var group = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4), BackColor = Background };
        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Border };
        Control body = content is DarkListView ? new ScrollHost(content) : content;
        body.Dock = DockStyle.Fill;
        frame.Controls.Add(body);
        group.Controls.Add(frame);
        if (buttons.Length > 0 || right.Length > 0) group.Controls.Add(new ButtonBar(buttons, right));
        group.Controls.Add(new Label
        {
            Text = title,
            Font = TitleFont,
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = Text,
            Padding = new Padding(0, 2, 0, 4),
        });
        return group;
    }

    public static void StyleSplit(SplitContainer split)
    {
        split.BackColor = Background;
        split.Panel1.BackColor = Background;
        split.Panel2.BackColor = Background;
        split.SplitterWidth = 5;
    }

    const int DwmUseImmersiveDarkMode = 20;
    const int ForceDarkMode = 2;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    static extern void FlushMenuThemes();
}

/// <summary>
/// A button drawn entirely by the app. The system draws a disabled button's text nearly black,
/// which is unreadable on the dark background.
/// </summary>
/// <summary>
/// One row of buttons that never wraps. Buttons that do not fit move, from the end, into a "더 보기" menu;
/// the controls on the right, such as a label and its list box, always stay together and in view.
/// </summary>
public sealed class ButtonBar : Panel
{
    const int Gap = 6;
    readonly Control[] buttons;
    readonly Control[] right;
    readonly DarkButton more = new(L.T("더 보기 ▾"), primary: false);
    readonly ContextMenuStrip menu = new() { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, BackColor = Theme.Surface, ForeColor = Theme.Text };

    public ButtonBar(Control[] buttons, Control[] right)
    {
        this.buttons = buttons;
        this.right = right;
        Dock = DockStyle.Bottom;
        Height = Theme.ControlHeight + 6;
        Padding = new Padding(0, 6, 0, 0);
        BackColor = Theme.Background;
        foreach (var c in buttons.Concat(right)) Controls.Add(c);
        Controls.Add(more);
        more.Margin = Padding.Empty;
        more.Click += (_, _) => ShowMenu();
        foreach (var b in buttons) b.EnabledChanged += (_, _) => more.Invalidate();
        Disposed += (_, _) => menu.Dispose();
    }

    /// <summary>The buttons the row could not show, now in the menu.</summary>
    public IEnumerable<Control> Hidden => buttons.Where(b => !b.Visible);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int top = Padding.Top, height = Theme.ControlHeight;
        int width = ClientSize.Width;

        // The right group, laid out from the right edge.
        int rightStart = width;
        foreach (var c in right.Reverse())
        {
            var w = c.PreferredSize.Width > 0 && c is not ComboBox ? c.PreferredSize.Width : c.Width;
            rightStart -= w;
            c.SetBounds(rightStart, top + (height - c.Height) / 2, w, c.Height);
            rightStart -= c is Label ? 4 : Gap;
        }
        int room = rightStart - (right.Length > 0 ? Gap * 2 : 0);

        int total = buttons.Sum(b => b.PreferredSize.Width + Gap);
        bool overflow = total > room;
        int limit = overflow ? room - more.PreferredSize.Width - Gap : room;
        // Buttons keep their order: once one does not fit, it and all after it go to the menu.
        int x = 0;
        bool full = false;
        foreach (var b in buttons)
        {
            var w = b.PreferredSize.Width;
            full |= x + w > limit;
            b.Visible = !full;
            if (full) continue;
            b.SetBounds(x, top, w, height);
            x += w + Gap;
        }
        more.Visible = overflow;
        if (overflow) more.SetBounds(x, top, more.PreferredSize.Width, height);
    }

    void ShowMenu()
    {
        menu.Items.Clear();
        foreach (var b in Hidden)
        {
            var item = new ToolStripMenuItem(b.Text) { Enabled = b.Enabled, ForeColor = Theme.Text };
            var target = b;
            // A hidden button ignores a click, and showing it for the moment lays the row out again and hides it
            // at once; so the menu runs the button's own action directly.
            item.Click += (_, _) => { if (target is DarkButton button && button.Enabled) button.Press(); };
            menu.Items.Add(item);
        }
        menu.Show(more, new Point(0, more.Height));
    }
}

/// <summary>
/// Dims the window during long work and shows a spinning ring with what is going on; a "3/10" or "30%"
/// in the message turns it into a filling ring. It only appears when the work takes a moment, so quick
/// steps do not flash. The windows never take focus from the main one.
/// </summary>
public sealed class BusyOverlay : IDisposable
{
    readonly Form owner;
    readonly System.Windows.Forms.Timer delay = new() { Interval = 300 };
    Shade? dim;
    Card? card;
    string message = "";

    public BusyOverlay(Form owner)
    {
        this.owner = owner;
        delay.Tick += (_, _) => { delay.Stop(); Open(); };
        owner.Move += (_, _) => Place();
        owner.Resize += (_, _) => Place();
    }

    public bool Visible => dim != null;

    public void Show(string text)
    {
        message = text;
        if (dim == null && !delay.Enabled) delay.Start();
    }

    public void Report(string text)
    {
        message = text;
        card?.SetMessage(text);
    }

    public void Hide()
    {
        delay.Stop();
        card?.Close();
        dim?.Close();
        card?.Dispose();
        dim?.Dispose();
        card = null;
        dim = null;
    }

    void Open()
    {
        if (owner.IsDisposed || !owner.Visible || owner.WindowState == FormWindowState.Minimized) return;
        dim = new Shade { Owner = owner };
        card = new Card { Owner = dim };
        card.SetMessage(message);
        Place();
        dim.Show();
        card.Show();
    }

    void Place()
    {
        if (dim == null || card == null) return;
        var area = owner.RectangleToScreen(owner.ClientRectangle);
        dim.Bounds = area;
        card.Location = new Point(area.Left + (area.Width - card.Width) / 2, area.Top + (area.Height - card.Height) / 2);
    }

    public void Dispose()
    {
        Hide();
        delay.Dispose();
    }

    class Quiet : Form
    {
        public Quiet()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int NoActivate = 0x08000000, ToolWindow = 0x00000080;
                var p = base.CreateParams;
                p.ExStyle |= NoActivate | ToolWindow;
                return p;
            }
        }
    }

    sealed class Shade : Quiet
    {
        public Shade()
        {
            BackColor = Color.Black;
            Opacity = 0.55;
        }
    }

    sealed class Card : Quiet
    {
        readonly System.Windows.Forms.Timer spin = new() { Interval = 30 };
        float angle;
        string text = "";
        float? share;

        public Card()
        {
            BackColor = Theme.Surface;
            Size = new Size(360, 170);
            DoubleBuffered = true;
            spin.Tick += (_, _) => { angle = (angle + 8) % 360; Invalidate(); };
            spin.Start();
            Disposed += (_, _) => spin.Dispose();
        }

        public void SetMessage(string message)
        {
            text = message;
            var count = System.Text.RegularExpressions.Regex.Match(message, @"(\d+)\s*/\s*(\d+)");
            var percent = System.Text.RegularExpressions.Regex.Match(message, @"(\d+)\s*%");
            share = count.Success && int.Parse(count.Groups[2].Value) > 0 ? Math.Min(1f, int.Parse(count.Groups[1].Value) / (float)int.Parse(count.Groups[2].Value))
                : percent.Success ? Math.Min(1f, int.Parse(percent.Groups[1].Value) / 100f)
                : null;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var ring = new Rectangle(Width / 2 - 26, 22, 52, 52);
            using (var track = new Pen(Theme.Raised, 6)) g.DrawEllipse(track, ring);
            using (var arc = new Pen(Theme.Accent, 6) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
            {
                if (share is { } s)
                {
                    if (s > 0) g.DrawArc(arc, ring, -90, 360 * s);
                    TextRenderer.DrawText(g, $"{(int)(s * 100)}%", Theme.TitleFont, ring, Theme.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else g.DrawArc(arc, ring, angle, 100);
            }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            TextRenderer.DrawText(g, text, Theme.BaseFont, new Rectangle(16, 90, Width - 32, Height - 100), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>Dark colors for drop-down menus.</summary>
public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new Colors()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Disabled;
        base.OnRenderItemText(e);
    }

    sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Selection;
        public override Color MenuItemSelected => Theme.Selection;
        public override Color MenuItemSelectedGradientBegin => Theme.Selection;
        public override Color MenuItemSelectedGradientEnd => Theme.Selection;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color SeparatorDark => Theme.Border;
    }
}

/// <summary>The input method's state for one control.</summary>
public static class Ime
{
    const int FullShape = 0x0008;

    /// <summary>Takes the input method out of full-width mode for the control; true when it was in it.</summary>
    public static bool UseHalfWidth(Control control)
    {
        if (!control.IsHandleCreated) return false;
        var context = ImmGetContext(control.Handle);
        if (context == IntPtr.Zero) return false;
        try
        {
            if (!ImmGetConversionStatus(context, out var conversion, out var sentence) || (conversion & FullShape) == 0) return false;
            return ImmSetConversionStatus(context, conversion & ~FullShape, sentence);
        }
        finally
        {
            ImmReleaseContext(control.Handle, context);
        }
    }

    /// <summary>Whether the control's input method is in full-width mode now.</summary>
    public static bool IsFullWidth(Control control)
    {
        var context = ImmGetContext(control.Handle);
        if (context == IntPtr.Zero) return false;
        try { return ImmGetConversionStatus(context, out var conversion, out _) && (conversion & FullShape) != 0; }
        finally { ImmReleaseContext(control.Handle, context); }
    }

    /// <summary>Puts the control's input method in full-width mode; for checks.</summary>
    public static void UseFullWidthForChecks(Control control)
    {
        var context = ImmGetContext(control.Handle);
        if (context == IntPtr.Zero) return;
        try { if (ImmGetConversionStatus(context, out var conversion, out var sentence)) ImmSetConversionStatus(context, conversion | FullShape, sentence); }
        finally { ImmReleaseContext(control.Handle, context); }
    }

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    static extern IntPtr ImmGetContext(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr context);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    static extern bool ImmGetConversionStatus(IntPtr context, out int conversion, out int sentence);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    static extern bool ImmSetConversionStatus(IntPtr context, int conversion, int sentence);
}

/// <summary>
/// Types a full-width space (U+3000), which a Korean or Japanese IME gives in its full-width mode, as an
/// ordinary space everywhere in the app: in names, memos and addresses it only looks like one.
/// </summary>
public sealed class FullWidthSpaceFilter : IMessageFilter
{
    const int WmChar = 0x0102, WmImeChar = 0x0286;

    // A changed message is not passed on as changed, so the full-width one is dropped and a space sent instead.
    // Only the final character is caught: the input method's own message turns into one of these by itself.
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmChar || (long)m.WParam != 0x3000) return false;
        // Sent now, so it keeps its place among the keys still waiting.
        SendMessageW(m.HWnd, WmChar, (IntPtr)' ', m.LParam);
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessageW(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}

public sealed class DarkButton : Button
{
    /// <summary>Runs the button's action as a click would, also while it sits hidden in a "더 보기" menu.</summary>
    public void Press() => OnClick(EventArgs.Empty);

    readonly bool primary;
    bool hover;
    bool pressed;

    public DarkButton(string text, bool primary)
    {
        this.primary = primary;
        Text = FormatText(text);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(0, Theme.ControlHeight);
        Padding = new Padding(8, 0, 8, 0);
        Margin = new Padding(0, 0, 6, 0);
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public static string FormatText(string text) => text;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = FormatText(value ?? "");
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Color fill, border, text;
        if (!Enabled)
        {
            fill = Theme.Surface;
            border = Theme.Border;
            text = Theme.Disabled;
        }
        else if (primary)
        {
            fill = pressed ? Theme.Accent : hover ? Theme.AccentHover : Theme.Accent;
            border = fill;
            text = Color.White;
        }
        else
        {
            fill = pressed ? Theme.Surface : hover ? Theme.Border : Theme.Raised;
            border = Theme.Border;
            text = Theme.Text;
        }
        if (Enabled && Focused && ShowFocusCues) border = Theme.Muted;

        using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, ClientRectangle);
        using (var pen = new Pen(border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        pressed = e.Button == MouseButtons.Left;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        pressed = false;
        Invalidate();
    }
}

/// <summary>A dropdown with the app's border, chevron, height and list colors.</summary>
public sealed class DarkComboBox : ComboBox
{
    const int WmPaint = 0x000F;
    const int WmEraseBkgnd = 0x0014;
    const int WmPrint = 0x0317;
    const int WmPrintClient = 0x0318;
    const int WmSetFocus = 0x0007, WmKillFocus = 0x0008, WmMouseMove = 0x0200, WmMouseLeave = 0x02A3, WmCaptureChanged = 0x0215;
    const int WmLButtonUp = 0x0202, CbnDropDownClosed = 0x0008;

    public DarkComboBox(ComboBoxStyle style)
    {
        DropDownStyle = style;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Theme.ControlHeight - 6;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Margin = Padding.Empty;
    }

    /// <summary>
    /// The themed native box redraws itself on hover and focus outside WM_PAINT, with a light frame that
    /// this control never gets to cover. Without the theme it only draws when asked, and the frame stays dark.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetWindowTheme(Handle, "", "");
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool inList = !e.State.HasFlag(DrawItemState.ComboBoxEdit);
        bool highlighted = inList && e.State.HasFlag(DrawItemState.Selected);
        using var fill = new SolidBrush(highlighted ? Theme.Selection : Theme.Surface);
        e.Graphics.FillRectangle(fill, e.Bounds);
        var text = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 6), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString(), Font, text, highlighted ? Color.White : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmEraseBkgnd)
        {
            m.Result = (IntPtr)1;
            return;
        }
        if (m.Msg is WmPrint or WmPrintClient)
        {
            // Drawing into another surface, as for a window picture: the same dark chrome over the native layer.
            base.WndProc(ref m);
            using var g = Graphics.FromHdc(m.WParam);
            PaintChrome(g);
            return;
        }
        if (m.Msg != WmPaint)
        {
            base.WndProc(ref m);
            // Anything the native box may have drawn straight to the screen on these is painted over again.
            if (m.Msg is WmSetFocus or WmKillFocus or WmMouseLeave or WmCaptureChanged or WmLButtonUp) Invalidate();
            return;
        }

        // Native combo boxes keep light borders and a system arrow. Painting over them on screen
        // shows the light version for a moment each repaint, so both layers go to a buffer first.
        var hdc = BeginPaint(Handle, out var paint);
        try
        {
            using var screen = Graphics.FromHdc(hdc);
            using var buffer = BufferedGraphicsManager.Current.Allocate(screen, ClientRectangle);
            var bufferDc = buffer.Graphics.GetHdc();
            var native = Message.Create(Handle, WmPaint, bufferDc, IntPtr.Zero);
            base.WndProc(ref native);
            buffer.Graphics.ReleaseHdc(bufferDc);
            PaintChrome(buffer.Graphics);
            buffer.Render(screen);
        }
        finally
        {
            EndPaint(Handle, ref paint);
        }
        m.Result = IntPtr.Zero;
    }

    void PaintChrome(Graphics g)
    {
        // The plain native box has a two-pixel sunken frame and a raised arrow button; both are covered.
        int buttonWidth = SystemInformation.VerticalScrollBarWidth;
        var button = new Rectangle(ClientSize.Width - buttonWidth - 3, 1, buttonWidth + 2, ClientSize.Height - 2);
        using (var fill = new SolidBrush(Theme.Surface))
        {
            g.FillRectangle(fill, button);
            using var inner = new Pen(Theme.Surface);
            g.DrawRectangle(inner, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
        }
        using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var chevron = new Pen(Theme.Muted, 1.5f);
        int cx = button.Left + button.Width / 2, cy = button.Top + button.Height / 2;
        g.DrawLines(chevron, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PaintStruct
    {
        public IntPtr Hdc;
        public int Erase;
        public int Left, Top, Right, Bottom;
        public int Restore;
        public int IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }

    [DllImport("user32.dll")]
    static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct paint);

    [DllImport("user32.dll")]
    static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);
}

/// <summary>
/// A splitter that shows where it is: a faint line, brighter under the mouse. It moves the panels as it is
/// dragged instead of drawing the system's dotted bar, and takes no focus, so no dotted frame stays on it.
/// </summary>
public sealed class DarkSplitContainer : SplitContainer
{
    // Where in the splitter the mouse took hold; -1 when not dragging.
    int grab = -1;
    bool hover;

    public DarkSplitContainer()
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    bool Vertical => Orientation == Orientation.Vertical;
    int At(MouseEventArgs e) => Vertical ? e.X : e.Y;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !IsSplitterFixed && SplitterRectangle.Contains(e.Location))
        {
            grab = At(e) - SplitterDistance;
            Capture = true;
            Invalidate(SplitterRectangle);
            return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (grab >= 0)
        {
            int size = Vertical ? Width : Height;
            int wanted = Math.Clamp(At(e) - grab, Panel1MinSize, Math.Max(Panel1MinSize, size - Panel2MinSize - SplitterWidth));
            if (wanted != SplitterDistance)
            {
                // Said before each step, so whoever listens knows the user moved it.
                OnSplitterMoving(new SplitterCancelEventArgs(e.X, e.Y, Vertical ? wanted : e.X, Vertical ? e.Y : wanted));
                SplitterDistance = wanted;
            }
            return;
        }
        var over = !IsSplitterFixed && SplitterRectangle.Contains(e.Location);
        Cursor = over ? (Vertical ? Cursors.VSplit : Cursors.HSplit) : Cursors.Default;
        if (over != hover)
        {
            hover = over;
            Invalidate(SplitterRectangle);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (grab >= 0)
        {
            grab = -1;
            Capture = false;
            Invalidate(SplitterRectangle);
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (grab < 0 && hover)
        {
            hover = false;
            Cursor = Cursors.Default;
            Invalidate(SplitterRectangle);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bar = SplitterRectangle;
        using (var back = new SolidBrush(Theme.Background)) e.Graphics.FillRectangle(back, bar);
        using var line = new SolidBrush(grab >= 0 ? Theme.Muted : hover ? Theme.Dim : Theme.Border);
        // A line along the middle, kept off the panels' frames at either end.
        var thin = Vertical
            ? new Rectangle(bar.X + bar.Width / 2, bar.Y + 8, 1, Math.Max(0, bar.Height - 16))
            : new Rectangle(bar.X + 8, bar.Y + bar.Height / 2, Math.Max(0, bar.Width - 16), 1);
        e.Graphics.FillRectangle(line, thin);
    }
}

/// <summary>
/// Shows a control with <see cref="DarkScrollBar"/> instead of its native scrollbar, which cannot be
/// colored. The control is laid out wider than the host, so the native bar falls outside and is clipped.
/// </summary>
public sealed class ScrollHost : Panel
{
    readonly Control target;
    readonly Padding inset;
    readonly DarkScrollBar bar;

    public ScrollHost(Control target, Padding inset = default)
    {
        this.target = target;
        this.inset = inset;
        bar = new DarkScrollBar(target);
        BackColor = Theme.Surface;
        target.Dock = DockStyle.None;
        Controls.Add(bar);
        Controls.Add(target);
        bar.BringToFront();
    }

    /// <summary>The width of the control's area that is actually visible.</summary>
    public int ViewWidth => Math.Max(0, ClientSize.Width - DarkScrollBar.Thickness - inset.Horizontal);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        target.SetBounds(inset.Left, inset.Top, ViewWidth + SystemInformation.VerticalScrollBarWidth,
            Math.Max(0, ClientSize.Height - inset.Vertical));
        bar.SetBounds(ClientSize.Width - DarkScrollBar.Thickness, 0, DarkScrollBar.Thickness, ClientSize.Height);
        (target as DarkListView)?.FitColumns();
    }
}

/// <summary>The app's one scrollbar. It reads and drives the native scroll state of its target.</summary>
public sealed class DarkScrollBar : Control
{
    public const int Thickness = 12;
    const int MinThumbHeight = 28;

    const int SbVert = 1;
    const uint SifAll = 0x17;
    const int WmVScroll = 0x0115;
    const int WmMouseWheel = 0x020A;
    const int SbThumbPosition = 4;
    const int LvmScroll = 0x1014;
    const int EmSetScrollPos = 0x04DE;

    readonly Control target;
    readonly System.Windows.Forms.Timer sync = new() { Interval = 60 };
    ScrollInfo info;
    bool hover;
    int dragOffset = -1;

    public DarkScrollBar(Control target)
    {
        this.target = target;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Theme.Surface;
        // Lists scroll through keys and the wheel with no event to hook, so the state is polled.
        sync.Tick += (_, _) => Sync();
        sync.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) sync.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>A list's header sits above the scrollable rows, so the track starts below it.</summary>
    int TrackTop => target is DarkListView list ? list.HeaderHeight : 0;

    bool CanScroll => info.Page > 0 && info.Page <= info.Max - info.Min;

    Rectangle ThumbBounds()
    {
        if (!CanScroll) return Rectangle.Empty;
        int track = Height - TrackTop - 4;
        int range = info.Max - info.Min + 1;
        int thumb = Math.Min(track, Math.Max(MinThumbHeight, (int)(track * (long)info.Page / range)));
        int span = range - info.Page;
        int y = span <= 0 ? 0 : (int)((track - thumb) * (long)(info.Pos - info.Min) / span);
        return new Rectangle(2, TrackTop + 2 + y, Width - 4, thumb);
    }

    void Sync()
    {
        if (!target.IsHandleCreated || !IsHandleCreated) return;
        var now = new ScrollInfo { Size = Marshal.SizeOf<ScrollInfo>(), Mask = SifAll };
        if (!GetScrollInfo(target.Handle, SbVert, ref now)) now = default;
        if (now.Min == info.Min && now.Max == info.Max && now.Page == info.Page && now.Pos == info.Pos) return;
        info = now;
        Invalidate();
    }

    void ScrollTo(int position)
    {
        position = Math.Clamp(position, info.Min, Math.Max(info.Min, info.Max - info.Page + 1));
        if (position == info.Pos) return;
        if (target is DarkListView list)
        {
            // Report-view positions count rows, but the scroll message takes pixels.
            SendMessage(list.Handle, LvmScroll, IntPtr.Zero, (position - info.Pos) * list.GetItemRect(0).Height);
        }
        else if (target is RichTextBox)
        {
            // A long text's position goes past what the scroll message's 16 bits can carry.
            var point = new Point(0, position);
            SendMessage(target.Handle, EmSetScrollPos, IntPtr.Zero, ref point);
        }
        else
        {
            SendMessage(target.Handle, WmVScroll, (IntPtr)((position << 16) | SbThumbPosition), 0);
        }
        Sync();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Surface);
        if (target is DarkListView)
        {
            // Continues the list's header band over the bar.
            using var header = new SolidBrush(Theme.Raised);
            e.Graphics.FillRectangle(header, 0, 0, Width, TrackTop);
            using var line = new Pen(Theme.Border);
            e.Graphics.DrawLine(line, 0, TrackTop - 1, Width, TrackTop - 1);
        }
        var thumb = ThumbBounds();
        if (thumb.IsEmpty) return;
        var color = dragOffset >= 0 ? Theme.Muted : hover ? Theme.Dim : Theme.ScrollThumb;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, thumb);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Sync();
        var thumb = ThumbBounds();
        if (e.Button != MouseButtons.Left || thumb.IsEmpty) return;
        if (e.Y >= thumb.Top && e.Y < thumb.Bottom)
        {
            dragOffset = e.Y - thumb.Top;
            Capture = true;
            Invalidate();
        }
        else if (e.Y >= TrackTop)
        {
            ScrollTo(info.Pos + (e.Y < thumb.Top ? -info.Page : info.Page));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragOffset < 0) return;
        var thumb = ThumbBounds();
        int free = Height - TrackTop - 4 - thumb.Height;
        if (free <= 0) return;
        int span = info.Max - info.Min + 1 - info.Page;
        var fraction = (e.Y - dragOffset - TrackTop - 2) / (double)free;
        ScrollTo(info.Min + (int)Math.Round(fraction * span));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragOffset = -1;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var point = PointToScreen(e.Location);
        SendMessage(target.Handle, WmMouseWheel, (IntPtr)(e.Delta << 16), (point.Y << 16) | (point.X & 0xFFFF));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ScrollInfo
    {
        public int Size;
        public uint Mask;
        public int Min;
        public int Max;
        public int Page;
        public int Pos;
        public int TrackPos;
    }

    [DllImport("user32.dll")]
    static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, int lParam);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, ref Point lParam);
}

public sealed record ListRow(string[] Cells, object Tag, Color? Color = null, bool DividerBelow = false, bool IsSeparator = false);

/// <summary>A tooltip drawn in the dark palette with custom borders, padding and font.</summary>
public sealed class DarkToolTip : ToolTip
{
    string? lastShownText;

    public DarkToolTip()
    {
        OwnerDraw = true;
        ShowAlways = true;
        BackColor = Theme.Raised;
        ForeColor = Theme.Text;

        Popup += (_, e) =>
        {
            var text = lastShownText ?? (e.AssociatedControl != null ? GetToolTip(e.AssociatedControl) : "");
            lastShownText = null;
            if (string.IsNullOrEmpty(text)) return;
            var font = Theme.BaseFont;
            var measured = TextRenderer.MeasureText(text, font, Size.Empty,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            e.ToolTipSize = new Size(measured.Width + 16, Math.Max(Theme.ControlHeight - 6, measured.Height + 8));
        };

        Draw += (_, e) =>
        {
            using (var bg = new SolidBrush(Theme.Raised))
                e.Graphics.FillRectangle(bg, e.Bounds);

            using (var border = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(border, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);

            var textRect = new Rectangle(8, 0, e.Bounds.Width - 16, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.BaseFont, textRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
    }

    public new void Show(string text, IWin32Window window, int x, int y, int duration)
    {
        lastShownText = text;
        base.Show(text, window, x, y, duration);
    }

    public new void Show(string text, IWin32Window window, int x, int y)
    {
        lastShownText = text;
        base.Show(text, window, x, y);
    }
}

/// <summary>A details list drawn in the dark palette, with one column taking up the spare width.</summary>
public sealed class DarkListView : ListView
{
    const TextFormatFlags CellFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
    const int LvmGetHeader = 0x101F;

    readonly int fillColumn;
    readonly HashSet<ListViewItem> dividerBelowItems = new();
    readonly HashSet<ListViewItem> separatorItems = new();

    public DarkListView(int fillColumn, params (string Title, int Width)[] columns)
    {
        this.fillColumn = fillColumn;
        DoubleBuffered = true;
        OwnerDraw = true;
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        MultiSelect = false;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        foreach (var (title, width) in columns) Columns.Add(title, width);

        DrawColumnHeader += (_, e) =>
        {
            using var fill = new SolidBrush(Theme.Raised);
            e.Graphics.FillRectangle(fill, e.Bounds);
            using var line = new Pen(Theme.Border);
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, Theme.TitleFont, Inset(e.Bounds), Theme.Muted, CellFlags);
        };
        DrawItem += (_, e) => e.DrawDefault = false;
        DrawSubItem += (_, e) =>
        {
            if (e.Item != null && separatorItems.Contains(e.Item))
            {
                using var sepFill = new SolidBrush(Theme.Surface);
                e.Graphics.FillRectangle(sepFill, e.Bounds);
                int y = e.Bounds.Top + e.Bounds.Height / 2;
                using var linePen = new Pen(Theme.Border);
                e.Graphics.DrawLine(linePen, e.Bounds.Left, y, e.Bounds.Right, y);
                return;
            }

            bool selected = e.Item?.Selected == true;
            using var fill = new SolidBrush(selected ? Theme.Selection : Theme.Surface);
            e.Graphics.FillRectangle(fill, e.Bounds);
            var color = selected ? Color.White : e.Item?.ForeColor ?? Theme.Text;
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text, Font, Inset(e.Bounds), color, CellFlags);
            if (e.Item != null && dividerBelowItems.Contains(e.Item))
            {
                using var linePen = new Pen(Theme.Border);
                e.Graphics.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        };

        ItemSelectionChanged += (_, e) =>
        {
            if (e.IsSelected && e.Item != null && separatorItems.Contains(e.Item))
            {
                e.Item.Selected = false;
            }
        };
        MouseDown += (_, e) =>
        {
            var hit = HitTest(e.Location);
            if (hit.Item != null && separatorItems.Contains(hit.Item))
            {
                hit.Item.Selected = false;
            }
        };

        ShowItemToolTips = false;
        var tip = new DarkToolTip();
        var hoverTimer = new System.Windows.Forms.Timer { Interval = 200 };
        string? pendingTip = null;
        string? showingTip = null;
        Point lastHoverPos = Point.Empty;

        hoverTimer.Tick += (_, _) =>
        {
            hoverTimer.Stop();
            if (!string.IsNullOrEmpty(pendingTip))
            {
                showingTip = pendingTip;
                tip.Show(showingTip, this, lastHoverPos.X + 8, lastHoverPos.Y + 22, 6000);
            }
        };

        MouseMove += (_, e) =>
        {
            var hit = HitTest(e.Location);
            string? targetTip = null;

            if (hit.Item != null && !separatorItems.Contains(hit.Item))
            {
                int x = e.X;
                int colLeft = 0;
                for (int c = 0; c < Columns.Count; c++)
                {
                    int colWidth = Columns[c].Width;
                    if (x >= colLeft && x < colLeft + colWidth)
                    {
                        if (c < hit.Item.SubItems.Count)
                        {
                            var text = hit.Item.SubItems[c].Text;
                            if (!string.IsNullOrWhiteSpace(text) && text != "■" && text != "□" && text != "▣")
                            {
                                var measured = TextRenderer.MeasureText(text, Font, Size.Empty,
                                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                                if (measured.Width > colWidth - 8)
                                {
                                    var clean = text.Trim();
                                    targetTip = clean.StartsWith("└ ") ? clean[2..].Trim() : clean;
                                }
                            }
                        }
                        break;
                    }
                    colLeft += colWidth;
                }
            }

            if (targetTip == null)
            {
                hoverTimer.Stop();
                pendingTip = null;
                if (showingTip != null)
                {
                    showingTip = null;
                    tip.Hide(this);
                }
            }
            else if (targetTip != pendingTip)
            {
                pendingTip = targetTip;
                lastHoverPos = e.Location;
                hoverTimer.Stop();
                hoverTimer.Start();
            }
            else
            {
                lastHoverPos = e.Location;
            }
        };

        MouseLeave += (_, _) =>
        {
            hoverTimer.Stop();
            pendingTip = null;
            if (showingTip != null)
            {
                showingTip = null;
                tip.Hide(this);
            }
        };
    }

    public int RowHeight => Font.Height + 8;

    /// <summary>
    /// Replaces the rows only if they differ. Clearing and refilling an unchanged list is what made
    /// it blink on every refresh, and it also lost the scroll position.
    /// </summary>
    public void SetRows(IReadOnlyList<ListRow> rows)
    {
        bool same = Items.Count == rows.Count;
        for (int i = 0; same && i < rows.Count; i++)
        {
            var item = Items[i];
            same = item.SubItems.Count == rows[i].Cells.Length
                && item.ForeColor == (rows[i].Color ?? ForeColor)
                && rows[i].Cells.Select((cell, c) => item.SubItems[c].Text == cell).All(equal => equal)
                && (dividerBelowItems.Contains(item) == rows[i].DividerBelow)
                && (separatorItems.Contains(item) == rows[i].IsSeparator);
        }
        if (same)
        {
            for (int i = 0; i < rows.Count; i++) Items[i].Tag = rows[i].Tag;
            return;
        }

        int topIndex = 0;
        try { if (IsHandleCreated && Items.Count > 0 && TopItem != null) topIndex = TopItem.Index; }
        catch { }

        dividerBelowItems.Clear();
        separatorItems.Clear();
        BeginUpdate();
        Items.Clear();
        foreach (var row in rows)
        {
            var item = new ListViewItem(row.Cells) { Tag = row.Tag };
            if (row.Color is { } color) item.ForeColor = color;
            if (row.DividerBelow) dividerBelowItems.Add(item);
            if (row.IsSeparator) separatorItems.Add(item);
            Items.Add(item);
        }
        EndUpdate();

        if (IsHandleCreated && Items.Count > 0 && topIndex > 0)
        {
            try { TopItem = Items[Math.Min(topIndex, Items.Count - 1)]; }
            catch { }
        }
        FitColumns();
    }

    /// <summary>Keeps the row matching <paramref name="wanted"/> selected, or the first row if none does.</summary>
    public void KeepSelection(Func<ListViewItem, bool> wanted, bool ensureVisible = true)
    {
        var validItems = Items.Cast<ListViewItem>().Where(i => !separatorItems.Contains(i));
        var target = validItems.FirstOrDefault(wanted) ?? validItems.FirstOrDefault();
        if (target != null)
        {
            if (!target.Selected) target.Selected = true;
            if (ensureVisible)
            {
                try { target.EnsureVisible(); } catch { }
            }
        }
    }

    public int HeaderHeight
    {
        get
        {
            if (!IsHandleCreated) return 0;
            var header = SendMessage(Handle, LvmGetHeader, IntPtr.Zero, IntPtr.Zero);
            return header != IntPtr.Zero && GetWindowRect(header, out var r) ? r.Bottom - r.Top : 0;
        }
    }

    static Rectangle Inset(Rectangle r) => new(r.Left + 6, r.Top, Math.Max(0, r.Width - 10), r.Height);

    /// <summary>Every column's width, the fill column's included, to be given back by <see cref="SetColumnWidths"/>.</summary>
    public int[] ColumnWidths => Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();

    /// <summary>
    /// Widths kept from before. The fill column still takes what the others leave; widths for another set of
    /// columns, as from an older version, are ignored.
    /// </summary>
    public void SetColumnWidths(int[]? widths)
    {
        if (widths == null || widths.Length != Columns.Count) return;
        for (int i = 0; i < widths.Length; i++)
            if (i != fillColumn && widths[i] > 0) Columns[i].Width = Math.Clamp(widths[i], 24, 1200);
        FitColumns();
    }

    /// <summary>Gives the fill column whatever width the others leave within the visible area.</summary>
    public void FitColumns()
    {
        // Also runs while the window closes, when the columns can no longer be changed.
        if (!IsHandleCreated || IsDisposed || Disposing || Columns.Count == 0) return;
        int visible = Parent is ScrollHost host ? host.ViewWidth : ClientSize.Width;
        int others = Columns.Cast<ColumnHeader>().Where(c => c.Index != fillColumn).Sum(c => c.Width);
        int width = Math.Max(120, visible - others);
        if (Columns[fillColumn].Width != width) Columns[fillColumn].Width = width;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // A taller row: the row height follows the image list even though no image is drawn.
        SmallImageList = new ImageList { ImageSize = new Size(1, RowHeight) };
        var header = SendMessage(Handle, LvmGetHeader, IntPtr.Zero, IntPtr.Zero);
        if (header != IntPtr.Zero) headerPainter.AssignHandle(header);
        FitColumns();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        headerPainter.ReleaseHandle();
        base.OnHandleDestroyed(e);
    }

    readonly HeaderPainter headerPainter = new();

    /// <summary>
    /// The header paints the part past its last column itself, in the light system color. After it has
    /// painted, that part is filled with the dark header color.
    /// </summary>
    sealed class HeaderPainter : NativeWindow
    {
        const int WmPaint = 0x000F, WmPrint = 0x0317, WmPrintClient = 0x0318, HdmGetItemCount = 0x1200, HdmGetItemRect = 0x1207;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmPaint)
            {
                using var g = Graphics.FromHwnd(Handle);
                PaintRest(g);
            }
            else if (m.Msg is WmPrint or WmPrintClient && m.WParam != IntPtr.Zero)
            {
                // Drawn into a picture of the window: the same dark end.
                using var g = Graphics.FromHdc(m.WParam);
                PaintRest(g);
            }
        }

        void PaintRest(Graphics g)
        {
            if (!GetClientRect(Handle, out var client)) return;
            int count = (int)SendMessage(Handle, HdmGetItemCount, IntPtr.Zero, IntPtr.Zero);
            int right = 0;
            if (count > 0 && SendMessage(Handle, HdmGetItemRect, (IntPtr)(count - 1), out var last) != IntPtr.Zero) right = last.Right;
            if (right >= client.Right) return;
            using (var fill = new SolidBrush(Theme.Raised)) g.FillRectangle(fill, right, 0, client.Right - right, client.Bottom);
            using var line = new Pen(Theme.Border);
            g.DrawLine(line, right, client.Bottom - 1, client.Right, client.Bottom - 1);
        }

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, out Rect lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        FitColumns();
    }

    /// <summary>
    /// A column narrowed by hand would leave the header's right end to Windows, which paints it white;
    /// the fill column takes up the room instead.
    /// </summary>
    protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
    {
        base.OnColumnWidthChanged(e);
        if (e.ColumnIndex != fillColumn) FitColumns();
    }

    protected override void WndProc(ref Message m)
    {
        // Columns always fit, so a horizontal bar would only ever flash up, light, during a resize.
        if (m.Msg == WmNcCalcSize)
        {
            var style = GetWindowLongPtr(Handle, GwlStyle).ToInt64();
            if ((style & WsHScroll) != 0) SetWindowLongPtr(Handle, GwlStyle, (IntPtr)(style & ~WsHScroll));
        }
        base.WndProc(ref m);
    }

    const int WmNcCalcSize = 0x0083;
    const int GwlStyle = -16;
    const long WsHScroll = 0x00100000;

    [DllImport("user32.dll")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
}

/// <summary>A message box in the dark palette; the system one cannot be themed.</summary>
public static class DarkDialog
{
    /// <summary>
    /// Asks with buttons of its own: the first is the suggested one, the last cancels, as closing the window does.
    /// Gives the index of the button pressed.
    /// </summary>
    public static int Choose(IWin32Window? owner, string message, string title, params string[] choices)
    {
        // Buttons take only the results Windows defines; up to six choices, each its own.
        DialogResult[] results = { DialogResult.Yes, DialogResult.No, DialogResult.Retry, DialogResult.Ignore, DialogResult.Abort, DialogResult.OK };
        var buttons = choices.Take(results.Length).Select((text, i) => (Text: text, Result: results[i])).ToArray();
        var index = Array.IndexOf(results, Show(owner, message, title, buttons: buttons));
        return index >= 0 && index < buttons.Length ? index : buttons.Length - 1;
    }

    public static DialogResult Show(IWin32Window? owner, string message, string title, bool yesNo = false, bool isError = false,
        (string Text, DialogResult Result)[]? buttons = null)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = owner == null,
            StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        Theme.Apply(form);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
        };
        Button confirm;
        if (buttons is { Length: > 0 })
        {
            // Right to left: the last added sits leftmost, so the suggested one is added last.
            var made = buttons.Select((b, i) => { var button = Theme.Button(b.Text, primary: i == 0); button.DialogResult = b.Result; return button; }).ToList();
            for (int i = made.Count - 1; i >= 0; i--) bar.Controls.Add(made[i]);
            confirm = made[0];
            form.CancelButton = made[^1];
        }
        else if (yesNo)
        {
            var no = Theme.Button(L.T("아니요"));
            no.DialogResult = DialogResult.No;
            confirm = Theme.Button(L.T("예"), primary: true);
            confirm.DialogResult = DialogResult.Yes;
            bar.Controls.Add(no);
            bar.Controls.Add(confirm);
            form.CancelButton = no;
        }
        else
        {
            confirm = Theme.Button(L.T("확인"), primary: true);
            confirm.DialogResult = DialogResult.OK;
            bar.Controls.Add(confirm);
            form.CancelButton = confirm;
        }
        form.AcceptButton = confirm;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(14), Dock = DockStyle.Fill };
        layout.Controls.Add(new Label
        {
            Text = message,
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            MinimumSize = new Size(300, 0),
            ForeColor = isError ? Theme.Danger : Theme.Text,
            Margin = new Padding(0, 0, 0, 14),
        }, 0, 0);
        layout.Controls.Add(bar, 0, 1);
        form.Controls.Add(layout);

        return owner == null ? form.ShowDialog() : form.ShowDialog(owner);
    }
}
