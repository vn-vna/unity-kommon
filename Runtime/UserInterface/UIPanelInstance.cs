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
                if (_panelReference == null)
                {
                    _panelReference = UIHelperClass.CurrentManager.RequirePanel<T>();
                    UIHelperClass.CurrentManager.PanelReleased += HandlePanelReleased;
                }
                return _panelReference;
            }
        }

        private T _panelReference;

        private void HandlePanelReleased(UIPanelBase panel)
        {
            if (_panelReference == null || panel != _panelReference) return;
            UIHelperClass.CurrentManager.PanelReleased -= HandlePanelReleased;
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
                UIHelperClass.CurrentManager.PanelReleased += HandlePanelReleased;
            }
        }

        public void Release()
        {
            if (_panelReference == null) return;
            UIHelperClass.CurrentManager.PanelReleased -= HandlePanelReleased;
            UIHelperClass.CurrentManager.ReleasePanel<T>();
            _panelReference = null;
        }
    }
}