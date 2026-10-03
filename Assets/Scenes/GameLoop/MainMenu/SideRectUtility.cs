using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;


public static class SideRectUtility
{

    public static void LoadCycleData(Character_Trainable c,
        RectTransform cycleRect,
        scr_HoverableText cycle_total,
        scr_HoverableText cycle_current,
        scr_HoverableText cycle_ovum,
        scr_HoverableText cycle_fertility)
    {
        if (c.ReproTemplate != null && c.ReproCycle != null)
        {

            int currentCycleRemaining = -1;
            // cycle type
            // 
            cycleRect.gameObject.SetActive(true);

            cycle_total.SetText(LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_total")
                .Replace("$total$", $"{c.ReproTemplate.cycleThreshold}"));

            var title_tooltips = new List<string>();
            c.ReproCycle.GetReproTemplateTooltip(c, c.ReproTemplate, title_tooltips);
            cycle_total.SetExternalTooltip(String.Join("\n", title_tooltips));

            currentCycleRemaining = c.ReproCycle.CurrentCycleRemaining(c.ReproTemplate);
            cycle_current.SetText(LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_current")
                .Replace("$count$", currentCycleRemaining == -1 ? "-" : $"{currentCycleRemaining}"), false, currentCycleRemaining == -1 ? "charaDetail_panel_cycle_current_none" : "");

            var ovucount = c.ReproTemplate.ovulationQuantityAverage * c.ReproTemplate.fertility / c.ReproTemplate.ovulationQuantityAverage;

            cycle_ovum.SetText(LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_ovumCount")
                .Replace("$count$", $"{ovucount}"));

            cycle_fertility.SetText(LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_ovumFertility")
                .Replace("$fertility$", $"{c.ReproTemplate.fertilizationChance}"));
        }
        else
        {
            cycleRect.gameObject.SetActive(false);
        }
    }

    public static void LoadHistoryLogsData(Character_Trainable c,
        RectTransform parent,
        Func<scr_memoryDaySplit> addDaySplit,
        Func<scr_memoryBox> addMemoryBox
        )
    {
        DateTime current = scr_System_Time.current.getCurrentTime();
        DateTime lastTime = scr_System_Time.current.getCurrentTime();
        bool first = true;
        bool shorten = false;
        string lastString = Utility.GetRelativeDayString(current, lastTime);

        for (int i = c.Memory.Entries.Count - 1; i >= 0; i--)// Memory_Entry mem in chara.MemoryManager.entries)
        {
            var currEntry = c.Memory.Entries[i];
            if (currEntry.noDisplay) continue;
            if (shorten || (current - currEntry.FinalEndTime).Days >= 7)
            {
                shorten = true;
            }
            var last2 = Utility.GetRelativeDayString(current, currEntry.FinalEndTime);

            if (first || lastString != last2)
            {
                first = false;
                lastTime = currEntry.FinalEndTime;
                lastString = last2;
                var rect = addDaySplit.Invoke();// Instantiate(prefab_DaySplit);
                rect.selfRect.SetParent(parent, false);
                rect.text.SetText($"{lastString}");
            }
            //if (!chara.Memory.Entries[i].isValid) continue;
            scr_memoryBox line = addMemoryBox.Invoke();// Instantiate(prefab_MemoryEntry);
            line.gameObject.transform.SetParent(parent, false);
            c.Memory.Entries[i].Draw(line, shorten);
            //line.SetText(chara.MemoryManager.entries[i]);

            //if (entry.Tags.Count > 0) prefab_MemoryEntry.GetComponent<scr_HoverableText>().SetExternalTooltip("Relevant Tags: " + String.Join(" ", entry.Tags));
        }
    }

    public static void LoadBodyInternalData(
        Character_Trainable chara,
        //List<BodyInternal_Instance> listInternal,
        //Dictionary<int, BodyInternal_Instance> internalIndex,
        Func<scr_Panel_BodyDetail> addBodyDetail,
        Func<scr_panel_wombdata> addWombDetail
        )
    {

        var listInternal = new List<BodyInternal_Instance>();

        foreach (BodyPart_Instance b in chara.Body.Body)
        {
            foreach (BodyInternal_Instance i in b.internals)
            {
                //if (i != null && i.canBeFucked || i.canFuck)
                //{
                listInternal.Add(i);
                //}
            }
        }

        listInternal.Sort((x, y) => x.sortOrder.CompareTo(y.sortOrder));
        //int j = 0;
        foreach (BodyInternal_Instance i in listInternal)
        {
            //internalIndex.Add(j, i);

            var box = addBodyDetail.Invoke();
            box.InitializeWithArgument(i);

            //j++;
        }

        foreach (var womb in chara.wombs)
        {
            var box = addWombDetail.Invoke();// Instantiate(initSexRecords.prefab_panel_womb);
           // box.selfRect.SetParent(initSexRecords.HealthTab_Pregnancy, false);
            box.InitializeWithArgument(womb);
        }
    }

    public static void LoadEquipmentData(Character_Trainable chara, 
        Func<RectTransform> instantiate_gear, 
        Func<RectTransform, RectTransform> instantiate_equip,
        Func<RectTransform, string, bool,bool> AddText,
        Func<RectTransform, string, bool, bool> AddButton)
    {
        bool safeMode = scr_System_CentralControl.current.isSafeMode;

        foreach (BodyPart_Instance b in chara.Body.Body)
        {
            if (b.availableSlots.Count < 1) continue;
            RectTransform rect = instantiate_gear.Invoke();

            RectTransform childRect = rect.GetComponent<scr_BodyInstanceGears>().Instantiate(b.DisplayName);

            foreach (BodyPartEquipSlot slot in b.availableSlots)
            {
                RectTransform box = instantiate_equip.Invoke(childRect);

                if (slot != BodyPartEquipSlot.None) AddText.Invoke(box, LocalizeDictionary.QueryThenParse("equip_slot_" + Utility.GetEnumString(typeof(BodyPartEquipSlot), slot)), false);

                int score = b.GetRevealingScore(BodyEquipLayer.Skin);

                Item_Instance skin, inner, outer;

                if (!safeMode)
                {
                    if (score > 1
                        && !scr_System_CampaignManager.current.XrayMode
                        && !scr_System_CampaignManager.current.DebugMode) AddText.Invoke(box, "(" + score + ")", false);
                    else if (b.TryGetEquip(out skin, BodyEquipLayer.Skin, slot)) AddButton.Invoke( box, skin.DisplayName,false);
                    else if (b.TryGetCover(out skin, BodyEquipLayer.Skin, slot)) AddButton.Invoke(box, skin.DisplayName, true);
                    else AddText.Invoke(box, " - ", false);

                }

                if (b.TryGetEquip(out inner, BodyEquipLayer.Inner, slot)) AddButton.Invoke(box, inner.DisplayName,false);
                else if (b.TryGetCover(out inner, BodyEquipLayer.Inner, slot)) AddButton.Invoke(box, inner.DisplayName, true);
                else AddText.Invoke(box, " - ", false);

                if (b.TryGetEquip(out outer, BodyEquipLayer.Outer, slot)) AddButton.Invoke(box, outer.DisplayName, false);
                else if (b.TryGetCover(out outer, BodyEquipLayer.Outer, slot)) AddButton.Invoke(box, outer.DisplayName, true);
                else AddText.Invoke(box, " - ", false);


                /*int l = b.GetEquip(BodyEquipLayer.Shell, slot);
                if (l > 0) AddBox(buttonBox, box, scr_System_CampaignManager.current.FindItemInstanceByID(l).DisplayName);
                else AddBox(textBox, box, " - ");*/

            }

            if (safeMode) continue;

            foreach (BodyInternal_Instance ins in b.internals)
            {
                foreach (BodyPartEquipSlot slot in ins.availableSlots)
                {
                    bool hasEquip = false;
                    RectTransform box = instantiate_equip.Invoke(childRect);
                    box.SetParent(childRect, false);

                    if (slot != BodyPartEquipSlot.None) AddText.Invoke(box, LocalizeDictionary.QueryThenParse("equip_slot_" + Utility.GetEnumString(typeof(BodyPartEquipSlot), slot)), false);

                    int score = b.GetRevealingScore(BodyEquipLayer.Skin);
                    if (score > 1 && !scr_System_CampaignManager.current.XrayMode)
                    {
                        AddText.Invoke(box, " " + score + " ", false);
                        AddText.Invoke(box, " " + score + " ", false);
                        //AddBox(textBox, box, " ??? ");
                    }
                    else
                    {
                        if (ins.equipLayers.Contains(BodyEquipLayer.Skin))
                        {
                            int i = ins.GetEquip(BodyEquipLayer.Skin, slot);
                            //if (i > 0) AddBox(buttonBox, box, scr_System_CampaignManager.current.FindItemInstanceByID(i).DisplayName + "[" + score + "]");
                            if (i > 0)
                            {
                                hasEquip = true;
                                AddButton.Invoke(box, scr_System_CampaignManager.current.FindItemInstanceByID(i).DisplayName, false);
                            }
                            else
                            {
                                AddText.Invoke(box, " - ", false);
                            }
                        }
                        else AddText.Invoke(box, " ", false);

                        if (ins.equipLayers.Contains(BodyEquipLayer.Inner))
                        {
                            int i = ins.GetEquip(BodyEquipLayer.Inner, slot);
                            //if (i > 0) AddBox(buttonBox, box, scr_System_CampaignManager.current.FindItemInstanceByID(i).DisplayName + "[" + score + "]");
                            if (i > 0)
                            {
                                hasEquip = true;
                                AddButton.Invoke(box, scr_System_CampaignManager.current.FindItemInstanceByID(i).DisplayName, false);
                            }
                            else
                            {
                                AddText.Invoke(box, " - ", false);
                            }
                        }
                        else AddText.Invoke(box, " ", false);

                        AddText.Invoke(box, " ", false);
                    }


                    if (!hasEquip) box.gameObject.SetActive(false);
                }
            }
        }
    }

}

