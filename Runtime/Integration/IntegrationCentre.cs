using System;
using System.Collections.Generic;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration
{
    [CreateAssetMenu(fileName = "IntegrationCentre", menuName = "Scheherazade/Integration/Integration Centre")]
    public class IntegrationCentre :
        SingletonScriptableObject<IntegrationCentre>
    {
        public IReadOnlyList<IIntegrationModule> Modules { get; private set; }
        public IReadOnlyDictionary<Type, IIntegrationModule> ModulesByType { get; private set; }

        [SerializeField]
        private ScriptableObject[] moduleScriptableObjects;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var centre = Instance;
            if (centre == null)
            {
                QuickLog.Warning<IntegrationCentre>("No IntegrationCentre asset found in resources or project. Skipping bootstrap.");
                return;
            }

            centre.Initialize();
        }

        public void Initialize()
        {
            Modules = moduleScriptableObjects
                .OfType<IIntegrationModule>()
                .ToList();

            ModulesByType = Modules
                .ToDictionary(module => module.GetType(), module => module);

            // Register each manager dynamically to Integration class
            foreach (var moduleObj in moduleScriptableObjects)
            {
                if (moduleObj == null) continue;

                // Call the static RegisterManager method via reflection for all implemented interfaces
                var registerMethod = typeof(Integration).GetMethod("RegisterManager", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (registerMethod != null)
                {
                    foreach (var iface in moduleObj.GetType().GetInterfaces())
                    {
                        try
                        {
                            var genericMethod = registerMethod.MakeGenericMethod(iface);
                            genericMethod.Invoke(null, new object[] { moduleObj });
                        }
                        catch { /* Skip interfaces that don't match the generic constraint */ }
                    }
                }
            }

            // Spawn Ticker to drive Update loops for SOs
            var tickerGo = new GameObject("[Scheherazade Integration Ticker]");
            DontDestroyOnLoad(tickerGo);
            tickerGo.hideFlags = HideFlags.HideInHierarchy;
            var ticker = tickerGo.AddComponent<IntegrationCentreTicker>();
            ticker.Setup(this);

            foreach (IIntegrationModule module in Modules)
            {
                module.Reset();
            }
        }

        public T GetModule<T>() where T : class, IIntegrationModule
        {
            if (ModulesByType != null && ModulesByType.TryGetValue(typeof(T), out IIntegrationModule module))
            {
                return module as T;
            }

            QuickLog.Warning<IntegrationCentre>(
                "Integration module of type {0} not found.",
                typeof(T).Name
            );

            return null;
        }
    }

    internal class IntegrationCentreTicker : MonoBehaviour
    {
        private IntegrationCentre _centre;
        private ITickableModule[] _tickables;

        public void Setup(IntegrationCentre centre)
        {
            _centre = centre;
            UpdateTickables();
        }

        private void UpdateTickables()
        {
            if (_centre != null && _centre.Modules != null)
            {
                _tickables = _centre.Modules.OfType<ITickableModule>().ToArray();
            }
        }

        private void Update()
        {
            if (_tickables == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _tickables.Length; i++)
            {
                _tickables[i].Tick(dt);
            }
        }
    }

    public interface ITickableModule
    {
        void Tick(float deltaTime);
    }
}