using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

using WardenCli.Formations;

namespace WardenCli.Views;

/// <summary>The console's one window. The RIBBON composes commands on the current golem's tab; a tab's script is that golem's QUEUE,
/// sent one command at a time — the next goes when the golem finished the one before (its pending routes back to zero); the golems are
/// boxes in their colour, checked to be addressed together; the coordinates box builds a command the operator adds here, to the selected
/// golems, or copies; the map shows each golem where it was last asked; the journal of the current golem streams live. The window decides
/// nothing for the golem: it carries lines.</summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<Golem> golems = new();
    // THE ACTIVE FORMATIONS (propuesta 96): every one laid out by the console, with the vertex each golem holds, rotated from here
    private readonly ObservableCollection<Formation> formations = new();
    private readonly Dictionary<string, CancellationTokenSource> runners = new();
    private CancellationTokenSource? journalFollow;
    private string? workspace;

    // THE READS, asked of the selected golem every few seconds and shown beside the map (Juan, 7-oct-2026): where, state, route, obstacles
    private readonly DispatcherTimer readings = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool reading;
    private int quietTicks;

    // the floor the map draws: the selected golem's scenario as it told it (GET /map), the arena's 11 × 11 m until one is known
    private double FloorSize => Current?.Plan?.Extent ?? 11.0;

    // the verbs whose command leaves a route in the golem: the queue waits for it to end before the next line goes
    private static readonly HashSet<string> Movers = new(StringComparer.Ordinal) { "visit", "cover", "then", "resume", "choreograph", "rotate" };

    public MainWindow()
    {
        InitializeComponent();
        GolemList.ItemsSource = golems;
        FormationList.ItemsSource = formations;
        FormationList.SelectionChanged += (_, _) => Draw();
        formations.CollectionChanged += (_, _) => { Draw(); NoFormation.Visibility = formations.Count == 0 ? Visibility.Visible : Visibility.Collapsed; };
        golems.CollectionChanged += (_, _) => { Draw(); NoGolem.Visibility = golems.Count == 0 ? Visibility.Visible : Visibility.Collapsed; };
        readings.Tick += async (_, _) => await RefreshCurrentAsync();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F9) { SetDebugger(DebuggerPanel.Visibility != Visibility.Visible); e.Handled = true; } };
        readings.Start();
        Log("WardenCli ready — add the golems in operation, compose a script per tab, SEND.");
    }

    // the selected golem's reads, refreshed in turn; one that does not answer is asked again every fifth tick
    private async Task RefreshCurrentAsync()
    {
        if (reading || Current is not { } g) return;
        if (g.Status == "unreachable" && ++quietTicks % 5 != 0) return;
        reading = true;
        try { await GolemClient.RefreshReadingsAsync(g); }
        finally { reading = false; }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Point_Changed(this, null);
        RefreshRecent();
        // the workspace named on the command line, else the last one opened: the console starts where it was left
        string? start = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => a.EndsWith(Workspace.Extension, StringComparison.OrdinalIgnoreCase)) ?? Workspace.Last();
        if (start != null) OpenWorkspace(start);
    }

    // the scripts are saved as they are when the window closes, when a workspace is open
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => AutoSave();

    private Golem? Current => GolemList.SelectedItem as Golem;
    private IEnumerable<Golem> SelectedGolems => golems.Where(g => g.Selected);

    // ==================================================================
    // The golems in operation
    // ==================================================================

    private void AddGolem_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(NewPort.Text.Trim(), out int port)) { Log("the port is a number"); return; }
        Add(new Golem { Name = NewName.Text, Host = NewHost.Text, Port = port });
    }

    private void AddFour_Click(object sender, RoutedEventArgs e)
    {
        var four = new (string Name, int Port)[] { ("blue", 8081), ("red", 8082), ("green", 8083), ("yellow", 8084) };
        foreach (var (name, port) in four) Add(new Golem { Name = name, Host = "localhost", Port = port });
    }

    private async void Add(Golem golem)
    {
        if (golem.Name == "") { Log("a golem needs its name"); return; }
        if (golems.Any(g => g.Name == golem.Name)) { Log($"{golem.Name} is already here"); return; }
        golems.Add(golem);
        golem.PropertyChanged += (_, args) => { if (args.PropertyName is nameof(Golem.LastX) or nameof(Golem.LastY) or nameof(Golem.LastHeading) or nameof(Golem.Plan) or nameof(Golem.Knowledge)) Draw(); };
        GolemList.SelectedItem = golem;
        await ConnectAsync(golem);
    }

    private void RemoveGolem_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } g) return;
        Stop(g);
        golems.Remove(g);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        foreach (var g in golems.ToList()) await ConnectAsync(g);
    }

    private async Task ConnectAsync(Golem golem)
    {
        golem.Status = "connecting…";
        string? name = await GolemClient.PingAsync(golem);
        if (name == null) { golem.Status = "unreachable"; Log($"{golem.Name} › nobody answers at {golem.Url}"); return; }
        if (name != golem.Name) Log($"{golem.Name} › the golem at {golem.Address} says it is '{name}'");
        await GolemClient.RefreshReadingsAsync(golem);
        Log($"{golem.Name} › {golem.Status} · {golem.LastSeen}");
    }

    private void GolemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Draw();
        _ = RefreshCurrentAsync();
        if (journalFollow != null && GolemList.SelectedItem is Golem j) FollowJournal(j);
    }

    // ==================================================================
    // The ribbon and the coordinates box: a command composed on a tab
    // ==================================================================

    private string Point => $"{XBox.Text.Trim()},{YBox.Text.Trim()}";

    // THE POINTS PICKED ON THE MAP (Juan, 6-oct-2026: "cada click en el mapa acumula una lista de coordenadas para el comando visit"): every
    // click appends one, in order; visit and cover take them all, then/place/forget take the last; the boxes show the last one, and editing
    // them edits that last point. The map draws them numbered and joined, the way the errand will go.
    private readonly List<(double X, double Y)> picked = new();
    private bool editingPicked;

    private string Verb => Verbs.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? "visit";
    private bool ManyPoints => Verb is "visit" or "cover";

    /// <summary>The command the coordinates box builds: the verb and its points — the ones clicked in order, or the one typed.</summary>
    private string Built()
    {
        if (!ManyPoints || picked.Count <= 1) return $"{Verb} {Point}";
        return Verb + " " + string.Join(" ", picked.Select(p => $"{Fmt(p.X)},{Fmt(p.Y)}"));
    }

    private static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private void ShowLastPoint()
    {
        if (picked.Count == 0) return;
        editingPicked = true;
        XBox.Text = Fmt(picked[^1].X);
        YBox.Text = Fmt(picked[^1].Y);
        editingPicked = false;
    }

    private void ClearPoints_Click(object sender, RoutedEventArgs e) { picked.Clear(); Point_Changed(this, null); Draw(); }

    private void UndoPoint_Click(object sender, RoutedEventArgs e)
    {
        if (picked.Count == 0) return;
        picked.RemoveAt(picked.Count - 1);
        ShowLastPoint();
        Point_Changed(this, null);
        Draw();
    }

    private void Compose_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } g) { Log("add a golem first: the ribbon composes on its tab"); return; }
        string template = (sender as Button)?.Tag as string ?? "";
        if (template.Contains("{point}") && !IsPoint(Point)) { Log("the point is x and y, numbers — type them or click the map"); return; }
        // visit and cover take every point picked on the map, in order; the rest take the one in the boxes
        string points = (template.StartsWith("visit") || template.StartsWith("cover")) && picked.Count > 1 ? string.Join(" ", picked.Select(p => $"{Fmt(p.X)},{Fmt(p.Y)}")) : Point;
        g.Enqueue(template.Replace("{point}", points));
        if (picked.Count > 1 && points != Point) PointsConsumed();
    }

    // the scenario drop-down is a MENU: the pick composes 'enter <scenario>' on this golem's tab and the box empties again (the golem's
    // current scenario is said in the list itself and in what it knows); nothing goes until SEND
    private void Scenario_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox box || box.SelectedItem is not ScenarioOption chosen) return;
        box.SelectedIndex = -1;
        if (box.DataContext is not Golem g) return;
        if (chosen.Current) { Log($"{g.Name} › already in {chosen.Name}"); return; }
        g.Enqueue($"enter {chosen.Name}");
        Log($"{g.Name} › enter {chosen.Name} on its tab — SEND to selected when ready");
    }

    private void Point_Changed(object sender, RoutedEventArgs? e)
    {
        if (BuiltBox == null || Verbs == null || PointsHint == null) return;
        // a point typed by hand edits the last one picked (or becomes the first)
        if (!editingPicked && sender is TextBox && IsPoint(Point))
        {
            var parts = Point.Split(',');
            var typed = (double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
            if (picked.Count == 0) picked.Add(typed); else picked[^1] = typed;
            Draw();
        }
        BuiltBox.Text = Built();
        PointsHint.Text = picked.Count switch
        {
            0 => "click the map: every click adds a point to the visit, in order",
            1 => ManyPoints ? "1 point — click more to visit them in order" : "1 point",
            _ => ManyPoints ? $"{picked.Count} points, in the order clicked" : $"{picked.Count} clicked — {Verb} takes the last one",
        };
    }

    private void AddBuilt_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } g) { Log("add a golem first"); return; }
        if (!IsPoint(Point)) { Log("the point is x and y, numbers"); return; }
        g.Enqueue(BuiltBox.Text);
        PointsConsumed();
    }

    private void AddBuiltSelected_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPoint(Point)) { Log("the point is x and y, numbers"); return; }
        int n = 0;
        foreach (var g in SelectedGolems) { g.Enqueue(BuiltBox.Text); n++; }
        Log(n == 0 ? "no golem is checked" : $"'{BuiltBox.Text}' added to {n} tab(s)");
        if (n > 0) PointsConsumed();
    }

    // the command went to a tab: the points start afresh for the next one
    private void PointsConsumed() { picked.Clear(); Point_Changed(this, null); Draw(); }

    private void CopyBuilt_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(BuiltBox.Text);

    private static bool IsPoint(string text)
    {
        var parts = text.Split(',');
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Current is { } g) { Clipboard.SetText(g.Script); Log($"{g.Name} › script copied"); }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } g) return;
        foreach (var line in Lines(Clipboard.GetText())) g.Enqueue(line);
    }

    private void CopyToSelected_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } from) return;
        int n = 0;
        foreach (var g in SelectedGolems.Where(x => x != from)) { foreach (var line in Lines(from.Script)) g.Enqueue(line); n++; }
        Log($"{from.Name} › script appended to {n} other tab(s)");
    }

    // ==================================================================
    // Sending: the CHECKED golems' queues, one command at a time each, together as a batch (Juan, 7-oct-2026: "sólo enviar a los selected";
    // the buttons for one golem and for everybody are gone — which golems go is said by the checks alone)
    // ==================================================================

    private void SendSelected_Click(object sender, RoutedEventArgs e)
    {
        var chosen = SelectedGolems.Where(g => !g.Running).ToList();
        if (chosen.Count == 0) { Log("no golem is checked (or they are all sending already)"); return; }
        var batch = new Batch(chosen.Count);
        foreach (var g in chosen) _ = RunQueueAsync(g, batch);
    }

    private void StopSelected_Click(object sender, RoutedEventArgs e)
    {
        var sending = SelectedGolems.Where(g => g.Running).ToList();
        if (sending.Count == 0) { Log("no checked golem is sending"); return; }
        foreach (var g in sending) Stop(g);
    }

    private void Stop(Golem golem)
    {
        if (runners.Remove(golem.Name, out var cts)) { cts.Cancel(); Log($"{golem.Name} › sending stopped; the rest of the queue stays on the tab"); }
    }

    // THE BARRIER of a batch (propuesta 95): the queues sent together wait for each other at every @sync — a queue that reaches it waits
    // until every other queue of the batch reached it too, or finished, or was stopped; then all go on together. The console's own
    // directive: never sent to a golem.
    private sealed class Batch
    {
        private int participants, arrived;
        private TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Batch(int participants) { this.participants = participants; }

        public Task ArriveAsync()
        {
            arrived++;
            if (arrived >= participants) Open();
            return gate.Task;
        }

        public void Leave()
        {
            participants--;
            if (arrived >= participants && participants > 0) Open();
        }

        private void Open()
        {
            var opened = gate;
            arrived = 0;
            gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            opened.TrySetResult();
        }
    }

    // THE QUEUE RUNNER: the first line goes; a line that opens a route waits for the golem's pending routes to come back to zero — THE ACK,
    // said in the log with the time it took; a read or a lever goes on at once; @sync waits for the batch. Everything runs on the window's
    // thread, awaiting the wire, so the tab is edited live and never torn.
    private async Task RunQueueAsync(Golem golem, Batch batch)
    {
        if (golem.Running) return;
        var cts = new CancellationTokenSource();
        runners[golem.Name] = cts;
        golem.Running = true;
        int done = 0, total = golem.Script.Replace("\r\n", "\n").Split('\n').Count(l => l.Trim() != "" && !l.TrimStart().StartsWith('#'));
        try
        {
            while (!cts.IsCancellationRequested)
            {
                string? line = golem.Dequeue();
                if (line == null) { Log($"{golem.Name} › queue done ({done} command(s))"); break; }
                if (line == Choreography.Sync)
                {
                    golem.Status = "waiting for the others…";
                    await batch.ArriveAsync().WaitAsync(cts.Token);
                    continue;
                }
                var started = DateTime.UtcNow;
                var reply = await GolemClient.SendAsync(golem, line, cts.Token);
                golem.Sent++;
                if (!reply.Ok)
                {
                    Log($"{golem.Name} › {line} — {reply.Kind}: {OneLine(reply.Text)}");
                    if (reply.Kind == "unreachable") { golem.Status = "unreachable"; Log($"{golem.Name} › stopped: the golem does not answer; the rest of the queue stays"); break; }
                    continue;   // refused or no command: the golem said why; the queue goes on
                }
                string verb = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (Movers.Contains(verb))
                {
                    Log($"{golem.Name} › {line} — sent: {OneLine(reply.Text)}");
                    bool finished = await WaitUntilDoneAsync(golem, cts.Token);
                    done++;
                    var ending = await GolemClient.LastRouteAsync(golem, cts.Token);
                    bool completed = finished && ending.Status == "completed";
                    Log($"{golem.Name} › {(completed ? "✓" : "✗")} {line} {(completed ? "completed" : ending.Status == "" ? "ended" : ending.Status)}{(ending.Why == "" ? "" : " — " + ending.Why)} in {(DateTime.UtcNow - started).TotalSeconds:0.0} s ({done} of {total})");
                    if (!completed) golem.Status = $"route {ending.Status}{(ending.Why == "" ? "" : ": " + ending.Why)}";
                }
                else
                {
                    done++;
                    Log($"{golem.Name} › ✓ {line} — {OneLine(reply.Text)} ({done} of {total})");
                }
                await GolemClient.RefreshReadingsAsync(golem, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            golem.Running = false;
            runners.Remove(golem.Name);
            batch.Leave();
            await GolemClient.RefreshReadingsAsync(golem);
        }
    }

    // the golem finished when nothing is pending; a route may take a moment to open (a choreography waits for the fleet's words), so a
    // short grace is given for it to appear before "nothing pending" counts as done
    private async Task<bool> WaitUntilDoneAsync(Golem golem, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        bool seenPending = false;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            int? pending = await GolemClient.PendingAsync(golem, ct);
            if (pending == null) { golem.Status = "unreachable"; return false; }
            if (pending > 0) { seenPending = true; golem.Status = $"busy: {pending} route(s) pending"; continue; }
            if (seenPending) return true;
            if ((DateTime.UtcNow - started).TotalSeconds > 8) return false;
        }
        return false;
    }

    // ==================================================================
    // The journal of the current golem, live: GET /events, server-sent
    // ==================================================================

    private void Journal_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } g) { Log("add a golem: the journal is its"); return; }
        JournalTab.IsSelected = true;
        FollowJournal(g);
    }

    private async void FollowJournal(Golem golem)
    {
        journalFollow?.Cancel();
        var cts = journalFollow = new CancellationTokenSource();
        JournalTab.Header = $"journal of {golem.Name}";
        JournalBox.Clear();
        try
        {
            using var wire = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await wire.GetAsync(new Uri(golem.Url, "events"), HttpCompletionOption.ResponseHeadersRead, cts.Token);
            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);
            while (!cts.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cts.Token);
                if (line == null) break;
                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                try
                {
                    var e = JsonDocument.Parse(line[6..]).RootElement;
                    string kind = JsonWalk.String(e, "Kind") ?? "";
                    string script = JsonWalk.String(e, "Script") ?? "";
                    string note = JsonWalk.String(e, "Note") ?? "";
                    if (kind == "command" && script.StartsWith("define action", StringComparison.Ordinal)) continue;   // the definitions, hidden like the panel did
                    string at = JsonWalk.String(e, "At") is string s && DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("HH:mm:ss") : "";
                    JournalBox.AppendText($"#{JsonWalk.Number(e, "Entry")} {at} {kind,-8} {OneLine(script == "" ? note : script)}{Environment.NewLine}");
                    JournalBox.ScrollToEnd();
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException) { JournalBox.AppendText($"— the journal's feed ended: {ex.Message}{Environment.NewLine}"); }
    }

    // ==================================================================
    // Workspaces and routines: files
    // ==================================================================

    // A workspace is one file (.golemws): the golems and the script each was left with. Switching is opening another; what is open is
    // saved by itself before, and when the window closes. The last one opened comes back at start; the recent ones are buttons.
    private void NewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        AutoSave();
        foreach (var g in golems.ToList()) Stop(g);
        golems.Clear();
        workspace = null;
        Title = "WardenCli — the operator's console";
        WorkspaceLabel.Text = "no workspace: the scripts live in this window until you save one";
        RefreshRecent();
    }

    private void OpenWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open a workspace", Filter = $"golem workspace (*{Workspace.Extension})|*{Workspace.Extension}|all files|*.*", InitialDirectory = Workspace.RoutinesFolder(workspace) };
        if (dialog.ShowDialog(this) != true) return;
        OpenWorkspace(dialog.FileName);
    }

    private void OpenWorkspace(string path)
    {
        try
        {
            var loaded = Workspace.Load(path);
            AutoSave();
            foreach (var g in golems.ToList()) Stop(g);
            golems.Clear();
            workspace = path;
            foreach (var g in loaded) Add(g);
            if (golems.Count > 0) GolemList.SelectedItem = golems[0];
            WorkspaceLabel.Text = $"workspace: {workspace}";
            Title = $"WardenCli — {System.IO.Path.GetFileNameWithoutExtension(path)}";
            Log($"workspace opened: {path} — {loaded.Count} golem(s)");
            RefreshRecent();
        }
        catch (Exception ex) { Log($"the workspace could not be opened: {ex.Message}"); Workspace.Forget(path); RefreshRecent(); }
    }

    private void SaveWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (workspace == null) { SaveWorkspaceAs_Click(sender, e); return; }
        Workspace.Save(workspace, golems);
        Log($"workspace saved: {workspace}");
        RefreshRecent();
    }

    private void SaveWorkspaceAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save the workspace as", Filter = $"golem workspace (*{Workspace.Extension})|*{Workspace.Extension}", DefaultExt = Workspace.Extension.TrimStart('.'), InitialDirectory = Workspace.RoutinesFolder(workspace), FileName = "fleet" };
        if (dialog.ShowDialog(this) != true) return;
        workspace = dialog.FileName;
        Workspace.Save(workspace, golems);
        WorkspaceLabel.Text = $"workspace: {workspace}";
        Title = $"WardenCli — {System.IO.Path.GetFileNameWithoutExtension(workspace)}";
        Log($"workspace saved: {workspace}");
        RefreshRecent();
    }

    private void AutoSave()
    {
        if (workspace == null) return;
        try { Workspace.Save(workspace, golems); } catch (IOException ex) { Log($"the workspace could not be saved: {ex.Message}"); }
    }

    private void RefreshRecent()
    {
        RecentPanel.Children.Clear();
        var recent = Workspace.Recent();
        if (recent.Count == 0) { RecentPanel.Children.Add(new TextBlock { Text = "none yet", Foreground = (Brush)FindResource("Dim"), Margin = new Thickness(4) }); return; }
        foreach (var path in recent)
        {
            var button = new Button { Content = System.IO.Path.GetFileNameWithoutExtension(path), ToolTip = path, Tag = path, Padding = new Thickness(8, 2, 8, 2), MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Left };
            if (string.Equals(path, workspace, StringComparison.OrdinalIgnoreCase)) button.BorderBrush = (Brush)FindResource("Accent");
            button.Click += (_, _) => OpenWorkspace(path);
            RecentPanel.Children.Add(button);
        }
    }

    private void SaveRoutine_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } g) return;
        var dialog = new SaveFileDialog { Title = "Save this tab as a routine", Filter = "routine (*.routine)|*.routine|all files|*.*", DefaultExt = "routine", InitialDirectory = Workspace.RoutinesFolder(workspace), FileName = g.Name };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, g.Script);
        Log($"routine saved: {dialog.FileName}");
    }

    private void LoadRoutine_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } g) return;
        string? text = PickRoutine();
        if (text == null) return;
        foreach (var line in Lines(text)) g.Enqueue(line);
    }

    private void LoadRoutineSelected_Click(object sender, RoutedEventArgs e)
    {
        string? text = PickRoutine();
        if (text == null) return;
        foreach (var g in SelectedGolems) foreach (var line in Lines(text)) g.Enqueue(line);
    }

    private string? PickRoutine()
    {
        var dialog = new OpenFileDialog { Title = "Load a routine", Filter = "routine (*.routine)|*.routine|all files|*.*", InitialDirectory = Workspace.RoutinesFolder(workspace) };
        if (dialog.ShowDialog(this) != true) return null;
        Log($"routine loaded: {dialog.FileName}");
        return File.ReadAllText(dialog.FileName);
    }

    private static IEnumerable<string> Lines(string text) => text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l != "");

    private void ClearTab_Click(object sender, RoutedEventArgs e)
    {
        if (Current is { } g) g.Script = "";
    }

    private void ClearAllTabs_Click(object sender, RoutedEventArgs e)
    {
        int n = 0;
        foreach (var g in golems.Where(g => !g.Running)) { g.Script = ""; n++; }
        Log(golems.Any(g => g.Running) ? $"{n} tab(s) cleared; the ones sending keep their queue — stop them first" : $"{n} tab(s) cleared");
    }

    // ==================================================================
    // Formations laid out by the console: queues of visits for every golem
    // ==================================================================

    private void Formation_Click(object sender, RoutedEventArgs e)
    {
        if (golems.Count == 0) { Log("add the golems first: a formation is laid out for the golems in operation"); return; }
        var dialog = new FormationDialog(golems.Select(g => g.Name).ToList(), Point) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var fleet = new Fleet(golems.Where(g => dialog.Chosen.Contains(g.Name)).Select(g => new Member(g.Name, g.LastX is double x && g.LastY is double y ? new Spot(x, y) : null)));
            var choreography = new Choreography(Figure.Named(dialog.Figure, new Spot(dialog.CenterX, dialog.CenterY), dialog.Side), fleet, Assignment.Named(dialog.Assignment), dialog.Steps, dialog.Clockwise);
            var scripts = choreography.Scripts(dialog.OneErrand ? Pace.OneErrand : Pace.Rounds);
            foreach (var (name, lines) in scripts)
            {
                var g = golems.First(x => x.Name == name);
                foreach (var line in lines) g.Enqueue(line);
            }
            Log($"laid out: {choreography.Describe()} — {scripts.Values.Sum(q => q.Count(l => l.StartsWith("visit")))} visit(s) on {scripts.Count} tab(s){(dialog.OneErrand ? "" : $"; the queues wait for each other at every {Choreography.Sync}")} — read them, then SEND to selected");
            // the formation this leaves in force joins the list: from here on it is rotated from the console (propuesta 96)
            var formation = choreography.Outcome(formations.Count == 0 ? 1 : formations.Max(f => f.Number) + 1);
            formation.PropertyChanged += (_, _) => Draw();
            formations.Add(formation);
            FormationList.SelectedItem = formation;
            FormationsTab.IsSelected = true;
            Log($"active: {formation.Describe()}");
        }
        catch (ArgumentException ex) { Log(ex.Message); }
    }

    // A ROTATION OF AN ACTIVE FORMATION (propuesta 96): the formation moves every member the steps asked in the sense and writes the visit each
    // golem gets; the console puts it on each member's tab and keeps the vertices held for the next rotation. Nothing goes until SEND.
    private void RotateClockwise_Click(object sender, RoutedEventArgs e) => Rotate(sender, Sense.Clockwise);
    private void RotateCounter_Click(object sender, RoutedEventArgs e) => Rotate(sender, Sense.Counterclockwise);

    private void Rotate(object sender, Sense sense)
    {
        if ((sender as Button)?.Tag is not Formation formation) return;
        // ONE STEP PER CLICK (Juan, 7-oct-2026: "quítala, deja siempre un paso por clic"): two steps are two clicks, two visits on each tab
        try
        {
            var lines = formation.Rotate(sense);
            int missing = 0;
            foreach (var (name, line) in lines)
            {
                var g = golems.FirstOrDefault(x => x.Name == name);
                if (g == null) { missing++; continue; }
                g.Enqueue($"# {formation.Name} rotates one step {(sense == Sense.Clockwise ? "clockwise" : "counter-clockwise")} — {name} to {formation.Figure.Label(formation.IndexOf(name), formation.Places.Count)}");
                g.Enqueue(line);
            }
            Log($"{formation.Name} › one step {(sense == Sense.Clockwise ? "clockwise" : "counter-clockwise")}: a visit on {lines.Count - missing} tab(s){(missing > 0 ? $" ({missing} golem(s) no longer here)" : "")} — now {formation.Holders}; SEND to selected when ready");
        }
        catch (ArgumentException ex) { Log(ex.Message); }
    }

    private void Dissolve_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not Formation formation) return;
        formations.Remove(formation);
        Log($"{formation.Name} dissolved — the golems stay where they are");
    }

    // ==================================================================
    // The golem debugger: the window's right side, whole height, hidden and shown on demand (Juan, 7-oct-2026: "que ese panel use todo el
    // lateral derecho… y que se pueda ocultar y mostrar"); hidden, it leaves a strip at the right edge and the console takes the width
    // ==================================================================

    private void HideDebugger_Click(object sender, RoutedEventArgs e) => SetDebugger(false);
    private void ShowDebugger_Click(object sender, MouseButtonEventArgs e) => SetDebugger(true);
    private void ToggleDebugger_Click(object sender, RoutedEventArgs e) => SetDebugger(DebuggerPanel.Visibility != Visibility.Visible);

    private void SetDebugger(bool shown)
    {
        DebuggerPanel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        DebuggerStrip.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        DebuggerColumn.MinWidth = shown ? 330 : 0;
        DebuggerColumn.Width = shown ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        if (shown) Draw();
    }

    // ==================================================================
    // The map
    // ==================================================================

    private void Map_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();

    private void Map_Click(object sender, MouseButtonEventArgs e)
    {
        var (x, y) = FloorPoint(e.GetPosition(Map));
        if (x < 0 || y < 0 || x > FloorSize || y > FloorSize) return;
        x = Math.Round(x, 1); y = Math.Round(y, 1);
        picked.Add((x, y));
        ShowLastPoint();
        Point_Changed(this, null);
        Draw();
    }

    // the floor is drawn SQUARE and CENTRED in the map's panel, whatever the panel's shape (Juan, 6-oct-2026: the map did not fit the resolution)
    private double Scale() => Math.Max(1, (Math.Min(Map.ActualWidth, Map.ActualHeight) - 12) / FloorSize);
    private double OffsetX => (Map.ActualWidth - FloorSize * Scale()) / 2;
    private double OffsetY => (Map.ActualHeight - FloorSize * Scale()) / 2;
    private (double X, double Y) FloorPoint(Point p) => ((p.X - OffsetX) / Scale(), FloorSize - (p.Y - OffsetY) / Scale());
    private Point Pixel(double x, double y) => new(OffsetX + x * Scale(), OffsetY + (FloorSize - y) * Scale());

    // the coordinates under the cursor, shown while it moves over the floor and gone when it leaves (Juan, 6-oct-2026)
    private void Map_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Map);
        var (x, y) = FloorPoint(p);
        if (x < 0 || y < 0 || x > FloorSize || y > FloorSize) { Hover.Visibility = Visibility.Collapsed; return; }
        HoverText.Text = $"x {x.ToString("0.0", CultureInfo.InvariantCulture)}  y {y.ToString("0.0", CultureInfo.InvariantCulture)}";
        Hover.Visibility = Visibility.Visible;
        double left = p.X + 14, top = p.Y + 14;
        if (left + 110 > Map.ActualWidth) left = p.X - 118;
        if (top + 26 > Map.ActualHeight) top = p.Y - 30;
        Hover.Margin = new Thickness(Math.Max(0, left), Math.Max(0, top), 0, 0);
    }

    private void Map_MouseLeave(object sender, MouseEventArgs e) => Hover.Visibility = Visibility.Collapsed;

    private void Draw()
    {
        Map.Children.Clear();
        double scale = Scale();
        var grid = new SolidColorBrush(Color.FromRgb(0x2b, 0x35, 0x40));
        var origin = Pixel(0, FloorSize); var far = Pixel(FloorSize, 0);
        Map.Children.Add(new Rectangle { Width = far.X - origin.X, Height = far.Y - origin.Y, Stroke = (Brush)FindResource("Line"), StrokeThickness = 1.5, Fill = new SolidColorBrush(Color.FromRgb(0x0e, 0x13, 0x19)) }.Also(r => { Canvas.SetLeft(r, origin.X); Canvas.SetTop(r, origin.Y); }));
        for (int i = 0; i <= FloorSize; i++)
        {
            var a = Pixel(i, 0); var b = Pixel(i, FloorSize);
            Map.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = grid, StrokeThickness = i % 5 == 0 ? 1.2 : 0.5 });
            var l = Pixel(0, i); var r = Pixel(FloorSize, i);
            Map.Children.Add(new Line { X1 = l.X, Y1 = l.Y, X2 = r.X, Y2 = r.Y, Stroke = grid, StrokeThickness = i % 5 == 0 ? 1.2 : 0.5 });
        }
        // THE SELECTED GOLEM'S SCENARIO, as it told it: its zones, the open sides faint, the doors marked (Juan, 6-oct-2026)
        var plan = Current?.Plan;
        MapHeader.Text = plan == null ? "map — the last position asked of each golem" : $"map — {Current!.Name}'s scenario: {plan.Name} · every golem where it was last asked · its obstacles in red";
        KnowsHeader.Text = Current == null ? "golem debugger — select a golem: what it knows, its map, a command from a point" : $"golem debugger — {Current.Name}: what it knows (asked every 3 s), its map, a command from a point";
        if (plan != null)
        {
            var wall = new SolidColorBrush(Color.FromRgb(0x8a, 0x91, 0x99));
            var open = new SolidColorBrush(Color.FromRgb(0x3a, 0x46, 0x55));
            var door = (Brush)FindResource("Accent");
            foreach (var z in plan.Zones)
            {
                var tl = Pixel(z.X, z.Y + z.H); var br = Pixel(z.X + z.W, z.Y);
                var box = new Rectangle { Width = br.X - tl.X, Height = br.Y - tl.Y, Stroke = wall, StrokeThickness = 1.5, Fill = new SolidColorBrush(Color.FromArgb(0x18, 0xe6, 0xe1, 0xcf)) };
                Canvas.SetLeft(box, tl.X); Canvas.SetTop(box, tl.Y); Map.Children.Add(box);
                var name = new TextBlock { Text = z.Name, Foreground = new SolidColorBrush(Color.FromRgb(0x8a, 0x91, 0x99)), FontSize = 10 };
                var centre = Pixel(z.X + z.W / 2, z.Y + z.H / 2); Canvas.SetLeft(name, centre.X - 3 * z.Name.Length); Canvas.SetTop(name, centre.Y - 8); Map.Children.Add(name);
            }
            foreach (var z in plan.Zones)
            {
                foreach (var to in z.Opens)
                {
                    var other = plan.Zones.FirstOrDefault(o => o.Name == to);
                    if (other == null || FloorPlan.SharedEdge(z, other) is not { } e) continue;
                    var a = Pixel(e.X0, e.Y0); var b = Pixel(e.X1, e.Y1);
                    Map.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = open, StrokeThickness = 3 });   // the open side: the wall painted out
                }
                foreach (var d in z.Doors)
                {
                    // the door is a GAP in the wall, as wide as the map says: the wall painted out over the gap, a faint threshold across it;
                    // the wall runs vertical when the door's point lies on the zone's left or right edge, horizontal otherwise
                    bool vertical = Math.Abs(d.X - z.X) < 1e-6 || Math.Abs(d.X - (z.X + z.W)) < 1e-6;
                    double half = d.Width / 2;
                    var a = vertical ? Pixel(d.X, d.Y - half) : Pixel(d.X - half, d.Y);
                    var b = vertical ? Pixel(d.X, d.Y + half) : Pixel(d.X + half, d.Y);
                    Map.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = open, StrokeThickness = 3.5 });
                    Map.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = door, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 }, Opacity = 0.8 });
                }
            }
        }
        // the axes' numbers, every metre, so a point is read off the picture
        for (int i = 0; i <= FloorSize; i += 1)
        {
            var tx = new TextBlock { Text = i.ToString(), Foreground = grid, FontSize = 9 }; var px = Pixel(i, 0); Canvas.SetLeft(tx, px.X - 3); Canvas.SetTop(tx, px.Y + 1); Map.Children.Add(tx);
            var ty = new TextBlock { Text = i.ToString(), Foreground = grid, FontSize = 9 }; var py = Pixel(0, i); Canvas.SetLeft(ty, py.X - 12); Canvas.SetTop(ty, py.Y - 7); Map.Children.Add(ty);
        }
        // THE OBSTACLES THE SELECTED GOLEM HYPOTHESIZES (Juan, 7-oct-2026: "mostrar los obstáculos en el mapa que el golem ha reconocido"): each
        // mark of a thing as a red disc of its reach, the vertices joined — two make a line, three or more a figure; a peer met as a dashed ring
        if (Current?.Knowledge.Obstacles is { } learned)
        {
            var markFill = new SolidColorBrush(Color.FromArgb(0x70, 0xe0, 0x77, 0x77));
            var markLine = new SolidColorBrush(Color.FromArgb(0xf0, 0xe0, 0x77, 0x77));
            var figureFill = new SolidColorBrush(Color.FromArgb(0x40, 0xe0, 0x77, 0x77));
            var peer = new SolidColorBrush(Color.FromArgb(0xa0, 0xe0, 0x77, 0x77));
            foreach (var o in learned)
            {
                if (!o.IsThing)
                {
                    var pc = Pixel(o.X, o.Y); double pr = Math.Max(5, 0.25 * scale);
                    var ring = new Ellipse { Width = 2 * pr, Height = 2 * pr, Stroke = peer, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2, 2 } };
                    Canvas.SetLeft(ring, pc.X - pr); Canvas.SetTop(ring, pc.Y - pr); Map.Children.Add(ring);
                    var who = new TextBlock { Text = $"peer {o.Who}", Foreground = peer, FontSize = 9 };
                    Canvas.SetLeft(who, pc.X + pr + 2); Canvas.SetTop(who, pc.Y - 7); Map.Children.Add(who);
                    continue;
                }
                foreach (var v in o.Vertices)
                {
                    var pv = Pixel(v.X, v.Y); double r = Math.Max(4, v.Reach * scale);
                    var disc = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = markFill, Stroke = markLine, StrokeThickness = 1.5 };
                    Canvas.SetLeft(disc, pv.X - r); Canvas.SetTop(disc, pv.Y - r); Map.Children.Add(disc);
                }
                if (o.Vertices.Count >= 2)
                {
                    var figure = new System.Windows.Shapes.Polygon { Stroke = markLine, StrokeThickness = 2, Fill = o.Vertices.Count >= 3 ? figureFill : Brushes.Transparent };
                    foreach (var v in o.Vertices) figure.Points.Add(Pixel(v.X, v.Y));
                    if (o.Vertices.Count == 2) Map.Children.Add(new Line { X1 = Pixel(o.Vertices[0].X, o.Vertices[0].Y).X, Y1 = Pixel(o.Vertices[0].X, o.Vertices[0].Y).Y, X2 = Pixel(o.Vertices[1].X, o.Vertices[1].Y).X, Y2 = Pixel(o.Vertices[1].X, o.Vertices[1].Y).Y, Stroke = markLine, StrokeThickness = 2 });
                    else Map.Children.Add(figure);
                }
            }
        }
        // THE ACTIVE FORMATIONS PROJECTED (propuesta 96; Juan, 7-oct-2026: "que se vea la forma de la figura encima del mapa para proyectar la
        // formación seleccionada… un botón como un ojo"): every formation whose eye is open shows its FIGURE — the polygon through its vertices
        // or the circle of its radius, filled faintly in purple, its centre crossed — and every vertex a diamond ringed in its holder's colour
        // with the compass point and the holder's name, hollow when free; the one selected in the list is drawn strongest, the others dashed
        var purple = ((SolidColorBrush)FindResource("Purple")).Color;
        var chosen = FormationList.SelectedItem as Formation;
        foreach (var f in formations.Where(f => f.Shown).OrderBy(f => f == chosen))
        {
            bool strong = f == chosen;
            var line = new SolidColorBrush(purple) { Opacity = strong ? 1 : 0.7 };
            var fill = new SolidColorBrush(Color.FromArgb(strong ? (byte)0x30 : (byte)0x14, purple.R, purple.G, purple.B));
            var dash = strong ? null : new DoubleCollection { 3, 3 };
            var vertices = f.Places;
            var centre = Pixel(f.Figure.Center.X, f.Figure.Center.Y);
            if (f.Figure is Circle circle)
            {
                double r = circle.Radius * scale;
                var ring = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = line, StrokeThickness = strong ? 2 : 1, StrokeDashArray = dash, Fill = fill };
                Canvas.SetLeft(ring, centre.X - r); Canvas.SetTop(ring, centre.Y - r); Map.Children.Add(ring);
            }
            else if (vertices.Count >= 3)
            {
                var shape = new System.Windows.Shapes.Polygon { Stroke = line, StrokeThickness = strong ? 2 : 1, StrokeDashArray = dash, Fill = fill };
                foreach (var v in vertices) shape.Points.Add(Pixel(v.X, v.Y));
                Map.Children.Add(shape);
            }
            Map.Children.Add(new Line { X1 = centre.X - 5, Y1 = centre.Y, X2 = centre.X + 5, Y2 = centre.Y, Stroke = line, StrokeThickness = 1 });
            Map.Children.Add(new Line { X1 = centre.X, Y1 = centre.Y - 5, X2 = centre.X, Y2 = centre.Y + 5, Stroke = line, StrokeThickness = 1 });
            var tag = new TextBlock { Text = f.Name, Foreground = line, FontSize = strong ? 10 : 9, FontWeight = strong ? FontWeights.Bold : FontWeights.Normal };
            Canvas.SetLeft(tag, centre.X + 6); Canvas.SetTop(tag, centre.Y + 2); Map.Children.Add(tag);
            for (int i = 0; i < vertices.Count; i++)
            {
                var pv = Pixel(vertices[i].X, vertices[i].Y);
                string? who = f.HolderOf(i);
                var holder = who == null ? line : new SolidColorBrush(GolemColor.Of(who));
                var diamond = new System.Windows.Shapes.Polygon { Stroke = holder, StrokeThickness = who == null ? 1 : 2, Fill = Brushes.Transparent, Opacity = who == null ? 0.6 : strong ? 1 : 0.8 };
                diamond.Points.Add(new Point(pv.X, pv.Y - 7)); diamond.Points.Add(new Point(pv.X + 7, pv.Y)); diamond.Points.Add(new Point(pv.X, pv.Y + 7)); diamond.Points.Add(new Point(pv.X - 7, pv.Y));
                Map.Children.Add(diamond);
                var label = new TextBlock { Text = f.Figure.Label(i, vertices.Count) + (who == null ? " free" : " " + who), Foreground = holder, FontSize = 9, Opacity = who == null ? 0.7 : 1 };
                Canvas.SetLeft(label, pv.X + 8); Canvas.SetTop(label, pv.Y + 2); Map.Children.Add(label);
            }
        }
        // the points picked: the way the errand will go, numbered, joined by a dashed line
        var way = new SolidColorBrush(Color.FromRgb(0xff, 0xb4, 0x54));
        for (int i = 0; i < picked.Count; i++)
        {
            var pp = Pixel(picked[i].X, picked[i].Y); double px = pp.X, py = pp.Y;
            if (i > 0 && ManyPoints)
            {
                var pq = Pixel(picked[i - 1].X, picked[i - 1].Y); double qx = pq.X, qy = pq.Y;
                Map.Children.Add(new Line { X1 = qx, Y1 = qy, X2 = px, Y2 = py, Stroke = way, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } });
            }
            bool counts = ManyPoints || i == picked.Count - 1;
            var mark = new Ellipse { Width = 10, Height = 10, Fill = counts ? way : Brushes.Transparent, Stroke = way, StrokeThickness = 1.5, Opacity = counts ? 1 : 0.4 };
            Canvas.SetLeft(mark, px - 5); Canvas.SetTop(mark, py - 5);
            Map.Children.Add(mark);
            var number = new TextBlock { Text = (i + 1).ToString(), Foreground = way, FontSize = 11, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(number, px + 6); Canvas.SetTop(number, py - 16);
            Map.Children.Add(number);
        }
        var selected = Current;
        foreach (var g in golems)
        {
            if (g.LastX == null || g.LastY == null) continue;
            var pc = Pixel(g.LastX.Value, g.LastY.Value); double cx = pc.X, cy = pc.Y, r = 0.25 * scale;
            var brush = new SolidColorBrush(GolemColor.Of(g.Name));
            var dot = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = brush, Stroke = g == selected ? Brushes.White : brush, StrokeThickness = g == selected ? 2 : 1 };
            Canvas.SetLeft(dot, cx - r); Canvas.SetTop(dot, cy - r);
            Map.Children.Add(dot);
            if (g.LastHeading is double h)
                Map.Children.Add(new Line { X1 = cx, Y1 = cy, X2 = cx + 2 * r * Math.Cos(h), Y2 = cy - 2 * r * Math.Sin(h), Stroke = Brushes.White, StrokeThickness = 1.5 });
            var label = new TextBlock { Text = g.Name, Foreground = brush, FontSize = 11, FontWeight = FontWeights.Bold };
            bool nearRight = g.LastX.Value > FloorSize - 1.5, nearTop = g.LastY.Value > FloorSize - 0.8;
            Canvas.SetLeft(label, nearRight ? cx - r - 6 * g.Name.Length - 4 : cx + r + 2); Canvas.SetTop(label, nearTop ? cy + r : cy - r - 4);
            Map.Children.Add(label);
        }
    }

    // ==================================================================
    // Help, log, exit, odds and ends
    // ==================================================================

    private async void Commands_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } g) { Log("add a golem: the commands are asked of it"); return; }
        Log($"{g.Name} › the commands:{Environment.NewLine}{await GolemClient.CommandsAsync(g)}");
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, "WardenCli — the operator's console for the golems.\n\nThe ribbon composes commands on each golem's tab; a tab's script is its queue, sent one command at a time, the next when the golem finished the one before. Check golems to address them together. Workspaces and routines are files. Formations laid out here are queues of visits; the golem's own choreography module stays apart.", "About WardenCli");

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Log(string text)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private static string OneLine(string text)
    {
        string s = text.Replace("\r", " ").Replace("\n", " ⏎ ").Trim();
        return s.Length > 220 ? s[..220] + "…" : s;
    }
}

internal static class Fluent
{
    /// <summary>Sets up an element inline and hands it back.</summary>
    public static T Also<T>(this T element, Action<T> setup) { setup(element); return element; }
}
