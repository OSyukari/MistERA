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
    readonly List<int> bedPreferenceButtonIDs = new List<int>();
    readonly List<BedPreferenceChoice> bedPreferenceChoices = new List<BedPreferenceChoice>();
    public RectTransform list_chara, list_prisoner;
    string charaLocAP;

    private List<Character_Trainable> charaInFaction;
    protected void Awake()
    {

        charaLocAP = LocalizeDictionary.QueryThenParse("ui_management_jobs_currentInfo");
    }
    public void Initialize()
    {
        ClearBedPreference();

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
    public RectTransform list_factionWork, list_factionRecreation, list_assignCOM, list_assignMembertypes, list_CharaNeeds, list_Beds, list_activities;

    public TMP_Text chara_fullname, charaGender, charaGenderSeparator;
    public scr_HoverableText chara_Race, chara_RaceTemplate;
    public scr_HoverableText chara_HomeFaction, chara_TempHomeFaction;
    public TMP_Text chara_location_ap;

    public RectTransform chara_schedulebox;
    public Image chara_scheduleBoxBG;

    public List<RectTransform> chara_scheduleCOMboxes;


    public void SetCurrentChara(Character_Trainable c)
    {
        ClearBedPreference();
        // destroy previous
        Utility.DestroyAllChildrenFrom(list_factionWork);
        Utility.DestroyAllChildrenFrom(list_factionRecreation);
        Utility.DestroyAllChildrenFrom(list_CharaNeeds);
        Utility.DestroyAllChildrenFrom(list_activities);

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
        chara_RaceTemplate.SetText(c.RaceTemplatesDisplayName);
        chara_RaceTemplate.SetExternalTooltip(c.RaceTemplatesTooltip);

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

        foreach (Manageable faction in c.FactionManager.RecreationFactions)
        {
            if (faction == null) continue;
            var hover = Instantiate(prefab_worktext);
            hover.SelfRect.SetParent(list_factionRecreation, false);
            Utility.FillRecreationFactionRect(hover, faction, c, "management_faction_recreation_tooltip", parent.CurrentFaction);
        }

        foreach(var i in RecreationUtility.GetReservedActivities(c))
        {
            if (i == null) continue;
            var hover = Instantiate(prefab_worktext);
            hover.SelfRect.SetParent(list_activities, false);
            RecreationUtility.PrintActivityDetail(hover, i);
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

        MakeBedPreference(c);

        // external job posts depend on the character's own work factions
        parent.RebuildExternalJobTab();
    }

    void ClearBedPreference()
    {
        if (parent != null) parent.UnloadButton(bedPreferenceButtonIDs);
        bedPreferenceButtonIDs.Clear();
        foreach (var choice in bedPreferenceChoices)
        {
            if (choice.Button == null) continue;
            choice.Button.Hover.DismissTooltip();
            choice.Button.gameObject.SetActive(false);
            Destroy(choice.Button.gameObject);
        }
        bedPreferenceChoices.Clear();
    }

    void OnDestroy()
    {
        ClearBedPreference();
    }

    bool IsBedPreferenceResident(Character_Trainable c, Manageable faction)
    {
        return c != null && faction != null && faction.isManagedChara(c.RefID)
            && c.FactionManager?.HomeFactions != null && c.FactionManager.HomeFactions.Contains(faction);
    }

    bool CanEditBedPreference(Character_Trainable c, Manageable faction)
    {
        var player = scr_System_CampaignManager.current?.Player;
        return parent != null && parent.CurrentFaction == faction && parent.currentChara == c
            && player != null && faction != null && faction.isCharaManager(player)
            && IsBedPreferenceResident(c, faction);
    }

    static string BedTypeLabel(string type)
    {
        return LocalizeDictionary.QueryThenParse("bedtype_" + type, type);
    }

    void MakeBedPreference(Character_Trainable c)
    {
        var faction = parent.CurrentFaction;
        if (!IsBedPreferenceResident(c, faction)) return;

        MakeBedPreferenceChoice(c, faction, null);
        foreach (var type in BedPreferenceUtility.KnownTypes)
            MakeBedPreferenceChoice(c, faction, type);
        RefreshBedPreference(c, faction);
    }

    void MakeBedPreferenceChoice(Character_Trainable c, Manageable faction, string type)
    {
        var button = Instantiate(prefab_charaNameButton);
        button.SelfRect.SetParent(list_Beds, false);
        button.isButtonToggle = true;
        button.showOptionID = false;
        button.linkText = "";
        button.Text.enableAutoSizing = true;
        button.Text.fontSizeMin = 8;
        button.Text.fontSizeMax = 18;
        var choice = new BedPreferenceChoice(this, button, c, faction, type);
        button.Initialize(parent, choice);
        button.SetText(type == null ? LocalizeDictionary.QueryThenParse("ui_bedPreference_inherited")
            : LocalizeDictionary.QueryThenParse("ui_bedPreference_choice").Replace("$type$", BedTypeLabel(type)));
        parent.RegisterButton(button, button.GetHashCode(), choice);
        bedPreferenceButtonIDs.Add(button.optionID);
        bedPreferenceChoices.Add(choice);
    }

    public scr_HoverableText bedPrefTitle;

    void RefreshBedPreference(Character_Trainable c, Manageable faction)
    {
        string preferredOverride = faction.GetPreferredBedTypeOverride(c.RefID);
        var memberType = faction.GetMemberType(c);
        string defaultType = memberType?.preferredBedTypeID;
        if (string.IsNullOrEmpty(defaultType)) defaultType = BedPreferenceUtility.HumanType;
        string source = LocalizeDictionary.QueryThenParse(preferredOverride == null
            ? "ui_bedPreference_inherited" : "ui_bedPreference_override");
        string defaultInfo = LocalizeDictionary.QueryThenParse("ui_bedPreference_default")
            .Replace("$type$", BedTypeLabel(defaultType)).Replace("$memberType$", memberType?.DisplayName ?? "-");
        string currentInfo = LocalizeDictionary.QueryThenParse("ui_bedPreference_current")
            .Replace("$type$", BedTypeLabel(BedPreferenceUtility.GetEffectiveType(c, faction)))
            .Replace("$source$", source);
        var info = defaultInfo + "\n" + currentInfo;
        bedPrefTitle.SetExternalTooltip(info);

        foreach (var choice in bedPreferenceChoices)
            choice.Refresh(preferredOverride, info);
    }

    sealed class BedPreferenceChoice : ButtonValidator, I_ButtonClickable
    {
        readonly initScript_ManageChara owner;
        readonly Character_Trainable target;
        readonly Manageable faction;
        readonly string type;
        readonly string permissionTooltip;
        string settingTooltip = "";
        public readonly scr_SelectableText Button;

        public BedPreferenceChoice(initScript_ManageChara owner, scr_SelectableText button,
            Character_Trainable target, Manageable faction, string type) : base(owner.parent)
        {
            this.owner = owner;
            Button = button;
            this.target = target;
            this.faction = faction;
            this.type = type;
            permissionTooltip = LocalizeDictionary.QueryThenParse("ui_bedPreference_permission");
        }

        public void Refresh(string preferredOverride, string info)
        {
            settingTooltip = info;
            Button.Toggle(true, preferredOverride == type);
            Button.Validate();
        }

        public override bool IsButtonValid()
        {
            // Preference reads and selection changes happen only on selection or a successful edit.
            bool allowed = owner.CanEditBedPreference(target, faction);
            state = allowed ? ButtonValidator_States.Valid : ButtonValidator_States.Invalid;
            tooltip = allowed ? settingTooltip : permissionTooltip;
            return allowed;
        }

        public void OnClickButton()
        {
            if (!owner.CanEditBedPreference(target, faction))
            {
                Button.Validate();
                return;
            }
            if (faction.SetPreferredBedTypeOverride(target.RefID, type, scr_System_CampaignManager.current.Player))
                owner.RefreshBedPreference(target, faction);
        }
    }

    public scr_HoverableText prefab_worktext;
}
