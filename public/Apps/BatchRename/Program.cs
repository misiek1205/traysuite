namespace BatchRename;

internal sealed class RenameWindow : ToolWindow
{
    private readonly TextBox find = new() { PlaceholderText = "Find (literal, case-insensitive)", AccessibleName = "Find" };
    private readonly TextBox replace = new() { PlaceholderText = "Replace with", AccessibleName = "Replace with" };
    private readonly TextBox prefix = new() { PlaceholderText = "Prefix", AccessibleName = "Prefix" };
    private readonly TextBox suffix = new() { PlaceholderText = "Suffix", AccessibleName = "Suffix" };
    private readonly CheckBox number = new() { Text = "Add numbering (001, 002, …)", AutoSize = true };
    private readonly ListView preview = Details(("Current name", 260), ("New name", 260), ("Result", 140));
    private readonly Button apply;
    private string[] paths = [];
    private RenameItem[] plan = [];

    [STAThread]
    private static int Main(string[] args) => Run("BatchRename", args, paths => new RenameWindow(paths));

    private RenameWindow(string[] initial) : base("BatchRename")
    {
        apply = ActionButton("Rename files", Apply);
        AddRow(Actions(ActionButton("Choose or drop files", () => PickFiles(Select)), apply));
        AddRow(Actions(find, replace));
        AddRow(Actions(prefix, suffix));
        AddRow(number, 29);
        AddRow(preview, 0);
        foreach (var field in new[] { find, replace, prefix, suffix })
            field.TextChanged += (_, _) => RefreshPlan();
        number.CheckedChanged += (_, _) => RefreshPlan();
        FileDrop(this, Select);
        FileDrop(preview, Select);
        Select(initial);
    }

    private void Select(string[] input)
    {
        paths = input.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        RefreshPlan();
    }

    private void RefreshPlan()
    {
        plan = paths.Select((source, index) =>
        {
            string stem = Path.GetFileNameWithoutExtension(source);
            if (find.TextLength > 0)
                stem = stem.Replace(find.Text, replace.Text, StringComparison.OrdinalIgnoreCase);
            string title = prefix.Text + stem + suffix.Text + (number.Checked ? $" {index + 1:000}" : "")
                + Path.GetExtension(source);
            string? error = InvalidName(title) ? "Invalid Windows name" : null;
            string destination = error is null ? Path.Combine(Path.GetDirectoryName(source)!, title) : source;
            return new RenameItem(source, destination, title, error);
        }).ToArray();

        var sources = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicates = plan.GroupBy(item => item.Destination, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        plan = plan.Select(item => item.Error is not null ? item : item with
        {
            Error = duplicates.Contains(item.Destination) || Directory.Exists(item.Destination)
                || !sources.Contains(item.Destination) && File.Exists(item.Destination) ? "Name already in use" : null
        }).ToArray();
        preview.BeginUpdate();
        preview.Items.Clear();
        foreach (var item in plan)
            preview.Items.Add(new ListViewItem([Path.GetFileName(item.Source), item.Title,
                item.Error ?? (item.Source == item.Destination ? "Unchanged" : "Ready")]));
        preview.EndUpdate();
        apply.Enabled = plan.All(item => item.Error is null) && plan.Any(item => item.Source != item.Destination);
        Status(paths.Length == 0 ? "Choose or drop files to get a preview." : $"{paths.Length} files selected.");
    }

    private static bool InvalidName(string name)
    {
        if (name.Length is 0 or > 255 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            return true;
        string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]);
    }

    private void Apply()
    {
        var items = plan.Where(item => item.Source != item.Destination).ToArray();
        if (!apply.Enabled || MessageBox.Show(this, $"Rename {items.Length} files?", Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        RunWork(async token =>
        {
            await Task.Run(() => Execute(items, token), token);
            Select(plan.Select(item => item.Destination).ToArray());
            Status($"Renamed {items.Length} files.");
        });
    }

    private static void Execute(RenameItem[] plan, CancellationToken token)
    {
        var staged = new List<(RenameItem Item, string Temp)>();
        var committed = new List<(RenameItem Item, string Temp)>();
        try
        {
            foreach (var item in plan)
            {
                token.ThrowIfCancellationRequested();
                string temp = Path.Combine(Path.GetDirectoryName(item.Source)!, $".traysuite-{Guid.NewGuid():N}.tmp");
                File.Move(item.Source, temp);
                staged.Add((item, temp));
            }
            foreach (var entry in staged)
            {
                token.ThrowIfCancellationRequested();
                File.Move(entry.Temp, entry.Item.Destination);
                committed.Add(entry);
            }
        }
        catch (Exception original) when (original is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            var errors = new List<string>();
            foreach (var entry in committed.AsEnumerable().Reverse())
                Restore(entry.Item.Destination, entry.Temp, errors);
            foreach (var entry in staged.AsEnumerable().Reverse())
                Restore(entry.Temp, entry.Item.Source, errors);
            if (errors.Count > 0)
                throw new IOException("Rename stopped and some files could not be restored. " + string.Join("; ", errors), original);
            throw;
        }
    }

    private static void Restore(string source, string destination, List<string> errors)
    {
        try { File.Move(source, destination); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{source} → {destination}: {error.Message}");
        }
    }

    private sealed record RenameItem(string Source, string Destination, string Title, string? Error);
}
