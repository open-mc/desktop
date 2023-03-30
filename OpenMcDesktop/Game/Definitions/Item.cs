using OpenMcDesktop.Networking;
using SFML.Graphics;

namespace OpenMcDesktop.Game.Definitions;

public abstract class Item : IDecodable
{
    public static Texture Texture => new Texture(0, 0);
    public virtual Type Places => typeof(Block);
    public virtual Tool Tool => Tool.None;
    public virtual int Speed => 0;

    public int Count;
    public string Name;
    public object? SaveData = null;
    public Type SaveDataType = typeof(object);
    
    /// <summary>
    /// Decodes a packet into a specific type of item with some runtime trickery.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="target">The object which will hold the newly created item, should be passed in as null.</param>
    /// <returns></returns>
    public object? Decode(ref ReadablePacket data)
    {
        var _ = data.ReadByte(); // Padding
        var count = data.ReadByte();
        var itemId = data.ReadUShort();

        var target = (Item) Activator.CreateInstance(StaticData.GameData.ItemDefinitions[itemId])!;
        target.Count = count;
        target.Name = data.ReadString();
        if (target.SaveData is not null)
        {
            target.SaveData = data.Read(target.SaveData, target.SaveDataType);
        }
        
        return target;
    }
}