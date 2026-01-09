using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Graph
{
    /// <summary>
    /// Represents a directed graph data structure with nodes containing values of type T.
    /// </summary>
    /// <typeparam name="T">The type of value stored in graph nodes, must be a reference type.</typeparam>
    /// <remarks>
    /// This class provides a graph structure where nodes can have parent-child relationships.
    /// Nodes can be added and removed while maintaining the integrity of these relationships.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create a graph of strings
    /// var graph = new Graph&lt;string&gt;();
    /// 
    /// // Create nodes
    /// var node1 = new GraphNode&lt;string&gt;("Node 1");
    /// var node2 = new GraphNode&lt;string&gt;("Node 2");
    /// 
    /// // Add child relationship
    /// node1.AddChild(node2);
    /// 
    /// // Add nodes to graph
    /// graph.AddNode(node1);
    /// graph.AddNode(node2);
    /// 
    /// // Remove a node (automatically updates relationships)
    /// graph.RemoveNode(node1);
    /// </code>
    /// </example>
    public class Graph<T>
        where T : class
    {
        /// <summary>
        /// Gets the collection of all nodes in the graph.
        /// </summary>
        public LinkedList<GraphNode<T>> Nodes;

        /// <summary>
        /// Initializes a new instance of the Graph class.
        /// </summary>
        public Graph()
        {
            Nodes = new LinkedList<GraphNode<T>>();
        }

        /// <summary>
        /// Adds a node to the graph if it doesn't already exist.
        /// </summary>
        /// <param name="node">The node to add.</param>
        public void AddNode(GraphNode<T> node)
        {
            if (node == null) return;
            if (Nodes.Contains(node)) return;

            Nodes.AddLast(node);
        }

        /// <summary>
        /// Removes a node from the graph and updates all parent-child relationships.
        /// </summary>
        /// <param name="node">The node to remove.</param>
        /// <remarks>
        /// When a node is removed, it is automatically removed from the children list of all its parents
        /// and from the parents list of all its children.
        /// </remarks>
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
