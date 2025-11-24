using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.UI;
using System.IO;

public class scr_CharIconBox : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public bool isCombatBox = false;
    public CombatStatManager CombatStats = null;

    public TextMeshProUGUI picture_AA;
    public Image picture;
    public RectTransform pictureBox;

    public TextMeshProUGUI nameBox;
    private RectTransform aaBox;

    public RectTransform box_Xray;
    public RectTransform box_lust;

    public RectTransform statBox;

    private float internalSizeY;
    private float internalSizeX;
    private RectTransform thisBox;
    public Image SelfBackground;

    public scr_HoverableText attitudeBox;

    // Start is called before the first frame update
    private void Awake()
    {
        thisBox = this.GetComponent<RectTransform>();

        internalSizeY = thisBox.sizeDelta.y;
        internalSizeX = thisBox.sizeDelta.x;
        update = 0.0f;

        aaBox = picture_AA.GetComponent<RectTransform>();
        picture_AA.gameObject.SetActive(false);

        //box_lust.gameObject.SetActive(false);
        box_Xray.gameObject.SetActive(false);
        box_ovum.gameObject.SetActive(false);


    }
    public void CombatRefresh(I_StatsManager stats, bool forceRefresh = false)
    {
        if (!isCombatBox) return;
        chara.PortraitManager.DrawCombatIcon(stats,this,forceRefresh);
    }

    private void OnPostUpdateTime3()
    {
        bool destroy = false;

        if (!isCombatBox)
        {
            var room = scr_System_CampaignManager.current.GetCharaRoomInstance(chara_refID);
            if (room == null || room.RefID != scr_System_CampaignManager.current.CurrentRoom.RefID)
            {
                destroy = true;
            }
        }


        if (destroy)
        {
            scr_System_CampaignManager.current.Observer_CurrentTarget -= ReadCurrentChar;
            scr_UpdateHandler.current.Observer_PostUpdateTime_3 -= OnPostUpdateTime3;
            scr_System_CampaignManager.current.Observer_UpdateNotice -= OnUpdateNotice;

            Destroy(this.gameObject);
        }
        else
        {
            updateImage();
        }
    }

    public void OnUpdateNotice(bool b = false)
    {
        OnPostUpdateTime3();
    }

    private void ReadCurrentChar(int i, bool foceUpdate)
    {
        if (!isCombatBox)
        {
            if (this.chara_refID == i) nameBox.color = scr_System_CentralControl.current.DisplaySetting.TextColor_toggle.Color;
            else nameBox.color = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
        }
        updateImage();
    }

    Character_Trainable chara = null;
    int chara_refID = -1;
    Canvas canvas;


    public bool InitializeWithArgument(int refID, Canvas canvas, CombatStatManager combatHandler = null)
    {
        this.CombatStats = combatHandler;
        if (this.CombatStats != null) isCombatBox = true;

        nameBox.gameObject.SetActive(!this.isCombatBox);
        rect_Combat.gameObject.SetActive(this.isCombatBox);
        rect_nonCombat.gameObject.SetActive(!this.isCombatBox);
        box_ovum.gameObject.SetActive(false);

        this.canvas = canvas;
        chara_refID = refID;
        chara = scr_System_CampaignManager.current.FindInstanceByID(chara_refID) as Character_Trainable;
        if (chara != null)
        {
            Initialize();
            nameBox.text = chara.FirstName;
            if (!isCombatBox) ReadCurrentChar(scr_System_CampaignManager.current.CurrentTargetRef, true);
            updateImage();
            return true;
        }
        else
        {
            return false;
        }
            
    }
    private void Initialize()
    {

        if (!isCombatBox) SelfBackground.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;

        picture.gameObject.SetActive(true);

        if (mp.selfRect.gameObject.activeInHierarchy &&( chara.Stats.MP == null || chara.Stats.MP.MaxValue < 1))
        {
            mp.selfRect.gameObject.SetActive(false);
        }


        if (!isCombatBox)
        {
            scr_System_CampaignManager.current.Observer_CurrentTarget += ReadCurrentChar;
            scr_System_CampaignManager.current.Observer_UpdateNotice += OnUpdateNotice;
            scr_UpdateHandler.current.Observer_PostUpdateTime_3 += OnPostUpdateTime3;
        }
    }

    public RectTransform prefab_Canvas_charaDetail;
    scr_Menu_CharaDetail detail;


    void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
    {
        if (this.chara != null && !isCombatBox)
        {
            if (scr_System_CampaignManager.current.CurrentTargetRef != chara_refID)
            {
                scr_System_CampaignManager.current.ChangeCurrentTarget(chara_refID);
            }
            else if (!scr_System_CampaignManager.current.displaySex)
            {
                scr_System_CampaignManager.current.ChangeCurrentTarget(0);
            }           
        }
    }

    bool mouseOver;
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isCombatBox)
        {
            if (nameBox.gameObject.activeInHierarchy) nameBox.color = scr_System_CentralControl.current.Color_hover;
            updateImage();

        }

    }

    public void ForceExit()
    {
        if (nameBox.gameObject.activeInHierarchy)
        {
            nameBox.color = scr_System_CentralControl.current.Color_neutral;

            if (scr_System_CampaignManager.current.CurrentTargetRef == chara_refID && !scr_System_CampaignManager.current.displaySex) nameBox.color = scr_System_CentralControl.current.DisplaySetting.TextColor_toggle.Color;
            else nameBox.color = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
        }
        updateImage();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isCombatBox) ForceExit();
    }

    Coroutine coroutine = null;

    private void updateImage()
    {
        if (!this.gameObject.activeInHierarchy) return;
        if (chara == null || chara.PortraitManager == null) return;
        if ( chara != null && chara.PortraitManager != null)
        {
            if (!isCombatBox) chara.PortraitManager.DrawIcon(this);
        }


        if (scr_System_CentralControl.current.xray_mode > 0 && scr_System_CampaignManager.current.XrayMode)
        {

            if (box_Xray.gameObject.activeInHierarchy)
            {
                //RefreshXray();
            }
            else
            {
                box_Xray.gameObject.SetActive(true);
                box_ovum.gameObject.SetActive(true);
            }
        }
        else
        {
            //if (box_lust.gameObject.activeInHierarchy) box_lust.gameObject.SetActive(false);
            if (box_Xray.gameObject.activeInHierarchy) box_Xray.gameObject.SetActive(false);
            if (box_ovum.gameObject.activeInHierarchy) box_ovum.gameObject.SetActive(false);
        }

        var cStats = CombatStats;

        if (cStats != null && hp.selfRect.gameObject.activeInHierarchy)
        {
            cStats.HP.Draw(hp.text);
            ScaleStatBar(hp.bar, cStats.HP);
        }
        if (cStats != null && mp.selfRect.gameObject.activeInHierarchy)
        {
            cStats.MP.Draw(mp.text);
            ScaleStatBar(mp.bar, cStats.MP);
        }
        if (st.selfRect.gameObject.activeInHierarchy) ScaleStatBar(st.bar, chara.Stats.Stamina);
        if (en.selfRect.gameObject.activeInHierarchy) ScaleStatBar(en.bar, chara.Stats.Energy);
        if (cStats != null && posture.selfRect.gameObject.activeInHierarchy)
        {
            CombatUtility.DrawPosture(cStats, posture.text);
            ScaleStatBar(posture.bar, cStats.Posture, cStats.MaxPosture);
        }

        Character_Relationship rel = chara.Relationships.FindRelationshipWith(0);
        if (rel != null)
        {
            RelationshipManager.Draw_Attitude(rel, attitudeBox);// rel.DrawAttitude(attitudeBox);
                                                                // RelationshipManager.Draw_Obedience(rel, obedienceBox);// rel.DrawObedience(obedienceBox);
        }

    }


    public PortraitManager.CharaPortrait currentHandler = null;
    public string currentIcon = "";
    public Coroutine currentlyRunning = null;
    public void Draw(IEnumerator routine)
    {
        if (currentlyRunning != null)
        {
            StopCoroutine(currentlyRunning);
            currentlyRunning = null;
        }
        currentlyRunning = StartCoroutine(routine);
    }

    private void OnEnable()
    {
        updateImage();
    }

    /// <summary>
    /// Run Every Update 
    /// </summary>
    private IEnumerator co_updateImage()
    {
        if (scr_System_CampaignManager.current.ColdLoad) yield break;
        
        
                
       // this.picture.SetNativeSize();
        //Debug.Log("refresh character " + chara.FirstName + " hp " + chara.Stats.HP.Value + "/" + chara.Stats.HP.MaxValue + " mp "+chara.Stats.MP.Value+"/"+ chara.Stats.MP.MaxValue+" st "+ chara.Stats.Stamina.Value+"/"+ chara.Stats.Stamina.MaxValue+" en "+ chara.Stats.Energy.Value+"/"+ chara.Stats.Energy.MaxValue);
    }

    public statBar hp, mp, st, en, posture;
    public RectTransform rect_nonCombat, rect_Combat;

    private void ScaleStatBar(RectTransform bar, Stats_Derived_Extended_Instance stat)
    {
        var scale = bar.localScale;
        if (stat != null && stat.MaxValue > 0.2f) scale.x = stat.Value / stat.MaxValue;
        else scale.x = 0f;
        //Debug.Log("scale stat bar : " + stat.DisplayName + " " + stat.Value + "/" + stat.MaxValue);
        bar.localScale = scale;
    }

    private void ScaleStatBar(RectTransform bar, float value, float maxValue)
    {
        var scale = bar.localScale;
        if (maxValue > 0.2f) scale.x = value / maxValue;
        else scale.x = 0f;
        //Debug.Log("scale stat bar : " + stat.DisplayName + " " + stat.Value + "/" + stat.MaxValue);
        bar.localScale = scale;
    }

    /*
    private void RefreshXray()
    {
        switch (scr_System_CentralControl.current.xray_mode)
        {
            case (XRay_Mode.widget_first):
                if (mouseOver)
                {
                    xray_eratw();
                }
                else
                {
                    xray_widget();
                }
                break;
            default:
                xray_none();
                break;
        }
        RefreshOvumBox();
    }
    */
    /*
private void xray_widget()
{
    SetSprite(xray_B, null);
    SetSprite(xray_W, null);

    if (scr_System_CampaignManager.current.XrayMode)
    {
        SetSprite(xray_M, XraySprite.widget_oral0);
    }
    else
    {
        SetSprite(xray_M, null);
    }

    if (scr_System_CampaignManager.current.XrayMode || chara.Body.GetRevealingScoreByTag("anus") < 2)
    {
        SetSprite(xray_A, XraySprite.widget_ass0);
    }
    else
    {
        SetSprite(xray_A, null);
    }

    if (scr_System_CampaignManager.current.XrayMode || chara.Body.GetRevealingScoreByTag("vagina") < 2)
    {
        SetSprite(xray_V, XraySprite.widget_vag1);
    }
    else
    {
        SetSprite(xray_V, null);
    }
}

private void xray_eratw()
{

    SetSprite(xray_B, null);
    SetSprite(xray_M, null);


    if (scr_System_CampaignManager.current.XrayMode && chara.Body.HasBodyTag(new List<string>() { "womb" }))
    {
        SetSprite(xray_W, XraySprite.eratw_w1);
    }
    else
    {
        SetSprite(xray_W, null);
    }

    if (scr_System_CampaignManager.current.XrayMode || chara.Body.GetRevealingScoreByTag("anus") < 2)
    {
        SetSprite(xray_A, XraySprite.eratw_a1);
    }
    else
    {
        SetSprite(xray_A, null);
    }

    if (scr_System_CampaignManager.current.XrayMode || chara.Body.GetRevealingScoreByTag("vagina") < 2)
    {
        SetSprite(xray_V, XraySprite.eratw_v1);
    }
    else
    {
        SetSprite(xray_V, null);
    }
}

private void RefreshOvumBox()
{

    if (chara.Womb != null && scr_System_CampaignManager.current.XrayMode)
    {
        if (chara.Womb.Pregnancy != -1) SetSprite(box_ovum, XraySprite.rjw_egg1);
        else if (chara.Womb.State == MenstruationStatus.PreOvulation) SetSprite(box_ovum, XraySprite.rjw_ovary3);
        else if (chara.Womb.State == MenstruationStatus.Insemination) SetSprite(box_ovum, XraySprite.rjw_eggInseminate3); 
        else if (chara.Womb.State == MenstruationStatus.Pregnant) SetSprite(box_ovum, XraySprite.rjw_eggPlanted); 
        else SetSprite(box_ovum, null);
    }
    else
    {
        SetSprite(box_ovum, null);
    }

}


private void xray_none()
{
    SetSprite(xray_A, null);
    SetSprite(xray_B, null);
    SetSprite(xray_M, null);
    SetSprite(xray_W, null);
    SetSprite(xray_V, null);
}


*/
    public Image xray_B, xray_M, xray_W, xray_V, xray_A, box_ovum;

    private float update;

}
