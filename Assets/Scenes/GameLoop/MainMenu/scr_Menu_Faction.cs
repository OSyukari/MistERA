using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using TMPro;

public class scr_Menu_Faction : MonoBehaviour
{


    public TMP_Text factionName;
    public TMP_Text factionResources;
    public scr_HoverableText factionPopulation;
    public scr_HoverableText canEat, canSleep, isRecording;


    // Start is called before the first frame update
    void Start()
    {

        scr_System_CampaignManager.current.Observer_UpdateNotice += observerUpdate;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += timeUpdate;

        factionPop = LocalizeDictionary.QueryThenParse("ui_management_topbar_population");
        factionRes = LocalizeDictionary.QueryThenParse("ui_management_topbar_resources");
        factionPopTooltip = LocalizeDictionary.QueryThenParse("ui_management_topbar_tooltip_populationMaintenance");

        refreshFaction();
    }

    string factionPop, factionRes, factionPopTooltip;
    private void observerUpdate(bool b)
    {
        refreshFaction();
    }

    private void timeUpdate()
    {
        refreshFaction();
    }

    //private Manageable targetFaction = null;

    string sleepname = "";

    Manageable previousFaction = null;
    int previousHour = -1, currentHour = -1;
    Character_Trainable player;

    private string GetTagString(string tag)
    {
        return LocalizeDictionary.QueryThenParse("tag_" + tag);
    }

    private void refreshFaction()
    {
        //if (scr_UpdateHandler.current.Updating && !scr_UpdateHandler.current.isLastUpdate()) return;
        //Debug.Log("CAMPAIGNMANAGER NOTIFY UPDATE -> refreshFaction");
        var currentroom = scr_System_CampaignManager.current.CurrentRoom;
        var targetFaction = currentroom?.FactionOwner?.FactionOwnerRoot;

        if (targetFaction == null)
        {
            //Debug.Log("Null faction skipping update");
            return;
        }

        factionName.text = targetFaction.FactionDisplayName;

        bool isPlayerManaged = targetFaction.isPlayerFaction;

        if (isPlayerManaged)
        {
            Dictionary<string, int> costChara = targetFaction.GetMaintenanceCost_Chara();

            List<string> s_chara = new List<string>();
            foreach (KeyValuePair<string, int> kvp in costChara)
            {
                s_chara.Add($"{GetTagString(kvp.Key)} {kvp.Value.ToString("+0;-#")}");
            }
            string extraTooltip = factionPopTooltip.Replace("$costs$", String.Join(" | ", s_chara));
            factionPopulation.SetExternalTooltip(extraTooltip);

            List<string> values = new List<string>();
            FactionUtility.ParseMaintenanceCost(values, targetFaction.GetMaintenanceCost_Total);
            factionResources.gameObject.SetActive(true);
            factionResources.text = factionRes.Replace("$resources$", String.Join(" | ", values));
        }
        else
        {
            factionPopulation.SetExternalTooltip("");
            factionResources.gameObject.SetActive(false);
        }

        factionPopulation.SetText(factionPop.Replace("$population$", targetFaction.ManagedChara.Count.ToString()));

        currentHour = scr_System_Time.current.getCurrentTime().Hour;
        player = scr_System_CampaignManager.current.Player;

        if (previousFaction != targetFaction || previousHour != currentHour)
        {
            canEat.gameObject.SetActive(targetFaction.mealHours.Contains(currentHour));

            if (player.FactionManager.HasSleepSchedule)
            {
                if (sleepname != "") canSleep.gameObject.SetActive(player.FactionManager.CurrentJobName(currentHour) == sleepname);
                else
                {
                    var com = player.FactionManager.CurrentJobPost(currentHour).getRandCOM;
                    if (com != null && com.ID == "com_furniture_sleep")
                    {
                        sleepname = player.FactionManager.CurrentJobName(currentHour);

                    }
                    else
                    {
                        canSleep.gameObject.SetActive(false);
                    }
                }
            }
        }

        previousHour = currentHour;
        previousFaction = targetFaction;

        if (currentroom == null || !currentroom.HasRecording) isRecording.gameObject.SetActive(false);
        else isRecording.gameObject.SetActive(true);
    }
}
