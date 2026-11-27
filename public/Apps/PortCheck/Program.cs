using System.ComponentModel;
using System.Diagnostics;

namespace PortCheck;

internal sealed class PortWindow : ToolWindow
{
    private readonly TextBox search = new() { PlaceholderText = "Filter by local port or process name", AccessibleName = "Filter ports" };
    private readonly ListView results = Details(("Protocol", 78), ("Address", 175), ("Port", 65), ("PID", 70), ("Process", 200));
    private PortOwner[] snapshot = [];

    [STAThread]
    private static int Main(string[] args) => Run("PortCheck", args, _ => new PortWindow());

    private PortWindow() : base("PortCheck")
    {
        AddRow(Actions(search, ActionButton("Refresh", RefreshSnapshot)));
        AddRow(results, 0);
        AddRow(Actions(ActionButton("Show executable in Explorer", () => RunWork(_ =>
        {
            if (Selection()?.Path is { } path)
                OpenExplorer(path, select: true);
            return Task.CompletedTask;
        })), ActionButton("End selected process…", EndProcess)));
        search.TextChanged += (_, _) => Render();
        Shown += (_, _) => RefreshSnapshot();
    }

    private void RefreshSnapshot() => RunWork(async token =>
    {
        snapshot = await Task.Run(() =>
        {
            var endpoints = LocalEndpoints.Read().Where(item => item.Listening || item.Protocol == EndpointProtocol.Udp).ToArray();
            var processes = endpoints.Select(item => item.ProcessId).Distinct()
                .ToDictionary(pid => pid, Describe);
            return endpoints.OrderBy(item => item.Port).ThenBy(item => item.Protocol)
                .Select(item => new PortOwner(item, processes[item.ProcessId].Path, processes[item.ProcessId].Started)).ToArray();
        }, token);
        Render();
    });

    private void Render()
    {
        string filter = search.Text.Trim();
        bool isPort = int.TryParse(filter, out int port);
        results.BeginUpdate();
        results.Items.Clear();
        foreach (var owner in snapshot)
        {
            var endpoint = owner.Endpoint;
            string name = owner.Name;
            if (filter.Length > 0 && (isPort ? port != endpoint.Port : !name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                continue;
            results.Items.Add(new ListViewItem([endpoint.Protocol.ToString().ToUpperInvariant(), endpoint.Address.ToString(),
                endpoint.Port.ToString(), endpoint.ProcessId.ToString(), name]) { Tag = owner });
        }
        results.EndUpdate();
        Status($"{results.Items.Count} local TCP listeners and UDP endpoints shown.");
    }

    private PortOwner? Selection() => results.SelectedItems.Count == 0 ? null : results.SelectedItems[0].Tag as PortOwner;

    private void EndProcess()
    {
        if (Selection() is not { } owner)
        {
            Status("Select an endpoint first.");
            return;
        }
        int pid = owner.Endpoint.ProcessId;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar;
        if (owner.Started is not { } started || owner.Path is not { } path || pid <= 4 || pid == Environment.ProcessId
            || path.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
        {
            Status("This process cannot be ended from PortCheck.");
            return;
        }
        if (MessageBox.Show(this, $"Force {owner.Name} (PID {pid}) to close? It may lose unsaved work.", Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        RunWork(async token =>
        {
            using var process = Process.GetProcessById(pid);
            using var current = Process.GetCurrentProcess();
            _ = process.SafeHandle;
            if (process.StartTime.ToUniversalTime() != started || process.SessionId != current.SessionId
                || !string.Equals(path, LocalEndpoints.Executable(pid), StringComparison.OrdinalIgnoreCase)
                || !LocalEndpoints.Read().Contains(owner.Endpoint))
                throw new InvalidOperationException("The process or endpoint changed. Refresh before retrying.");
            process.Kill();
            await process.WaitForExitAsync(token);
            snapshot = snapshot.Where(item => item.Endpoint.ProcessId != pid).ToArray();
            Render();
            Status($"Ended {owner.Name} (PID {pid}).");
        });
    }

    private static (string? Path, DateTime? Started) Describe(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var started = process.StartTime.ToUniversalTime();
            return (LocalEndpoints.Executable(pid), started);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return (null, null);
        }
    }

    private sealed record PortOwner(LocalEndpoint Endpoint, string? Path, DateTime? Started)
    {
        public string Name => System.IO.Path.GetFileName(Path) ?? "Unavailable";
    }
}
