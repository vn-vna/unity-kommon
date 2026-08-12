using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Thrown when attempting to delete or mutate an entity owned by
    /// another module.
    /// </summary>
    public sealed class ModuleOwnedEntityException : InvalidOperationException
    {
        public string OwnerModule { get; }
        public string EntityName { get; }

        public ModuleOwnedEntityException(
            string entityName, string ownerModule)
            : base($"Cannot modify '{entityName}' — owned by '{ownerModule}'.")
        {
            EntityName = entityName;
            OwnerModule = ownerModule;
        }
    }
}
