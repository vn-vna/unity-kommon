using System.Collections.Generic;

[System.Serializable]
public class SavedItemData
{
    public string itemId;
    public int itemStack;

    public Dictionary<string, string> InventoryTraits = new();
}
