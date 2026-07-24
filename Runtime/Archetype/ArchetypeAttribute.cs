using System;

namespace Com.Hapiga.Scheherazade.Common.Archetype
{
    [AttributeUsage(
        AttributeTargets.Class,
        Inherited = false,
        AllowMultiple = true
    )]
    public sealed class ArchetypeAttribute : Attribute
    {
        public Type InterfaceType { get; }
        public string ArchetypeName { get; }
        public string ArchetypeField { get; }

        public ArchetypeAttribute(
            Type interfaceType,
            string archetypeName,
            string archetypeField)
        {
            InterfaceType = interfaceType;
            ArchetypeName = archetypeName;
            ArchetypeField = archetypeField;
        }
    }
}
