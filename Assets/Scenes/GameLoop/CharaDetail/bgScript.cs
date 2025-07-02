using UnityEngine;
using UnityEngine.UI;

public class bgScript : MonoBehaviour
{

    public Image bgImage;
    
    private void Start()
    {
        this.bgImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Opaque.Color;
    }
}
