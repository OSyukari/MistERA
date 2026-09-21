using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static scr_Canvas_Management;
using UnityEngine.UI;

public class initScript_ManageChara : MonoBehaviour
{
    public scr_Canvas_Management parent;
    List<int> tempCharaRefIDStorage = new List<int>();
    public RectTransform list_chara, list_prisoner;
    string charaLocAP;

    private List<Character_Trainable> charaInFaction;
    protected void Awake()
    {

        charaLocAP = LocalizeDictionary.QueryThenParse("ui_management_jobs_currentInfo");
    }
    public void Initialize()
    {

        parent.UnloadButton(tempCharaRefIDStorage);
        Utility.DestroyAllChildrenFrom(list_chara);
        Utility.DestroyAllChildrenFrom(list_prisoner);
        tempCharaRefIDStorage.Clear();

        if (parent.CurrentFaction == null) return;

        charaInFaction = parent.CurrentFaction.ManagedChara;
        if (charaInFaction == null || charaInFaction.Count < 1) return;

        this.parent.SetCurrentChara(scr_System_CampaignManager.current.Player);


        foreach (Character_Trainable chara in parent.CurrentFaction.ManagedChara_Members)
        {
            MakeCharaButton(list_chara, prefab_charaNameButton, chara);
        }
        foreach (Character_Trainable chara in parent.CurrentFaction.ManagedChara_Prisoners)
        {
            MakeCharaButton(list_prisoner, prefab_charaNameButton, chara);
        }

    }
    public scr_SelectableText prefab_charaNameButton;
    private void MakeCharaButton(RectTransform parent, scr_SelectableText prefab, Character_Trainable chara)
    {

        scr_SelectableText comp = Instantiate(prefab);
        RectTransform r = comp.GetComponent<RectTransform>();
        r.SetParent(parent, false);


        comp.Initialize(this.parent, new ButtonValidator_charaSelect(this.parent, comp, chara));
        comp.SetText(chara.FirstName);

        this.parent.RegisterButton(comp, chara.GetHashCode(), comp.Validator);

        tempCharaRefIDStorage.Add(comp.optionID);

        comp.Validate();
    }
    public RectTransform list_factionWork, list_assignCOM, list_CharaNeeds;

    public TMP_Text chara_fullname, charaGender, charaGenderSeparator;
    public scr_HoverableText chara_Race, chara_RaceTemplate;
    public scr_HoverableText chara_HomeFaction, chara_TempHomeFaction;
    public TMP_Text chara_location_ap;

    public RectTransform chara_schedulebox;
    public Image chara_scheduleBoxBG;

    public List<RectTransform> chara_scheduleCOMboxes;


    public void SetCurrentChara(Character_Trainable c)
    {
        // destroy previous
        Utility.DestroyAllChildrenFrom(list_factionWork);
        Utility.DestroyAllChildrenFrom(list_CharaNeeds);


        bool safe = scr_System_CentralControl.current.isSafeMode;

        // set current

        chara_fullname.text = c.FullName;
        if (safe)
        {
            charaGenderSeparator.gameObject.SetActive(false);
            charaGender.gameObject.SetActive(false);
        }
        else
        {
            charaGenderSeparator.gameObject.SetActive(true);
            charaGender.gameObject.SetActive(true);
            charaGender.SetText(LocalizeDictionary.QueryThenParse(c.Appearance.ToString()));

        }
        chara_Race.SetText(c.Race.DisplayName, false, c.Race.ID + "_tooltip");
        chara_RaceTemplate.SetText(c.RaceTemplate.DisplayName, false, c.RaceTemplate.ID + "_tooltip");

        chara_location_ap.SetText(charaLocAP.Replace("$location$", scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID).DisplayName).Replace("$jobdescription$", parent.currentChara.GetJobDescription()));

        Utility.FillFactionRect(chara_HomeFaction, c.FactionManager.Faction_Home, c, "management_faction_home_tooltip", parent.CurrentFaction);
        //chara_HomeFaction.SetText(currentChara.FactionManager.Faction_Home == null ? " - " : currentChara.FactionManager.Faction_Home.FactionDisplayName);

        Utility.FillFactionRect(chara_TempHomeFaction, c.FactionManager.Faction_Home_Temporary, c, "management_faction_home_temporary_tooltip", parent.CurrentFaction);
        //chara_TempHomeFaction.SetText(currentChara.FactionManager.Faction_Home_Temporary == null ? " - " : currentChara.FactionManager.Faction_Home_Temporary.FactionDisplayName);

        scr_System_CampaignManager.current.CurrentTargetEX = c;

        // int currentHour = scr_System_Time.current.getCurrentTime().Hour;

        foreach (Manageable faction in c.FactionManager.WorkFactions)
        {
            var hover = Instantiate(prefab_worktext);
            hover.SelfRect.SetParent(list_factionWork, false);
            Utility.FillFactionRect(hover, faction, c, "management_faction_work_tooltip", parent.CurrentFaction);
        }

        if (c.hasSleepNeed)
        {
            var text = Instantiate(prefab_worktext);
            text.SetText("sleep");
            text.SelfRect.SetParent(list_CharaNeeds, false);
        }

        foreach (var need in c.Stats.Needs)
        {
            var text = Instantiate(prefab_worktext);
            text.SetText(need.DisplayName);
            text.SelfRect.SetParent(list_CharaNeeds, false);
        }
    }

    public scr_HoverableText prefab_worktext;
}
