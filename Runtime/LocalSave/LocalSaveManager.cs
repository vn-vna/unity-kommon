using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LocalSave
{

    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class LocalSaveDataAttribute : Attribute
    { }

    public class LocalSaveManager :
        Singleton.SingletonBehavior<LocalSaveManager>
    {
        #region Private Fields
        private FieldInfo[] _localSaveFields;
        #endregion

        #region Unity Methods
        protected override void Awake()
        {
            _localSaveFields = GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .Where(field => field.GetCustomAttributes(typeof(LocalSaveDataAttribute), false).Length > 0)
                .ToList()
                .ToArray();
        }
        #endregion
    }

}
