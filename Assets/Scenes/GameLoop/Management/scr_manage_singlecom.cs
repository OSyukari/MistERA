using UnityEngine;

public class scr_manage_singlecom : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void OnEnable()
    {
        parent.OnChildActive(tabID, selfRect);
    }

    private void Awake()
    {
        selfRect = this.gameObject.GetComponent<RectTransform>();
    }
    protected RectTransform selfRect;
    public scr_Canvas_Management parent;
    public scr_Canvas_Management.JobAssignmentTab tabID;

    private void OnDisable()
    {
        while (selfRect.transform.childCount > 0) DestroyImmediate(selfRect.transform.GetChild(0).gameObject);
        parent.OnChildDisable(tabID, selfRect);
        //Debug.Log("ondisabled");
    }
}
