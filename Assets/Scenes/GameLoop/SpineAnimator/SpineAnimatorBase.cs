using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpineAnimatorBase : MonoBehaviour
{
    public Material SGmaterial;
    public Material SGmaterial_Alpha;


    public virtual IEnumerator Initialize(PortraitManager.CharaPortrait_Spine manager, SpineLoaderTiny loader, List<string> texturePath, string atlasPath, string skeletonPath, bool straightAlpha, string idleAnimName = "idle", string addonAnimName = "action")
    {
        Debug.LogError("SpineAnimatorBase Initialize called!");
        yield break;
    }

    public virtual IEnumerator PreCacheData(PortraitManager.CharaPortrait_Spine manager, List<string> texturePath, string atlasPath, string skeletonPath, bool straightAlpha)
    {
        yield break;
    }
}