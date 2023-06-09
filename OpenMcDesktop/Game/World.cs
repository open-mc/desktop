using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenMcDesktop.Game.Definitions.Blocks;
using OpenMcDesktop.Game.Definitions;
using OpenMcDesktop.Networking;
using SFML.Graphics;
using SFML.Graphics.Glsl;
using SFML.System;
using SFML.Window;

namespace OpenMcDesktop.Game;

/// <summary>
/// Contains helper methods for interacting with game world, similar to client file
/// https://github.com/open-mc/client/blob/preview/iframe/world.js, except more involved within the actual game, and can
/// co-ordinate core game functions like chunk rendering.
/// </summary>
public class World
{
    public static Texture TerrainAtlas;
    public static Texture ItemsAtlas;
    public static int BlockTextureWidth = 16;
    public static int BlockTextureHeight = 16;

    // Game world components
    public ConcurrentDictionary<int, Chunk> Map { get; set; }
    public ConcurrentDictionary<long, Entity> Entities { get; set; }
    public double TickCount { get; set; }
    public float TicksPerSecond { get; set; }
    public Vector2f Gravity { get; set; }
    public string Dimension { get; set; }
    public Texture CloudMap;
    public Texture[] Moons;
    public Texture EndSky;
    public Texture Stars;
    public Texture Sun;
    
    // These are in world units
    public Vector2f CameraCentre { get => CameraPosition + CameraSize / 2; set => CameraPosition = value + CameraSize / 2; }
    public Vector2f CameraPosition { get; set; } = new Vector2f(128, -128); // Where the camera is in the world
    public Vector2f CameraSize { get; set; } = new Vector2f(28, 16); // How many (blocks) across and up/down camera can see
    public int CameraZoomLevel { get; set; } = 1;
    public int[] CameraZoomRealBlockSizes { get; set; } = { 32, 64, 128, 256 };
    
    private GameData gameData;

    static World()
    {
        TerrainAtlas = new Texture("Resources/Textures/terrain.png");
        ItemsAtlas = new Texture("Resources/Textures/items.png");
    }

    public World(GameData data, string dimension)
    {
        Dimension = dimension;
        gameData = data;

        Map = new ConcurrentDictionary<int, Chunk>();
        Entities = new ConcurrentDictionary<long, Entity>();
        
        var skyTexture = new Texture("Resources/Textures/sky.png");
        Sun = new Texture(skyTexture.CopyToImage(), new IntRect(128, 64, 32, 32));
        Moons = new[]
        {
            new Texture(skyTexture.CopyToImage(), new IntRect(128, 0, 32, 32)),
            new Texture(skyTexture.CopyToImage(), new IntRect(160, 0, 32, 32)),
            new Texture(skyTexture.CopyToImage(), new IntRect(192, 0, 32, 32)),
            new Texture(skyTexture.CopyToImage(), new IntRect(224, 0, 32, 32)),
            new Texture(skyTexture.CopyToImage(), new IntRect(128, 32, 32, 32)),
            new Texture(skyTexture.CopyToImage(), new IntRect(160, 32, 32, 32)),
            new Texture(skyTexture.CopyToImage(), new IntRect(192, 32, 32, 32)),
            new Texture(skyTexture.CopyToImage(), new IntRect(224, 32, 32, 32))
        };
        CloudMap = new Texture(skyTexture.CopyToImage(), new IntRect(128, 127, 128, 1));
        Stars = new Texture("Resources/Textures/stars.png")
        {
            Repeated = true
        };
    }

    public Block GetBlock(int x, int y)
    {
        var chunkKey = (x >>> 6) + (y >>> 6) * 67108864;
        var chunk = Map.GetValueOrDefault(chunkKey);
        return chunk?.Tiles[(x & 63) + ((y & 63) << 6)] ?? gameData.Blocks[gameData.BlockIndex[nameof(Air)]];
    }
    
    public void SetBlock(int x, int y, int blockId)
    {
        var chunkKey = (x >>> 6) + (y >>> 6) * 67108864;
        var chunk = Map.GetValueOrDefault(chunkKey);
        if (chunk is not null)
        {
            chunk.Tiles[x & 63 + (y & 63 << 6)] = gameData.Blocks[blockId];
        }
    }

    public void AddEntity(Entity entity)
    {
        Entities.TryAdd(entity.Id, entity);
        if (entity.Id == gameData.MyPlayerId)
        {
            gameData.MyPlayer = entity;
            CameraCentre = new Vector2f((float) gameData.MyPlayer.X, (float) gameData.MyPlayer.Y);
        }
    }

    public void MoveEntity(Entity entity)
    {
        // Chunk that the entity now is in
        var newChunk = Map.GetValueOrDefault((((int) Math.Floor(entity.X)) >>> 6) + (((int) Math.Floor(entity.Y)) >>> 6) * 67108864);
        if (newChunk != entity.Chunk)
        {
            entity.Chunk?.Entities.Remove(entity);
            entity.Chunk = newChunk;
            entity.Chunk?.Entities.Add(entity);
        }
    }

    public void RemoveEntity(Entity entity)
    {
        Entities.TryRemove(entity.Id, out _);
        if (entity == gameData.MyPlayer)
        {
            gameData.MyPlayerId = -1;
        }
        
        entity.Chunk?.Entities.Remove(entity);
    }
    
    /// <summary>
    /// #0a0c14: RGB(10, 12, 20) - A dark blue-black color used as the darker shade in the sky gradient.
    /// #040609: RGB(4, 6, 9) - A darker blue-black color used as the darkest shade in the sky gradient.
    /// #c3d2ff: RGB(195, 210, 255) - A light blue color used as the lighter shade in the sky gradient.
    /// #78a7ff: RGB(120, 167, 255) - A brighter blue color used as the brightest shade in the sky gradient.
    /// #c5563b: RGB(197, 86, 59) - A reddish-orange color used as the base color for the horizon during sunset and sunrise.
    /// </summary>
    private void RenderSky(RenderWindow window, View worldLayer, View backgroundLayer)
    {
        window.SetView(backgroundLayer);
        
        var time = TickCount % 24000;
        var lightness = time < 1800 ? time / 1800 * 255 : time < 13800 ? 255 : time < 15600 ? (15600 - time) / 1800 * 255 : 0;
        var orangeness = time switch
        {
            < 1800 => (int) (255 * (1 - Math.Abs(time - 900) / 900f)),
            >= 13800 and < 15600 => (int) (255 * (1 - Math.Abs(time - 14700) / 900f)),
            _ => 0
        };        

        var atmosphereColour = new Color(10, 12, 20);
        var horizonColour = new Color(4, 6, 9);
        // Night sky backing sky layer
        CreateSkyGradient(window, atmosphereColour, horizonColour);
        // Daylight horizon and sky
        CreateSkyGradient(window, new Color(120, 167, 255, (byte) lightness),
            new Color(195, 210, 255, (byte) lightness));
        // Orange sunrise/sunset hue
        CreateSkyGradient(window, Color.Transparent, new Color(197, 86, 59, (byte) orangeness));
        
        window.SetView(worldLayer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CreateSkyGradient(RenderWindow window, Color atmosphereColour, Color horizonColour)
    {
        var vertices = new VertexArray(PrimitiveType.Quads, 4);
        
        vertices[0] = new Vertex(new Vector2f(0, 0), atmosphereColour);
        vertices[1] = new Vertex(new Vector2f(window.GetView().Size.X, 0), atmosphereColour);
        vertices[2] = new Vertex(new Vector2f(window.GetView().Size.X, window.GetView().Size.Y), horizonColour);
        vertices[3] = new Vertex(new Vector2f(0, window.GetView().Size.Y), horizonColour);

        window.Draw(vertices);
    }

    /// <summary>
    /// Turns a given position in block world co-ordinates to screen co-ordinates, so that the rendering position can be determined.
    /// </summary>
    /*[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2f WorldToScreen(Vector2f blockXy)
    {
        AdjustCameraSize();
        var relative = blockXy - CameraPosition;
        var fraction = new Vector2f(relative.X / CameraSize.X, relative.Y / CameraSize.Y);
        return new Vector2f(fraction.X * gameData.View.Size.X, -fraction.Y * gameData.View.Size.Y);
    }*/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2f ScreenToWorld(Vector2f screenXy)
    {
        AdjustCameraSize();
        var fraction = new Vector2f(screenXy.X / gameData.WorldLayer.Size.X, -screenXy.Y / gameData.WorldLayer.Size.Y);
        var relative = new Vector2f(fraction.X * CameraSize.X, fraction.Y * CameraSize.Y);
        return relative + CameraPosition;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AdjustCameraSize()
    {
        // 64px screen width and height at standard zoom
        var realBlockSize = CameraZoomRealBlockSizes[Math.Clamp(CameraZoomLevel, 0, CameraZoomRealBlockSizes.Length - 1)];
        // At fullscreen, 1920 / n = realBlockSize, 1080 / n realBlockSize, so to keep blocks square, we just have to find N for X and Y
        CameraSize = new Vector2f(gameData.WorldLayer.Size.X / realBlockSize, gameData.WorldLayer.Size.Y / realBlockSize);
    }
    
    private Vector2f lastPosition;
    public void Update(float deltaTime)
    {
        if (Keyboard.IsKeyPressed(Keyboard.Key.W))
        {
            CameraPosition += new Vector2f(0, 0.1f) * (Keyboard.IsKeyPressed(Keyboard.Key.LShift) ? 20 : 1);
        }
        if (Keyboard.IsKeyPressed(Keyboard.Key.A))
        {
            CameraPosition += new Vector2f(-0.1f, 0) * (Keyboard.IsKeyPressed(Keyboard.Key.LShift) ? 20 : 1);
        }
        if (Keyboard.IsKeyPressed(Keyboard.Key.S))
        {
            CameraPosition += new Vector2f(0, -0.1f) * (Keyboard.IsKeyPressed(Keyboard.Key.LShift) ? 20 : 1);
        }
        if (Keyboard.IsKeyPressed(Keyboard.Key.D))
        {
            CameraPosition += new Vector2f(0.1f, 0) * (Keyboard.IsKeyPressed(Keyboard.Key.LShift) ? 20 : 1);
        }
        if (Keyboard.IsKeyPressed(Keyboard.Key.Num1))
        {
               
        }
        
        if (Mouse.IsButtonPressed(Mouse.Button.Left))
        {
            var mousePosition = Mouse.GetPosition();
            ScreenToWorld(new Vector2f(mousePosition.X, mousePosition.Y));
            SetBlock(mousePosition.X, mousePosition.Y, gameData.BlockIndex[nameof(Stone)]);
        }
        if (!lastPosition.Equals(CameraPosition))
        {
            Console.WriteLine(CameraPosition);
            lastPosition = CameraPosition;
        }
    }
    
    public void Render(RenderWindow window, View worldLayer, View backgroundLayer, float deltaTime)
    {
        RenderSky(window, worldLayer, backgroundLayer);
        worldLayer.Center = new Vector2f(CameraCentre.X * CameraZoomRealBlockSizes[CameraZoomLevel] / BlockTextureWidth,
            -CameraCentre.Y * CameraZoomRealBlockSizes[CameraZoomLevel] / BlockTextureHeight);
        worldLayer.Zoom(CameraZoomLevel);

        // 1024 px real chunk width 64 * 16
        foreach (var chunk in Map.Values)
        {
            // If it is not on screen, we can skip drawing this chunk
            var states = new RenderStates(BlendMode.Alpha, Transform.Identity, null, null);
            chunk.Render(window, worldLayer, states);
        }
    }
}