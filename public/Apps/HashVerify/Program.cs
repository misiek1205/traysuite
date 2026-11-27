using System.Security.Cryptography;

namespace HashVerify;

internal sealed class HashWindow : ToolWindow
{
    private readonly TextBox path = new() { ReadOnly = true, AccessibleName = "Selected file" };
    private readonly TextBox expected = new() { PlaceholderText = "Paste the expected checksum", AccessibleName = "Expected checksum" };
    private readonly TextBox actual = new() { ReadOnly = true, AccessibleName = "Computed checksum" };
    private readonly ComboBox algorithm = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Algorithm" };

    [STAThread]
    private static int Main(string[] args) => Run("HashVerify", args, paths => new HashWindow(paths));

    private HashWindow(string[] paths) : base("HashVerify", 620, 330)
    {
        algorithm.Items.AddRange(["SHA-256", "MD5 (legacy)"]);
        algorithm.SelectedIndex = 0;
        AddRow(Actions(ActionButton("Choose or drop a file", () => PickFiles(Select, false)), algorithm));
        AddRow(path, 30);
        AddRow(expected, 30);
        AddRow(Actions(ActionButton("Compute checksum", Compute), ActionButton("Copy checksum", () =>
        {
            RunWork(_ =>
            {
                if (actual.TextLength > 0)
                    Clipboard.SetText(actual.Text);
                return Task.CompletedTask;
            });
        })));
        AddRow(actual, 30);
        FileDrop(this, Select);
        FileDrop(path, Select);
        expected.TextChanged += (_, _) => Report();
        algorithm.SelectedIndexChanged += (_, _) => { actual.Clear(); Report(); };
        Shown += (_, _) => Select(paths);
    }

    private void Select(string[] paths)
    {
        if (paths.FirstOrDefault(File.Exists) is not { } file)
            return;
        path.Text = Path.GetFullPath(file);
        actual.Clear();
        Compute();
    }

    private void Compute()
    {
        if (path.TextLength == 0)
            return;
        actual.Clear();
        bool sha256 = algorithm.SelectedIndex == 0;
        string file = path.Text;
        RunWork(async token =>
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(sha256 ? HashAlgorithmName.SHA256 : HashAlgorithmName.MD5);
            byte[] buffer = new byte[128 * 1024];
            long length = stream.Length;
            long read = 0;
            long displayed = -1;
            Status("Computing checksum…");
            int count;
            while ((count = await stream.ReadAsync(buffer, token)) > 0)
            {
                hash.AppendData(buffer, 0, count);
                read += count;
                long percent = length == 0 ? 100 : (long)(read * 100d / length);
                if (percent != displayed)
                {
                    Status($"Computing checksum… {percent}%");
                    displayed = percent;
                }
            }
            actual.Text = Convert.ToHexStringLower(hash.GetHashAndReset());
            Report();
        });
    }

    private void Report()
    {
        string digest = expected.Text.Trim();
        if (actual.TextLength == 0)
            Status("Choose a file and compute its checksum.");
        else if (digest.Length == 0)
            Status("Checksum computed. Paste an expected value to verify it.");
        else if (digest.Length != actual.TextLength || !digest.All(Uri.IsHexDigit))
            Status($"Enter {actual.TextLength} hexadecimal characters.");
        else
        {
            bool match = digest.Equals(actual.Text, StringComparison.OrdinalIgnoreCase);
            Status(match ? "Checksum matches." : "Checksum does not match.", match ? Color.DarkGreen : Color.DarkRed);
        }
    }
}
