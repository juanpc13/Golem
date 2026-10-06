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
using Microsoft.Win32;

namespace GolemCli;

/// <summary>The console's one window. The RIBBON composes commands on the current golem's tab; a tab's script is that golem's QUEUE,
/// sent one command at a time — the next goes when the golem finished the one before (its pending routes back to zero); the golems are
/// boxes in their colour, checked to be addressed together; the coordinates box builds a command the operator adds here, to the selected
/// golems, or copies; the map shows each golem where it was last asked; the journal of the current golem streams live. The window decides
/// nothing for the golem: it carries lines.</summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<Golem> golems = new();
    private readonly Dictionary<string, CancellationTokenSource> runners = new();
    private CancellationTokenSource? journalFollow;
    private string? workspace;

    // the floor the map draws: the arena's interior, 11 × 11 m, as the open-floor map of the domain
    private const double FloorSize = 11.0;

    // the verbs whose command leaves a route in the golem: the queue waits for it to end before the next line goes
    private static readonly HashSet<string> Movers = new(StringComparer.Ordinal) { "visit", "cover", "then", "resume", "choreograph", "rotate" };

    public MainWindow()
    {
        InitializeComponent();
        GolemList.ItemsSource = golems;
        golems.CollectionChanged += (_, _) => { Draw(); NoGolem.Visibility = golems.Count == 0 ? Visibility.Visible : Visibility.Collapsed; };
        Log("GolemCli ready — add the golems in operation, compose a script per tab, SEND.");
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
        golem.PropertyChanged += (_, args) => { if (args.PropertyName is nameof(Golem.LastX) or nameof(Golem.LastY) or nameof(Golem.LastHeading)) Draw(); };
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
        await GolemClient.RefreshWhereAsync(golem);
        Log($"{golem.Name} › {golem.Status} · {golem.LastSeen}");
    }

    private void GolemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Draw();
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
        g.Enqueue(template.Replace("{point}", Point));
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
    // Sending: a golem's queue, one command at a time; the checked ones; everybody
    // ==================================================================

    private void SendOne_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is Golem g) _ = RunQueueAsync(g);
    }

    private void StopOne_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is Golem g) Stop(g);
    }

    private void SendSelected_Click(object sender, RoutedEventArgs e)
    {
        var chosen = SelectedGolems.ToList();
        if (chosen.Count == 0) { Log("no golem is checked"); return; }
        foreach (var g in chosen) _ = RunQueueAsync(g);
    }

    private void SendAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var g in golems.ToList()) _ = RunQueueAsync(g);
    }

    private void Stop(Golem golem)
    {
        if (runners.Remove(golem.Name, out var cts)) { cts.Cancel(); Log($"{golem.Name} › sending stopped; the rest of the queue stays on the tab"); }
    }

    // THE QUEUE RUNNER: the first line goes; a line that opens a route waits for the golem's pending routes to come back to zero; a read or
    // a lever goes on at once. Everything runs on the window's thread, awaiting the wire, so the tab is edited live and never torn.
    private async Task RunQueueAsync(Golem golem)
    {
        if (golem.Running) return;
        var cts = new CancellationTokenSource();
        runners[golem.Name] = cts;
        golem.Running = true;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                string? line = golem.Dequeue();
                if (line == null) { Log($"{golem.Name} › queue done"); break; }
                var reply = await GolemClient.SendAsync(golem, line, cts.Token);
                golem.Sent++;
                Log($"{golem.Name} › {line} — {reply.Kind}: {OneLine(reply.Text)}");
                if (!reply.Ok)
                {
                    if (reply.Kind == "unreachable") { golem.Status = "unreachable"; Log($"{golem.Name} › stopped: the golem does not answer; the rest of the queue stays"); break; }
                    continue;   // refused or no command: the golem said why; the queue goes on
                }
                string verb = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (Movers.Contains(verb)) await WaitUntilDoneAsync(golem, cts.Token);
                await GolemClient.RefreshWhereAsync(golem, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            golem.Running = false;
            runners.Remove(golem.Name);
            await GolemClient.RefreshWhereAsync(golem);
        }
    }

    // the golem finished when nothing is pending; a route may take a moment to open (a choreography waits for the fleet's words), so a
    // short grace is given for it to appear before "nothing pending" counts as done
    private async Task WaitUntilDoneAsync(Golem golem, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        bool seenPending = false;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            int? pending = await GolemClient.PendingAsync(golem, ct);
            if (pending == null) { golem.Status = "unreachable"; return; }
            if (pending > 0) { seenPending = true; golem.Status = $"busy: {pending} route(s) pending"; continue; }
            if (seenPending || (DateTime.UtcNow - started).TotalSeconds > 8) return;
        }
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
        Title = "GolemCli — the operator's console";
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
            Title = $"GolemCli — {System.IO.Path.GetFileNameWithoutExtension(path)}";
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
        Title = $"GolemCli — {System.IO.Path.GetFileNameWithoutExtension(workspace)}";
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
            var queues = Formations.Queues(dialog.Figure, dialog.CenterX, dialog.CenterY, dialog.Side, dialog.Chosen, dialog.Steps, dialog.Clockwise);
            foreach (var (name, lines) in queues)
            {
                var g = golems.First(x => x.Name == name);
                g.Enqueue($"# {dialog.Figure} at ({dialog.CenterX}, {dialog.CenterY}), {dialog.Steps} step(s) {(dialog.Clockwise ? "clockwise" : "counter-clockwise")} — laid out by the console");
                foreach (var line in lines) g.Enqueue(line);
            }
            Log($"{dialog.Figure} laid out for {dialog.Chosen.Count} golem(s): {queues.Values.Sum(q => q.Count)} visit(s) queued — read the tabs, then SEND");
        }
        catch (ArgumentException ex) { Log(ex.Message); }
    }

    // ==================================================================
    // The map
    // ==================================================================

    private void Map_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();

    private void Map_Click(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Map);
        double scale = Scale();
        double x = Math.Round(p.X / scale, 1), y = Math.Round((FloorSize * scale - p.Y) / scale, 1);
        if (x < 0 || y < 0 || x > FloorSize || y > FloorSize) return;
        picked.Add((x, y));
        ShowLastPoint();
        Point_Changed(this, null);
        Draw();
    }

    private double Scale() => Math.Max(1, Math.Min(Map.ActualWidth, Map.ActualHeight) / FloorSize);

    // the coordinates under the cursor, shown while it moves over the floor and gone when it leaves (Juan, 6-oct-2026)
    private void Map_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Map);
        double scale = Scale();
        double x = p.X / scale, y = (FloorSize * scale - p.Y) / scale;
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
        for (int i = 0; i <= FloorSize; i++)
        {
            Map.Children.Add(new Line { X1 = i * scale, Y1 = 0, X2 = i * scale, Y2 = FloorSize * scale, Stroke = grid, StrokeThickness = i % 5 == 0 ? 1.2 : 0.5 });
            Map.Children.Add(new Line { X1 = 0, Y1 = i * scale, X2 = FloorSize * scale, Y2 = i * scale, Stroke = grid, StrokeThickness = i % 5 == 0 ? 1.2 : 0.5 });
        }
        // the points picked: the way the errand will go, numbered, joined by a dashed line
        var way = new SolidColorBrush(Color.FromRgb(0xff, 0xb4, 0x54));
        for (int i = 0; i < picked.Count; i++)
        {
            double px = picked[i].X * scale, py = (FloorSize - picked[i].Y) * scale;
            if (i > 0 && ManyPoints)
            {
                double qx = picked[i - 1].X * scale, qy = (FloorSize - picked[i - 1].Y) * scale;
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
            double cx = g.LastX.Value * scale, cy = (FloorSize - g.LastY.Value) * scale, r = 0.25 * scale;
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
        MessageBox.Show(this, "GolemCli — the operator's console for the golems.\n\nThe ribbon composes commands on each golem's tab; a tab's script is its queue, sent one command at a time, the next when the golem finished the one before. Check golems to address them together. Workspaces and routines are files. Formations laid out here are queues of visits; the golem's own choreography module stays apart.", "About GolemCli");

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
