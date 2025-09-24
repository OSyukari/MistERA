
using UnityEngine;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;


/// <summary>
/// what type of targeting makes sense? in a dialogue query ?
/// 1. player exist
/// 2. target might or might not exist
/// 
/// 1. check player stat
/// 2. check target stat
/// 3. check player and target relationship
/// 4. check player and a 3rd party stat
/// 5. check target and 3rd party stat
/// 6. 
/// baseid target? no
/// </summary>
[System.Serializable]
public enum ConditionValidator_Target
{
    Self,
    Target
}


[System.Serializable]
public class ConditionValidator
{
    string target;

}
