using Choreography.Transport.Brokered;

namespace GolemAPI.Membrane;

// THE WIRE THE TELLS TRAVEL (propuesta 52, fase 0, 23-sep-2026): a broker the engine's tell transport and told listener
// speak through, plus what the operator's levers ask of the peers directly (reset everything, in cascade). HttpBroker is
// the wire between containers; a scenario test gives the golems one in memory. An interface of the HOST, never of the domain.
public interface ITellWire : IMessageBroker
{
    /// <summary>Where the peers answer: every destination the wire can reach.</summary>
    IReadOnlyCollection<Uri> Peers { get; }
    /// <summary>A plain request to a peer's endpoint (an operator's lever passed on); true when the peer took it.</summary>
    Task<bool> AskPeerAsync(Uri peer, string relativePath, string json);
}
