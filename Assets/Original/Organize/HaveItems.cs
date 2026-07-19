using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class HaveItems : MonoBehaviour
{

    public List<int> having = new List<int>();
    public OrganizeButton playerButton;
    public Transform content;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        foreach(var id in having)
        {
            GameObject instance = Instantiate(playerButton.gameObject, content);
            instance.GetComponent<OrganizeButton>().currentCharacter = id;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
