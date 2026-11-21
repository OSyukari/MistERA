using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpineAnimatorBase : MonoBehaviour
{
    public Material SGmaterial;
    public Material SGmaterial_Alpha;

    /// <summary>Crossfade time when a portrait switches animation variant on a live skeleton.</summary>
    protected const float SpineMixDuration = 0.25f;


    public virtual IEnumerator Initialize(PortraitManager.CharaPortrait_Spine manager, SpineLoaderTiny loader, List<string> texturePath, string atlasPath, string skeletonPath, bool straightAlpha, string idleAnimName = "idle", string addonAnimName = "action", bool lowPriority = false)
    {
        Debug.LogError("SpineAnimatorBase Initialize called!");
        yield break;
    }

    public virtual IEnumerator PreCacheData(PortraitManager.CharaPortrait_Spine manager, List<string> texturePath, string atlasPath, string skeletonPath, bool straightAlpha)
    {
        yield break;
    }
}