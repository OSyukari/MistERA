
public enum CardType
{
    // Fusion Monster
    None,

}

public enum CardAttribute
{
    DARK,
    WATER,
    FIRE,
    LIGHT
}

[System.Serializable]
public class CardsDB : I_SerializationCallbackReceiver
{
    public List<CardInfo> data;


    public void OnAfterDeserialize()
    {
        foreach(var card in data)
        {
            card.OnAfterDeserialize();
        }
    }

    [System.Serializable]
    public class CardInfo
    {

        public void OnAfterDeserialize()
        {
            switch((type, race))
            {
                case ("Trap Card", "Normal"):
                    cardType = 
            }
        }

        public int id;
        public string name;
        public List<string> typeline;
        // Dragon Fusion Effect

        public string type;
        // Fusion Monster 
        // Trap Card
        // Spell Card

        [Nonserialized][JsonIgnore]
        public CardType cardType = CardType.None;

        public bool isCardType(string type)
        {
            if (type == null || type.length < 1) return false;
            return humanReadableCardType.Contains(type);
        }

        public string humanReadableCardType;
        // Fusion Effect Monster
        // Normal Trap
        // Continuous Trap
        // Normal Spell
        // Quick-Play Spell
        // Counter Trap
        // Continuous Spell -> type + race
        // Field Spell

        public string frameType;
        // fusion
        // spell
        // trap

        public string desc;

        public string race;
        // Normal
        // Dragon
        // Continuous
        // Quick-Play
        // Counter
        // Field

        public int atk;
        public int def;
        public int level;
        public CardAttribute attribute;
        public string archetype;
        public string ygoprodeck_url;
        // card_sets
        // card_images
        // card_prices
    }
}


/// <summary>
/// allow overwriting info
/// </summary>
public class CardInDeck
{

}

/// <summary>
/// On duel, create card for each cardinfo in deck ?
/// </summary>
public class Cards
{
    public CardsDB.CardInfo baseData;
    public CardsDB.CardInfo dataOverride;

    public string alternateText;



}

/*
when editing deck, allow modify card -> new instance of cardinfo
each card in deck hold multiple cardinfo




*/