using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.UI;
using System.IO;

public class scr_CharPortraitBox : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public Image picture;
    public scr_SpineLoader spineLoader;
    public RectTransform spineRect { get { return (spineLoader == null ? null : spineLoader.getLoaderRect); } }

    public RectTransform box_Xray;
    public RectTransform box_lust;
    public RectTransform box_ovum;

    public bool isCurrentTargetBox = false;

    // Start is called before the first frame update

    private void Awake()
    {

    }

    bool firstInit = true;

    private void Start()
    {
        box_lust.gameObject.SetActive(false);
        box_Xray.gameObject.SetActive(false);
        ActivateOvumBox(false);

        if (isCurrentTargetBox)
        {
            scr_System_CampaignManager.current.Observer_CurrentTarget += ReadCurrentChar;
            scr_System_CampaignManager.current.Observer_LogsCharaChange += ReadCurrentLogImage;
            scr_System_CampaignManager.current.Observer_CurrentViewMode += OnVMChange;

        }

        InitializeWithArgument(0, canvas);

    }

    private void OnVMChange(ViewMode vm, bool lockView)
    {
        if (vm == ViewMode.View_Room) ReadCurrentChar(scr_System_CampaignManager.current.CurrentTargetRef);
    }

    private void ReadCurrentLogImage(PortraitManager id)
    {
        //Debug.Log("ReadCurrentChar");
        if (id == null) return;
        else if (this.canvas == null) return;
        else if (scr_System_CampaignManager.current.CurrentViewMode != ViewMode.View_Logs) return;
        else CheckCharaChange(id);
    }

    private void ReadCurrentChar(int id)
    {
        //Debug.Log("ReadCurrentChar");
        if (id == -1) return;
        else if (this.canvas == null) return;
        else if (scr_System_CampaignManager.current.CurrentViewMode == ViewMode.View_Logs) return;
        else CheckCharaChange(id);
        
    }

    /*
    Character_Trainable chara { get
        {
            if (chara_refID < 0) return null;
            return scr_System_CampaignManager.current.FindInstanceByID(chara_refID);
        } }*/

    //int chara_refID = -1;
    PortraitManager portrait = null;
    Canvas canvas = null;
    public bool InitializeWithArgument(int refID, Canvas canvas)
    {
        this.canvas = canvas;
        if (refID < 0) return false;

        CheckCharaChange(refID);

        return true;
    }

    PortraitManager PreviousRef = null;

    private void CheckCharaChange(int refID)
    {
        var chara = scr_System_CampaignManager.current.FindInstanceByID(refID);
        CheckCharaChange(chara == null ? null : chara.PortraitManager);
    }
    private void CheckCharaChange(PortraitManager newPortrait)
    {
        //spineRect.gameObject.SetActive(false);
        //picture.gameObject.SetActive(true);
    

        PreviousRef = portrait;
        portrait = newPortrait;
        if (portrait != null)
        {
            if(portrait != PreviousRef) {
                // spineRect.gameObject.SetActive(false);
                if (spineRect != null)
                {
                    spineRect.gameObject.SetActive(false);
                }
                picture.gameObject.SetActive(true);
            }
            portrait.GetValidPortrait().DrawPortrait(this);
        }


        if (spineRect != null && firstInit)
        {   // somehow first activation of spine does not play animation correctly
            spineRect.gameObject.SetActive(false);
            firstInit = false;
            portrait.GetValidPortrait().DrawPortrait(this);
        }

        if(spineLoader != null)
        {
            if (spineRect != null) spineLoader.NotifyChange(spineRect);
            else spineLoader.NotifyChange(picture.rectTransform);
        }


        mouseOver = false;
        updateImage();
    }

    void OnDestroy()
    {
        //Debug.Log("scr_CharPortraitBox: OnDestroy Called!");
        //Destroy(SpriteTexture);
    }

    public RectTransform prefab_Canvas_charaDetail;
    scr_Menu_CharaDetail detail;

    void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
    {
        if (portrait != null) portrait.Click();

        /*
        if (eventData.button == PointerEventData.InputButton.Left && Utility.isClickBelowDragThreshold(eventData) && this.chara != null)
        {
            //Debug.Log("Mouse Click on [" + this.chara.baseID + "] refID [" + chara.referenceID + "]");

            detail = scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_Canvas_charaDetail, canvas.GetComponent<RectTransform>()).GetComponent<scr_Menu_CharaDetail>();
            detail.InitializeWithArgument(chara_refID);
        }*/
    }

    bool mouseOver;
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (mouseOver != true)
        {
            //picture.gameObject.SetActive(true);
            //picture_AA.gameObject.SetActive(false);
            mouseOver = true;
            updateImage();

        }

    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (mouseOver != false)
        {
            //picture.gameObject.SetActive(false);
            //picture_AA.gameObject.SetActive(true);
            mouseOver = false;

            updateImage();
        }

    }

    private void updateImage()
    {
        if (portrait == null)
        {
            picture.gameObject.SetActive(false);
            if(spineRect != null) spineRect.gameObject.SetActive(false);
            return;
        }

        //image.rectTransform.anchoredPosition = new Vector2(chara.portrait_offset_x*image.rectTransform.sizeDelta.x, chara.portrait_offset_y*image.rectTransform.sizeDelta.y);
        //picture.rectTransform.anchoredPosition = new Vector2(chara.portrait_offset_x*picture.rectTransform.sizeDelta.x, chara.portrait_offset_y*picture.rectTransform.sizeDelta.y);
        //picture.rectTransform.localScale = new Vector3(chara.portrait_offset_size, chara.portrait_offset_size, chara.portrait_offset_size);



        if (scr_System_CentralControl.current.adult)
        {
            box_lust.gameObject.SetActive(true);
        }

        if (scr_System_CentralControl.current.xray_mode > 0 && scr_System_CampaignManager.current.XrayMode)
        {
            box_Xray.gameObject.SetActive(true);
            switch (scr_System_CentralControl.current.xray_mode)
            {
                case (XRay_Mode.widget_first):
                    if (mouseOver) xray_eratw();
                    else xray_widget();
                    break;
                default:
                    xray_none();
                    break;
            }
            if (box_ovum.gameObject.activeInHierarchy) ActivateOvumBox(true);
        }
        else
        {
            box_lust.gameObject.SetActive(false);
            box_Xray.gameObject.SetActive(false);
            ActivateOvumBox(false);
        }

        //ScaleStatBar(hp, chara.Stats.HP);
        //ScaleStatBar(mp, chara.Stats.MP);
        //ScaleStatBar(st, chara.Stats.Stamina);
        //ScaleStatBar(en, chara.Stats.Energy);

        //Debug.Log("refresh character " + chara.FirstName + " hp " + chara.Stats.HP.Value + "/" + chara.Stats.HP.MaxValue + " mp "+chara.Stats.MP.Value+"/"+ chara.Stats.MP.MaxValue+" st "+ chara.Stats.Stamina.Value+"/"+ chara.Stats.Stamina.MaxValue+" en "+ chara.Stats.Energy.Value+"/"+ chara.Stats.Energy.MaxValue);
    }

    public RectTransform hp, mp, st, en;

    private void ScaleStatBar(RectTransform bar, Stats_Derived_Extended_Instance stat)
    {
        var scale = bar.localScale;
        if (stat != null && stat.MaxValue > 0.2f) scale.x = stat.ValuePercentile;
        else scale.x = 0f;
        //Debug.Log("scale stat bar : " + stat.DisplayName + " " + stat.Value + "/" + stat.MaxValue);
        bar.localScale = scale;
    }


    private void xray_widget()
    {
        xray_B.GetComponent<Image>().sprite = null;
        xray_W.GetComponent<Image>().sprite = null;


        if (true)//chara.Template.isMale && !scr_System_CentralControl.current.gay)
        {
            xray_A.GetComponent<Image>().sprite = null;
            xray_M.GetComponent<Image>().sprite = null;
        }
        else
        {
            Utility.LoadSprite(XraySprite.widget_ass0, xray_A.GetComponent<Image>());
            Utility.LoadSprite(XraySprite.widget_oral0, xray_M.GetComponent<Image>());
        }

        if (false)//chara.Template.isFemale)
        {
            ActivateOvumBox(true);
            Utility.LoadSprite(XraySprite.rjw_egg1, box_ovum.GetComponent<Image>());
            Utility.LoadSprite(XraySprite.widget_vag1, xray_V.GetComponent<Image>());
        }
        else
        {
            ActivateOvumBox(false);
            xray_V.GetComponent<Image>().sprite = null;
        }

    }

    private void ActivateOvumBox(bool toggle)
    {

        if (false)//toggle)
        {
            /*
            box_ovum.gameObject.SetActive(true);
            if (chara.Womb != null)
            {
                if (chara.Womb.State == MenstruationStatus.PreOvulation) Utility.LoadSprite(XraySprite.rjw_ovary3, box_ovum.GetComponent<Image>());
                else if (chara.Womb.State == MenstruationStatus.Ovulation) Utility.LoadSprite(XraySprite.rjw_egg1, box_ovum.GetComponent<Image>());
                else if (chara.Womb.State == MenstruationStatus.Insemination) Utility.LoadSprite(XraySprite.rjw_eggInseminate3, box_ovum.GetComponent<Image>());
                else if (chara.Womb.State == MenstruationStatus.Pregnant) Utility.LoadSprite(XraySprite.rjw_eggPlanted, box_ovum.GetComponent<Image>());
                else Utility.LoadSprite(null, box_ovum.GetComponent<Image>());
            }
            else
            {
                Utility.LoadSprite(null, box_ovum.GetComponent<Image>());
            }
            */
        }
        else
        {
            box_ovum.gameObject.SetActive(false);
        }
    }

    private void xray_eratw()
    {
        xray_B.GetComponent<Image>().sprite = null;
        xray_M.GetComponent<Image>().sprite = null;

        /*
        if (chara.Template.isMale && !scr_System_CentralControl.current.gay) xray_A.GetComponent<Image>().sprite = null;
        else Utility.LoadSprite(XraySprite.eratw_a1, xray_A.GetComponent<Image>());

        if (chara.Template.isFemale)
        {
            ActivateOvumBox(true);
            Utility.LoadSprite(XraySprite.rjw_egg1, box_ovum.GetComponent<Image>());
            Utility.LoadSprite(XraySprite.eratw_w1, xray_W.GetComponent<Image>());
            Utility.LoadSprite(XraySprite.eratw_v1, xray_V.GetComponent<Image>());
        }
        else
        {
            ActivateOvumBox(false);
            xray_W.GetComponent<Image>().sprite = null;
            xray_V.GetComponent<Image>().sprite = null;
        }*/
    }


    private void xray_none()
    {
        ActivateOvumBox(false);
        xray_A.GetComponent<Image>().sprite = null;
        xray_B.GetComponent<Image>().sprite = null;
        xray_M.GetComponent<Image>().sprite = null;
        xray_W.GetComponent<Image>().sprite = null;
        xray_V.GetComponent<Image>().sprite = null;
    }

    public RectTransform xray_B, xray_M, xray_W, xray_V, xray_A;

    
}
