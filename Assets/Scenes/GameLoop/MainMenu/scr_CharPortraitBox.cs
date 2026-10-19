using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class scr_CharPortraitBox : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image picture;
    public Image picture_landscape;
    public CanvasGroup picture_landscape_group;
    /// <summary>
    /// Optional, on the picture object. Used (stretch to fit) for image portraits whose x/y/size offsets are all default (0).
    /// Keep it disabled in the prefab: its anchors are recorded on Awake and restored when manual offsets are used.
    /// </summary>
    public AspectRatioFitter picture_fitter;
    protected Vector2 _pictureAnchorMin, _pictureAnchorMax;
    public scr_SpineLoader spineLoader;
    /// <summary>
    /// Optional animated .webp player: its own slot, a sibling of picture laid out like it (same anchors/pivot in the prefab).
    /// Activated only while it draws/shows; every other draw path deactivates it. Its optional fitter is used like picture_fitter
    /// (keep it disabled in the prefab). Null: animated webp is drawn as its first frame through picture.
    /// </summary>
    public scr_WebpPlayer webpPlayer;
    /// <summary>
    /// Optional animated .webp player for the landscape slot: inside picture_landscape_group, covering picture_landscape (fixed, no offsets).
    /// While it shows, the group is visible (isShowingLandscape) and picture_landscape is deactivated.
    /// Null: landscape animated webp is drawn as its first frame through picture_landscape.
    /// </summary>
    public scr_WebpPlayer webpPlayer_landscape;
    protected bool _showingWebp = false;
    protected bool _showingWebpLandscape = false;
    protected Vector2 _webpAnchorMin, _webpAnchorMax;
    protected RectTransform WebpRect { get { return webpPlayer == null ? null : webpPlayer.transform as RectTransform; } }
    public Transform spineRect { get { return (spineLoader == null ? null : spineLoader.GetLoaderRect); } }

    public RectTransform box_Xray;
    public RectTransform box_lust;
    public RectTransform box_ovum;

    public bool InvertXAxis = false;

    public bool isCurrentTargetBox = false;
    public bool isCurrentTargetEXBox = false;
    public bool isCombatBox = false;
    public bool isBannerBox = false;

    // Start is called before the first frame update
    protected Character_Trainable currentChara = null;

    private void Awake()
    {
        if (picture_fitter != null && picture != null)
        {
            _pictureAnchorMin = picture.rectTransform.anchorMin;
            _pictureAnchorMax = picture.rectTransform.anchorMax;
        }
        if (webpPlayer != null && webpPlayer.fitter != null)
        {
            _webpAnchorMin = WebpRect.anchorMin;
            _webpAnchorMax = WebpRect.anchorMax;
        }

        if (isCurrentTargetBox)
        {
            scr_System_CampaignManager.current.Observer_CurrentTarget += ReadCurrentChar;
            scr_System_CampaignManager.current.Observer_LogsCharaChange += ReadCurrentLogImage;
            scr_System_CampaignManager.current.Observer_CurrentViewMode += OnVMChange;
            scr_System_CampaignManager.current.Observer_UpdateCurrentTargetAnchor += OnAnchorChange;
            scr_System_CampaignManager.current.Observer_CurrentTargetClick += OnPointerClick;
        }
        if (isCurrentTargetEXBox)
        {
            scr_System_CampaignManager.current.Observer_CurrentTargetEX += ReadCurrentChar;
            scr_System_CampaignManager.current.Observer_UpdateCurrentTargetAnchor += OnAnchorChange;
            if(!isBannerBox) scr_System_CampaignManager.current.CurrentTargetEX_Box = this;
        }
    }

    bool firstInit = true;

    bool active = true;

    private void Start()
    {
       // box_lust.gameObject.SetActive(false);
       // box_Xray.gameObject.SetActive(false);
       // ActivateOvumBox(false);

        InitializeWithArgument(0);
    }

    
    private void OnAnchorChange(PortraitManager.CharaPortrait p)
    {
        if (this.currentHandler != p) return;
        UpdateAnchor(p);
    }

    private void OnVMChange(ViewMode vm, bool lockView)
    {
        if (this.isCurrentTargetBox && vm == ViewMode.View_Room) ReadCurrentChar(scr_System_CampaignManager.current.CurrentTargetRef, false);
    }

    private void ReadCurrentLogImage(PortraitManager id, I_hasPortrait handler)
    {
        //Debug.Log("ReadCurrentChar");
        if (id == null) return;
        else if (!this.gameObject.activeInHierarchy) return;
        else if (scr_System_CampaignManager.current.CurrentViewMode != ViewMode.View_Logs) return;
        else CheckCharaChange(id, handler);
    }

    private void ReadCurrentChar(int id, bool foceUpdate)
    {
        //Debug.Log("ReadCurrentChar");
        if (id == -1) return;
        else if (!this.gameObject.activeInHierarchy) return;
        else if (this.isCurrentTargetBox && scr_System_CampaignManager.current.CurrentViewMode != ViewMode.View_Room) return;
        //else if (scr_System_CampaignManager.current.CurrentViewMode == ViewMode.View_Logs) return;

        if (foceUpdate)
        {
            currentHandler = null;
            currentPortrait = "";
        }
        CheckCharaChange(id);
    }
    /*
    Character_Trainable chara { get
        {
            if (chara_refID < 0) return null;
            return scr_System_CampaignManager.current.FindInstanceByID(chara_refID);
        } }*/

    //int chara_refID = -1;
    PortraitManager portrait = null;
   // Canvas canvas = null;
    public bool InitializeWithArgument(int refID)
    {
        if (refID < 0) return false;
        CheckCharaChange(refID);
        return true;
    }
    public bool InitializeWithArgument(Character_Trainable chara)
    {
        if (chara == null) return false;
        CheckCharaChange(chara);
        return true;
    }

    public bool InitializeWithArgument(Character_SerializableBase template)
    {
        if (template == null)
        {
# if UNITY_EDITOR
            Debug.LogError("error serialized base null");
#endif
            return false;
        }
        else
        {
            CheckCharaChange(template.Portrait);
            return true;
        }
    }


    PortraitManager PreviousRef = null;

    private void CheckCharaChange(int refID)
    {
        //Debug.Log($"CheckCharaChange {refID}");
        var chara = scr_System_CampaignManager.current.FindInstanceByID(refID);
        CheckCharaChange(chara == null ? null : chara.PortraitManager);
    }
    private void CheckCharaChange(Character_Trainable chara)
    {
        CheckCharaChange(chara == null ? null : chara.PortraitManager);
    }

    public Coroutine currentlyRunning = null;

    /// <summary>
    /// True while the landscape slot is the visible image (every draw path sets the group alpha).
    /// Landscape images are fixed: no offsets, no drag.
    /// </summary>
    public bool isShowingLandscape { get { return picture_landscape_group != null && picture_landscape_group.alpha > 0; } }

    public void UpdateAnchor(PortraitManager.CharaPortrait p)
    {
        if (picture == null || picture.rectTransform == null) return;
        if (isShowingLandscape) return;
        //Debug.Log($"update anchor {x} {y} {size}");
        if (isShowingWebp)
        {
            UpdateWebpAnchor(p);
        }
        else if (UsePictureFitter(p))
        {
            picture.rectTransform.localScale = Vector3.one;
            picture_fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            picture_fitter.aspectRatio = picture.sprite.rect.width / picture.sprite.rect.height;
            picture_fitter.enabled = true;
            picture.rectTransform.localPosition = new Vector3(picture.rectTransform.localPosition.x, picture.rectTransform.localPosition.y, 0);
        }
        else
        {
            if (picture_fitter != null)
            {
                // fitter stretches the anchors, put them back before manual offsets
                picture_fitter.enabled = false;
                picture.rectTransform.anchorMin = _pictureAnchorMin;
                picture.rectTransform.anchorMax = _pictureAnchorMax;
            }
            // size 0 marks "auto" (fitter); without the fitter fall back to native size
            float size = Mathf.Approximately(p.portrait_offset_size, 0) ? 1 : p.portrait_offset_size;
            picture.SetNativeSize();
            picture.rectTransform.localScale = new Vector3(size, size, size);
            picture.rectTransform.anchoredPosition = new Vector2(p.portrait_offset_x, p.portrait_offset_y);
            picture.rectTransform.localPosition = new Vector3(picture.rectTransform.localPosition.x, picture.rectTransform.localPosition.y, 0);

            if (this.spineRect != null)
            {
                spineRect.localScale = new Vector3(size, size, size);
                spineRect.position = new Vector2(p.portrait_offset_x, p.portrait_offset_y);
                spineRect.localPosition = new Vector3(picture.rectTransform.localPosition.x, picture.rectTransform.localPosition.y, 0);
            }
        }

        this.spineLoader.SelfRect.localScale = new Vector3(InvertXAxis && p.AllowXAxisFlip ? - 1 : 1, 1, 1);

    }

    /// <summary>
    /// Fitter only for image portraits (spine positions itself from the picture rect) with untouched x/y/size offsets (all 0).
    /// </summary>
    protected bool UsePictureFitter(PortraitManager.CharaPortrait p)
    {
        if (picture_fitter == null || !(p is PortraitManager.CharaPortrait_Image)) return false;
        if (picture.sprite == null || picture.sprite.rect.height <= 0) return false;
        return HasDefaultOffsets(p);
    }

    protected static bool HasDefaultOffsets(PortraitManager.CharaPortrait p)
    {
        return Mathf.Approximately(p.portrait_offset_x, 0) && Mathf.Approximately(p.portrait_offset_y, 0) && Mathf.Approximately(p.portrait_offset_size, 0);
    }

    /// <summary>Same layout rules as the picture slot (fitter for default offsets, else native size + offsets), on the webp slot.</summary>
    protected void UpdateWebpAnchor(PortraitManager.CharaPortrait p)
    {
        // size unknown while the player reloads (eg. box re-enabled): keep the current layout, it is still valid
        if (webpPlayer.Height <= 0) return;
        var rect = WebpRect;
        var fitter = webpPlayer.fitter;
        if (fitter != null && HasDefaultOffsets(p))
        {
            rect.localScale = Vector3.one;
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = (float)webpPlayer.Width / webpPlayer.Height;
            fitter.enabled = true;
        }
        else
        {
            if (fitter != null)
            {
                // fitter stretches the anchors, put them back before manual offsets
                fitter.enabled = false;
                rect.anchorMin = _webpAnchorMin;
                rect.anchorMax = _webpAnchorMax;
            }
            float size = Mathf.Approximately(p.portrait_offset_size, 0) ? 1 : p.portrait_offset_size;
            rect.sizeDelta = new Vector2(webpPlayer.Width, webpPlayer.Height);  // native size
            rect.localScale = new Vector3(size, size, size);
            rect.anchoredPosition = new Vector2(p.portrait_offset_x, p.portrait_offset_y);
        }
        rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0);
    }

    /// <summary>True while the portrait webp slot is the visible portrait (offsets apply).</summary>
    public bool isShowingWebp { get { return _showingWebp && webpPlayer != null; } }
    /// <summary>True while the landscape webp player is the visible portrait (fixed, isShowingLandscape is also true).</summary>
    public bool isShowingWebpLandscape { get { return _showingWebpLandscape && webpPlayer_landscape != null; } }

    /// <summary>True while either webp player is showing this path (it is not in the sprite cache, so redraw checks use this).</summary>
    public bool IsShowingWebp(string path)
    {
        return (isShowingWebp && webpPlayer.CurrentPath == path)
            || (isShowingWebpLandscape && webpPlayer_landscape.CurrentPath == path);
    }

    /// <summary>The path is an animated webp and this box has a player for that slot. Otherwise it is drawn as a still image.</summary>
    public bool CanPlayAnimatedWebp(string path, bool landscape)
    {
        var player = landscape ? webpPlayer_landscape : webpPlayer;
        return player != null && (!landscape || picture_landscape_group != null) && PortraitImageInfo.IsAnimatedWebp(path);
    }

    /// <summary>
    /// Plays an animated webp in the portrait or landscape webp slot. The previous portrait stays visible until
    /// the first frame is ready (the player keeps its RawImage hidden until then), then every other slot is hidden.
    /// On failure only that player is stopped and deactivated, nothing else is touched and IsShowingWebp stays false,
    /// so the caller can fall back to a still draw. Portrait offsets are not applied here (call UpdateAnchor after).
    /// </summary>
    public IEnumerator DrawAnimatedWebp(string path, bool landscape)
    {
        var player = landscape ? webpPlayer_landscape : webpPlayer;
        player.gameObject.SetActive(true);
        player.Play(path);
        while (player.IsBusy) yield return null;

        if (!player.IsShowing)
        {
            StopWebpPlayer(player);
            if (landscape) _showingWebpLandscape = false;
            else _showingWebp = false;
            yield break;
        }

        picture.gameObject.SetActive(false);
        if (spineRect != null) spineRect.gameObject.SetActive(false);
        if (landscape)
        {
            StopWebpPlayer(webpPlayer);
            _showingWebp = false;
            _showingWebpLandscape = true;
            if (picture_landscape != null) picture_landscape.gameObject.SetActive(false);
            picture_landscape_group.alpha = 1;
        }
        else
        {
            StopWebpPlayer(webpPlayer_landscape);
            _showingWebpLandscape = false;
            if (picture_landscape != null) picture_landscape.gameObject.SetActive(true);
            if (picture_landscape_group != null) picture_landscape_group.alpha = 0;
            _showingWebp = true;
        }
    }

    /// <summary>
    /// Stops and deactivates both webp players (frees their decoders and textures) and gives the landscape slot back to
    /// picture_landscape. Called by every non-webp draw.
    /// </summary>
    public void HideWebp()
    {
        _showingWebp = false;
        _showingWebpLandscape = false;
        StopWebpPlayer(webpPlayer);
        StopWebpPlayer(webpPlayer_landscape);
        if (picture_landscape != null) picture_landscape.gameObject.SetActive(true);
    }

    protected static void StopWebpPlayer(scr_WebpPlayer player)
    {
        if (player == null) return;
        player.Stop();
        player.gameObject.SetActive(false);
    }

    private void CheckCharaChange(PortraitManager newPortrait, I_hasPortrait handler = null)
    {
        //spineRect.gameObject.SetActive(false);
        //picture.gameObject.SetActive(true);
        //PreviousRef = portrait;
        //bool skipUpdate = portrait == newPortrait && Input.GetMouseButton(1);
        portrait = newPortrait;
        if (portrait != null)
        {
            //Debug.Log($"drawing portrait for {newPortrait.Owner.RefID}");
            if (!isCombatBox)
            {
                if (isBannerBox)
                {
                    portrait.DrawBanner(this);
                }
                else portrait.DrawPortrait(this, handler);
            }
        }
        else
        {
            Debug.LogError("error portrait null");
        }
    }

    public void CombatRefresh(I_StatsManager stats, bool forceRefresh = false)
    {
        if (!isCombatBox || portrait == null) return;
        portrait.DrawCombatPortrait(stats, this, forceRefresh);
    }

    public void Draw(IEnumerator routine)
    {
        if (currentlyRunning != null)
        {
            StopCoroutine(currentlyRunning);
            currentlyRunning = null;
        }
        _storedPortrait = this.currentPortrait;
        bool finished = false;
        IEnumerator DrawToCompletion()
        {
            try { yield return routine; }
            finally
            {
                finished = true;
                currentlyRunning = null;
            }
        }
        var running = StartCoroutine(DrawToCompletion());
        // Cached Spine draws may finish synchronously, before StartCoroutine returns its handle.
        currentlyRunning = finished ? null : running;
        mouseOver = false;
        updateImage();
    }

    string _storedPortrait = "";

    public void NotifyEndDraw()
    {
        if (_storedPortrait != "" && currentPortrait != _storedPortrait)
        {
            if (isCurrentTargetBox || isCombatBox) scr_System_CentralControl.current.UnloadTextureCache(_storedPortrait);
        }
        _storedPortrait = "";
        currentlyRunning = null;
    }

    public PortraitManager.CharaPortrait currentHandler = null;
    public string currentPortrait = "";


    void OnDestroy()
    {
        //Debug.Log("scr_CharPortraitBox: OnDestroy Called!");
        //Destroy(SpriteTexture);

    }

    //public RectTransform prefab_Canvas_charaDetail;
    scr_Menu_CharaDetail detail;

    void OnPointerClick()
    {
        if (portrait != null && isCurrentTargetBox) portrait.ActivityClick(this);
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

    /// <summary>
    /// This updates the extended UI (currently disabled)
    /// </summary>
    private void updateImage()
    {
        if (portrait == null)
        {
            Debug.Log("updateImage null portrait disabling");
            picture.gameObject.SetActive(false);
            if(spineRect != null) spineRect.gameObject.SetActive(false);
            HideWebp();
            return;
        }

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
           // Utility.LoadSprite(XraySprite.widget_ass0, xray_A.GetComponent<Image>());
          //  Utility.LoadSprite(XraySprite.widget_oral0, xray_M.GetComponent<Image>());
        }

        if (false)//chara.Template.isFemale)
        {
            ActivateOvumBox(true);
           // Utility.LoadSprite(XraySprite.rjw_egg1, box_ovum.GetComponent<Image>());
           // Utility.LoadSprite(XraySprite.widget_vag1, xray_V.GetComponent<Image>());
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
