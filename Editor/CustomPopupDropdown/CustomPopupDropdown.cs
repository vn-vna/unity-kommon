using System;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor
{
    public static class CustomPopupDropdown
    {
        public static void Show(
            Rect activatorRect,
            PopupWindowContent content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            PopupWindow.Show(activatorRect, content);
        }

        public static void ShowLastRect(PopupWindowContent content)
        {
            Show(GUILayoutUtility.GetLastRect(), content);
        }
    }
}
