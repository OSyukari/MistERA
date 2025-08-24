using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.UI;
using System.IO;
using Newtonsoft.Json.Bson;

public class scr_CharIconBox : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
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
    
    // Start is called before the first frame update
    private void Awake()
    {
        thisBox = this.GetComponent<RectTransform>();
        SelfBackground.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;

        internalSizeY = thisBox.sizeDelta.y;
        internalSizeX = thisBox.sizeDelta.x;
        update = 0.0f;

        aaBox = picture_AA.GetComponent<RectTransform>();
        picture_AA.gameObject.SetActive(false);

        //box_lust.gameObject.SetActive(false);
        box_Xray.gameObject.SetActive(false);
        box_ovum.gameObject.SetActive(false);

        scr_System_CampaignManager.current.Observer_CurrentTarget += ReadCurrentChar;
        scr_System_CampaignManager.current.Observer_UpdateNotice += OnUpdateNotice;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += OnPostUpdateTime3;
    }

    private void OnPostUpdateTime3()
    {
        if (this.chara_refID < 1 || scr_System_CampaignManager.current.GetCharaRoomInstance(chara_refID).RefID != scr_System_CampaignManager.current.CurrentRoom.RefID)
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

    private void OnUpdateNotice(bool b)
    {
        OnPostUpdateTime3();
    }

    private void ReadCurrentChar(int i)
    {
        if (this.chara_refID == i) nameBox.color = scr_System_CentralControl.current.DisplaySetting.TextColor_toggle.Color;
        else nameBox.color = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
    }

    Character_Trainable chara = null;
    int chara_refID = -1;
    Canvas canvas;
    public bool InitializeWithArgument(int refID, Canvas canvas)
    {
        this.canvas = canvas;
        chara_refID = refID;
        chara = scr_System_CampaignManager.current.FindInstanceByID(chara_refID) as Character_Trainable;
        if (chara != null && chara.RefID > 0)
        {
            Initialize();
            nameBox.text = chara.FirstName;
            updateImage();
            return true;
        }
        else
        {
            return false;
        }
            
    }
    /*
    private void readTXT(string path, TextMeshProUGUI box)
    {
        var sr = new StreamReader(Application.dataPath + "/" + path);
        var fileContents = sr.ReadToEnd();
        sr.Close();
        box.text = fileContents;
    }

    private Texture2D LoadTexture(string FilePath)
    {

        // Load a PNG or JPG file from disk to a Texture2D
        // Returns null if load fails

        Texture2D Tex2D;
        byte[] FileData;

        if (File.Exists(FilePath))
        {
            FileData = File.ReadAllBytes(FilePath);
            Tex2D = new Texture2D(2, 2);           // Create new "empty" texture
            if (Tex2D.LoadImage(FileData))           // Load the imagedata into the texture (size is set automatically)
                return Tex2D;                 // If data = readable -> return texture
        }
        return null;                     // Return null if load failed

    }*/

    //Texture2D SpriteTexture = null;
    //Sprite NewSprite;

    /*
    public void loadSprite(string path)
    {
        var image = this.picture;
        if (path == null||path=="")
        {
            image.sprite = SpriteAsset.transparent;
            return;
        }
        SpriteTexture = LoadTexture(Application.dataPath+"/"+path);
        NewSprite = Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), new Vector2(0, 0), 100.0f);
        image.sprite = NewSprite;

        // RESIZE IMAGE TO FIT MINIMUM
        float resize = 0.0f;
        bool resiz = false;
        var targetSizeX = pictureBox.sizeDelta.x;
        var targetSizeY = pictureBox.sizeDelta.y;

        image.SetNativeSize();
        //Debug.Log("image resize targetX [" + targetSizeX + "] targetY[" + targetSizeY + "] imageX ["+image.rectTransform.sizeDelta.x+"] imageY ["+ image.rectTransform.sizeDelta.y+"]");

        /*
        float x = image.rectTransform.sizeDelta.x * image.transform.localScale.x;
        float y = image.rectTransform.sizeDelta.y * image.transform.localScale.y;

        if (x + 0.01f < targetSizeX || y + 0.01f < targetSizeY)
        {
            resize = Mathf.Max(targetSizeX / (x + 0.01f), targetSizeY / (y + 0.01f));
            resiz = true;
        }
        else if ((x - 0.01f) > targetSizeX || (y - 0.01f) > targetSizeY)
        {
            resize = Mathf.Max(targetSizeX / (x - 0.01f), targetSizeY / (y - 0.01f));
            resiz = true;
        }

        if (resiz)
        {
            //Debug.Log("UpdateImage: resize localX " + aaBox.localScale.x + "*" + resize + " localY " + aaBox.localScale.y + "*" + resize + " localZ " + aaBox.localScale.z + "*" + resize);
            image.transform.localScale = new Vector3(resize, resize, resize);
        }
    }
*/


    private void Initialize()
    {

        picture.gameObject.SetActive(true);
        mouseOver = false;
    }

    public RectTransform prefab_Canvas_charaDetail;
    scr_Menu_CharaDetail detail;


    void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
    {
        if (this.chara != null)
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
        if (mouseOver != true)
        {
            //picture.gameObject.SetActive(true);
            //picture_AA.gameObject.SetActive(false);
            mouseOver = true;
            nameBox.color = scr_System_CentralControl.current.Color_hover;
            updateImage();

        }

    }

    public void ForceExit()
    {
        mouseOver = false;
        nameBox.color = scr_System_CentralControl.current.Color_neutral;

        if (scr_System_CampaignManager.current.CurrentTargetRef == chara_refID && !scr_System_CampaignManager.current.displaySex) nameBox.color = scr_System_CentralControl.current.DisplaySetting.TextColor_toggle.Color;
        else nameBox.color = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;

        updateImage();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (mouseOver != false) ForceExit();
    }

    Coroutine coroutine = null;

    private void updateImage()
    {
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
            coroutine = null;
        }
        if (!this.gameObject.activeInHierarchy) return;
        if (chara == null || chara.PortraitManager == null) return;
        coroutine = StartCoroutine(co_updateImage());
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


        yield return chara.PortraitManager.GetValidPortrait().DrawIcon(this);
        
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

        ScaleStatBar(hp, chara.Stats.HP);
        ScaleStatBar(mp, chara.Stats.MP);
        ScaleStatBar(st, chara.Stats.Stamina);
        ScaleStatBar(en, chara.Stats.Energy);

       // this.picture.SetNativeSize();
        //Debug.Log("refresh character " + chara.FirstName + " hp " + chara.Stats.HP.Value + "/" + chara.Stats.HP.MaxValue + " mp "+chara.Stats.MP.Value+"/"+ chara.Stats.MP.MaxValue+" st "+ chara.Stats.Stamina.Value+"/"+ chara.Stats.Stamina.MaxValue+" en "+ chara.Stats.Energy.Value+"/"+ chara.Stats.Energy.MaxValue);
    }

    public RectTransform hp, mp, st, en;

    private void ScaleStatBar(RectTransform bar, Stats_Derived_Extended_Instance stat)
    {
        var scale = bar.localScale;
        if (stat != null && stat.MaxValue > 0.2f) scale.x = stat.Value / stat.MaxValue;
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

    private void SetSprite(Image image, Texture2D SpriteTexture)
    {
        if (SpriteTexture == null)
        {
            if (image.sprite == null || image.sprite != SpriteAsset.transparent) image.sprite = SpriteAsset.transparent;
        }
        else
        {
            if (image.sprite == null || image.sprite.texture != SpriteTexture) UtilityEX.LoadSprite(SpriteTexture, image);
        }
        image.SetNativeSize();

    }
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
