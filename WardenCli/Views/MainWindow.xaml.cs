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
    // THE FORMATIONS ARE THE GOLEM'S (propuesta 104): the list shows the selected golem's `formations` read; the console keeps only its
    // own way of looking at them — the eyes closed, by name, and the one selected
    private readonly HashSet<string> hiddenFormations = new(StringComparer.Ordinal);
    // the figures reshaped on the map and not told yet (the drafts, 9-oct-2026), by formation name: they survive the reads, which make the views again
    private readonly Dictionary<string, Figure> drafts = new(StringComparer.Ordinal);
    private string? selectedFormation;
    // a formation being RESHAPED on the map (propuestas 97, 98): grabbed by one of the grips of the selected one — its centre moves it, the
    // square on its first vertex resizes it, the knob beyond that vertex turns it — and the figure it would become while the mouse is held
    private enum Grip { Move, Size, Turn }
    private FormationView? dragging;
    private Grip grip;
    private Figure? ghost;
    private readonly Dictionary<string, CancellationTokenSource> runners = new();
    private CancellationTokenSource? journalFollow;
    private string? workspace;

    // THE READS, shown beside the map (Juan, 7-oct-2026): where, state, route, obstacles, formations — asked ON DEMAND with *refresh*, or every
    // 3 s while *auto* is on (Juan, 9-oct-2026: "no lo hagas cada 3 segundos el refresh"); the queue asks them by itself when a line ends
    private readonly DispatcherTimer readings = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool reading;
    private int quietTicks;

    // the floor the map draws: the selected golem's scenario as it told it (GET /map), the arena's 11 × 11 m until one is known
    private double FloorSize => Current?.Plan?.Extent ?? 11.0;

    /// <summary>The console's own directive on a tab (propuesta 95): never sent — the queues of one SEND wait for each other there.</summary>
    public const string Sync = "@sync";

    // the verbs whose command leaves a route in the golem: the queue waits for it to end before the next line goes
    private static readonly HashSet<string> Movers = new(StringComparer.Ordinal) { "visit", "cover", "then", "resume", "rotate", "take" };

    public MainWindow()
    {
        InitializeComponent();
        GolemList.ItemsSource = golems;
        FormationList.SelectionChanged += (_, _) =>
        {
            if (FormationList.SelectedItem is FormationView f) selectedFormation = f.Name;
            Draw();
        };
        golems.CollectionChanged += (_, _) => { Draw(); NoGolem.Visibility = golems.Count == 0 ? Visibility.Visible : Visibility.Collapsed; };
        readings.Tick += async (_, _) => await RefreshCurrentAsync();
        DebuggerBody.SizeChanged += (_, _) => FitDebugger();
        ReadsBox.SizeChanged += (_, _) => FitDebugger();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F9) { SetDebugger(DebuggerPanel.Visibility != Visibility.Visible); e.Handled = true; }
            else if (e.Key == Key.Escape && placing != null) { Disarm("cancelled"); e.Handled = true; }
        };
        // place armed: a click anywhere but the map (or the button itself, which disarms it) cancels it (Juan, 8-oct-2026)
        PreviewMouseDown += (_, e) => { if (placing != null && !IsWithin(e.OriginalSource, Map) && !IsWithin(e.OriginalSource, PlaceButton)) Disarm("cancelled: a click elsewhere"); };
        Log("WardenCli ready — add the golems in operation, compose a script per tab, SEND.");
    }

    private async void RefreshReads_Click(object sender, RoutedEventArgs e) => await RefreshCurrentAsync();

    private void Auto_Toggled(object sender, RoutedEventArgs e)
    {
        if (AutoBox.IsChecked == true) readings.Start(); else readings.Stop();
    }

    // the selected golem's reads, refreshed in turn; one that does not answer is asked again every fifth tick
    private async Task RefreshCurrentAsync()
    {
        if (reading || Current is not { } g) return;
        if (g.Status == "unreachable" && ++quietTicks % 5 != 0) return;
        reading = true;
        try { await GolemClient.RefreshReadingsAsync(g); }
        finally { reading = false; }
        SyncFormations(g);
    }

    // the selected golem's formations, as the console looks at them: the eyes closed by name stay closed, the one selected stays selected
    private void SyncFormations(Golem g)
    {
        var views = g.Knowledge.Formations;
        foreach (var gone in drafts.Keys.Where(k => views.All(v => v.Name != k)).ToList()) drafts.Remove(gone);   // a formation dissolved takes its draft with it
        foreach (var v in views)
        {
            v.Shown = !hiddenFormations.Contains(v.Name);
            v.Draft = drafts.TryGetValue(v.Name, out var d) ? d : null;
        }
        if (selectedFormation != null && views.FirstOrDefault(v => v.Name == selectedFormation) is { } keep && FormationList.SelectedItem != keep) FormationList.SelectedItem = keep;
        NoFormation.Text = views.Count == 0 ? $"{g.Name} is in no formation — Console › Formation forms one" : "the golem's own: select one — its centre moves it, the square resizes it, the knob turns it (a draft until shot writes form + take on the tab); ↻ ↺ write a step on the tab, ✕ dissolves it now";
        Draw();
    }

    // the selected golem's formations, as it tells them
    private IReadOnlyList<FormationView> Views() => Current?.Knowledge.Formations ?? Array.Empty<FormationView>();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Point_Changed(this, null);
        RefreshRecent();
        RestorePanes();
        // the workspace named on the command line, else the last one opened: the console starts where it was left
        string? start = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => a.EndsWith(Workspace.Extension, StringComparison.OrdinalIgnoreCase)) ?? Workspace.Last();
        if (start != null) OpenWorkspace(start);
    }

    // the scripts are saved as they are when the window closes, when a workspace is open
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        AutoSave();
        RememberPanes();
    }

    // THE PANES' SIZES, remembered when the console closes and given back when it opens (Juan, 7-oct-2026: "que los tamaños de los paneles se
    // recuerden al cerrar"): the golems' column, the console and the debugger's shares, the log's height, the map's own height, the debugger
    // shown or hidden — kept in the console's settings, not in the workspace
    private void RememberPanes()
    {
        var debugger = DebuggerPanel.Visibility == Visibility.Visible ? DebuggerColumn.Width : debuggerWidth;
        Workspace.RememberPanes(new PaneSizes(
            GolemsColumn.ActualWidth,
            ConsoleColumn.Width.IsStar ? ConsoleColumn.Width.Value : ConsoleColumn.ActualWidth,
            debugger.IsStar ? debugger.Value : 1,
            LogRow.ActualHeight,
            mapHeight,
            DebuggerPanel.Visibility == Visibility.Visible,
            OthersBox.IsChecked == true,
            AutoBox.IsChecked == true));
    }

    private void RestorePanes()
    {
        if (Workspace.Panes() is not { } p) return;
        if (p.GolemsWidth >= GolemsColumn.MinWidth) GolemsColumn.Width = new GridLength(p.GolemsWidth);
        if (p.ConsoleShare > 0 && p.DebuggerShare > 0)
        {
            ConsoleColumn.Width = new GridLength(p.ConsoleShare, GridUnitType.Star);
            debuggerWidth = new GridLength(p.DebuggerShare, GridUnitType.Star);
            DebuggerColumn.Width = debuggerWidth;
        }
        if (p.LogHeight >= LogRow.MinHeight) LogRow.Height = new GridLength(p.LogHeight);
        mapHeight = p.MapHeight is > 0 ? p.MapHeight : null;
        if (!p.DebuggerShown) SetDebugger(false);
        if (p.ShowOthers is bool others) OthersBox.IsChecked = others;
        if (p.AutoRefresh is bool auto) AutoBox.IsChecked = auto;
        FitDebugger();
    }

    private Golem? Current => GolemList.SelectedItem as Golem;
    private IEnumerable<Golem> SelectedGolems => golems.Where(g => g.Selected);

    // ==================================================================
    // The golems in operation
    // ==================================================================

    // the golems to add, said in a dialog (Juan, 7-oct-2026): one by host, port and name, or the four of the compose; the port offered is the
    // one after the highest here
    private void AddGolem_Click(object sender, RoutedEventArgs e)
    {
        int next = golems.Count == 0 ? 8081 : golems.Max(g => g.Port) + 1;
        var dialog = new AddGolemDialog(golems.Select(g => g.Name).ToList(), next) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        foreach (var (name, host, port) in dialog.Chosen) Add(new Golem { Name = name, Host = host, Port = port });
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
        int lines = g.Script.Replace("\r\n", "\n").Split('\n').Count(l => l.Trim() != "");
        string script = lines == 0 ? "its tab is empty" : $"its script ({lines} line(s)) goes with it";
        if (MessageBox.Show(this, $"remove {g.Name} from this workspace? {script}; the golem itself keeps running.", $"remove {g.Name}", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Stop(g);
        golems.Remove(g);
        Log($"{g.Name} › removed from this workspace");
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
    // click appends one, in order; visit and cover take them all (then, place and forget left the tool on 8-oct-2026: then was refused once its visit had ended, place is the map's button, forget the obstacles' tab's 'gone'); the boxes show the last one, and editing
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

    // ==================================================================
    // THE GOLEM'S ENVIRONMENT (Juan, 7-oct-2026: "parece más de ambiente del golem, no tanto de operaciones de los scripts"): its scenario,
    // its way of taking the doors, its levers — acted at once on the selected golem, never queued on its script
    // ==================================================================

    // the drop-downs are MENUS: the pick acts and the box empties again; the prompt says what is in force
    private async void Scenario_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox box || box.SelectedItem is not Choice chosen) return;
        box.SelectedIndex = -1;
        if (box.DataContext is not Golem g) return;
        if (chosen.Current) { Log($"{g.Name} › already in {chosen.Name}"); return; }
        await ActNowAsync(g, $"use map {chosen.Name}");
    }

    private async void Navigation_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox box || box.SelectedItem is not Choice chosen) return;
        box.SelectedIndex = -1;
        if (box.DataContext is not Golem g) return;
        if (chosen.Current) { Log($"{g.Name} › already goes {chosen.Name}"); return; }
        await ActNowAsync(g, $"optimize {chosen.Name.Replace(' ', '-')}");
    }

    private async void ResetNow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is Golem g) await ActNowAsync(g, "reset");
    }

    private async void ResetAllNow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not Golem g) return;
        var sure = MessageBox.Show(this, $"reset --all wipes {g.Name}'s journal: every route, every mark, every scenario it learned. Go on?", $"reset {g.Name} entirely", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (sure != MessageBoxResult.Yes) { Log($"{g.Name} › reset --all not sent"); return; }
        await ActNowAsync(g, "reset --all");
    }

    // one line to the golem now, its answer in the log, its reads asked again
    private async Task ActNowAsync(Golem g, string line)
    {
        var reply = await GolemClient.SendAsync(g, line);
        Log($"{g.Name} › {line} — {(reply.Ok ? "done" : reply.Kind)}: {OneLine(reply.Text)}");
        await GolemClient.RefreshReadingsAsync(g);
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
        var batch = new SyncBarrier(chosen.Count);
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


    // THE QUEUE RUNNER: the first line goes; a line that opens a route waits for the golem's pending routes to come back to zero — THE ACK,
    // said in the log with the time it took; a read or a lever goes on at once; @sync waits for the batch. Everything runs on the window's
    // thread, awaiting the wire, so the tab is edited live and never torn.
    private async Task RunQueueAsync(Golem golem, SyncBarrier batch)
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
                if (line == Sync)
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
            Log($"workspace opened: {path} — {loaded.Count} golem(s); the formations are the golems' own, read from each (propuesta 104)");
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
        if (Current is not { } g) { Log("select a golem: the formation is formed in it, and it tells its fleet"); return; }
        var dialog = new FormationDialog(golems.Select(x => x.Name).ToList(), Point) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        // THE FORMATION IS THE GOLEM'S (propuesta 104; Juan: "el CLI sólo servirá como interfaz"): two lines on the selected golem's tab — the
        // form, which the golem spreads to its fleet, and the take, which moves the whole fleet by the formation's policy; the name is the
        // figure's with the next number the golem does not know yet
        string shape = dialog.Figure == "double ring" ? "double-ring" : dialog.Figure;
        var known = Views().Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        int n = 1;
        while (known.Contains($"{shape}-{n}")) n++;
        string name = $"{shape}-{n}";
        string centre = $"{Fmt(dialog.CenterX)},{Fmt(dialog.CenterY)}";
        string size = dialog.Figure == "double ring" ? $"--radius {Fmt(dialog.Measure)} --inner-radius {Fmt(dialog.InnerRadius)}"
                    : dialog.Figure == "circle" ? $"--radius {Fmt(dialog.Measure)}" : $"--side {Fmt(dialog.Measure)}";
        string fleet = $"--fleet {string.Join(",", dialog.Chosen)}" + (dialog.Figure == "double ring" ? $" --inner-fleet {string.Join(",", dialog.InnerChosen)}" : "");
        g.Enqueue($"form {name} {shape} --center {centre} {size} --angle 0 {fleet} --by {dialog.Policy}");
        g.Enqueue($"take {name}");
        FormationsTab.IsSelected = true;
        Log($"{g.Name} › form {name} and take {name} on its tab — the golem tells its fleet; SEND to selected when ready");
    }

    // A STEP OF A FORMATION (propuesta 104; ajuste 77): the line `rotate <name> <sense> [--ring …]` WRITTEN on the selected golem's tab, sent
    // with SEND (9-oct-2026; Juan: "el botón de rotar deja preparado el comando para enviar al golem; luego de haberlo enviado el CLI le hace
    // get de la formación para ver cómo están distribuidos — no es el CLI quien dicta eso, lo dicta el objeto de la formación del golem"): the
    // console shows nothing of its own — the holders are the golem's `formations` read, asked again every 3 s and once the step's routes end;
    // the golem spreads the step and every copy queues it, which opens when everybody stands on its place. The whole figure, or one ring.
    private void RotateClockwise_Click(object sender, RoutedEventArgs e) => Rotate(sender, Sense.Clockwise, Ring.Whole);
    private void RotateCounter_Click(object sender, RoutedEventArgs e) => Rotate(sender, Sense.Counterclockwise, Ring.Whole);
    private void RotateOuterClockwise_Click(object sender, RoutedEventArgs e) => Rotate(sender, Sense.Clockwise, Ring.Outer);
    private void RotateOuterCounter_Click(object sender, RoutedEventArgs e) => Rotate(sender, Sense.Counterclockwise, Ring.Outer);
    private void RotateInnerClockwise_Click(object sender, RoutedEventArgs e) => Rotate(sender, Sense.Clockwise, Ring.Inner);
    private void RotateInnerCounter_Click(object sender, RoutedEventArgs e) => Rotate(sender, Sense.Counterclockwise, Ring.Inner);

    private void Rotate(object sender, Sense sense, Ring ring)
    {
        if ((sender as Button)?.Tag is not FormationView formation || Current is not { } g) return;
        string line = $"rotate {formation.Name} {(sense == Sense.Clockwise ? "clockwise" : "counterclockwise")}{(ring == Ring.Whole ? "" : $" --ring {ring.ToString().ToLowerInvariant()}")}";
        g.Enqueue(line);
        Log($"{g.Name} › {line} on its tab — SEND to selected when ready; the golem tells its fleet, and its formation says who holds what");
    }

    // the eye: the console's own way of looking, remembered by name across the reads
    private void Eye_Toggled(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FormationView f) return;
        if (f.Shown) hiddenFormations.Remove(f.Name); else hiddenFormations.Add(f.Name);
        Draw();
    }

    // THE OBSTACLES' TAB (8-oct-2026): a row under the cursor lit on the map; 'gone' forgets it at once — the golem's own lever, like the
    // environment's, never a line of the script
    private Obstacle? litObstacle;

    private void ObstacleRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Obstacle o) { litObstacle = o; Draw(); }
    }

    private void ObstacleRow_MouseLeave(object sender, MouseEventArgs e)
    {
        litObstacle = null;
        Draw();
    }

    private async void ForgetObstacle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not Obstacle o || Current is not { } g) return;
        litObstacle = null;
        await ActNowAsync(g, $"forget {o.Point}");
    }

    // EVERYTHING IT LEARNED BY TOUCHING, FORGOTTEN (Juan, 8-oct-2026: "permitir remover los obstáculos del golem"): one forget at each obstacle's
    // centre, in turn — the golem's own verb, so every peer is told too; asked first
    private async void ForgetAll_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } g) return;
        var all = g.Knowledge?.Obstacles?.ToList() ?? new List<Obstacle>();
        if (all.Count == 0) { Log($"{g.Name} › nothing to forget: it learned nothing by touching"); return; }
        var sure = MessageBox.Show(this, $"{g.Name} forgets its {all.Count} obstacle(s) — the things and the peers met — and tells its peers. Go on?", $"forget all of {g.Name}'s obstacles", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (sure != MessageBoxResult.Yes) return;
        litObstacle = null;
        foreach (var o in all) await ActNowAsync(g, $"forget {o.Point}");
        int left = g.Knowledge?.Obstacles?.Count ?? 0;
        Log(left == 0 ? $"{g.Name} › every obstacle forgotten" : $"{g.Name} › {left} obstacle(s) still there: two stood at one point, or one was learned meanwhile — forget all again");
    }

    private async void Dissolve_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not FormationView formation || Current is not { } g) return;
        await ActNowAsync(g, $"dissolve {formation.Name}");   // the golem lets it go and tells its fleet
    }

    // ==================================================================
    // The golem debugger: the window's right side, whole height, hidden and shown on demand (Juan, 7-oct-2026: "que ese panel use todo el
    // lateral derecho… y que se pueda ocultar y mostrar"); hidden, it leaves a strip at the right edge and the console takes the width
    // ==================================================================

    // the map as high as it is wide — the floor is square — so the foot sits right under it and keeps the height left; a short window takes
    // height from the map (never under 120), never from the foot's least (Juan, 7-oct-2026: "está muy abajo esta lista de points y formations")
    private void FitDebugger()
    {
        if (DebuggerBody.ActualHeight <= 0 || DebuggerBody.ActualWidth <= 0) return;
        double above = DebuggerTitle.ActualHeight + DebuggerTitle.Margin.Top + DebuggerTitle.Margin.Bottom
                     + ReadsBox.ActualHeight + ReadsBox.Margin.Top + ReadsBox.Margin.Bottom
                     + MapHeader.ActualHeight + MapHeader.Margin.Top + MapHeader.Margin.Bottom
                     + MapDivider.ActualHeight;
        double room = DebuggerBody.ActualHeight - above - DebuggerFoot.MinHeight - DebuggerFoot.Margin.Top;
        double side = Math.Max(120, Math.Min(mapHeight ?? DebuggerBody.ActualWidth, room));
        // the height the operator chose is kept as chosen: only what is drawn is cut to the room there is (a window opened small, then maximized)
        if (double.IsNaN(MapArea.Height) || Math.Abs(MapArea.Height - side) > 1) MapArea.Height = side;
    }

    // THE MAP'S OWN DIVIDER (Juan, 7-oct-2026: "poder hacer resize de las siguientes zonas"): dragged, the map takes the height the operator
    // gives it (still never under 120 nor over what leaves the tabs their least); double-clicked, it is as high as it is wide again
    private double? mapHeight;
    private void MapDivider_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        mapHeight = (double.IsNaN(MapArea.Height) ? MapArea.ActualHeight : MapArea.Height) + e.VerticalChange;
        FitDebugger();
    }

    private void MapDivider_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        mapHeight = null;
        FitDebugger();
    }

    private void HideDebugger_Click(object sender, RoutedEventArgs e) => SetDebugger(false);
    private void ShowDebugger_Click(object sender, MouseButtonEventArgs e) => SetDebugger(true);
    private void ToggleDebugger_Click(object sender, RoutedEventArgs e) => SetDebugger(DebuggerPanel.Visibility != Visibility.Visible);

    // the width the operator gave the debugger with the divider, kept while it is hidden and given back when it shows again
    private GridLength debuggerWidth = new(1, GridUnitType.Star);

    private void SetDebugger(bool shown)
    {
        bool wasShown = DebuggerPanel.Visibility == Visibility.Visible;
        if (wasShown && !shown) debuggerWidth = DebuggerColumn.Width;
        DebuggerPanel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        DebuggerStrip.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        ShellDivider.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        DebuggerColumn.MinWidth = shown ? 330 : 0;
        DebuggerColumn.Width = shown ? debuggerWidth : GridLength.Auto;
        if (shown) Draw();
    }

    // ==================================================================
    // The map
    // ==================================================================

    private void Map_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();
    // the box is born checked while the window is being built, before the map exists: draw only once loaded
    private void Others_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Draw(); }

    // PLACE ON THE MAP (Juan, 8-oct-2026: "un botón arriba del mapa que cuando se dé clic espere por la posición de donde desea ser ubicado en el
    // mapa el golem actual; si da clic a otra cosa se cancela"): the button arms it for the selected golem, the next click on the floor carries
    // its body there at once — `place x,y`, the golem's own lever, never a line of the script — and anything else disarms it
    private Golem? placing;

    private void PlaceOnMap_Click(object sender, RoutedEventArgs e)
    {
        if (placing != null) { Disarm("cancelled"); return; }
        if (Current is not { } g) { Log("select a golem: place carries the selected golem's body"); return; }
        placing = g;
        PlaceButton.Content = $"⌖ click where {g.Name} goes…";
        PlaceButton.BorderBrush = (Brush)FindResource("Accent");
        PlaceButton.Foreground = (Brush)FindResource("Accent");
        Map.Cursor = Cursors.Cross;
        Log($"{g.Name} › place: click the map where its body goes — a click anywhere else, or Esc, cancels");
    }

    private void Disarm(string? why)
    {
        if (placing is not { } g) return;
        placing = null;
        PlaceButton.Content = "⌖ place";
        PlaceButton.ClearValue(Control.BorderBrushProperty);
        PlaceButton.ClearValue(Control.ForegroundProperty);
        Map.ClearValue(CursorProperty);
        if (why != null) Log($"{g.Name} › place {why}");
    }

    private async Task PlaceAtAsync(Golem g, double x, double y) =>
        await ActNowAsync(g, $"place {Fmt(x)},{Fmt(y)}");

    // whether what was clicked lies inside that element
    private static bool IsWithin(object source, DependencyObject ancestor)
    {
        for (var d = source as DependencyObject; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }

    private void Map_Click(object sender, MouseButtonEventArgs e)
    {
        if (placing is { } who)
        {
            // armed: this click is where the golem goes, never a point of the tool
            e.Handled = true;
            var (px, py) = FloorPoint(e.GetPosition(Map));
            if (px < 0 || py < 0 || px > FloorSize || py > FloorSize) { Disarm("cancelled: off the floor"); return; }
            Disarm(null);
            _ = PlaceAtAsync(who, Math.Round(px, 1), Math.Round(py, 1));
            return;
        }
        if (OnGrip(e.GetPosition(Map)) is { } grabbed)
        {
            (dragging, grip) = grabbed;
            ghost = dragging.AsFigure();
            Map.CaptureMouse();
            e.Handled = true;
            Draw();
            return;
        }
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

    // THE GRIPS of the selected formation, projected (propuestas 97, 98): the ring at its centre moves it, the square on its first vertex
    // resizes it, the knob beyond that vertex turns it; null when the point is on none of them
    private (FormationView Formation, Grip Grip)? OnGrip(Point p)
    {
        if (FormationList.SelectedItem is not FormationView f || !f.Shown || f.Projected is not { } figure) return null;
        var (centre, size, knob) = Grips(figure, f.CrewCount);
        bool near(Point q, double r) => Math.Abs(p.X - q.X) <= r && Math.Abs(p.Y - q.Y) <= r;
        if (near(size, 8)) return (f, Grip.Size);
        if (near(knob, 9)) return (f, Grip.Turn);
        if (near(centre, 10)) return (f, Grip.Move);
        return null;
    }

    // where the grips of a figure stand on the map: its centre, its first vertex, and the knob 26 px beyond that vertex, away from the centre
    private (Point Centre, Point Size, Point Knob) Grips(Figure figure, int count)
    {
        var c = Pixel(figure.Center.X, figure.Center.Y);
        var first = figure.Places(count)[0];
        var v = Pixel(first.X, first.Y);
        double dx = v.X - c.X, dy = v.Y - c.Y, len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        return (c, v, new Point(v.X + dx / len * 26, v.Y + dy / len * 26));
    }

    // the figure the grip held would make of it with the cursor here: a centre to a tenth of a metre inside the floor, a measure to a tenth
    // of a metre (at least a tenth), an orientation to five degrees
    private Figure? Reshaped(FormationView f, Grip held, Point p)
    {
        var (x, y) = FloorPoint(p);
        if ((ghost ?? f.Projected) is not { } figure) return null;
        switch (held)
        {
            case Grip.Move:
                return figure.At(new Spot(Math.Round(Math.Clamp(x, 0, FloorSize), 1), Math.Round(Math.Clamp(y, 0, FloorSize), 1)));
            case Grip.Size:
                double reach = new Spot(x, y).DistanceTo(figure.Center);
                return figure.Sized(Math.Max(0.1, Math.Round(figure.MeasureFor(reach), 1)));
            default:
                double cursor = Math.Atan2(y - figure.Center.Y, x - figure.Center.X) * 180 / Math.PI;
                double laidOut = figure.Bearing(0, f.CrewCount) - figure.Angle;
                return figure.Oriented(Math.Round((cursor - laidOut) / 5) * 5);
        }
    }

    // what the figure became, in the operator's words: its centre, its measure, its orientation
    private static string Shape(Figure figure) => figure is DoubleRing rings
        ? $"at {figure.Center}, diameters {Spot.Fmt(rings.OuterDiameter)} and {Spot.Fmt(rings.InnerDiameter)} m, {Spot.Fmt(figure.Angle)}°"
        : $"at {figure.Center}, {(figure is Circle ? "radius" : "side")} {Spot.Fmt(figure.Measure)} m, {Spot.Fmt(figure.Angle)}°";

    // the formation let go: reshaped if its figure changed — every golem the visit to its own vertex on its tab
    private void Map_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (dragging is not { } formation) return;
        Map.ReleaseMouseCapture();
        var next = ghost;
        var held = grip;
        dragging = null;
        ghost = null;
        if (next != null && formation.Projected is { } was && Shape(next) != Shape(was)) Reshape(formation, held, next);
        Draw();
    }

    // the grip let go: the new figure is a DRAFT (9-oct-2026; Juan: "hasta estar seguros de los nuevos ajustes se envían al golem seleccionado") —
    // drawn over the golem's, kept by name; *shot* tells the formation AGAIN with it (propuesta 104: saying it again is making it again) to the
    // selected golem, which tells its fleet; ↶ drops it. A draft dropped or shot back to the golem's own figure is no draft.
    private void Reshape(FormationView formation, Grip held, Figure next)
    {
        string what = held switch { Grip.Move => "moved", Grip.Size => "resized", _ => "turned" };
        if (formation.AsFigure() is { } told && Shape(next) == Shape(told))
        {
            drafts.Remove(formation.Name);
            formation.Draft = null;
            Log($"{formation.Name} › back to {Shape(told)}, as the golem has it — no draft");
            return;
        }
        drafts[formation.Name] = next;
        formation.Draft = next;
        Log($"{formation.Name} › {what} to {Shape(next)} — a draft: shot tells it to {Current?.Name ?? "the golem"}, which tells its fleet; `take {formation.Name}` moves them");
    }

    // the draft SHOT (9-oct-2026; Juan: "una vez hecho el shot, ¿no deberían salir el script para lograr ese ajuste en el golem, para poder
    // enviárselo?"): the SCRIPT of the adjustment on the selected golem's tab, like the Formation dialog's — the `form` line with the drafted
    // figure (the same name, fleet and policy; the golem tells its fleet) and the `take` that moves the fleet to it — and nothing goes until SEND
    private void Shot_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not FormationView formation || formation.Draft is not { } next || Current is not { } g) return;
        drafts.Remove(formation.Name);
        formation.Draft = null;
        g.Enqueue(formation.FormLine(next));
        g.Enqueue($"take {formation.Name}");
        Log($"{g.Name} › shot: form {formation.Name} {Shape(next)} and take {formation.Name} on its tab — the golem tells its fleet; SEND to selected when ready");
        Draw();
    }

    // the draft dropped: the map shows the formation as the golem has it
    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not FormationView formation) return;
        drafts.Remove(formation.Name);
        formation.Draft = null;
        Log($"{formation.Name} › draft dropped");
        Draw();
    }

    // the coordinates under the cursor, shown while it moves over the floor and gone when it leaves (Juan, 6-oct-2026); while a formation is
    // held, its centre follows the cursor (to a tenth of a metre, inside the floor) and the map draws it there
    private void Map_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Map);
        if (dragging != null)
        {
            ghost = Reshaped(dragging, grip, p) ?? ghost;
            Draw();
        }
        var over = dragging != null ? grip : OnGrip(p)?.Grip;
        Map.Cursor = over switch { Grip.Move => Cursors.SizeAll, Grip.Size => Cursors.SizeNWSE, Grip.Turn => Cursors.Hand, _ => null };
        var (x, y) = FloorPoint(p);
        if (x < 0 || y < 0 || x > FloorSize || y > FloorSize) { Hover.Visibility = Visibility.Collapsed; return; }
        HoverText.Text = dragging != null && ghost != null && grip != Grip.Move
            ? (grip == Grip.Size ? $"{(ghost is Circle ? "radius" : "side")} {Spot.Fmt(ghost.Measure)} m" : $"{Spot.Fmt(ghost.Angle)}°")
            : $"x {x.ToString("0.0", CultureInfo.InvariantCulture)}  y {y.ToString("0.0", CultureInfo.InvariantCulture)}";
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
        MapScenario.Text = plan?.Name ?? "no scenario yet";
        MapNote.Text = Current == null ? "" : $"{Current.Name}'s view · obstacles in red";
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
        // the obstacle under the cursor in the obstacles' tab, lit on the map: a ring in the accent around its marks
        if (litObstacle is { } lit)
        {
            var accent = (Brush)FindResource("Accent");
            var points = lit.IsThing && lit.Vertices.Count > 0 ? lit.Vertices.Select(v => (v.X, v.Y, R: v.Reach)).ToList() : new List<(double X, double Y, double R)> { (lit.X, lit.Y, 0.25) };
            foreach (var (x, y, reach) in points)
            {
                var pl = Pixel(x, y); double rl = Math.Max(9, reach * scale + 6);
                var halo = new Ellipse { Width = 2 * rl, Height = 2 * rl, Stroke = accent, StrokeThickness = 2.5 };
                Canvas.SetLeft(halo, pl.X - rl); Canvas.SetTop(halo, pl.Y - rl); Map.Children.Add(halo);
            }
        }
        // THE GOLEM'S FORMATIONS PROJECTED (propuesta 96, read from the golem since propuesta 104; Juan, 7-oct-2026: "que se vea la forma de la figura encima del mapa para proyectar la
        // formación seleccionada… un botón como un ojo"): every formation whose eye is open shows its figure; the one selected in the list is drawn
        // strongest and carries the HANDLE at its centre that moves it (propuesta 97); while it is held, where it stands fades and the figure is
        // drawn where the cursor would put it, every vertex with its holder
        var chosen = FormationList.SelectedItem as FormationView;
        foreach (var f in Views().Where(f => f.Shown).OrderBy(f => f == chosen))
        {
            if (f.AsFigure() is not { } figure) continue;
            bool strong = f == chosen;
            if (f == dragging && ghost != null)
            {
                DrawFigure(f, figure, scale, strong: false, faint: true);
                DrawFigure(f, ghost, scale, strong: true, faint: false);
                DrawGrips(ghost, f.CrewCount);
            }
            else if (f.Draft is { } draft)
            {
                // a draft (9-oct-2026): the golem's own figure fades, the drafted one is drawn in its place until shot or dropped
                DrawFigure(f, figure, scale, strong: false, faint: true);
                DrawFigure(f, draft, scale, strong, faint: false);
                if (strong) DrawGrips(draft, f.CrewCount);
            }
            else
            {
                DrawFigure(f, figure, scale, strong, faint: false);
                if (strong) DrawGrips(figure, f.CrewCount);
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
            if (g != selected && OthersBox.IsChecked != true) continue;   // the selected golem is always drawn; the others when the box says so
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

    // one formation's figure on the map — the polygon through its vertices or the circle of its radius, filled faintly in purple, its centre
    // crossed, every vertex a diamond ringed in its holder's colour with the compass point and the holder's name ("free" when nobody holds it)
    private void DrawFigure(FormationView f, Figure figure, double scale, bool strong, bool faint)
    {
        var purple = ((SolidColorBrush)FindResource("Purple")).Color;
        var layer = new Canvas { Opacity = faint ? 0.3 : 1, IsHitTestVisible = false };
        Map.Children.Add(layer);
        var line = new SolidColorBrush(purple) { Opacity = strong ? 1 : 0.7 };
        var fill = new SolidColorBrush(Color.FromArgb(strong ? (byte)0x30 : (byte)0x14, purple.R, purple.G, purple.B));
        var dash = strong ? null : new DoubleCollection { 3, 3 };
        var vertices = figure.Places(f.CrewCount);
        var centre = Pixel(figure.Center.X, figure.Center.Y);
        if (figure is Circle circle)
        {
            double r = circle.Radius * scale;
            var ring = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = line, StrokeThickness = strong ? 2 : 1, StrokeDashArray = dash, Fill = fill };
            Canvas.SetLeft(ring, centre.X - r); Canvas.SetTop(ring, centre.Y - r); layer.Children.Add(ring);
        }
        else if (figure is DoubleRing rings)
        {
            // the two rings: the outer one filled faintly, the inner one drawn over it
            foreach (var (radius, filled) in new[] { (rings.OuterRadius, true), (rings.InnerRadius, false) })
            {
                double r = radius * scale;
                var ring = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = line, StrokeThickness = strong ? 2 : 1, StrokeDashArray = dash, Fill = filled ? fill : Brushes.Transparent };
                Canvas.SetLeft(ring, centre.X - r); Canvas.SetTop(ring, centre.Y - r); layer.Children.Add(ring);
            }
        }
        else if (vertices.Count >= 3)
        {
            var shape = new System.Windows.Shapes.Polygon { Stroke = line, StrokeThickness = strong ? 2 : 1, StrokeDashArray = dash, Fill = fill };
            foreach (var v in vertices) shape.Points.Add(Pixel(v.X, v.Y));
            layer.Children.Add(shape);
        }
        layer.Children.Add(new Line { X1 = centre.X - 5, Y1 = centre.Y, X2 = centre.X + 5, Y2 = centre.Y, Stroke = line, StrokeThickness = 1 });
        layer.Children.Add(new Line { X1 = centre.X, Y1 = centre.Y - 5, X2 = centre.X, Y2 = centre.Y + 5, Stroke = line, StrokeThickness = 1 });
        var tag = new TextBlock { Text = strong ? $"{f.Name} · {Shape(figure)}" : $"{f.Name} · {figure.Center}", Foreground = line, FontSize = strong ? 10 : 9, FontWeight = strong ? FontWeights.Bold : FontWeights.Normal };
        tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));   // under the centre, centred: clear of the vertices whatever the turn
        Canvas.SetLeft(tag, centre.X - tag.DesiredSize.Width / 2); Canvas.SetTop(tag, centre.Y + 11); layer.Children.Add(tag);
        for (int i = 0; i < vertices.Count; i++)
        {
            var pv = Pixel(vertices[i].X, vertices[i].Y);
            string? who = f.HolderOf(i);
            var holder = who == null ? line : new SolidColorBrush(GolemColor.Of(who));
            var diamond = new System.Windows.Shapes.Polygon { Stroke = holder, StrokeThickness = who == null ? 1 : 2, Fill = Brushes.Transparent, Opacity = who == null ? 0.6 : strong ? 1 : 0.8 };
            diamond.Points.Add(new Point(pv.X, pv.Y - 7)); diamond.Points.Add(new Point(pv.X + 7, pv.Y)); diamond.Points.Add(new Point(pv.X, pv.Y + 7)); diamond.Points.Add(new Point(pv.X - 7, pv.Y));
            layer.Children.Add(diamond);
            var label = new TextBlock { Text = figure.Label(i, vertices.Count) + (who == null ? " free" : " " + who), Foreground = holder, FontSize = 9, Opacity = who == null ? 0.7 : 1 };
            Canvas.SetLeft(label, pv.X + 8); Canvas.SetTop(label, pv.Y + 2); layer.Children.Add(label);
        }
    }

    // the grips of the selected formation: a ring at its centre (move), a square on its first vertex (resize), a knob beyond it on a thin
    // stem (turn); the cursor tells them apart — four arrows, the diagonal, the hand
    private void DrawGrips(Figure figure, int count)
    {
        var purple = ((SolidColorBrush)FindResource("Purple")).Color;
        var stroke = new SolidColorBrush(purple);
        var fill = new SolidColorBrush(Color.FromArgb(0x55, purple.R, purple.G, purple.B));
        var (c, v, k) = Grips(figure, count);
        var ring = new Ellipse { Width = 18, Height = 18, Stroke = stroke, StrokeThickness = 2, Fill = fill, ToolTip = "drag to move the formation" };
        Canvas.SetLeft(ring, c.X - 9); Canvas.SetTop(ring, c.Y - 9); Map.Children.Add(ring);
        Map.Children.Add(new Line { X1 = v.X, Y1 = v.Y, X2 = k.X, Y2 = k.Y, Stroke = stroke, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 2, 2 } });
        var size = new Rectangle { Width = 12, Height = 12, Stroke = stroke, StrokeThickness = 2, Fill = fill, ToolTip = "drag in or out to resize the figure" };
        Canvas.SetLeft(size, v.X - 6); Canvas.SetTop(size, v.Y - 6); Map.Children.Add(size);
        var knob = new Ellipse { Width = 14, Height = 14, Stroke = stroke, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(0xaa, purple.R, purple.G, purple.B)), ToolTip = "drag around the centre to turn the figure" };
        Canvas.SetLeft(knob, k.X - 7); Canvas.SetTop(knob, k.Y - 7); Map.Children.Add(knob);
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
