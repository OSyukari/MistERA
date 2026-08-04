using System;
using System.Collections.Generic;
using System.Text;

public class Requirement_Faction
{
    // com open production recipe tab. player only. Fetch player inventory -> party inventory -> settlement inventory, set production order of corresponding inventory
    // furniture allow open this menu, same menu as when open from management menu
    // furniture menu if selected and satisfy, immediately go to production (single)
    // management menu add production order, anyone can satisfy order in production furniture (job)

    // com fulfill production order (isJob), require a existing production order of specified tag (from settlement or from party)

    // NPC assigned by production order tag.

    public bool allowInNonPlayerFaction = true;
    public bool allowInPlayerFaction = true;
    public string jobKeyword = "";
    public string inventoryItemBaseID = "";
    public bool requireCanPrepMeal = false;
    public bool Validate(I_IsJobGiver m, out string tooltip)
    {
        tooltip = "";
        var mm = m as Manageable;
        if (jobKeyword != "" && (mm == null || !mm.ExistOngoingProductionOrder(jobKeyword)))
        {
            tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_jobKeyword")
                    .Replace("$faction$", mm == null ? "-" : mm.FactionDisplayName)
                    .Replace("$keywords$", jobKeyword);
            return false;
        }
        if (!allowInNonPlayerFaction && !m.isPlayerFaction)
        {
            tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_disallowInNonPlayerFaction")
                    .Replace("$faction$", mm == null ? "-" : mm.FactionDisplayName);
            return false;
        }
        if (!allowInPlayerFaction && m.isPlayerFaction)
        {
            tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_disallowInPlayerFaction")
                    .Replace("$faction$", mm == null ? "-" : mm.FactionDisplayName);
            return false;
        }

        if (inventoryItemBaseID != "" && (m == null || m.Inventory == null || m.Inventory.GetItemCount(inventoryItemBaseID) < 1))
        {
            var name = scr_System_Serializer.current.index_Item_Base.GetByID(inventoryItemBaseID);
            var replace = name == null ? inventoryItemBaseID : name.DisplayName;
            tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_inventoryItemBaseID")
                    .Replace("$faction$", mm == null ? "-" : mm.FactionDisplayName)
                    .Replace("$name$", replace);
            return false;
        }
        if (requireCanPrepMeal)
        {
            var nextHour = Math.Clamp(scr_System_Time.current.getCurrentTime().Hour + 1, 0, 23);
            if (!m.isPlayerFaction)
            {
                tooltip = "non player faction, can always prep";
            }
            else if (m.isMealHourAt(nextHour))
            {
                tooltip = "next hour is already meal hour";
                return false;
            }
            else
            {
                bool existFood = false;
                foreach (var item in m.Inventory.Contents)
                {
                    if (item.isFoodConsumable)
                    {
                        existFood = true;
                        break;
                    }
                }
                if (!existFood)
                {
                    tooltip = "no consumable item in faction inventory";
                    return false;
                }
            }
        }
        return true;
    }

    public bool isValid { get { return jobKeyword != "" || inventoryItemBaseID != ""; } }

}