using System;

namespace Com.Hapiga.Scheherazade.Common.UserInterface
{
    [AttributeUsage(
        AttributeTargets.Class,
        AllowMultiple = false,
        Inherited = true
    )]
    public class UIPanelInfoAttribute : Attribute
    {
        public string PanelId { get; set; }

        public UIPanelInfoAttribute(string panelId)
        {
            PanelId = panelId;
        }
    }
}