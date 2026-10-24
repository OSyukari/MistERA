using System.Collections.Generic;

/// <summary>
/// Undress / redress command of one clothing layer (or all layers), run through ActionPackage_Wardrobe either on a
/// whole layer or on a single worn/unworn item. Layer, direction and acceptance live in the data
/// (Data/COM_Defs/COM_wardrobe.json): the COM manager picks the command by Find(isRedress, layer, allLayers),
/// never by ID. $item$ in texts is the item name (single item) or the layer name (whole layer).
/// </summary>
public class COM_Wardrobe : COM
{
    public bool isRedress = false;
    /// <summary> Skin / Inner / Outer - items of any other layer (None) use the Outer command </summary>
    public BodyEquipLayer layer = BodyEquipLayer.Outer;
    /// <summary> every layer at once (Undress all / Fix clothes) - layer is then not used by the package </summary>
    public bool allLayers = false;

    /// <summary> layer of the command an item of this equip layer uses </summary>
    public static BodyEquipLayer CommandLayer(BodyEquipLayer itemLayer)
    {
        return itemLayer == BodyEquipLayer.Skin || itemLayer == BodyEquipLayer.Inner ? itemLayer : BodyEquipLayer.Outer;
    }

    public static COM_Wardrobe Find(bool isRedress, BodyEquipLayer layer, bool allLayers = false)
    {
        if (!allLayers) layer = CommandLayer(layer);
        foreach (var w in All)
        {
            if (w.isRedress == isRedress && w.allLayers == allLayers && (allLayers || w.layer == layer)) return w;
        }
        return null;
    }

    // Find runs on every clothing button refresh: the commands are collected once per COM index
    static Index_COM cachedIndex = null;
    static List<COM_Wardrobe> cached = new List<COM_Wardrobe>();
    static List<COM_Wardrobe> All
    {
        get
        {
            var index = scr_System_Serializer.current.index_COM;
            if (index != cachedIndex)
            {
                cachedIndex = index;
                cached.Clear();
                foreach (var c in index.LIST) if (c is COM_Wardrobe w) cached.Add(w);
            }
            return cached;
        }
    }
}
