using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.UI;

public class OrganizedList : MonoBehaviour
{
    public static OrganizedList Instance;
    public List<Character> organizedList = new List<Character>();
    public List<OrganizeButton> characters = new List<OrganizeButton>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else Destroy(gameObject);
    }

    public void initialize()
    {
        //ゲームマネージャーがあるかどうかによってバトルシーンなのを判断していますが将来性はありません
        if (GameManager.Instance != null)
        {
            for (int i = 0; i < organizedList.Count; i++)
            {
                GameManager.Instance.playeredAgents[i].agentName = organizedList[i].Name;
                GameManager.Instance.playeredAgents[i].hsRate = organizedList[i].HsPercent;
                GameManager.Instance.playeredAgents[i].avoidRate = organizedList[i].DodgeRate;
                GameManager.Instance.playeredAgents[i].accuracyRate = organizedList[i].HitPercent;
                GameManager.Instance.playeredAgents[i].IQ = organizedList[i].Iq;
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        for (int i = 0; i < organizedList.Count; i++)
        {
            if (organizedList[i] == null || characters[i] == null) return;

            if (characters[i].currentCharacter != organizedList[i].Id)
            {
                characters[i].currentCharacter = organizedList[i].Id;
            }
        }
    }

    public void Organize(Character cha)
    {
        if (organizedList.Contains(cha)) return;

        for (int i = 0; i < 5; i++)
        {
            if(i >= organizedList.Count)
            {
                organizedList.Add(cha);
                return;
            }
            if (organizedList[i].Id == 0 || organizedList[i] == null)
            {
                organizedList[i] = cha;
                return;
            }
        }
    }

    public void Disorganize(Character cha)
    {
        if (!organizedList.Contains(cha)) return;
        
        for (int i = 0; i < organizedList.Count; i++)
        {
            if (organizedList[i] == cha)
            {
                organizedList[i] = CharacterManager.GetById(0);
                return;
            }
        } 
    }
}
