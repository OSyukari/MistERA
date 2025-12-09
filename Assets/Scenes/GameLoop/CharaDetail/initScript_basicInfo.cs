using UnityEngine;
using TMPro;

public class initScript_basicInfo : MonoBehaviour
{

    public scr_HoverableText fullName, gender, genderfollowup, race, raceTemplate, factionStatus;
    public scr_HoverableText hp, mp, st, en;
    public scr_HoverableText mood, stress, lust; // statusex instance
    // consciousness fatigue sexstimulation
    // basestats
    public scr_HoverableText stat_str, stat_con, stat_psy, stat_will;
    // skills
    public scr_HoverableText shooting, stealth, survival;
    public scr_HoverableText athletics, construction, plants, melee;
    public scr_HoverableText arts, medecine, cooking, engineering, science, spellcraft;
    public scr_HoverableText animal, perception, social;

    // traits
    public RectTransform traitsList;

    // derivedStats
    public RectTransform statsGrid;
    public RectTransform statusEXGrid;
    public RectTransform statusGrid;

    // common prefabs
    public TextMeshProUGUI textBox, linkBox, buttonBox;
    public TextMeshProUGUI linkBox_resize;


    public RectTransform SkillsGrid;
    public scr_HoverableText viewExpButton;

    public void InitData(Character_Trainable chara)
    {
        if (chara == null) return;

        bool safe = scr_System_CentralControl.current.isSafeMode;

        while (SkillsGrid.transform.childCount > 0) DestroyImmediate(SkillsGrid.transform.GetChild(0).gameObject);

        fullName.SetText(chara.FullName, false);
        //Debug.LogError("Query gender data on string " + chara.Appearance.ToString());
        if (safe)
        {
            genderfollowup.gameObject.SetActive(false);
            gender.gameObject.SetActive(false);
        }
        else
        {
            genderfollowup.gameObject.SetActive(true);
            gender.gameObject.SetActive(true);
            gender.SetText(LocalizeDictionary.QueryThenParse(chara.Appearance.ToString()), false);
        }
        race.SetText(chara.Race.DisplayName, false, chara.Race.ID + "_tooltip");
        raceTemplate.SetText(chara.RaceTemplate.DisplayName, false, chara.RaceTemplate.ID+"_tooltip");
        factionStatus.SetText(chara.FactionManager.CurrentlyActiveFactionStatus);

        UI_Utility.Draw(chara.Stats.Strength, this.stat_str);
        UI_Utility.Draw(chara.Stats.Constitution, this.stat_con);
        UI_Utility.Draw(chara.Stats.Psyche, this.stat_psy);
        UI_Utility.Draw(chara.Stats.Willpower, this.stat_will);

        if(chara.Stats.HP != null) chara.Stats.HP.Draw(this.hp);
        else this.hp.SetText(" - ");

        if (chara.Stats.MP != null) chara.Stats.MP.Draw(this.mp);
        else this.mp.SetText(" - ");

        if (chara.Stats.Stamina != null) chara.Stats.Stamina.Draw(this.st);
        else this.st.SetText(" - ");

        if (chara.Stats.Energy != null) chara.Stats.Energy.Draw(this.en);
        else this.en.SetText(" - ");


        if (chara.Stats.Mood != null) chara.Stats.Mood.Draw(this.mood);
        else this.mood.SetText(" - ");

        if (chara.Stats.Stress != null) chara.Stats.Stress.Draw(this.stress);
        else this.stress.SetText(" - ");

        if (chara.Stats.Lust != null) chara.Stats.Lust.Draw(this.lust);
        else this.lust.SetText(" - ");


        /*
        stat_str.SetText("Strength: " + chara.Stat_STR_Base + ((chara.Stat_STR_Final - chara.Stat_STR_Base) > 0 ?  " + " + (chara.Stat_STR_Final - chara.Stat_STR_Base) : ""), false, "tooltip_strength");
        stat_str.SetExternalTooltip(chara.Stats.Strength.ModStrings);

        stat_con.SetText("Constitution: " + chara.Stat_CON_Base + ((chara.Stat_CON_Final - chara.Stat_CON_Base) > 0 ? " + " + (chara.Stat_CON_Final - chara.Stat_CON_Base) : ""), false, "tooltip_constitution");
        stat_con.SetExternalTooltip(chara.Stats.Constitution.ModStrings);

        stat_psy.SetText("Psyche: " + chara.Stat_PSY_Base + ((chara.Stat_PSY_Final - chara.Stat_PSY_Base) > 0 ? " + " + (chara.Stat_PSY_Final - chara.Stat_PSY_Base) : ""), false, "tooltip_psyche");
        stat_psy.SetExternalTooltip(chara.Stats.Psyche.ModStrings);

        stat_will.SetText("Willpower: " + chara.Stat_WIL_Base + ((chara.Stat_WIL_Final - chara.Stat_WIL_Base) > 0 ? " + " + (chara.Stat_WIL_Final - chara.Stat_WIL_Base) : ""), false, "tooltip_willpower");
        stat_will.SetExternalTooltip(chara.Stats.Willpower.ModStrings);
        */

        // clean statGrid
        while (statsGrid.transform.childCount > 0) DestroyImmediate(statsGrid.transform.GetChild(0).gameObject);
        // clean traitsGrid
        while (traitsList.transform.childCount > 0) DestroyImmediate(traitsList.transform.GetChild(0).gameObject);

        var isDebug = scr_System_CampaignManager.current.DebugMode;
        // get stats grid
        foreach (var statDerived in chara.Stats.list_statsDerived)
        {
            if (!statDerived.Parent.isValidStatFor(chara.Stats)) continue;
            if (!isDebug && statDerived.Parent.noDisplay) continue;
            scr_HoverableText link = Instantiate(linkBox_resize).GetComponent<scr_HoverableText>();
            UI_Utility.Draw(statDerived, link);
            link.GetComponent<RectTransform>().SetParent(statsGrid, false);
        }

        // get traits grid
        foreach (var trait in chara.Traits)
        {
            if (!trait.isDisplayable) continue;
            scr_HoverableText link = Instantiate(linkBox_resize).GetComponent<scr_HoverableText>();
            link.SetText(trait.displayname, false, trait.ID);
            link.GetComponent<RectTransform>().SetParent(traitsList, false);
        }

        while (statusEXGrid.transform.childCount > 0) DestroyImmediate(statusEXGrid.transform.GetChild(0).gameObject);
        var siex_list = scr_System_CampaignManager.current.DebugMode ? chara.Stats.statusInstancesEx : chara.Stats.statusInstancesEx_Displayable;
        foreach (var stat in siex_list)
        {
            scr_HoverableText link = Instantiate(linkBox_resize).GetComponent<scr_HoverableText>();
            stat.Draw(link);
            link.GetComponent<RectTransform>().SetParent(statusEXGrid, false);

        }

        while (statusGrid.transform.childCount > 0) DestroyImmediate(statusGrid.transform.GetChild(0).gameObject);
        //int ii = 0;
        var si_list = scr_System_CampaignManager.current.DebugMode ? chara.Stats.StatusInstances : chara.Stats.StatusInstances_Displayable;
        foreach (Status_Instance i in si_list)
        {
            scr_HoverableText link = Instantiate(linkBox_resize).GetComponent<scr_HoverableText>();
            
            UI_Utility.Draw(i, link);
            link.GetComponent<RectTransform>().SetParent(statusGrid, false);
            //box.GetComponent<TMP_Text>().text = i.BaseRef.displayName + " : severity["+i.Severity + "] duration["+i.duration+"] displayName[" + i.SeverityDisplayName+"]";
        }

        if (scr_System_CentralControl.current.isSafeMode)
        {
            lust.gameObject.SetActive(false);
        }
        //if (ii == 0) statusGrid.transform.GetChild(0).gameObject.SetActive(true);
        //else statusGrid.transform.GetChild(0).gameObject.SetActive(false);
    }
}
