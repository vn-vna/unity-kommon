using System;

namespace Com.Hapiga.Scheherazade.Common.ImplicitDeps
{
    /// <summary>
    /// Attribute to mark methods that are implicitly called somewhere.
    /// This can be used for quickly navigating to methods that are not directly 
    /// referenced in the codebase, feel free to remove it if you don't need it.
    /// <br/>
    /// 
    /// Why use this?
    /// <br/>
    /// In large codebases, especially those with complex dependencies or dynamic method calls
    /// (eg. reflection, event handlers, or dependency injection), it can be challenging to track
    /// where a method is called from. This attribute serves as a marker to indicate that a method
    /// is expected to be called implicitly, even if it doesn't have a direct reference in the code.
    /// <br/>
    /// 
    /// <br/>
    /// How to use it?
    /// <br/>
    /// <code>
    /// [ImplicitlyCalledBy(nameof(SomeMethod/Class))]
    /// private void SomeMethodWouldBeCalledByReflection()
    /// {
    ///     ... Two thousand lines of code later ...
    /// }
    /// </code>
    /// This way, you can easily find methods that are expected to be called implicitly by searching
    /// 
    /// <br/>
    /// Yayyy, implicit dependencies!
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public class ImplicitlyCalledByAttribute : Attribute
    {
        public string MethodName { get; }

        public ImplicitlyCalledByAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}