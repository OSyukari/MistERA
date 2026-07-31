using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;
using System.Linq;
using System;
using UnityEngine.EventSystems;

public class scr_bgImageSwapper : MonoBehaviour
{
    // Start is called before the first frame update
    public bool monitorCurrentRoom = false;

    /// <summary>
    /// The room-view instance (monitorCurrentRoom == true) that events can apply a SetBGImage override to.
    /// Other instances (e.g. the combat background) never register themselves here.
    /// </summary>
    public static scr_bgImageSwapper current = null;
    public Image darkCover;
    bool hasOverride = false;

    /// <summary>
    /// Reserved for future work: re-apply a historical entry's bg image override as the player scrolls
    /// past it in the logs panel. Intentionally unused for now - no ScrollRect tracking is wired up.
    /// </summary>
    public bool ReapplyOnScroll = false;

    private void Start()
    {
        image.color = disabledColor;
        if (monitorCurrentRoom)
        {
            current = this;
            scr_System_CampaignManager.current.Observer_CurrentRoom += OnRoomChange;
            scr_System_Time.current.Observer_globalTime_Day += OnDailyUpdate;
            OnRoomChange(0, scr_System_CampaignManager.current.CurrentRoom);
            scr_System_Time.current.Observer_globalTime_Hours += OnHourUpdate;
            scr_System_CampaignManager.current.Observer_CurrentViewMode += OnViewModeChange;
            scr_System_CampaignManager.current.Observer_MessageDisplay += ApplyOverride;
        }
    }

    private void OnViewModeChange(ViewMode vm, bool lockView)
    {
        ClearOverride();
    }

    /// <summary>
    /// Applies an event-driven background image override on top of the normal room background.
    /// Empty path clears the override and restores the normal room-driven image.
    /// </summary>
    public void ApplyOverride(UISpec spec)
    {
        //if (darkCover != null) darkCover.gameObject.SetActive(spec == null || spec.displayMode != LogsDisplayMode.AVG);
        var path = spec.BGImagePath;

        if (path == "")
        {
            ClearOverride();
            return;
        }

        hasOverride = true;
        if (lastImagePath != path)
        {
            lastImagePath = path;
            image.color = activeColor;
            if (co != null)
            {
                StopCoroutine(co);
                co = null;
            }
            co = StartCoroutine(roomchange(path));
        }
    }

    public void ClearOverride()
    {
        if (!hasOverride) return;
        hasOverride = false;
        lastImagePath = ""; // force reload even if it matches the last room image path
        OnRoomChange(0, scr_System_CampaignManager.current.CurrentRoom);
    }
    private void OnHourUpdate(TimeSpan t)
    {
        OnRoomChange(0, scr_System_CampaignManager.current.CurrentRoom);
    }

    private void OnDailyUpdate(int i)
    {
        scr_System_CentralControl.current.GetSprite(lastImagePath, out var sprite);
    }

    public Coroutine co = null;
    string lastImagePath = "";

    private void OnRoomChange(int updateSequence, Room_Instance room)
    {
        if (updateSequence != 0) return;
        if (!this.gameObject.activeInHierarchy) return;
        if (hasOverride) return; // event background override active, skip ambient room image updates
        if (room.Base.roomImagePath != "")
        {
            var imagepath = room.Base.roomImagePath;

            if (room.FactionOwner is Manageable)
            {
                var faction = room.FactionOwner as Manageable;

                if (faction == null)
                {

                }
                else if (room.ActivityState == RoomActivityState.AlwaysActive 
                    || (room.ActivityState == RoomActivityState.DayOnly && faction.IsActiveHour())
                    || (room.ActivityState == RoomActivityState.NightOnly && !faction.IsActiveHour()))
                {
                    // active
                    if (!faction.isWorldDay && room.Base.roomImagePath_Night != "") imagepath = room.Base.roomImagePath_Night;
                    // else default

                }
                else // inactive
                {
                    if (!faction.isWorldDay && room.Base.roomImagePath_Inactive_Night != "") imagepath = room.Base.roomImagePath_Inactive_Night;
                    else imagepath = room.Base.roomImagePath_Inactive;
                }
            }
            
            if (lastImagePath != imagepath)
            {
                lastImagePath = imagepath;
                image.color = activeColor;
                if (co != null)
                {
                    StopCoroutine(co);
                    co = null;
                }
                co = StartCoroutine(roomchange(imagepath));
            }
        }
        else
        {
            image.color = disabledColor;
        }
    }

    public IEnumerator roomchange(string a)
    {
        if (scr_System_CentralControl.current.GetSprite(a, out var sprite))
        {
            image.sprite = sprite;
        }
        else
        {
            Texture2D loaded = null;
            yield return AssetsLoader.LoadTextureCoroutine(a, texture => loaded = texture);
            image.sprite = scr_System_CentralControl.current.MakeSprite(a, loaded);
            image.preserveAspect = true;
        }
    }


    public Color32 disabledColor = new Color32(255, 255, 255, 0);
    public Color32 activeColor = new Color32(255, 255, 255, 255);
    public Image image, cover;

}
