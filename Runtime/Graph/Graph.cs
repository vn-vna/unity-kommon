using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Graph
{
    public class Graph<T>
        where T : class
    {
        public LinkedList<GraphNode<T>> Nodes;

        public Graph()
        {
            Nodes = new LinkedList<GraphNode<T>>();
        }

        public void AddNode(GraphNode<T> node)
        {
            if (node == null) return;
            if (Nodes.Contains(node)) return;

            Nodes.AddLast(node);
        }

        public void RemoveNode(GraphNode<T> node)
        {
            if (node == null) return;
            if (!Nodes.Contains(node)) return;

            foreach (var child in node.Children)
            {
                child.Parents.Remove(node);
            }

            foreach (var parent in node.Parents)
            {
                parent.Children.Remove(node);
            }

            Nodes.Remove(node);
        }
    }
}
