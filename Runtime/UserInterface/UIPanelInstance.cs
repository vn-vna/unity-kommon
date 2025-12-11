using System;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
{
    public struct UIPanelInstance<T>
        where T : UIPanelBase
    {
        public bool IsVisible => Panel != null && _panelReference.IsVisible;

        public T Panel
        {
            get
            {
                if (UIHelperClass.CurrentManager == null)
                {
                    throw new InvalidOperationException(
                        "No UI Manager is registered"
                    );
                }

                if (!UIHelperClass.CurrentManager.CheckInstance(_panelReference))
                {
                    _panelReference = null;
                }

                if (_panelReference == null)
                {
                    _panelReference = UIHelperClass.CurrentManager.RequirePanel<T>();
                }

                return _panelReference;
            }
        }

        private T _panelReference;

        private void HandlePanelReleased(UIPanelBase panel)
        {
            if (_panelReference == null || panel != _panelReference) return;
            _panelReference = null;
        }

        public void Show(bool immediate = false, Action callback = null)
        {
            Panel.Show(immediate, callback);
        }

        public void Hide(bool immediate = false, Action callback = null)
        {
            Panel.Hide(immediate, callback);
        }

        public void ForceInitialize()
        {
            if (_panelReference == null)
            {
                _panelReference = UIHelperClass.CurrentManager.RequirePanel<T>();
            }
        }

        public void Release()
        {
            if (_panelReference == null) return;
            UIHelperClass.CurrentManager.ReleasePanel<T>();
            _panelReference = null;
        }
    }
}