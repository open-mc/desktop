using SFML.Graphics;

namespace OpenMcDesktop.Game.Definitions.Blocks;

public class Water : Block
{
    public new static Texture Texture => World.TerrainAtlas.AtBlock(13, 12);
    public override Texture InstanceTexture { get; }
    public override bool Solid => false;
    public override bool Climbable => true;
    public override float Viscosity => 0.07f;
    public Water() { InstanceTexture = Texture; }
}

public class Lava : Water
{
    public new static Texture Texture => World.TerrainAtlas.AtBlock(14, 12);
    public override Texture InstanceTexture { get; }
    public override float Viscosity => 0.5f;
    public Lava() { InstanceTexture = Texture; }
}