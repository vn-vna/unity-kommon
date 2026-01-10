namespace Com.Hapiga.Scheherazade.Common.AutoBind
{
    /// <summary>
    /// Specifies the source locations from which to automatically bind components.
    /// </summary>
    /// <remarks>
    /// This is a flag enum that can be combined to search for components in multiple locations.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Bind from GameObject only
    /// var flag = AutoBindFromFlag.GameObject;
    /// 
    /// // Bind from GameObject and Children
    /// var combinedFlag = AutoBindFromFlag.GameObject | AutoBindFromFlag.Children;
    /// </code>
    /// </example>
    [System.Flags]
    public enum AutoBindFromFlag
    {
        /// <summary>
        /// Bind components from the GameObject itself.
        /// </summary>
        GameObject = 1 << 0,
        
        /// <summary>
        /// Bind components from child GameObjects.
        /// </summary>
        Children = 1 << 1,
        
        /// <summary>
        /// Bind components from parent GameObjects.
        /// </summary>
        Parents = 1 << 2,
    }

    /// <summary>
    /// Attribute that marks a field for automatic component binding.
    /// </summary>
    /// <remarks>
    /// This attribute is used to automatically bind Unity components to fields at runtime or in the editor,
    /// reducing manual assignment work and potential errors.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class MyComponent : MonoBehaviour
    /// {
    ///     [AutoBind(From = AutoBindFromFlag.GameObject)]
    ///     private Rigidbody rb;
    ///     
    ///     [AutoBind(From = AutoBindFromFlag.Children, Condition = "name:PlayerModel")]
    ///     private Renderer playerRenderer;
    /// }
    /// </code>
    /// </example>
    public class AutoBindAttribute
    {
        /// <summary>
        /// Gets the source location(s) from which to bind the component.
        /// </summary>
        public AutoBindFromFlag From { get; private set; }
        
        /// <summary>
        /// Gets the optional condition string for filtering components during binding.
        /// </summary>
        public string Condition { get; private set; }
    }
}