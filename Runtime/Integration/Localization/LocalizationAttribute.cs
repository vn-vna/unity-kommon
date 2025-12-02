using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.L18n
{
    [AttributeUsage(
        AttributeTargets.Class,
        AllowMultiple = false
    )]
    public class LocalizationAttribute : Attribute
    {}
    
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property, 
        AllowMultiple = false
    )]
    public class LocalizedFieldAttribute : Attribute
    {
        public string LocalizationKey => _localizationKey;

        private readonly string _localizationKey;

        public LocalizedFieldAttribute(string localizationKey)
        {
            _localizationKey = localizationKey;
        }
    }
}