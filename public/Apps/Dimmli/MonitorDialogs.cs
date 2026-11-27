using System.Runtime.InteropServices;
using System.Text;

namespace Dimmli;

internal static class MonitorDiagnostics
{
    public static string CreateReport(IReadOnlyList<Display> displays)
    {
        var report = new StringBuilder();
        report.AppendLine($"Dimmli {typeof(MonitorDiagnostics).Assembly.GetName().Version?.ToString(3)}");
        report.AppendLine($"{Environment.OSVersion.VersionString} ({RuntimeInformation.OSArchitecture})");
        report.AppendLine();
        if (displays.Count == 0)
            report.AppendLine(Texts.Get("No displays were detected.", "Nie wykryto żadnego monitora."));

        for (int index = 0; index < displays.Count; index++)
        {
            var display = displays[index];
            report.AppendLine($"{index + 1}. {display.Name}");
            string result = display.ReadMethod switch
            {
                BrightnessReadMethod.Wmi => "WMI",
                BrightnessReadMethod.DdcCi => "DDC/CI",
                BrightnessReadMethod.DdcVcp => "DDC/CI (VCP 0x10)",
                BrightnessReadMethod.DdcNoReply => Texts.Get(
                    "DDC/CI did not return a usable brightness level.",
                    "DDC/CI nie zwróciło poprawnego poziomu jasności."),
                _ => Texts.Get("No working brightness interface detected.", "Nie wykryto działającej metody odczytu jasności.")
            };
            report.Append("   ").Append(result);
            if (display.SetBrightness is not null)
                report.Append(" · ").Append(Math.Clamp(display.Brightness, 0, 100)).Append('%');
            report.AppendLine();
        }
        return report.ToString();
    }
}

internal static class MonitorDialogs
{
    private static PopupAppearance Theme => PopupAppearance.Current;

    public static AutomationSettings? EditAutomation(AutomationSettings current)
    {
        using var dialog = CreateDialog(Texts.Get("Automation", "Automatyzacja"), new Size(526, 391));
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));

        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        mode.Items.AddRange([Texts.Get("Off", "Wyłączony"), Texts.Get("Fixed hours", "Stałe godziny"),
            Texts.Get("Sunrise and sunset", "Wschód i zachód słońca")]);
        mode.SelectedIndex = (int)current.Schedule;
        var day = new ClockField(current.DayMinutes);
        var evening = new ClockField(current.EveningMinutes);
        var latitude = new NumericUpDown { Minimum = -90, Maximum = 90, DecimalPlaces = 2, Value = (decimal)current.Latitude };
        var longitude = new NumericUpDown { Minimum = -180, Maximum = 180, DecimalPlaces = 2, Value = (decimal)current.Longitude };
        var boost = new PopupToggle { Checked = current.Fullscreen, Text = Texts.Get("Enabled", "Włączone") };
        var level = new NumericUpDown { Minimum = 0, Maximum = 100, Value = current.FullscreenLevel };
        var hint = ModuleWindow.Label("", muted: true);

        AddField(Texts.Get("Profile schedule", "Harmonogram profili"), mode);
        AddField(Texts.Get("Day / fallback", "Dzień / godzina rezerwowa"), day);
        AddField(Texts.Get("Evening / fallback", "Wieczór / godzina rezerwowa"), evening);
        AddField(Texts.Get("Latitude", "Szerokość geograficzna"), latitude);
        AddField(Texts.Get("Longitude", "Długość geograficzna"), longitude);
        AddField(Texts.Get("Fullscreen boost", "Rozjaśnianie na pełnym ekranie"), boost);
        AddField(Texts.Get("Fullscreen brightness (%)", "Jasność na pełnym ekranie (%)"), level);
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 1.25f));
        fields.Controls.Add(hint, 0, fields.RowCount++);
        fields.SetColumnSpan(hint, 2);
        var buttons = DialogButtons(dialog);
        dialog.Controls.Add(fields);
        dialog.Controls.Add(buttons);

        mode.SelectedIndexChanged += (_, _) => UpdateFields();
        boost.CheckedChanged += (_, _) => UpdateFields();
        UpdateFields();
        AutomationSettings? result = null;
        dialog.FormClosing += (_, args) =>
        {
            if (dialog.DialogResult != DialogResult.OK)
                return;
            var candidate = new AutomationSettings((ScheduleMode)mode.SelectedIndex,
                day.Minutes, evening.Minutes,
                (double)latitude.Value, (double)longitude.Value, boost.Checked, (int)level.Value);
            if (candidate.IsValid())
                result = candidate;
            else
            {
                args.Cancel = true;
                hint.Text = Texts.Get("Day and Evening need different times.", "Ustaw różne godziny dla profili Dzień i Wieczór.");
            }
        };
        dialog.ShowDialog();
        return result;

        void AddField(string label, Control control)
        {
            control.AccessibleName = label;
            if (control is ClockField clock)
                clock.Describe(label);
            var field = control is PopupToggle or ClockField ? control : PopupField.Wrap(control);
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(0, 2, 0, 3);
            int row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 1));
            fields.Controls.Add(ModuleWindow.Label(label), 0, row);
            fields.Controls.Add(field, 1, row);
        }

        void UpdateFields()
        {
            day.Enabled = evening.Enabled = mode.SelectedIndex != (int)ScheduleMode.Off;
            latitude.Enabled = longitude.Enabled = mode.SelectedIndex == (int)ScheduleMode.Solar;
            level.Enabled = boost.Checked;
            hint.Text = latitude.Enabled
                ? Texts.Get("Approximate coordinates for your town. Calculations stay on this PC.",
                    "Wystarczą przybliżone współrzędne miasta. Obliczenia są lokalne.")
                : Texts.Get("Scheduling uses the saved Day and Evening profiles.",
                    "Harmonogram korzysta z zapisanych profili Dzień i Wieczór.");
        }
    }

    public static int? EditOffset(string displayName, int offset)
    {
        using var dialog = CreateDialog(Texts.Get("Brightness calibration", "Kalibracja jasności"), new Size(456, 178));
        var label = new Label { Text = displayName, AutoEllipsis = true, Dock = DockStyle.Top, Height = 28,
            Font = PopupAppearance.LabelFont };
        var hint = new Label
        {
            Text = Texts.Get("Offset from the common brightness level, in percentage points.",
                "Przesunięcie względem wspólnej jasności, w punktach procentowych."),
            Dock = DockStyle.Top, Height = 35, ForeColor = Theme.Muted
        };
        var input = new NumericUpDown
        {
            Minimum = -20, Maximum = 20, Value = offset, Dock = DockStyle.Top, AccessibleName = dialog.Text
        };
        var buttons = DialogButtons(dialog);
        dialog.Controls.Add(TopField(input));
        dialog.Controls.Add(hint);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.Shown += (_, _) => input.Focus();
        return dialog.ShowDialog() == DialogResult.OK ? (int)input.Value : null;
    }

    private sealed class ClockField : TableLayoutPanel
    {
        private readonly ClockPart hour;
        private readonly ClockPart minute;

        public ClockField(int minutes)
        {
            hour = new ClockPart(23, minutes / 60);
            minute = new ClockPart(59, minutes % 60);
            ColumnCount = 3;
            RowCount = 1;
            RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            Controls.Add(PopupField.Wrap(hour), 0, 0);
            var separator = ModuleWindow.Label(":", font: PopupAppearance.LabelFont);
            separator.AutoSize = true;
            separator.Padding = new Padding(4, 0, 4, 0);
            Controls.Add(separator, 1, 0);
            Controls.Add(PopupField.Wrap(minute), 2, 0);
            foreach (Control control in Controls)
            {
                control.Dock = DockStyle.Fill;
                control.Margin = Padding.Empty;
            }
        }

        public int Minutes => (int)(hour.Value * 60 + minute.Value);

        public void Describe(string label)
        {
            hour.AccessibleName = $"{label} (HH)";
            minute.AccessibleName = $"{label} (mm)";
        }
    }

    private sealed class ClockPart : NumericUpDown
    {
        public ClockPart(int maximum, int initial)
        {
            Maximum = maximum;
            Value = initial;
            UpdateEditText();
        }

        protected override void UpdateEditText()
        {
            ChangingText = true;
            Text = Value.ToString("00");
            UserEdit = false;
        }
    }

    private static FlowLayoutPanel DialogButtons(Form dialog)
    {
        var buttons = Footer();
        var save = DialogButton(Texts.Get("Save", "Zapisz"), DialogResult.OK, primary: true);
        var cancel = DialogButton(Texts.Get("Cancel", "Anuluj"), DialogResult.Cancel);
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        return buttons;
    }

    public static string? Rename(string displayName, string alias)
    {
        using var dialog = CreateDialog(Texts.Get("Monitor name", "Nazwa monitora"), new Size(456, 181));
        var label = new Label
        {
            Text = displayName, AutoEllipsis = true, Dock = DockStyle.Top, Height = 30,
            Font = PopupAppearance.LabelFont
        };
        var hint = new Label
        {
            Text = Texts.Get("Leave blank to use the monitor's original name.",
                "Pozostaw puste pole, aby przywrócić nazwę monitora."),
            Dock = DockStyle.Top, Height = 35, ForeColor = Theme.Muted
        };
        var input = new TextBox { Dock = DockStyle.Top, Text = alias, MaxLength = 64, AccessibleName = dialog.Text };
        var buttons = DialogButtons(dialog);
        dialog.Controls.Add(TopField(input));
        dialog.Controls.Add(hint);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == DialogResult.OK ? input.Text.Trim() : null;
    }

    public static void ShowReport(string report)
    {
        using var dialog = CreateDialog(Texts.Get("Monitor diagnostics", "Diagnostyka monitorów"), new Size(524, 365));
        var header = new Label
        {
            Text = Texts.Get("Brightness read results", "Wyniki odczytu jasności"),
            Dock = DockStyle.Top, Height = 32, Font = PopupAppearance.LabelFont
        };
        var details = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            Text = report, AccessibleName = header.Text, BackColor = Theme.Card, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None, Font = PopupAppearance.KeyFont
        };
        var buttons = Footer();
        var close = DialogButton(Texts.Get("Close", "Zamknij"), DialogResult.Cancel);
        var copy = DialogButton(Texts.Get("Copy report", "Kopiuj raport"), primary: true, width: 158);
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(report);
                copy.Text = Texts.Get("Copied", "Skopiowano");
            }
            catch (ExternalException)
            {
                copy.Text = Texts.Get("Could not copy", "Nie udało się skopiować");
            }
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        dialog.Controls.Add(ModuleWindow.Card(details, new Padding(13)));
        dialog.Controls.Add(header);
        dialog.Controls.Add(buttons);
        dialog.CancelButton = close;
        dialog.ActiveControl = copy;
        dialog.ShowDialog();
    }

    private static PopupField TopField(Control input)
    {
        var field = PopupField.Wrap(input);
        field.Dock = DockStyle.Top;
        field.Height = 36;
        return field;
    }

    private static FlowLayoutPanel Footer() => new()
    {
        Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft,
        Padding = new Padding(0, 10, 0, 0)
    };

    private static PopupButton DialogButton(string text, DialogResult result = DialogResult.None, bool primary = false,
        int width = 96) => new(style: primary ? PopupButtonStyle.Primary : PopupButtonStyle.Standard)
    {
        Text = text, DialogResult = result, Size = new Size(width, 34), Margin = new Padding(7, 0, 0, 0)
    };

    private static Form CreateDialog(string title, Size size) => new MonitorDialog
    {
        Text = title, ClientSize = size, Padding = new Padding(20),
        AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96),
        FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
        ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false, TopMost = true,
        Font = PopupAppearance.CaptionFont, BackColor = Theme.Background, ForeColor = Theme.Text
    };

    private sealed class MonitorDialog : Form
    {
        private const int ImmersiveDarkMode = 20;
        private const int CaptionColor = 35;
        private const int TextColor = 36;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || SystemInformation.HighContrast)
                return;
            SetFrameAttribute(ImmersiveDarkMode, Theme.Background.GetBrightness() < .5f ? 1 : 0);
            SetFrameAttribute(CaptionColor, ColorTranslator.ToWin32(Theme.Background));
            SetFrameAttribute(TextColor, ColorTranslator.ToWin32(Theme.Text));
        }

        private void SetFrameAttribute(int attribute, int value) => DwmSetWindowAttribute(Handle, attribute, ref value, sizeof(int));

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    }
}
