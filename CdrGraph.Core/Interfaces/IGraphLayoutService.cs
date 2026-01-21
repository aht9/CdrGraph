using CdrGraph.Core.Domain.Models;

namespace CdrGraph.Core.Interfaces;

public interface IGraphLayoutService
{
    /// <summary>
    /// Calculates positions (X, Y) for all nodes using a force-directed algorithm.
    /// </summary>
    Task ApplyLayoutAsync(List<GraphNode> nodes, List<GraphEdge> edges, CancellationToken cancellationToken = default);
}