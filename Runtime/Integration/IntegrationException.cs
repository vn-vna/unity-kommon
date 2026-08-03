using System;

namespace Com.Hapiga.Scheherazade.Common.Integration
{
    /// <summary>
    /// Base exception for all integration facade errors.
    /// </summary>
    public class IntegrationException : Exception
    {
        public IntegrationException(string message) : base(message) { }
        public IntegrationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Thrown by strict (async) facade APIs when the required manager
    /// is registered but not yet initialized.
    /// </summary>
    public class IntegrationNotInitializedException : IntegrationException
    {
        public string ManagerName { get; }

        public IntegrationNotInitializedException(string managerName)
            : base($"{managerName} manager is not initialized. Call Initialize() first.")
        {
            ManagerName = managerName;
        }

        public IntegrationNotInitializedException(string managerName, string message)
            : base(message)
        {
            ManagerName = managerName;
        }
    }

    /// <summary>
    /// Thrown by strict (async) facade APIs when the required manager
    /// is not registered in the IntegrationCentre.
    /// </summary>
    public class IntegrationModuleNotFoundException : IntegrationException
    {
        public Type ManagerType { get; }

        public IntegrationModuleNotFoundException(Type managerType)
            : base(
                $"Integration manager of type '{managerType.Name}' is not registered. " +
                "Ensure the module is enabled in the IntegrationCentre asset."
            )
        {
            ManagerType = managerType;
        }
    }
}
