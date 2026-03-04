using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

public enum EffectKeyword
{
    None,
    ModStatValue,           // [statID, value]
    ModStatValuePercent    // [statID, percentage]

}