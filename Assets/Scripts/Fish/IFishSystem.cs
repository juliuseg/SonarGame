// Runtime controls for the fish simulation (used by the debug menu).
public interface IFishSystem
{
    bool Enabled { get; set; }
    bool UseSdfAtlas { get; set; }
    int MaxInstances { get; set; }
    void ClearAllFish();
    void Reload();
}
