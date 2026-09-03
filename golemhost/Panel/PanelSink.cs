using Puppeteer;

namespace GolemHost.Panel;

// The panel is an output surface: it materializes the projections the golem's
// view reactions `print` (reactions guide, Scenario 3). Each PushDocument carries
// the journal entry that triggered it, the reaction's name (the fact) and the
// rendered projection — nothing here reads the journal's storage.
public sealed class PanelSink : IOutputSink
{
    private readonly PanelFeed feed;

    public PanelSink(PanelFeed feed)
    {
        this.feed = feed;
    }

    public void Push(in PushDocument document) =>
        feed.Broadcast(new PanelEvent(document.EntryId, "command",
            document.ReactionName, document.Document, document.OccurredAt));
}
