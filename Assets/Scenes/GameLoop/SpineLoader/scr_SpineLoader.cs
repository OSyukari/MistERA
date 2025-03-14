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
        selfRect = GetComponent<RectTransform>();
    }
    public RectTransform parentRect;
    protected RectTransform selfRect;
    public float selfWidth = 0;
    public float parentWidth = 0;

    // Start is called before the first frame update
    void Start()
    {
        //skeletonAnimation = GetComponent<SkeletonAnimation>();
        //if (skeletonAnimation == null) { return; }

    }

    [ExecuteInEditMode]
    private void Update()
    {
        selfWidth = selfRect.rect.width;
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

    Dictionary<string, SpineLoader> loaders = new Dictionary<string, SpineLoader>();
    /// <summary>
    /// PATH VALUES WILL HAVE APPLICATION_DATAPATH APPENDED TO IT 
    /// </summary>
    /// <param name="materialTexturePath"></param>
    /// <param name="atlasJSON_path"></param>
    /// <param name="skeletonJSON_path"></param>
    /// <param name="skeletonScale"></param>
    /// <param name="idleAnimName"></param>
    public void SetBase(List<string> materialTexturePath, string atlasJSON_path, string skeletonJSON_path, float skeletonScale, string idleAnimName = "idle", string touchAnimName = "action")
    {
        var version = scr_System_CentralControl.GetSkelVersion(skeletonJSON_path);
        // this need full path
        if (spineLoader != null && spineLoader.Version == version)
        {   // then call this one

        }
        else
        {
            if(spineLoader != null)
            {
                if(!loaders.ContainsValue(spineLoader)) loaders.Add(spineLoader.Version, spineLoader);
                spineLoader.gameObject.SetActive(false);
                spineLoader = null;

            }

            if (!loaders.ContainsKey(version)) loaders.Add(version, scr_System_CentralControl.current.GetSpineLoader(skeletonJSON_path));
            spineLoader = loaders[version];
        }

        spineLoader.Initialize(Utility.ResourcesPath, materialTexturePath, atlasJSON_path, skeletonJSON_path, skeletonScale, idleAnimName, touchAnimName);
        spineLoader.MatchWithBound();

       // NotifyChange(spineLoader.transform);
    }

    public void NotifyChange(RectTransform targetRect)
    {
        //selfRect.sizeDelta = new Vector2(Math.Min(targetRect.sizeDelta.x * targetRect.localScale.x, targetRect.sizeDelta.x), selfRect.sizeDelta.y);
    }


    public void Destroy()
    {
        Destroy(spineLoader);
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
