using System;
using System.Collections.Generic;
using System.Linq;

public class Character
{
    public int Id { get; set; }
    public string Name { get; set; }
    public float HsPercent { get; set; }      // floatに変更
    public float DodgeRate { get; set; }      // 弾除け率に変更
    public int Iq { get; set; }
    public float HitPercent { get; set; }     // floatに変更
}

public class CharacterManager
{
    public static List<Character> characters = new List<Character>
    {
        new Character { Id = 0, Name = "Null", HsPercent = 0f, DodgeRate = 0f, Iq = 0, HitPercent = 0f },
        new Character { Id = 1, Name = "Chronicle", HsPercent = 0.35f, DodgeRate = 0.20f, Iq = 80, HitPercent = 0.56f },
        new Character { Id = 2, Name = "Demon1", HsPercent = 0.45f, DodgeRate = 0.11f, Iq = 80, HitPercent = 0.63f },
        new Character { Id = 3, Name = "something", HsPercent = 0.35f, DodgeRate = 0.25f, Iq = 70, HitPercent = 0.58f },
        new Character { Id = 4, Name = "Leo", HsPercent = 0.25f, DodgeRate = 0.15f, Iq = 150, HitPercent = 0.63f },
        new Character { Id = 5, Name = "Alfajer", HsPercent = 0.45f, DodgeRate = 0.13f, Iq = 65, HitPercent = 0.60f },
        new Character { Id = 6, Name = "Derke", HsPercent = 0.35f, DodgeRate = 0.20f, Iq = 75, HitPercent = 0.55f },
        new Character { Id = 7, Name = "Boaster", HsPercent = 0.20f, DodgeRate = 0.10f, Iq = 165, HitPercent = 0.48f },
        new Character { Id = 8, Name = "Aspas", HsPercent = 0.40f, DodgeRate = 0.30f, Iq = 80, HitPercent = 0.55f },
        new Character { Id = 9, Name = "F0rsakeN", HsPercent = 0.25f, DodgeRate = 0.15f, Iq = 125, HitPercent = 0.52f },
        new Character { Id = 10, Name = "Jinggg", HsPercent = 0.30f, DodgeRate = 0.25f, Iq = 68, HitPercent = 0.53f },
        new Character { Id = 11, Name = "D4v41", HsPercent = 0.35f, DodgeRate = 0.15f, Iq = 70, HitPercent = 0.57f },
        new Character { Id = 12, Name = "Sato", HsPercent = 0.40f, DodgeRate = 0.20f, Iq = 67, HitPercent = 0.57f },
        new Character { Id = 13, Name = "jawgemo", HsPercent = 0.30f, DodgeRate = 0.29f, Iq = 89, HitPercent = 0.50f },
        new Character { Id = 14, Name = "valyn", HsPercent = 0.30f, DodgeRate = 0.13f, Iq = 135, HitPercent = 0.50f },
        new Character { Id = 15, Name = "Ethan", HsPercent = 0.30f, DodgeRate = 0.20f, Iq = 140, HitPercent = 0.56f },
        new Character { Id = 16, Name = "wo0t", HsPercent = 0.40f, DodgeRate = 0.15f, Iq = 71, HitPercent = 0.60f },
        new Character { Id = 17, Name = "kaajak", HsPercent = 0.35f, DodgeRate = 0.15f, Iq = 89, HitPercent = 0.58f },
        new Character { Id = 18, Name = "Meiy", HsPercent = 0.35f, DodgeRate = 0.23f, Iq = 65, HitPercent = 0.56f },
        new Character { Id = 19, Name = "Verno", HsPercent = 0.35f, DodgeRate = 0.16f, Iq = 86, HitPercent = 0.51f },
        new Character { Id = 20, Name = "Sayonara", HsPercent = 0.33f, DodgeRate = 0.20f, Iq = 89, HitPercent = 0.52f },
        new Character { Id = 21, Name = "Jamppi", HsPercent = 0.25f, DodgeRate = 0.16f, Iq = 125, HitPercent = 0.50f },
        new Character { Id = 22, Name = "Boostio", HsPercent = 0.28f, DodgeRate = 0.17f, Iq = 128, HitPercent = 0.50f },
        new Character { Id = 23, Name = "味方が全滅したLeo", HsPercent = 0.45f, DodgeRate = 0.30f, Iq = 170, HitPercent = 0.65f },
        new Character { Id = 24, Name = "チャンピオンズのEthan", HsPercent = 0.30f, DodgeRate = 0.25f, Iq = 145, HitPercent = 0.65f },
        new Character { Id = 25, Name = "Primmie", HsPercent = 0.45f, DodgeRate = 0.25f, Iq = 59, HitPercent = 0.60f },
        new Character { Id = 26, Name = "Demon1（坊主）", HsPercent = 0.50f, DodgeRate = 0.20f, Iq = 89, HitPercent = 0.65f },
        new Character { Id = 27, Name = "Tortlilyan", HsPercent = 0.20f, DodgeRate = 0.31f, Iq = 123, HitPercent = 0.60f },
        new Character { Id = 28, Name = "まーやまくん", HsPercent = 0.35f, DodgeRate = 0.25f, Iq = 79, HitPercent = 0.70f },
        new Character { Id = 29, Name = "おもこ", HsPercent = 0.20f, DodgeRate = 0.50f, Iq = 55, HitPercent = 0.65f },
        new Character { Id = 30, Name = "Meteor", HsPercent = 0.36f, DodgeRate = 0.30f, Iq = 73, HitPercent = 0.58f },
        new Character { Id = 31, Name = "Laz", HsPercent = 0.40f, DodgeRate = 0.15f, Iq = 70, HitPercent = 0.55f },
        new Character { Id = 32, Name = "ZMJKK", HsPercent = 0.30f, DodgeRate = 0.25f, Iq = 55, HitPercent = 0.58f },
        new Character { Id = 33, Name = "Brawk", HsPercent = 0.30f, DodgeRate = 0.15f, Iq = 79, HitPercent = 0.52f },
        new Character { Id = 34, Name = "HYUNMIN", HsPercent = 0.37f, DodgeRate = 0.20f, Iq = 76, HitPercent = 0.55f },
        new Character { Id = 35, Name = "Flashback", HsPercent = 0.33f, DodgeRate = 0.24f, Iq = 79, HitPercent = 0.55f }
    };

    public static Character GetById(int id) => characters.FirstOrDefault(c => c.Id == id);
    public static Character GetByName(string name) => characters.FirstOrDefault(c => c.Name == name);
}