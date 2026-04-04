using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;
using Spine;
using System.IO;
using UnityEngine.UI;
using System;
using System.Reflection;
using System.Text;


public class scr_SpineLoader : MonoBehaviour
{

    //public SkeletonAnimation skeletonAnimation;
    //public AnimationReferenceAsset checkpointMovement, checkpointOpening;

    //public SkeletonGraphic self_SkeletonGraphic;

    //public SkeletonData self_SkeletonData;  // required by SkeletonDataAsset
    // this.Rin_RaidDungeon.skel
    // scale 0.005
    // 
    private void Awake()
    {

    }
    public RectTransform parentRect;
    protected RectTransform selfRect = null;
    public RectTransform SelfRect
    { get
        {
            if (selfRect == null) selfRect  = GetComponent<RectTransform>(); return selfRect;
        } }
    public float selfWidth = 0;
    public float parentWidth = 0;

    // Start is called before the first frame update
    void Start()
    {
        //skeletonAnimation = GetComponent<SkeletonAnimation>();
        //if (skeletonAnimation == null) { return; }

    }

    private void Update()
    {
        selfWidth = SelfRect.rect.width;
        parentWidth = parentRect.rect.width;
    }

    /*
    public RectTransform getLoaderRect
    {
        get
        {
            if (spineLoader != null) return spineLoader.GetComponent<RectTransform>();
            return null;
        }
    }*/
    public Transform GetLoaderRect
    {
        get
        {
            if (tiny != null && tiny.GameObject != null) return tiny.GameObject.transform;
            return null;
        }
    }

    public SpineLoaderTiny tiny;

    protected void OnDestroy()
    {
        loader40.Clear();
        loader41.Clear();
        loader42.Clear();
    }

    /// <summary>
    /// PATH VALUES WILL HAVE APPLICATION_DATAPATH APPENDED TO IT 
    /// </summary>
    /// <param name="materialTexturePath"></param>
    /// <param name="atlasJSON_path"></param>
    /// <param name="skeletonJSON_path"></param>
    /// <param name="skeletonScale"></param>
    /// <param name="idleAnimName"></param>
    public IEnumerator SetBase(PortraitManager.CharaPortrait_Spine manager, List<string> materialTexturePath, string atlasJSON_path, string skeletonJSON_path, bool straightAlpha, string idleAnimName, string addonAnimName, bool lowPriority = false)
    {
        var version = "";
        if (manager.dataHolder != null && manager.dataHolder.skeletonTA != null && manager.dataHolder.skeletonPath == skeletonJSON_path)
        {
            var text = Encoding.UTF8.GetString(manager.dataHolder.skeletonTA, 0, 100);// manager.dataHolder.skeletonTA.text;
            if (text.Contains("4.0.")) version = "4.0";
            else if (text.Contains("4.1.")) version = "4.1";
            else if (text.Contains("4.2.")) version = "4.2";
        }
        else
        {

            byte[] ta = null;
            yield return AssetsLoader.LoadSkelCoroutine(skeletonJSON_path, text => ta = text);
            var text = Encoding.UTF8.GetString(ta, 0, 100);// manager.dataHolder.skeletonTA.text;

            if (text.Contains("4.0."))
            {
                version = "4.0";
                manager.dataHolder = new SpineDataTiny_40();
            }
            else if (text.Contains("4.1."))
            {
                version = "4.1";
                manager.dataHolder = new SpineDataTiny_41();
            }
            else if (text.Contains("4.2."))
            {
                version = "4.2";
                manager.dataHolder = new SpineDataTiny_42();
            }
            if (manager.dataHolder != null)
            {
                manager.dataHolder.skeletonTA = ta;
                manager.dataHolder.skeletonPath = skeletonJSON_path;
            }
        }



        // this need full path
        /**/
        if (tiny == null || tiny.Version != version)
        {
           // StoreTiny();
            tiny = GetAnimator(version);
        }

        var animator = scr_System_CentralControl.current.GetSpineAnimator(tiny.Version);
        yield return animator.Initialize(manager, tiny, materialTexturePath, atlasJSON_path, skeletonJSON_path, straightAlpha, idleAnimName, addonAnimName, lowPriority);
        // yield return spineLoader.Initialize(materialTexturePath, atlasJSON_path, skeletonJSON_path, straightAlpha, idleAnimName, addonAnimName);
        HideAnimator(version);
    }

    SpineLoaderTiny_40 loader40 = new SpineLoaderTiny_40();
    SpineLoaderTiny_41 loader41 = new SpineLoaderTiny_41();
    SpineLoaderTiny_42 loader42 = new SpineLoaderTiny_42();

    protected void HideAnimator(string version)
    {
        if (loader40.GameObject != null) loader40.GameObject.SetActive(version == "4.0");
        if (loader41.GameObject != null) loader41.GameObject.SetActive(version == "4.1");
        if (loader42.GameObject != null) loader42.GameObject.SetActive(version == "4.2");
    }
    protected SpineLoaderTiny GetAnimator(string version)
    {
        switch (version)
        {
            case "4.0": return loader40;
            case "4.1": return loader41;
            case "4.2": return loader42;
            default: return null;
        }
    }

    public void Clean()
    {
        loader40.Clear();
        loader41.Clear();
        loader42.Clear();
    }

    public void AddAnimation(string animationName, bool loop)
    {
        // track index, animation name, whether loop, delay
        // take given animation set on track 1 play once
        // track 0 and track 1 blend animation

        // Spine.TrackEntry animationEntry = skeletonAnimation.state.AddAnimation(1, animationName, loop, 0);
        //Spine.TrackEntry animationEntry = self_SkeletonGraphic.AnimationState.AddAnimation(1, animationName, loop, 0);
       // animationEntry.Complete += AnimationEntry_Complete;

    }



    /* Reference Materials
     skeletonAnimation.skeleton.SetSkin(""); change base skin. not required as BA assets dont have variant skin

     
    */

    /// <summary>
    ///  Perform operation when animation track has ended
    /// </summary>
    /// <param name="trackEntry"></param>
    private void AnimationEntry_Complete(Spine.TrackEntry trackEntry)
    {

        
    }
}
