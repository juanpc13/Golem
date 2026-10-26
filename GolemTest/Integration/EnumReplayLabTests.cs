using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Panel;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;
using Puppeteer.EventSourcing;
using Puppeteer.EventSourcing.DB;
using Puppeteer.EventSourcing.Interpreter.Formatters;

namespace GolemTest;

// A LAB (23-oct-2026): on the live fleet every golem failed its rehydration at the `program` act of 22-oct —
// "Rehydration execution failed. EntryId=362, Cause=GolemDomainException: the beats by asking are not built yet: --link cue".
// The act passed live with `by = "Distance"` and `link = "Cue"` (two string parameters the engine resolves to the domain's enums
// Policy and Link at the verb, ajuste 90); on replay `Union..ctor` received `link == Link.Ask`. Acts with ONE enum parameter
// (`Form` with `@by`) have replayed well for weeks. This lab performs the role's own scripts verbatim against a FileSystem
// journal, reads the enums live, restarts the actor on the same journal and reads them again — and dumps the journal's own
// records (define + action) so the stored argument can be seen. Findings in lab/lab-enum-replay.txt (Spanish).
[TestClass]
public sealed class EnumReplayLabTests
{
    private sealed class Sink : IOutputSink
    {
        public readonly List<(string Reaction, string Document)> Pushed = new();
        public void Push(in PushDocument d) { lock (Pushed) Pushed.Add((d.ReactionName, d.Document)); }
    }

    // The engine's logger, captured: what the rehydration says is the evidence
    private sealed class LabLogger : IPuppeteerLogger
    {
        public readonly ConcurrentQueue<string> Errors = new();
        public void Error(string message, Exception exception) => Errors.Enqueue($"{message} | {exception.GetType().Name}: {exception.Message}");
        public void Debug(string message) { }
    }

    private const string Releases = @"
        upgrade('body_v1') {
            radius = Meters(0.25);
            speed = MetersPerSecond(2.0);
            linger = Seconds(6.0);
            retreat = Meters(0.6);
            body = Body(radius, speed, linger, retreat);
        }
    ";

    // ChoreographerRol.Program, verbatim (the Check and the act)
    private const string ProgramCheck = @"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                ";
    private const string ProgramAct = @"
                    {
                        fleet = Fleet(@names);
                        period = Seconds(@everySeconds);
                        union = g.Choreography.Program(@programName, @definition, fleet, @by, @link, period, @stamp);
                        me = fleet.Member(g);
                        from = g.Destination;
                        union.Convene(from, me);
                        print union.Name 'called', union.Bill 'formations', fleet.Roster 'candidates', union.Policy 'policy', union.Link 'link', union.StoodCount 'stood', fleet.Count 'of', union.IsKnown 'known', union.IsResolved 'resolved';
                        if (union.IsResolved) {
                            if (union.IsMember) {
                                mine = union.Mine;
                                print mine.Name 'mine', mine.HasRoute 'routed';
                                if (mine.HasRoute) {
                                    route = mine.Route;
                                    if (g.Strategy.OnTheWay.IsActive) {
                                        route = g.Dash(route);
                                    }
                                    if (route.IsPending()) {
                                        print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
                                        if (route.IsWalkable) {
                                            print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                                        }
                                    } else {
                                        print route.Id 'route', route.Status 'ended';
                                        if (route.EndedShort) {
                                            print route.Why 'why';
                                        }
                                    }
                                }
                            } else {
                                print union.IsLeftOut 'leftOut';
                            }
                        }
                        expose @programName call, union.Stamp nonce, me.Name who, from.X stoodX, from.Y stoodY;
                    }
                ";

    // ChoreographerRol.Form, verbatim (the Check and the act): ONE enum, @by — the control
    private const string FormCheck = @"
                    Check(g.Current.Map.IsOnMap(Position(@cx, @cy))) Error 'the centre is nowhere on the map';
                ";
    private const string FormAct = @"
                    {
                        center = Position(@cx, @cy);
                        measure = Meters(@measureLength);
                        turn = Degrees(@angle);
                        fleet = Fleet(@names);
                        formation = g.Choreography.Form(@formationName, @figure, center, measure, turn, fleet, @by, @stamp);
                        print formation.Name 'called', formation.Figure.Name 'shape', formation.Figure.Center.X 'atX', formation.Figure.Center.Y 'atY',
                              formation.Figure.Measure.InMeters 'length', formation.Figure.Turn.InDegrees 'degrees', formation.Policy 'policy',
                              fleet.Count 'crew', formation.PlaceCount 'count';
                        foreach (holders in formation.Holders()) {
                            print holders.Name 'who', holders.Index 'place', holders.X 'x', holders.Y 'y', holders.Orbit 'ring';
                        }
                    }
                ";

    private const string Definition = "tri-left: triangle 3,5.5 side 3 places 3 = cw; tri-right: triangle 8,5.5 side 3 places 3 = cw";
    private const string Six = "blue,cyan,green,orange,red,yellow";

    private static readonly object FileGate = new();
    private static string reportPath;

    [ClassInitialize]
    public static void OpenTheReport(TestContext _)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Golem.sln"))) dir = Path.GetDirectoryName(dir);
        string lab = Path.Combine(dir ?? AppContext.BaseDirectory, "lab");
        Directory.CreateDirectory(lab);
        reportPath = Path.Combine(lab, "lab-enum-replay.txt");
        // Appended, never truncated: the verdict written by hand at the top of the file survives a run of the lab (the raw evidence of a run follows it)
        File.AppendAllLines(reportPath, new[]
        {
            "",
            "################ CORRIDA DEL LAB — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " ################",
            "Motor: Ncubo.Puppeteer 3.0.0-local.586d2c9. Scripts: los de ChoreographerRol.Program y ChoreographerRol.Form, verbatim.",
            "Cada sección es un test de GolemTest/Integration/EnumReplayLabTests.cs; las líneas son hallazgos en el orden en que ocurrieron.",
            ""
        });
    }

    [TestInitialize]
    public void PinTheCulture() { CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; }

    private static void Report(string title, IEnumerable<string> findings)
    {
        lock (FileGate)
        {
            File.AppendAllLines(reportPath, new[] { "== " + title + " ==" }.Concat(findings).Concat(new[] { "" }));
        }
    }

    private static PerformanceV2 Build(string name, string folder, Sink sink, LabLogger logger)
    {
        var perf = new PerformanceV2(name, DomainLibrary.Assembly);
        perf.Actor.UseLogger(logger);
        perf.ConfigureStorage(DatabaseType.FileSystem, $"path={folder}");
        perf.OutputTarget(sink, new JsonFormatter());
        return perf;
    }

    private static void Release(PerformanceV2 perf, string golem) =>
        perf.Actor.Using(Releases + Catalog.OpenFloor().AsRelease() + "upgrade('init') { g = Golem(body, @golemName); scenario = g.Stage(openFloor); g.Enter(scenario); }")
            .WithParameters(p => { p["golemName", typeof(string)] = golem; })
            .PerformCommand();

    // The waking (GolemEmbodiment.Wake): the Program's Check asks g.KnowsWhereItStands and the act reads g.Destination
    private static void Wake(PerformanceV2 perf, double x, double y, double theta) =>
        perf.Actor.Using("{ me = Pose(@px, @py, @ptheta); g.Wake(me); }")
            .WithParameters(p => { p["px", typeof(double)] = x; p["py", typeof(double)] = y; p["ptheta", typeof(double)] = theta; })
            .PerformCommand();

    private static string Program(PerformanceV2 perf, string name, string definition, string by, string link, string stamp) =>
        perf.Actor.Using(ProgramCheck, ProgramAct).WithParameters(p =>
        {
            p["programName", typeof(string)] = name;
            p["definition", typeof(string)] = definition;
            p["names", typeof(string)] = Six;
            p["by", typeof(string)] = by;
            p["link", typeof(string)] = link;
            p["everySeconds", typeof(double)] = 5.0;
            p["stamp", typeof(string)] = stamp;
        }).PerformCheckThenCommand();

    private static string Form(PerformanceV2 perf, string name, string by, string stamp) =>
        perf.Actor.Using(FormCheck, FormAct).WithParameters(p =>
        {
            p["formationName", typeof(string)] = name;
            p["figure", typeof(string)] = "triangle";
            p["cx", typeof(double)] = 5.5;
            p["cy", typeof(double)] = 5.5;
            p["measureLength", typeof(double)] = 3.0;
            p["angle", typeof(double)] = 0.0;
            p["names", typeof(string)] = Six;
            p["by", typeof(string)] = by;
            p["stamp", typeof(string)] = stamp;
        }).PerformCheckThenCommand();

    private static string UnionRead(PerformanceV2 perf, string name) =>
        perf.Actor.Using("{ union = g.Choreography.FindUnion(@unionName); print union.Name 'called', union.Policy 'policy', union.Link 'link', union.EverySeconds 'every', union.Stamp 'stamp', union.Bill 'formations'; }")
            .WithParameters(p => { p["unionName", typeof(string)] = name; }).PerformQuery();

    private static string FormationRead(PerformanceV2 perf, string name) =>
        perf.Actor.Using("{ formation = g.Choreography.Find(@formationName); print formation.Name 'called', formation.Policy 'policy', formation.Stamp 'stamp', formation.Figure.Name 'shape'; }")
            .WithParameters(p => { p["formationName", typeof(string)] = name; }).PerformQuery();

    private static string Field(string json, string name)
    {
        try { return JsonDocument.Parse(json).RootElement.GetProperty(name).ToString(); }
        catch { return "(no '" + name + "' in: " + Cut(json, 200) + ")"; }
    }

    private static string Cut(string s, int max) => s == null ? "" : s.Length <= max ? s : s[..max] + "…";

    // The journal's own records, decoded with the host's peek (GolemAPI.Panel.JournalPeek): the define (the template with its typed parameters)
    // and every action (its argument list as the journal holds it). The releases and the waking are cut short.
    private static IEnumerable<string> JournalDump(PerformanceV2 perf)
    {
        var records = new List<JournalWireRecord>();
        new StageHook(perf.Actor).ReadJournalRecordsAfter(0, records);
        foreach (var r in records)
        {
            if (!JournalPeek.TryDescribe(r.Record, out var peek)) { yield return $"  #{r.EntryId} (record not readable)"; continue; }
            string text = JournalPeek.OneLine(peek.Text, 6000);
            bool ours = peek.Kind == "action" || text.Contains("Choreography.Program") || text.Contains("Choreography.Form(");
            string expose = peek.ExposeData != null ? " | expose=" + Cut(peek.ExposeData, 300) : "";
            yield return $"  #{peek.EntryId} {peek.Kind} actionId={peek.ActionId}: {(ours ? text : Cut(text, 110))}{expose}";
        }
    }

    private static (PerformanceV2 Perf, LabLogger Log, string StartError, string Suspension) Restart(string name, string folder, Sink sink)
    {
        var logger = new LabLogger();
        var perf = Build(name, folder, sink, logger);
        string startError = null;
        try { perf.Start(); }
        catch (Exception e) { startError = $"{e.GetType().Name}: {e.Message}"; }
        Thread.Sleep(300);   // the logger's queue drains on the pool
        string suspension = new StageHook(perf.Actor).TryGetReplaySuspension(out var s) ? $"{s.Kind} at #{s.EntryId}: {s.Message}" : null;
        return (perf, logger, startError, suspension);
    }

    private static string NewFolder(string tag)
    {
        string folder = Path.Combine(Path.GetTempPath(), "enum-replay-" + tag + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string TryRead(Func<string> read)
    {
        try { return read(); }
        catch (Exception e) { return $"REFUSED -> {e.GetType().Name}: {e.Message}"; }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // A — THE ACT OF THE FINDING: Program with by = Distance and link = Cue; replayed on a restart
    // ------------------------------------------------------------------------------------------------------------------
    [TestMethod]
    public void A_TheProgramAct_WithTwoEnums_DistanceAndCue_ReplaysWithTheSameLink()
    {
        var findings = new List<string>();
        string folder = NewFolder("program");
        const string name = "enum-replay-program";
        string liveLink, liveJson;
        try
        {
            {
                var perf = Build(name, folder, new Sink(), new LabLogger());
                perf.Start();
                Release(perf, "blue");
                Wake(perf, 5.5, 2.0, 1.5708);
                string answer;
                try { answer = Program(perf, "two-triangles", Definition, "Distance", "Cue", "stamp-blue-1"); }
                catch (Exception e) { findings.Add($"A1 program LIVE REFUSED -> {e.GetType().Name}: {e.Message}"); throw; }
                findings.Add("A1 program live (by=Distance, link=Cue) -> " + Cut(answer, 400));
                liveJson = UnionRead(perf, "two-triangles");
                liveLink = Field(liveJson, "link");
                findings.Add($"A2 read live -> policy={Field(liveJson, "policy")} link={liveLink} every={Field(liveJson, "every")} stamp={Field(liveJson, "stamp")}");
                findings.Add("A3 the journal as written (define + action):");
                findings.AddRange(JournalDump(perf));
                perf.Dispose();
            }
            {
                var (perf, log, startError, suspension) = Restart(name, folder, new Sink());
                try
                {
                    findings.Add("A4 restart -> Start " + (startError ?? "ok") + (suspension != null ? " | suspension: " + suspension : ""));
                    foreach (var e in log.Errors) findings.Add("A4 engine logged -> " + Cut(e, 700));
                    string json = TryRead(() => UnionRead(perf, "two-triangles"));
                    string link = Field(json, "link");
                    findings.Add($"A5 read after the restart -> policy={Field(json, "policy")} link={link} every={Field(json, "every")} stamp={Field(json, "stamp")}");
                    // A second Program on the restarted actor: the action cached FROM THE DEFINE, resolved on its first invocation here
                    string again = TryRead(() => Program(perf, "two-triangles-2", Definition, "Distance", "Cue", "stamp-blue-2"));
                    findings.Add("A6 program again on the restarted actor -> " + Cut(again, 300));
                    string json2 = TryRead(() => UnionRead(perf, "two-triangles-2"));
                    findings.Add($"A7 read the second -> policy={Field(json2, "policy")} link={Field(json2, "link")}");
                    Assert.IsNull(startError, "the restart should rehydrate: " + startError);
                    Assert.AreEqual(0, log.Errors.Count, "the engine logged a rehydration failure: " + string.Join(" || ", log.Errors));
                    Assert.AreEqual(liveLink, link, "the Link read after the restart differs from the live one");
                }
                finally { perf.Dispose(); }
            }
        }
        finally { Report("A — Program con dos enums (by=Distance, link=Cue): en vivo, el journal, el reinicio", findings); }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // B — CONTROL: Form with ONE enum (@by = Distance); replayed on a restart
    // ------------------------------------------------------------------------------------------------------------------
    [TestMethod]
    public void B_Control_TheFormAct_WithOneEnum_Distance_ReplaysWithTheSamePolicy()
    {
        var findings = new List<string>();
        string folder = NewFolder("form");
        const string name = "enum-replay-form";
        string livePolicy;
        try
        {
            {
                var perf = Build(name, folder, new Sink(), new LabLogger());
                perf.Start();
                Release(perf, "blue");
                Wake(perf, 5.5, 2.0, 1.5708);
                string answer = Form(perf, "tri-1", "Distance", "stamp-blue-1");
                findings.Add("B1 form live (by=Distance) -> " + Cut(answer, 300));
                string json = FormationRead(perf, "tri-1");
                livePolicy = Field(json, "policy");
                findings.Add($"B2 read live -> policy={livePolicy} stamp={Field(json, "stamp")}");
                findings.Add("B3 the journal as written (define + action):");
                findings.AddRange(JournalDump(perf));
                perf.Dispose();
            }
            {
                var (perf, log, startError, suspension) = Restart(name, folder, new Sink());
                try
                {
                    findings.Add("B4 restart -> Start " + (startError ?? "ok") + (suspension != null ? " | suspension: " + suspension : ""));
                    foreach (var e in log.Errors) findings.Add("B4 engine logged -> " + Cut(e, 700));
                    string json = TryRead(() => FormationRead(perf, "tri-1"));
                    findings.Add($"B5 read after the restart -> policy={Field(json, "policy")} stamp={Field(json, "stamp")}");
                    Assert.IsNull(startError, "the restart should rehydrate: " + startError);
                    Assert.AreEqual(0, log.Errors.Count, "the engine logged a rehydration failure: " + string.Join(" || ", log.Errors));
                    Assert.AreEqual(livePolicy, Field(json, "policy"));
                }
                finally { perf.Dispose(); }
            }
        }
        finally { Report("B — CONTROL: Form con un solo enum (by=Distance)", findings); }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // C — VARIANT: Program with link = Ask is refused live by the domain ("not built yet"); nothing of it must be journaled as an act
    // ------------------------------------------------------------------------------------------------------------------
    [TestMethod]
    public void C_TheProgramAct_WithLinkAsk_IsRefusedLive_AndWhatTheJournalKeepsOfIt_OnARestart()
    {
        var findings = new List<string>();
        string folder = NewFolder("ask");
        const string name = "enum-replay-ask";
        int actionsBefore;
        try
        {
            {
                var perf = Build(name, folder, new Sink(), new LabLogger());
                perf.Start();
                Release(perf, "blue");
                Wake(perf, 5.5, 2.0, 1.5708);
                string answer;
                try { answer = Program(perf, "two-triangles", Definition, "Distance", "Ask", "stamp-blue-1"); findings.Add("C1 program live (link=Ask) -> ACCEPTED?! " + Cut(answer, 300)); }
                catch (Exception e) { answer = null; findings.Add($"C1 program live (link=Ask) -> refused: {e.GetType().Name}: {e.Message}"); }
                string read = TryRead(() => UnionRead(perf, "two-triangles"));
                findings.Add("C2 FindUnion after the refusal -> " + Cut(read, 200));
                var dump = JournalDump(perf).ToList();
                actionsBefore = dump.Count(l => l.Contains(" action "));
                findings.Add($"C3 the journal after the refusal ({actionsBefore} actions): is the REFUSED act among them?");
                findings.AddRange(dump.Where(l => l.Contains(" action ") || l.Contains("Program")));
                // and a good Program after the refused one, as the fleet would go on
                findings.Add("C4 program again, live, link=Cue -> " + Cut(TryRead(() => Program(perf, "two-triangles", Definition, "Distance", "Cue", "stamp-blue-2")), 200));
                Assert.IsNull(answer, "the domain should have refused link = Ask");
                perf.Dispose();
            }
            {
                var (perf, log, startError, suspension) = Restart(name, folder, new Sink());
                try
                {
                    findings.Add("C5 restart on that journal -> Start " + (startError ?? "ok") + (suspension != null ? " | suspension: " + suspension : ""));
                    foreach (var e in log.Errors) findings.Add("C5 engine logged -> " + Cut(e, 400));
                    string json = TryRead(() => UnionRead(perf, "two-triangles"));
                    findings.Add($"C6 read after the restart -> policy={Field(json, "policy")} link={Field(json, "link")} stamp={Field(json, "stamp")} (the good one, stamp-blue-2, is what should be in force)");
                    findings.Add("C7 alive after the restart: " + new StageHook(perf.Actor).IsAlive + " — a query answers: " + Cut(TryRead(() => perf.Actor.Using("{ print g.Name 'who'; }").PerformQuery()), 100));
                }
                finally { perf.Dispose(); }
            }
        }
        finally { Report("C — Program con link=Ask en vivo: el rechazo del dominio, lo que el journal guarda de él y el reinicio sobre ese journal", findings); }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // E — THE FLEET'S OWN JOURNALS, READ OFFLINE: the archive of 23-oct (journal-legacy-20261023-semaforo) holds the journals that failed
    // their rehydration at EntryId 362. Decoded with the host's peek, file by file: what IS entry 362, and every `program` act's arguments.
    // ------------------------------------------------------------------------------------------------------------------
    [TestMethod]
    public void E_TheArchivedJournalsOfTheFleet_WhatEntry362Holds()
    {
        var findings = new List<string>();
        try
        {
            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "Golem.sln"))) root = Path.GetDirectoryName(root);
            string archive = Path.Combine(root ?? ".", "journal-legacy-20261023-semaforo");
            if (!Directory.Exists(archive)) { findings.Add("E0 no archive at " + archive); Assert.Inconclusive("no archive"); }
            foreach (var golemDir in Directory.GetDirectories(archive).OrderBy(d => d))
            {
                string golem = Path.GetFileName(golemDir);
                string journalDir = Path.Combine(golemDir, "journal");
                if (!Directory.Exists(journalDir)) continue;
                var defines = new Dictionary<int, string>();
                int entries = 0, programs = 0;
                foreach (var file in Directory.GetFiles(journalDir, "journal_*.bin").OrderBy(f => f))
                {
                    foreach (var (entryId, peek) in ReadJournalFile(file))
                    {
                        entries++;
                        if (peek == null) { findings.Add($"E {golem} #{entryId} not readable"); continue; }
                        string text = JournalPeek.OneLine(peek.Text, 6000);
                        if (peek.Kind == "define") defines[peek.ActionId] = text;
                        bool isProgramDefine = peek.Kind == "define" && text.Contains("Choreography.Program(");
                        bool isProgramAct = peek.Kind == "action" && defines.TryGetValue(peek.ActionId, out var d) && d.Contains("Choreography.Program(");
                        if (isProgramDefine) findings.Add($"E {golem} #{entryId} define actionId={peek.ActionId}: {Cut(text, 260)}");
                        if (isProgramAct) { programs++; findings.Add($"E {golem} #{entryId} action actionId={peek.ActionId}: {text}{(peek.ExposeData != null ? " | expose=" + Cut(peek.ExposeData, 200) : "")}"); }
                        if (entryId == 362 && !isProgramAct && !isProgramDefine) findings.Add($"E {golem} #362 is {peek.Kind} actionId={peek.ActionId}: {Cut(text, 300)}");
                    }
                }
                findings.Add($"E {golem}: {entries} entries read, {programs} program acts");
            }
        }
        finally { Report("E — Los journals archivados de la flota (journal-legacy-20261023-semaforo), leídos offline: la entrada 362 y los actos program", findings); }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // F — THE FLEET'S OWN JOURNAL, REHYDRATED: a copy of blue's archived journal (752 entries, the refused `--link ask` program at #362) is started
    // in the lab with the engine's logger captured — does the actor come alive, what does it log, and which `show-1` is in force after it?
    // ------------------------------------------------------------------------------------------------------------------
    [TestMethod]
    public void F_BluesArchivedJournal_Rehydrated_LogsTheRefusedActAndGoesOn()
    {
        var findings = new List<string>();
        try
        {
            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "Golem.sln"))) root = Path.GetDirectoryName(root);
            string archive = Path.Combine(root ?? ".", "journal-legacy-20261023-semaforo", "blue");
            if (!Directory.Exists(archive)) { findings.Add("F0 no archive at " + archive); Assert.Inconclusive("no archive"); }
            string folder = NewFolder("fleet-blue");
            CopyTree(archive, Path.Combine(folder, "blue"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (perf, log, startError, suspension) = Restart("blue", folder, new Sink());
            try
            {
                findings.Add($"F1 blue's journal rehydrated in {sw.ElapsedMilliseconds} ms -> Start {startError ?? "ok"}{(suspension != null ? " | suspension: " + suspension : "")}; alive: {new StageHook(perf.Actor).IsAlive}; entries: {new StageHook(perf.Actor).CurrentEntryId}");
                findings.Add($"F2 the engine logged {log.Errors.Count} error(s):");
                foreach (var e in log.Errors) findings.Add("   " + Cut(e.Replace("\r", "").Replace("\n", " "), 330));
                string json = TryRead(() => UnionRead(perf, "show-1"));
                findings.Add($"F3 show-1 in force after the replay -> policy={Field(json, "policy")} link={Field(json, "link")} every={Field(json, "every")} stamp={Field(json, "stamp")}");
                findings.Add("F4 the unions told, in order (name · stamp · link): " + Cut(TryRead(() => perf.Actor.Using("{ foreach (u in g.Choreography.Unions()) { print u.Name 'name', u.Stamp 'stamp', u.Link 'link'; } }").PerformQuery()), 900));
                Assert.IsNull(startError, "blue should come alive on its own journal: " + startError);
                Assert.AreEqual("Cue", Field(json, "link"), "the show-1 in force is the last one told with link=Cue");
            }
            finally { perf.Dispose(); }
        }
        finally { Report("F — El journal archivado de blue, rehidratado en el lab", findings); }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }

    // The FileSystem journal file: a 32-byte header, then [len:4][record] until the end (JournalWriter / JournalReader of the engine)
    private static IEnumerable<(long EntryId, JournalPeek.Peek Peek)> ReadJournalFile(string path)
    {
        byte[] all = File.ReadAllBytes(path);
        int pos = 32;
        while (pos + 4 <= all.Length)
        {
            int len = BitConverter.ToInt32(all, pos); pos += 4;
            if (len <= 0 || pos + len > all.Length) yield break;
            byte[] body = new byte[len];
            Buffer.BlockCopy(all, pos, body, 0, len); pos += len;
            long entryId = len >= 9 ? BitConverter.ToInt64(body, 1) : -1;
            yield return (entryId, JournalPeek.TryDescribe(body, out var peek) ? peek : null);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // D — LOCALIZING: two Programs in one journal, the second with by = RockPaperScissors (ordinal 2) — if the replay hands `link`
    // the ordinal of `by`, Link(2) is undefined and the domain says "not by '2'"; if it hands the NAME of `by`, "not by 'RockPaperScissors'"
    // ------------------------------------------------------------------------------------------------------------------
    [TestMethod]
    public void D_TwoPrograms_DistanceThenRockPaperScissors_ReplayBothWithTheirOwnLink()
    {
        var findings = new List<string>();
        string folder = NewFolder("two");
        const string name = "enum-replay-two";
        try
        {
            {
                var perf = Build(name, folder, new Sink(), new LabLogger());
                perf.Start();
                Release(perf, "blue");
                Wake(perf, 5.5, 2.0, 1.5708);
                findings.Add("D1 program a (by=Distance, link=Cue) -> " + Cut(Program(perf, "show-a", Definition, "Distance", "Cue", "stamp-a"), 200));
                findings.Add("D2 program b (by=RockPaperScissors, link=Cue) -> " + Cut(Program(perf, "show-b", Definition, "RockPaperScissors", "Cue", "stamp-b"), 200));
                string a = UnionRead(perf, "show-a"), b = UnionRead(perf, "show-b");
                findings.Add($"D3 read live -> a: policy={Field(a, "policy")} link={Field(a, "link")} | b: policy={Field(b, "policy")} link={Field(b, "link")}");
                findings.Add("D4 the journal's actions:");
                findings.AddRange(JournalDump(perf).Where(l => l.Contains(" action ")));
                perf.Dispose();
            }
            {
                var (perf, log, startError, suspension) = Restart(name, folder, new Sink());
                try
                {
                    findings.Add("D5 restart -> Start " + (startError ?? "ok") + (suspension != null ? " | suspension: " + suspension : ""));
                    foreach (var e in log.Errors) findings.Add("D5 engine logged -> " + Cut(e, 700));
                    string a = TryRead(() => UnionRead(perf, "show-a")), b = TryRead(() => UnionRead(perf, "show-b"));
                    findings.Add($"D6 read after the restart -> a: policy={Field(a, "policy")} link={Field(a, "link")} | b: policy={Field(b, "policy")} link={Field(b, "link")}");
                    Assert.IsNull(startError, "the restart should rehydrate: " + startError);
                    Assert.AreEqual(0, log.Errors.Count, "the engine logged a rehydration failure: " + string.Join(" || ", log.Errors));
                    Assert.AreEqual("Cue", Field(a, "link")); Assert.AreEqual("Cue", Field(b, "link"));
                }
                finally { perf.Dispose(); }
            }
        }
        finally { Report("D — Dos Program en un journal (Distance, luego RockPaperScissors), ambos link=Cue", findings); }
    }
}
