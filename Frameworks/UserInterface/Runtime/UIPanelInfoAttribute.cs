using System;

namespace Com.Scheherazade.Common.UserInterface
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