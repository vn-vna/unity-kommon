using System;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
{
    internal static class UIHelperClass
    {
        public static IUIManager CurrentManager { get; private set; }

        internal static void RegisterManager<T>(this UIManagerBase<T> manager)
            where T : UIManagerBase<T>
        {
            if (CurrentManager != null)
            {
                throw new InvalidOperationException(
                    "Multiple UI Manager is not allowed"
                );
            }

            CurrentManager = manager;
        }

        internal static void UnregisterManager<T>(this UIManagerBase<T> manager)
            where T : UIManagerBase<T>
        {
            if (CurrentManager == null)
            {
                QuickLog.SWarning(
                    "No UI Manager is registered before"
                );
                return;
            }

            if (CurrentManager is not T cmanager)
            {
                throw new InvalidOperationException(
                    "Cannot validate UI Manager to unregister"
                );
            }

            if (cmanager != manager)
            {
                throw new InvalidOperationException(
                    "Unregistered manager is not valid"
                );
            }

            CurrentManager = null;
        }
    }
}