using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Amateurlog
{
    class Graph<N, E> where N : IEquatable<N>
    {
        private ImmutableDictionary<N, ImmutableList<(E, N)>> _adjacencyList;

        private Graph(ImmutableDictionary<N, ImmutableList<(E, N)>> adjacencyList)
        {
            _adjacencyList = adjacencyList;
        }

        public Graph<N, E> AddNode(N node)
            => new Graph<N, E>(_adjacencyList.Add(node, ImmutableList<(E, N)>.Empty));

        public Graph<N, E> AddEdge(N sourceNode, E edge, N destinationNode)
        {
            if (!this.ContainsNode(sourceNode) || !this.ContainsNode(destinationNode))
            {
                throw new InvalidOperationException();
            }
            var newList = _adjacencyList[sourceNode].Add((edge, destinationNode));
            return new Graph<N, E>(_adjacencyList.SetItem(sourceNode, newList));
        }

        public bool ContainsNode(N node)
            => _adjacencyList.ContainsKey(node);
        
        public ImmutableList<(E, N)> GetEdges(N node)
            => _adjacencyList[node];


        public IEnumerable<ImmutableHashSet<N>> SCC()
        {
            // tarjan's algorithm

            var ids = new Dictionary<N, (int id, int ancestorId)>();
            var stack = new Stack<N>();
            var stackContents = new HashSet<N>();

            var result = new List<ImmutableHashSet<N>>();

            foreach (var n in _adjacencyList.Keys)
            {
                if (!ids.ContainsKey(n))
                {
                    Go(n);
                }
            }

            return result;

            void Go(N node)
            {
                ids[node] = (ids.Count, ids.Count);
                stack.Push(node);
                stackContents.Add(node);

                foreach (var (_, neighbour) in _adjacencyList[node])
                {
                    if (!ids.ContainsKey(neighbour))
                    {
                        // neighbour is unvisited
                        Go(neighbour);
                        SetAncestorId(node, ids[neighbour].ancestorId);
                    }
                    else if (stackContents.Contains(neighbour))
                    {
                        // node and neighbour are part of an SCC
                        SetAncestorId(node, ids[neighbour].id);
                    }
                    else
                    {
                        // we've already determined neighbour
                        // to be a member of some other SCC
                    }
                }
                var (id, ancestorId) = ids[node];
                if (ancestorId == id)
                {
                    // node is a root of an SCC
                    var thisSCC = ImmutableHashSet.CreateBuilder<N>();
                    while (!thisSCC.Contains(node))
                    {
                        var descendant = stack.Pop();
                        stackContents.Remove(descendant);
                        thisSCC.Add(descendant);
                    }
                    result.Add(thisSCC.ToImmutable());
                }
            }

            void SetAncestorId(N node, int candidateAncestor)
            {
                var (id, originalAncestor) = ids[node];
                ids[node] = (id, Math.Min(candidateAncestor, originalAncestor));
            }
        }

        public static Graph<N, E> Empty { get; }
            = new Graph<N, E>(ImmutableDictionary<N, ImmutableList<(E, N)>>.Empty);

        public static Graph<N, E> FromNodes(IEnumerable<N> nodes)
            => new Graph<N, E>(
                nodes.ToImmutableDictionary(
                    n => n,
                    _ => ImmutableList<(E, N)>.Empty
                )
            );
    }
}