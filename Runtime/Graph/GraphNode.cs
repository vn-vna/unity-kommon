using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Graph
{
    /// <summary>
    /// Represents a node in a directed graph with parent-child relationships.
    /// </summary>
    /// <typeparam name="T">The type of value stored in the node.</typeparam>
    /// <remarks>
    /// Each node can have multiple parents and children, forming a directed acyclic or cyclic graph.
    /// Parent-child relationships are bidirectionally maintained.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create nodes
    /// var parent = new GraphNode&lt;string&gt;("Parent");
    /// var child1 = new GraphNode&lt;string&gt;("Child 1");
    /// var child2 = new GraphNode&lt;string&gt;("Child 2");
    /// 
    /// // Establish relationships
    /// parent.AddChild(child1);
    /// parent.AddChild(child2);
    /// 
    /// // Access node data
    /// Debug.Log(parent.Value);
    /// Debug.Log($"Parent has {parent.Children.Count} children");
    /// </code>
    /// </example>
    public class GraphNode<T>
    {
        /// <summary>
        /// Gets or sets the value stored in this node.
        /// </summary>
        public T Value;
        
        /// <summary>
        /// Gets the collection of parent nodes.
        /// </summary>
        public LinkedList<GraphNode<T>> Parents;
        
        /// <summary>
        /// Gets the collection of child nodes.
        /// </summary>
        public LinkedList<GraphNode<T>> Children;

        /// <summary>
        /// Initializes a new instance of the GraphNode class with the specified value.
        /// </summary>
        /// <param name="value">The value to store in the node.</param>
        public GraphNode(T value)
        {
            Value = value;
            Parents = new LinkedList<GraphNode<T>>();
            Children = new LinkedList<GraphNode<T>>();
        }

        /// <summary>
        /// Adds a child node and establishes a bidirectional parent-child relationship.
        /// </summary>
        /// <param name="child">The child node to add.</param>
        /// <remarks>
        /// This method updates both this node's children list and the child's parents list.
        /// If the relationship already exists, no action is taken.
        /// </remarks>
        public void AddChild(GraphNode<T> child)
        {
            if (child == null) return;
            if (child.Parents.Contains(this)) return;

            child.Parents.AddLast(this);
            Children.AddLast(child);
        }

        /// <summary>
        /// Removes a child node and breaks the bidirectional parent-child relationship.
        /// </summary>
        /// <param name="child">The child node to remove.</param>
        /// <remarks>
        /// This method updates both this node's children list and the child's parents list.
        /// If the relationship doesn't exist, no action is taken.
        /// </remarks>
        public void RemoveChild(GraphNode<T> child)
        {
            if (child == null) return;
            if (!child.Parents.Contains(this)) return;

            child.Parents.Remove(this);
            Children.Remove(child);
        }
    }
}
