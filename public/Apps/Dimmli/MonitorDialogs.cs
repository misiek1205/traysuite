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
    public static AutomationSettings? EditAutomation(AutomationSettings current)
    {
        using var dialog = CreateDialog(Texts.Get("Automation", "Automatyzacja"), new Size(495, 340));
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));

        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        mode.Items.AddRange([Texts.Get("Off", "Wyłączony"), Texts.Get("Fixed hours", "Stałe godziny"),
            Texts.Get("Sunrise and sunset", "Wschód i zachód słońca")]);
        mode.SelectedIndex = (int)current.Schedule;
        var day = ClockField(current.DayMinutes);
        var evening = ClockField(current.EveningMinutes);
        var latitude = new NumericUpDown { Minimum = -90, Maximum = 90, DecimalPlaces = 2, Value = (decimal)current.Latitude };
        var longitude = new NumericUpDown { Minimum = -180, Maximum = 180, DecimalPlaces = 2, Value = (decimal)current.Longitude };
        var boost = new CheckBox { Checked = current.Fullscreen, Text = Texts.Get("Enabled", "Włączone") };
        var level = new NumericUpDown { Minimum = 0, Maximum = 100, Value = current.FullscreenLevel };
        var hint = new Label { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        AddField(Texts.Get("Profile schedule", "Harmonogram profili"), mode);
        AddField(Texts.Get("Day / fallback", "Dzień / godzina rezerwowa"), day);
        AddField(Texts.Get("Evening / fallback", "Wieczór / godzina rezerwowa"), evening);
        AddField(Texts.Get("Latitude", "Szerokość geograficzna"), latitude);
        AddField(Texts.Get("Longitude", "Długość geograficzna"), longitude);
        AddField(Texts.Get("Fullscreen boost", "Rozjaśnianie na pełnym ekranie"), boost);
        AddField(Texts.Get("Fullscreen brightness (%)", "Jasność na pełnym ekranie (%)"), level);
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
                (int)day.Value.TimeOfDay.TotalMinutes, (int)evening.Value.TimeOfDay.TotalMinutes,
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
            control.Dock = DockStyle.Fill;
            control.AccessibleName = label;
            int row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            fields.Controls.Add(new Label
            {
                Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
            }, 0, row);
            fields.Controls.Add(control, 1, row);
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
        using var dialog = CreateDialog(Texts.Get("Brightness calibration", "Kalibracja jasności"), new Size(436, 165));
        var label = new Label { Text = displayName, AutoEllipsis = true, Dock = DockStyle.Top, Height = 28,
            Font = PopupAppearance.LabelFont };
        var hint = new Label
        {
            Text = Texts.Get("Offset from the common brightness level, in percentage points.",
                "Przesunięcie względem wspólnej jasności, w punktach procentowych."),
            Dock = DockStyle.Top, Height = 31
        };
        var input = new NumericUpDown
        {
            Minimum = -20, Maximum = 20, Value = offset, Dock = DockStyle.Top, AccessibleName = dialog.Text
        };
        var buttons = DialogButtons(dialog);
        dialog.Controls.Add(input);
        dialog.Controls.Add(hint);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.ActiveControl = input;
        return dialog.ShowDialog() == DialogResult.OK ? (int)input.Value : null;
    }

    private static DateTimePicker ClockField(int minutes) => new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true,
        Value = DateTime.Today.AddMinutes(minutes)
    };

    private static FlowLayoutPanel DialogButtons(Form dialog)
    {
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = Texts.Get("Save", "Zapisz"), DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = Texts.Get("Cancel", "Anuluj"), DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        return buttons;
    }

    public static string? Rename(string displayName, string alias)
    {
        using var dialog = CreateDialog(Texts.Get("Monitor name", "Nazwa monitora"), new Size(420, 172));
        var label = new Label
        {
            Text = displayName, AutoEllipsis = true, Dock = DockStyle.Top, Height = 30,
            Font = PopupAppearance.LabelFont
        };
        var hint = new Label
        {
            Text = Texts.Get("Leave blank to use the monitor's original name.",
                "Pozostaw puste pole, aby przywrócić nazwę monitora."),
            Dock = DockStyle.Top, Height = 30
        };
        var input = new TextBox { Dock = DockStyle.Top, Text = alias, MaxLength = 64, AccessibleName = dialog.Text };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft
        };
        var save = new Button
        {
            Text = Texts.Get("Save", "Zapisz"), DialogResult = DialogResult.OK, AutoSize = true
        };
        var cancel = new Button
        {
            Text = Texts.Get("Cancel", "Anuluj"), DialogResult = DialogResult.Cancel, AutoSize = true
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        dialog.Controls.Add(input);
        dialog.Controls.Add(hint);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        dialog.ActiveControl = input;
        input.SelectAll();
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
            Text = report, AccessibleName = header.Text, BackColor = SystemColors.Window
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 7, 0, 0)
        };
        var close = new Button { Text = Texts.Get("Close", "Zamknij"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var copy = new Button { Text = Texts.Get("Copy report", "Kopiuj raport"), AutoSize = true };
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
        dialog.Controls.Add(details);
        dialog.Controls.Add(header);
        dialog.Controls.Add(buttons);
        dialog.CancelButton = close;
        dialog.ActiveControl = copy;
        dialog.ShowDialog();
    }

    private static Form CreateDialog(string title, Size size) => new()
    {
        Text = title, ClientSize = size, Padding = new Padding(16),
        AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96),
        FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
        ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false, TopMost = true,
        Font = PopupAppearance.CaptionFont
    };
}
