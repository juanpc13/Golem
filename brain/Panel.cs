using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace GolemHost;

// One journal event as the panel sees it. Kind: "command" (journaled),
// "runtime" (ephemeral, never journaled), "info" (lifecycle).
public sealed record PanelEvent(long Entry, string Kind, string Script, string Note, DateTime At);

// The golem's operations panel: a tiny HTTP server (BCL HttpListener, no ASP.NET).
//   GET  /        -> panel page
//   GET  /state   -> snapshot JSON (entry, pending, total, pose)
//   GET  /events  -> SSE live feed of journal events (with recent history replay)
//   POST /assign  -> queue a mission (?x=..&y=..)
//
// The feed is emitted by the single journal writer at commit time — every event
// carries the entry id the journal reached after that command.
public sealed class ControlPanel
{
    private readonly HttpListener listener = new();
    private readonly List<StreamWriter> clients = new();
    private readonly List<PanelEvent> history = new();
    private readonly object gate = new();
    private readonly Func<double, double, PanelEvent> assign;
    private readonly Func<string> state;
    private readonly Func<PanelEvent> reset;
    private readonly Func<string, string> query;
    private readonly string pageHtml;

    public ControlPanel(int port, Func<double, double, PanelEvent> assign, Func<string> state,
                        Func<PanelEvent> reset, Func<string, string> query)
    {
        this.assign = assign;
        this.state = state;
        this.reset = reset;
        this.query = query;
        pageHtml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "panel.html"));
        listener.Prefixes.Add($"http://*:{port}/");
    }

    public void Start(CancellationToken ct)
    {
        listener.Start();
        Task.Run(() => AcceptLoopAsync(ct), CancellationToken.None);
    }

    public void Broadcast(PanelEvent e)
    {
        string frame = "data: " + JsonSerializer.Serialize(e) + "\n\n";
        lock (gate)
        {
            history.Add(e);
            if (history.Count > 300) history.RemoveAt(0);
            clients.RemoveAll(w =>
            {
                try { w.Write(frame); w.Flush(); return false; }
                catch { return true; } // client went away
            });
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => Handle(ctx), CancellationToken.None);
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            string path = ctx.Request.Url?.AbsolutePath ?? "/";

            if (path == "/" && ctx.Request.HttpMethod == "GET")
            {
                WriteText(ctx, "text/html; charset=utf-8", pageHtml);
            }
            else if (path == "/state" && ctx.Request.HttpMethod == "GET")
            {
                WriteText(ctx, "application/json", state());
            }
            else if (path == "/assign" && ctx.Request.HttpMethod == "POST")
            {
                double x = double.Parse(ctx.Request.QueryString["x"] ?? "", CultureInfo.InvariantCulture);
                double y = double.Parse(ctx.Request.QueryString["y"] ?? "", CultureInfo.InvariantCulture);
                var e = assign(x, y);
                WriteText(ctx, "application/json", JsonSerializer.Serialize(e));
            }
            else if (path == "/query" && ctx.Request.HttpMethod == "POST")
            {
                string script;
                using (var r = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    script = r.ReadToEnd();
                WriteText(ctx, "application/json", query(script));
            }
            else if (path == "/reset" && ctx.Request.HttpMethod == "POST")
            {
                // no keep-alive: the process exits right after, and a reused
                // connection dying mid-flight makes clients silently retry the POST
                ctx.Response.KeepAlive = false;
                var e = reset();
                WriteText(ctx, "application/json", JsonSerializer.Serialize(e));
            }
            else if (path == "/events" && ctx.Request.HttpMethod == "GET")
            {
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                ctx.Response.SendChunked = true;
                var w = new StreamWriter(ctx.Response.OutputStream, new UTF8Encoding(false));
                lock (gate)
                {
                    foreach (var e in history)
                        w.Write("data: " + JsonSerializer.Serialize(e) + "\n\n");
                    w.Flush();
                    clients.Add(w); // response stays open: this writer now lives in the hub
                }
            }
            else
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { }
        }
    }

    private static void WriteText(HttpListenerContext ctx, string contentType, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }
}
