using UnityEngine;
using UnityEngine.EventSystems;
public class moveOrderScriptBTN : MonoBehaviour, IPointerDownHandler
{
    public scr_prOrderManage script1 = null;
    public scr_prefabTransactionManage script2 = null;

    public int CurrentIndex
    {
        get
        {
            if (script1 != null) return script1.CurrentIndex;
            else if (script2 != null) return script2.CurrentIndex;
            else return -1;
        }
    }
    public int SiblingIndex
    {
        get
        {
            if (script1 != null) return script1.SiblingIndex;
            else if (script2 != null) return script2.SiblingIndex;
            else return -1;
        }
    }

    public bool InactiveOverride = false;

    public void NotifyChanged()
    {
        if (script1 != null) script1.NotifyChanged();
        if (script2 != null) script2.NotifyChanged();
    }
    bool active = false;
    public void OnPointerDown(PointerEventData eventData)
    {
        if (InactiveOverride) return;
        if (active) return;
        if (script1 != null) active = script1.ActivateScript();
        if (script2 != null) active = script2.ActivateScript();
    }

    public void OnPointerUp()
    {
        if (InactiveOverride) return;
        if (!active) return;
        if (script1 != null) script1.DeactivateScript();
        if (script2 != null) script2.DeactivateScript();
        active = false;


    }
}
