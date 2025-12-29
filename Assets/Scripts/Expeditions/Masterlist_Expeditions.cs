using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


public class Expeditions : MonoBehaviour
{
    public static Expeditions Instance { get; private set; }

    protected Index_Expeditions expeditionEntry = new Index_Expeditions();
    public static Index_Expeditions ExpeditionEntry { get { return Instance.expeditionEntry; } }

    protected Index_ExpEvents explorationEvents = new Index_ExpEvents();
    public static Index_ExpEvents ExplorationEvents { get { return Instance.explorationEvents; } }

    protected Index_FeatureSet explorationFeatures = new Index_FeatureSet();
    public static Index_FeatureSet ExplorationFeatures { get { return Instance.explorationFeatures; } }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }
}