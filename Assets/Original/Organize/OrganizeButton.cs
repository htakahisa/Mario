using System;
using TMPro;
using UnityEngine;

public class OrganizeButton : MonoBehaviour
{

    public TextMeshProUGUI text;
    private OrganizedList organized;
    public int currentCharacter;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        organized = OrganizedList.Instance;
    }

    // Update is called once per frame
    void Update()
    {
        text.text = CharacterManager.GetById(currentCharacter).Name;
    }

    public void OrganizeSelf()
    {
        organized.Organize(CharacterManager.GetById(currentCharacter));
    }

    public void DisorganizeSelf()
    {
        organized.Disorganize(CharacterManager.GetById(currentCharacter));
    }

    public void OrganizeByIndex(int index)
    {
        organized.Organize(organized.organizedList[index]);
    }
    public void DisorganizeByIndex(int index)
    {
        organized.Disorganize(organized.organizedList[index]);
    }

    public void OrganizeById(int id)
    {
        organized.Organize(CharacterManager.GetById(id));
    }

    public void DisorganizeById(int id)
    {
        organized.Disorganize(CharacterManager.GetById(id));
    }
}
