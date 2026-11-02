using UnityEngine;

public class scr_manage_singlecom : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void OnEnable()
    {
        if (parent != null && selfRect != null) parent.OnChildActive(tabID, selfRect);
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
        // Leave hierarchy cleanup to the next population, or Unity's teardown.
        if (parent != null) parent.OnChildDisable(tabID, selfRect);
    }
}
