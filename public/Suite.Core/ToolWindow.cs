using System.Diagnostics;

namespace Suite.Core;

public class ToolWindow : Form
{
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 38, AutoEllipsis = true };
    private readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1 };
    private readonly Button cancel = new() { Text = "Cancel operation", Dock = DockStyle.Bottom, Height = 30, Visible = false };
    private CancellationTokenSource? work;
    private bool closeAfterWork;

    protected ToolWindow(string title, int width = 720, int height = 480)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = PopupAppearance.LabelFont;
        ClientSize = new Size(width, height);
        MinimumSize = new Size(420, 270);
        Padding = new Padding(12);
        Icon = ModuleIdentity.For(title).CreateTrayIcon(32);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        Controls.Add(cancel);
        Controls.Add(status);
        cancel.Click += (_, _) => work?.Cancel();
    }

    public static int Run(string title, string[] args, Func<string[], Form> create, bool pro = false)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (pro && !LicenseManager.IsPro)
        {
            if (args is not ["--start"])
                MessageBox.Show("Activate Pro in TraySuite Hub to use this tool.", title);
            return 1;
        }
        if (args is ["--start"])
            return 0;

        using var window = create(args is ["--open"] ? [] : args);
        Application.Run(window);
        return 0;
    }

    protected void AddRow(Control control, int height = 36)
    {
        int row = layout.RowCount++;
        layout.RowStyles.Add(height > 0 ? new RowStyle(SizeType.Absolute, height) : new RowStyle(SizeType.Percent, 100));
        control.Dock = DockStyle.Fill;
        layout.Controls.Add(control, 0, row);
    }

    protected static Control Actions(params Control[] controls)
    {
        var panel = new TableLayoutPanel { ColumnCount = controls.Length, RowCount = 1, Margin = Padding.Empty };
        foreach (var control in controls)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
            control.Dock = DockStyle.Fill;
            panel.Controls.Add(control);
        }
        return panel;
    }

    protected static Button ActionButton(string label, Action action)
    {
        var button = new Button { Text = label, AutoEllipsis = true };
        button.Click += (_, _) => action();
        return button;
    }

    protected static ListView Details(params (string Label, int Width)[] columns)
    {
        var list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false };
        foreach (var column in columns)
            list.Columns.Add(column.Label, column.Width);
        return list;
    }

    protected void FileDrop(Control control, Action<string[]> accept)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += (_, e) =>
        {
            if (work is null && e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                accept(paths);
        };
    }

    protected void PickFiles(Action<string[]> accept, bool multiple = true, string filter = "All files|*.*")
    {
        using var dialog = new OpenFileDialog { Multiselect = multiple, Filter = filter };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            accept(dialog.FileNames);
    }

    protected void Status(string message, Color? color = null)
    {
        status.Text = message;
        status.ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : color ?? SystemColors.ControlText;
    }

    protected async void RunWork(Func<CancellationToken, Task> action)
    {
        if (work is not null)
            return;

        using var operation = new CancellationTokenSource();
        work = operation;
        layout.Enabled = false;
        cancel.Visible = true;
        try { await action(operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { Status("Canceled."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException
            or System.Runtime.InteropServices.ExternalException or System.Security.SecurityException)
        {
            Status(error.Message);
        }
        finally
        {
            work = null;
            layout.Enabled = true;
            cancel.Visible = false;
            if (closeAfterWork)
                Close();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (work is not null)
        {
            closeAfterWork = true;
            work.Cancel();
            e.Cancel = true;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var icon = Icon;
            Icon = null;
            icon?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected static void OpenExplorer(string path, bool select = false)
    {
        var start = new ProcessStartInfo("explorer.exe");
        if (select) start.ArgumentList.Add("/select," + path);
        else start.ArgumentList.Add(path);
        using var process = Process.Start(start);
    }

    protected static string SizeText(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.##} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.##} MB"
        : bytes >= 1024 ? $"{bytes / 1024d:0.##} KB" : $"{bytes:N0} B";
}
