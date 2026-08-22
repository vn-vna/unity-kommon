using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ItemTagIdAttribute : Attribute
    {
        public string Id { get; }

        public ItemTagIdAttribute(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Tag ID cannot be empty.", nameof(id));
            }

            Id = id.Trim();
        }
    }
}
