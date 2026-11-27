using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace CleanLink;

internal sealed class CleanWindow : ToolWindow
{
    private readonly ListView results = Details(("Selected path", 440), ("Result", 235));
    private string[] files = [];

    [STAThread]
    private static int Main(string[] args) => Run("CleanLink", args, paths => new CleanWindow(paths));

    private CleanWindow(string[] paths) : base("CleanLink", 760)
    {
        AddRow(new Label { Text = "Best-effort overwrite and permanent deletion. Data may remain on SSDs, in snapshots, backups or named streams.",
            TextAlign = ContentAlignment.MiddleLeft }, 48);
        AddRow(Actions(ActionButton("Choose or drop files", () => PickFiles(Select)), ActionButton("Overwrite and delete…", Confirm)));
        AddRow(results, 0);
        FileDrop(this, Select);
        FileDrop(results, Select);
        Select(paths);
    }

    private void Select(string[] paths)
    {
        files = paths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        results.Items.Clear();
        foreach (string file in files)
            results.Items.Add(new ListViewItem([file, "Pending"]));
        Status($"{files.Length} files selected.");
    }

    private void Confirm()
    {
        if (files.Length == 0) return;
        if (MessageBox.Show(this, $"Permanently overwrite and delete {files.Length} selected files? This cannot be undone. An I/O failure can leave a file partially overwritten.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        RunWork(async token =>
        {
            int removed = 0;
            for (int index = 0; index < files.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                string path = files[index];
                Status($"Processing {index + 1} of {files.Length}. Cancel stops before the next file.");
                try
                {
                    await Task.Run(() => Overwrite(path));
                    results.Items[index].SubItems[1].Text = "Overwritten and deleted";
                    removed++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
                {
                    results.Items[index].SubItems[1].Text = error.Message;
                }
            }
            Status($"{removed} of {files.Length} files deleted.");
        });
    }

    private static void Overwrite(string path)
    {
        string root = Path.GetPathRoot(path)!;
        if (root.Length != 3 || path.AsSpan(root.Length).Contains(':') || new DriveInfo(root).DriveType != DriveType.Fixed)
            throw new IOException("Choose an ordinary file on a local fixed drive.");

        for (var directory = new FileInfo(path).Directory; directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Files under links or mounted folders are skipped.");

        const uint writeAndDelete = 0x40000000 | 0x00010000 | 0x80;
        const uint openReparsePoint = 0x00200000;
        using var handle = CreateFile(path, writeAndDelete, 0, 0, 3, openReparsePoint, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        const FileAttributes unsupported = FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly
            | FileAttributes.SparseFile | FileAttributes.Offline | FileAttributes.Compressed | FileAttributes.Encrypted;
        if (info.Links != 1 || (info.Attributes & unsupported) != 0)
            throw new IOException("Linked files and special file attributes are skipped.");

        using var stream = new FileStream(handle, FileAccess.Write);
        var buffer = new byte[256 * 1024];
        long remaining = stream.Length;
        while (remaining > 0)
        {
            int count = (int)Math.Min(remaining, buffer.Length);
            RandomNumberGenerator.Fill(buffer.AsSpan(0, count));
            stream.Write(buffer, 0, count);
            remaining -= count;
        }
        stream.Flush(flushToDisk: true);
        byte delete = 1;
        if (!SetFileInformationByHandle(handle, 4, ref delete, 1))
            throw new IOException("The contents were overwritten, but Windows could not delete the file.",
                new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public FileAttributes Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref byte information, int size);
}
