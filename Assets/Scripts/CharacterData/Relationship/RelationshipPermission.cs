using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;



public class RelationshipPermission
{
    public bool intimacy_low = false;
    public bool intimacy_medium = false;
    public bool intimacy_high = false;
    public bool lead_follow = false;
    /// <summary>Obsolete: the Family permission is retired. Kept so existing relationship data still loads; nothing reads it.</summary>
    public bool formFamily = false;

}