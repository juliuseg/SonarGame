public class Inventory
{
    public float Ore { get; private set; }

    public void AddOre(float amount)
    {
        Ore += amount;
    }
}
