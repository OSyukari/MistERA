using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

[System.Serializable]
public enum EffectKeyword
{
    None,
    ModStatValue,           // [statID, value]
    ModStatValuePercent    // [statID, percentage]

}