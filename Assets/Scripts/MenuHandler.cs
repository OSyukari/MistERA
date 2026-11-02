using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class MenuHandler : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public bool ObserveGameMode = true;
    private scr_System_CampaignManager campaignManager;

    void Awake()
    {
        if (ObserveGameMode)
        {
            campaignManager = scr_System_CampaignManager.current;
            campaignManager.Observer_CurrentViewMode += OnViewModeChange;
            campaignManager.Observer_UpdateNotice += OnUpdateNotice;
            campaignManager.CanvasAnchor = this;
        }
        else
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (!ReferenceEquals(campaignManager, null))
        {
            campaignManager.Observer_CurrentViewMode -= OnViewModeChange;
            campaignManager.Observer_UpdateNotice -= OnUpdateNotice;
            if (ReferenceEquals(campaignManager.CanvasAnchor, this)) campaignManager.CanvasAnchor = null;
            campaignManager = null;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public List<CanvasGroup> roomView;
    public List<CanvasGroup> logsView;
    public CanvasGroup Self;
    public RectTransform PanelAnchor;
    public RectTransform PanelAnchor_AlwaysEnable;
    public RectTransform backgroundRect;

    private void OnUpdateNotice(bool i)
    {
        OnViewModeChange(scr_System_CampaignManager.current.CurrentViewMode, false);
    }

    private void OnViewModeChange(ViewMode vm, bool lockView)
    {
        /*
        if (vm == ViewMode.View_Combat)
        {
            viewDisable(Self, false);
            backgroundRect.gameObject.SetActive(false);
        }
        else
        {*/
        viewEnable(Self, false);
            backgroundRect.gameObject.SetActive(true);

            switch (vm)
            {
                case ViewMode.View_Room:
                    foreach (CanvasGroup group in logsView) viewDisable(group);
                    foreach (CanvasGroup group in roomView) viewEnable(group);
                    break;
                case ViewMode.View_Logs:
                    foreach (CanvasGroup group in roomView) viewDisable(group);
                    foreach (CanvasGroup group in logsView) viewEnable(group);
                    break;
                default:
                    break;
            }
        //}
    }


    private void viewEnable(CanvasGroup group, bool disableObject = true)
    {
        if (disableObject)
        {
            group.gameObject.SetActive(true);
        }

        group.alpha = 1;
        group.interactable = true;
        group.blocksRaycasts = true;
    }

    private void viewDisable(CanvasGroup group, bool disableObject = true)
    {
        group.alpha = 0;
        group.interactable = false;
        group.blocksRaycasts = false;

        if (disableObject)
        {
            group.gameObject.SetActive(false);
        }
    }
}
