namespace OpenMcDesktop.Game.Definitions.Entities;

public class PlayerSaveData
{
    public byte Health { get; set; } = 0;
    public Definitions.Item[] Inventory { get; set; } = new Definitions.Item[37];
    public Definitions.Item[] Items { get; set; } = new Definitions.Item[6];
    public byte Selected { get; set; } = 0;
    public byte[] Skin { get; set; }
}