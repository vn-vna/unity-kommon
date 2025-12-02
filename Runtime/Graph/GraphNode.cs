using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Graph
{
    public class GraphNode<T>
    {
        public T Value;
        public LinkedList<GraphNode<T>> Parents;
        public LinkedList<GraphNode<T>> Children;

        public GraphNode(T value)
        {
            Value = value;
            Parents = new LinkedList<GraphNode<T>>();
            Children = new LinkedList<GraphNode<T>>();
        }

        public void AddChild(GraphNode<T> child)
        {
            if (child == null) return;
            if (child.Parents.Contains(this)) return;

            child.Parents.AddLast(this);
            Children.AddLast(child);
        }

        public void RemoveChild(GraphNode<T> child)
        {
            if (child == null) return;
            if (!child.Parents.Contains(this)) return;

            child.Parents.Remove(this);
            Children.Remove(child);
        }
    }
}
