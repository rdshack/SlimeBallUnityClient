using System;
using System.Collections;
using System.Collections.Generic;
using Indigo.EcsClientCore;
using UnityEngine;
using UnityEngine.UI;

public class SpawnPlayerButton : MonoBehaviour
{
    public LocalWorldManager LocalWorldManager;
    public Button            Button;
    public Button            AttackButton;
    

    void Start()
    {
        Button.onClick.AddListener(Join);
    }

    private void Join()
    {

    }
    
}
