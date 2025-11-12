using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;
using Spine;
using System.IO;
using UnityEngine.UI;
using System;
using System.Reflection;


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

    public RectTransform getLoaderRect
    {
        get
        {
            if (spineLoader != null) return spineLoader.GetComponent<RectTransform>();
            return null;
        }

    }
    protected SpineLoader PREV;
    public SpineLoader spineLoader;
    TextAsset spineLoader_skeletonJSON;

    SpineLoader spineLoader_previous = null;
    /// <summary>
    /// PATH VALUES WILL HAVE APPLICATION_DATAPATH APPENDED TO IT 
    /// </summary>
    /// <param name="materialTexturePath"></param>
    /// <param name="atlasJSON_path"></param>
    /// <param name="skeletonJSON_path"></param>
    /// <param name="skeletonScale"></param>
    /// <param name="idleAnimName"></param>
    public IEnumerator SetBase(List<string> materialTexturePath, string atlasJSON_path, string skeletonJSON_path, bool straightAlpha, string idleAnimName, string addonAnimName)
    {

        TextAsset ta = null;
        yield return AssetsLoader.LoadTextCoroutine(skeletonJSON_path, text => ta = text);

        var version = "";
        if (ta.text.Contains("4.0.")) version = "4.0";
        else if (ta.text.Contains("4.1.")) version = "4.1";
        else if (ta.text.Contains("4.2.")) version = "4.2";

        // this need full path
        if (spineLoader != null && (spineLoader.Version != version || spineLoader.atlasPath != atlasJSON_path || spineLoader.skeletonPath != skeletonJSON_path))
        {   // then call this one
            spineLoader_previous = spineLoader;
            
            spineLoader = scr_System_CentralControl.current.GetSpineLoader(version);
        }
        else if (spineLoader == null) spineLoader = scr_System_CentralControl.current.GetSpineLoader(version);

        yield return spineLoader.Initialize(materialTexturePath, atlasJSON_path, skeletonJSON_path, straightAlpha, idleAnimName, addonAnimName);

        if (spineLoader_previous != null)
        {
            spineLoader_previous.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(spineLoader_previous.gameObject);
            spineLoader_previous = null;
        }
    }

    public void Store()
    {
        if (spineLoader == null) return;
        spineLoader_previous = spineLoader;
        spineLoader = null;
    }

    public void Destroy()
    {
        if (spineLoader_previous == null) return;
        spineLoader_previous.gameObject.SetActive(false);
        Destroy(spineLoader_previous);
        spineLoader_previous = null;
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
