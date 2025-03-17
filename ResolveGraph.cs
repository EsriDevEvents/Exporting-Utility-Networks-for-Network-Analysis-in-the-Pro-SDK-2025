using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Desktop.Framework.Dialogs;

namespace NetworkAnalysis
{
  public class GraphSolver
  {
    // This method creates a network networkGraph based on connectivity 
    public static INetworkGraph CreateNetworkGraph(Dictionary<string, List<string>> connectivityResults, bool directed)
    {
      INetworkGraph networkGraph = null;

      try
      {
        List<dynamic> nodes = new List<dynamic>();
        List<(dynamic node, dynamic link)> edges = new List<(dynamic, dynamic)>();

        foreach (KeyValuePair<string, List<string>> kvp in connectivityResults)
        {
          dynamic node = kvp.Key.ToString();
          dynamic links = kvp.Value;
          if (!nodes.Contains(node))
          {
            nodes.Add(node);
          }

          if (links != null)
          {
            foreach (dynamic link in links)
            {
              nodes.Add(link.ToString());
              edges.Add((node.ToString(), link.ToString()));
            }
          }
        }

        if (directed)
        {
          networkGraph = new DirectedNetworkGraph();
        }
        else
        {
          networkGraph = new NetworkGraph();
        }

        networkGraph.AddNodesFrom(nodes);
        networkGraph.AddEdgesFrom(edges);
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.Message);
      }


      return networkGraph;
    }

    public static dynamic GetEdgeElement(INetworkGraph networkGraph, dynamic edgeAndPercent)
    {
      dynamic[] incomingInfo = edgeAndPercent.Split('$');
      dynamic incomingFeature = $"{incomingInfo[0]}${incomingInfo[1]}";
      double percentAlong = double.Parse(incomingInfo[2]);

      int a = 0;
      int b = 0;
      int c = 0;
      foreach (KeyValuePair<dynamic, Dictionary<dynamic, dynamic>> kvp in networkGraph.Nodes)
      {
        dynamic key = kvp.Key;
        // In the original code, node_info is not used.
        dynamic[] keyInfo = key.Split('$');
        dynamic edgeFeature = $"{keyInfo[0]}${keyInfo[1]}";
        if (edgeFeature != incomingFeature)
        {
          a += 1;
          continue;
        }

        if (keyInfo.Length < 3)
        {
          b += 1;
          continue;
        }

        c += 1;
        double fromPosition = double.Parse(keyInfo[2]);
        double toPosition = double.Parse(keyInfo[3]);
        if (percentAlong >= fromPosition && percentAlong <= toPosition)
        {
          return key;
        }
      }

      return null;
    }

    public static List<dynamic> AllNodes(INetworkGraph networkGraph)
    {
      List<dynamic> results = new List<dynamic>();
      foreach (dynamic node in networkGraph.Nodes.Keys)
      {
        results.Add(node);
      }

      return results;
    }

    public static List<string> DownstreamTrace(INetworkGraph networkGraph, IEnumerable<string> startingElements)
    {
      List<string> results = new List<string>(startingElements);
      List<string> pending = new List<string>(results);
      HashSet<string> visited = new HashSet<string>(results);
      while (pending.Count > 0)
      {
        dynamic node = pending[pending.Count - 1];
        pending.RemoveAt(pending.Count - 1);
        foreach (dynamic next in networkGraph?.Successors(node) ?? Enumerable.Empty<dynamic>())
        {
          if (!results.Contains(next))
          {
            results.Add(next);
          }

          if (!visited.Contains(next))
          {
            pending.Add(next);
            // Preserving original functionality: adding _this instead of next.
            visited.Add(node);
          }
        }
      }

      return results;
    }

    public static List<dynamic> UpstreamTrace(INetworkGraph networkGraph, IEnumerable<dynamic> startingElements)
    {
      List<dynamic> results = new List<dynamic>(startingElements);
      List<dynamic> pending = new List<dynamic>(results);
      HashSet<dynamic> visited = new HashSet<dynamic>(results);
      while (pending.Count > 0)
      {
        dynamic node = pending[pending.Count - 1];
        pending.RemoveAt(pending.Count - 1);
        foreach (dynamic item in networkGraph?.Predecessors(node) ?? Enumerable.Empty<dynamic>())
        {
          if (!results.Contains(item))
          {
            results.Add(item);
          }

          if (!visited.Contains(item))
          {
            pending.Add(item);
            // Preserving original functionality: adding _this instead of next.
            visited.Add(node);
          }
        }
      }

      return results;
    }

    private static List<dynamic> ShortestPath(INetworkGraph networkGraph, dynamic source, dynamic target)
    {
      // Dictionary to track predecessors in the path.
      Dictionary<dynamic, dynamic> pred = new Dictionary<dynamic, dynamic>();
      Queue<dynamic> queue = new Queue<dynamic>();
      HashSet<dynamic> visited = new HashSet<dynamic>();

      queue.Enqueue(source);
      visited.Add(source);

      bool found = false;
      while (queue.Count > 0)
      {
        dynamic current = queue.Dequeue();
        if (current == target)
        {
          found = true;
          break;
        }

        foreach (dynamic neighbor in networkGraph.Successors(current))
        {
          if (!visited.Contains(neighbor))
          {
            visited.Add(neighbor);
            pred[neighbor] = current;
            queue.Enqueue(neighbor);
          }
        }
      }

      if (!found)
      {
        throw new Exception("No path found");
      }

      List<dynamic> path = new List<dynamic>();
      dynamic temp = target;
      while (temp != source)
      {
        path.Add(temp);
        temp = pred[temp];
      }

      path.Add(source);
      path.Reverse();
      return path;
    }

    public static List<(string start, List<string> result, List<string> barriers)> ForwardStar(IEnumerable<string> startingElements, INetworkGraph networkGraph,
      List<string> barrierInfo, bool withReplacement = false)
    {
      List<(string start, List<string> result, List<string> barriers)> results = new List<(string start, List<string> result, List<string> barriers)>();
      HashSet<string> globalVisited = new HashSet<string>();

      foreach (string start in startingElements)
      {
        if (!withReplacement && globalVisited.Contains(start))
        {
          continue;
        }

        HashSet<string> visited = new HashSet<string>();
        List<string> barriers = new List<string>();
        List<string> result = new List<string>();

        List<dynamic> pending = new List<dynamic>();
        pending.Add(start);
        visited.Add(start);
        if (!withReplacement)
        {
          globalVisited.Add(start);
        }

        while (pending.Count > 0)
        {
          dynamic value = pending[pending.Count - 1];
          pending.RemoveAt(pending.Count - 1);

          // Intentionally do not include barriers in results, since they can easily be added after-the-fact.
          // This also allows developers control over which barriers to include in their results (open/closed device)
          // and which barriers to exclude from results (proposed features).

          bool isBarrier = CheckBarrier(value, barrierInfo);

          if (isBarrier)
          {
            barriers.Add(value);
            if (!visited.Contains(value))
            {
              visited.Add(value);
              if (!withReplacement)
              {
                globalVisited.Add(value);
              }
            }

            continue;
          }

          // Add this to the result
          result.Add(value);

          List<dynamic> neighbors = networkGraph.GetNeighbors(value);
          foreach (dynamic neighbor in neighbors)
          {
            if (visited.Contains(neighbor))
            {
              continue;
            }

            pending.Add(neighbor);
            // Add all neighbors to visited list so we don't visit neighbors already on the stack                    
            visited.Add(neighbor);
            if (!withReplacement)
            {
              globalVisited.Add(neighbor);
            }
          }
        }

        result.Remove(start);

        results.Add((start, result, barriers));
      }
      return results;
    }

    private static bool CheckBarrier(string node, List<string> barrierInfo)
    {
      if (!string.IsNullOrEmpty(node))
      {
        string[] nodeValues = node.Split(',');
        string sourceID = nodeValues[0];
        string globalID = nodeValues[1];
        string sourceGlobalIDPair = $"{sourceID},{globalID})";

        return barrierInfo.Contains(sourceGlobalIDPair);
      }

      return false;
    }
  }


  // Interface defining the minimal networkGraph behavior.
  public interface INetworkGraph
  {
    Dictionary<dynamic, Dictionary<dynamic, dynamic>> Nodes { get; }
    bool IsDirected { get; }
    List<dynamic> GetNeighbors(dynamic node);
    void AddNodesFrom(IEnumerable<dynamic> nodes);
    void AddEdgesFrom(IEnumerable<(dynamic, dynamic)> edges);
    List<dynamic> Successors(dynamic node);
    List<dynamic> Predecessors(dynamic node);
  }

  // Undirected networkGraph implementation.
  public class NetworkGraph : INetworkGraph
  {
    public Dictionary<dynamic, Dictionary<dynamic, dynamic>> Nodes { get; private set; } = new();

    // Adjacency list for neighbors.
    protected Dictionary<dynamic, List<dynamic>> Adj = new();
    public bool IsDirected { get; protected set; } = false;

    // Indexer to mimic networkGraph[this] functionality.
    public List<dynamic> this[dynamic node]
    {
      get
      {
        if (Adj.ContainsKey(node))
        {
          return Adj[node];
        }
        else
        {
          return new List<dynamic>();
        }
      }
    }

    public List<dynamic> GetNeighbors(dynamic node)
    {
      return this[node];
    }

    public virtual void AddNodesFrom(IEnumerable<dynamic> nodes)
    {
      foreach (dynamic node in nodes)
      {
        if (!Nodes.ContainsKey(node))
        {
          Nodes[node] = new Dictionary<dynamic, dynamic>();
          if (!Adj.ContainsKey(node))
          {
            Adj[node] = new List<dynamic>();
          }
        }
      }
    }


    public virtual void AddEdgesFrom(IEnumerable<(dynamic, dynamic)> edges)
    {
      foreach ((dynamic, dynamic) edge in edges)
      {
        dynamic u = edge.Item1;
        dynamic v = edge.Item2;

        if (!Nodes.ContainsKey(u))
        {
          Nodes[u] = new Dictionary<dynamic, dynamic>();
          if (!Adj.ContainsKey(u))
          {
            Adj[u] = new List<dynamic>();
          }
        }

        if (!Nodes.ContainsKey(v))
        {
          Nodes[v] = new Dictionary<dynamic, dynamic>();
          if (!Adj.ContainsKey(v))
          {
            Adj[v] = new List<dynamic>();
          }
        }

        // Add edge u -> v
        Adj[u].Add(v);
        // For undirected graphs add edge v -> u.
        if (!IsDirected)
        {
          Adj[v].Add(u);
        }
      }
    }

    public virtual List<dynamic> Successors(dynamic node)
    {
      if (Adj.ContainsKey(node))
      {
        return Adj[node];
      }

      return new List<dynamic>();
    }

    // For undirected graphs, predecessors are the same as neighbors.
    public virtual List<dynamic> Predecessors(dynamic node)
    {
      return Successors(node);
    }
  }

  public class DirectedNetworkGraph : NetworkGraph
  {
    // Reverse adjacency list for directed networkGraph.
    protected Dictionary<dynamic, List<dynamic>> RevAdj;

    public DirectedNetworkGraph() : base()
    {
      IsDirected = true;
      RevAdj = new Dictionary<dynamic, List<dynamic>>();
    }

    public override void AddNodesFrom(IEnumerable<dynamic> nodes)
    {
      foreach (dynamic node in nodes)
      {
        if (!Nodes.ContainsKey(node))
        {
          Nodes[node] = new Dictionary<dynamic, dynamic>();
          if (!Adj.ContainsKey(node))
          {
            Adj[node] = new List<dynamic>();
          }

          if (!RevAdj.ContainsKey(node))
          {
            RevAdj[node] = new List<dynamic>();
          }
        }
      }
    }

    public override void AddEdgesFrom(IEnumerable<(dynamic, dynamic)> edges)
    {
      foreach ((dynamic, dynamic) edge in edges)
      {
        dynamic u = edge.Item1;
        dynamic v = edge.Item2;

        if (!Nodes.ContainsKey(u))
        {
          Nodes[u] = new Dictionary<dynamic, dynamic>();
          if (!Adj.ContainsKey(u))
          {
            Adj[u] = new List<dynamic>();
          }

          if (!RevAdj.ContainsKey(u))
          {
            RevAdj[u] = new List<dynamic>();
          }
        }

        if (!Nodes.ContainsKey(v))
        {
          Nodes[v] = new Dictionary<dynamic, dynamic>();
          if (!Adj.ContainsKey(v))
          {
            Adj[v] = new List<dynamic>();
          }

          if (!RevAdj.ContainsKey(v))
          {
            RevAdj[v] = new List<dynamic>();
          }
        }

        // Add edge u -> v
        Adj[u].Add(v);
        // For directed networkGraph, update reverse adjacency.
        RevAdj[v].Add(u);
      }
    }

    public override List<dynamic> Successors(dynamic node)
    {
      if (Adj.ContainsKey(node))
      {
        return Adj[node];
      }

      return new List<dynamic>();
    }

    public override List<dynamic> Predecessors(dynamic node)
    {
      if (RevAdj.ContainsKey(node))
      {
        return RevAdj[node];
      }

      return new List<dynamic>();
    }
  }
}

