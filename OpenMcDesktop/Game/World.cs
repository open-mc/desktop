using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Timers;
using OpenMcDesktop.Game.Definitions;
using OpenMcDesktop.Gui;
using SFML.Graphics;
using SFML.System;
using SFML.Window;

namespace OpenMcDesktop.Game;

/// <summary>
/// Contains methods for the current session's  game world, borrows methods from https://github.com/open-mc/client/blob/preview/iframe/world.js.
/// Each desktop session should have it's own World.cs and Connections.cs instances. Co-ordinates core game functions like game rendering,
/// input handling for the current player and consuming/sending game events from Connection.cs
/// </summary>
public class World
{
    public static Texture TerrainAtlas;
    public static Texture ItemsAtlas;
    public static int BlockTextureWidth = 16;
    public static int BlockTextureHeight = 16;

    // Session world interface
    public readonly Page GameGuiPage;
    public Hotbar GameHotbar;
    public ChatBox GameChat;
    public ServerMenu GameServerMenu;
    public PauseMenu GamePauseMenu;
    public ChatInput GameChatInput;

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

    public World(GameData data)
    {
        // Setup defaults
        gameData = data;
        Dimension = "void";
        Gravity = new Vector2f(0, 0);
        TickCount = 0;

        // Game GUI page
        GameGuiPage = new Page();
        int GameHotbarHeight() => (int) (22 / 182.0f * gameData.Window.GetView().Size.X * 0.4f);
        int GameHotbarWidth() => (int) (gameData.Window.GetView().Size.X * 0.4f);

        GameChat = new ChatBox(
            Control.BoundsZero,
            () => (int) (gameData.Window.GetView().Size.Y / 2 - GameHotbarHeight() - 32),
            () => (int) (gameData.Window.GetView().Size.X / 2),
            () => (int) (gameData.Window.GetView().Size.Y / 2));
        GameGuiPage.Children.Add(GameChat);
        GameChatInput = new ChatInput("Press Enter to send chat message...",
            Control.BoundsZero,
            () => (int) gameData.Window.GetView().Size.Y - 48,
            () => (int) gameData.Window.GetView().Size.X,
            () => 48);
        GameChatInput.OnSubmit += (_, _) =>
        {
            gameData.CurrentServer?.SendAsync(GameChatInput.Text.Trim());
            HideChatInput();
        };

        GameHotbar = new Hotbar(
            () => (int) (gameData.Window.GetView().Size.X / 2 - GameHotbarWidth() / 2.0f),
            () => (int) (gameData.Window.GetView().Size.Y - GameHotbarHeight() - 8),
            GameHotbarWidth,
            GameHotbarHeight);
        GameGuiPage.Children.Add(GameHotbar);
        GameServerMenu = new ServerMenu(
            () => 128,
            () => 128,
            () => (int) gameData.Window.GetView().Size.X - 256,
            () => (int) gameData.Window.GetView().Size.Y - 256);
        // Pause
        GamePauseMenu = new PauseMenu(
            Control.BoundsZero,
            Control.BoundsZero,
            () => (int) gameData.Window.GetView().Size.X,
            () => (int) gameData.Window.GetView().Size.Y);

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
        EndSky = new Texture(skyTexture.CopyToImage(), new IntRect(128, 128, 128, 128))
        {
            Repeated = true
        };

        var tickTimer = new System.Timers.Timer
        {
            Interval = TimeSpan.FromSeconds(1).TotalMilliseconds,
            AutoReset = true,
        };
        tickTimer.Elapsed += Tick;
        tickTimer.Start();

        data.Window.MouseWheelScrolled += (_, args) =>
        {
            if (!data.Window.HasFocus())
            {
                return;
            }

            GameHotbar.ScrollSelection((int) args.Delta);
        };

        data.Window.KeyPressed += (_, args) =>
        {
            if (GameGuiPage.Children.Contains(GameChatInput) && GameChatInput.Focused)
            {
                switch (args.Code)
                {
                    case Keyboard.Key.Escape:
                        HideChatInput();
                        break;
                    case Keyboard.Key.Backspace:
                        if (string.IsNullOrEmpty(GameChatInput.Text))
                        {
                            HideChatInput();
                        }
                        break;
                }

                return;
            }

            switch (args.Code)
            {
                case Keyboard.Key.Tab:
                    if (!GameGuiPage.Children.Contains(GameServerMenu))
                    {
                        GameGuiPage.Children.Add(GameServerMenu);
                    }
                    break;
                case Keyboard.Key.Slash:
                case Keyboard.Key.T:
                    if (!GameGuiPage.Children.Contains(GameChatInput))
                    {
                        GameGuiPage.Children.Add(GameChatInput);
                    }
                    break;
                case Keyboard.Key.Escape:
                    if (!GameGuiPage.Children.Contains(GamePauseMenu))
                    {
                        GameGuiPage.Children.Add(GamePauseMenu);
                    }
                    else
                    {
                        GameGuiPage.Children.Remove(GamePauseMenu);
                    }
                    break;
                case Keyboard.Key.Num1:
                    GameHotbar.Selected = 0;
                    break;
                case Keyboard.Key.Num2:
                    GameHotbar.Selected = 1;
                    break;
                case Keyboard.Key.Num3:
                    GameHotbar.Selected = 2;
                    break;
                case Keyboard.Key.Num4:
                    GameHotbar.Selected = 3;
                    break;
                case Keyboard.Key.Num5:
                    GameHotbar.Selected = 4;
                    break;
                case Keyboard.Key.Num6:
                    GameHotbar.Selected = 5;
                    break;
                case Keyboard.Key.Num7:
                    GameHotbar.Selected = 6;
                    break;
                case Keyboard.Key.Num8:
                    GameHotbar.Selected = 7;
                    break;
                case Keyboard.Key.Num9:
                    GameHotbar.Selected = 8;
                    break;
            }
        };

        data.Window.KeyReleased += (_, args) =>
        {
            switch (args.Code)
            {
                case Keyboard.Key.Tab:
                    GameGuiPage.Children.Remove(GameServerMenu);
                    break;
                case Keyboard.Key.Slash:
                    if (GameGuiPage.Children.Contains(GameChatInput) && !GameChatInput.Focused)
                    {
                        GameChatInput.Focused = true;
                        GameChatInput.Text = "/";
                    }
                    break;
                case Keyboard.Key.T:
                    // Workaround for T getting mistakenly input into input 
                    if (GameGuiPage.Children.Contains(GameChatInput) && !GameChatInput.Focused)
                    {
                        GameChatInput.Focused = true;
                        GameChatInput.Text = "";
                    }
                    //GameGuiPage.Children.Remove(GameChatInput);
                    break;
            }
        };
    }

    public Block GetBlock(int x, int y)
    {
        // TODO: Fix this
        var chunkKey = (x >>> 6) + (y >>> 6) * 67108864;
        var chunk = Map.GetValueOrDefault(chunkKey);
        return chunk?.Tiles[(x & 63) + ((y & 63) << 6)] ?? throw new NotImplementedException(); /*?? gameData.Blocks[gameData.BlockIndex[nameof(Air)]];*/
    }

    public void SetBlock(int x, int y, int blockId)
    {
        // TODO: Fix this
        var chunkKey = (x >>> 6) + (y >>> 6) * 67108864;
        var chunk = Map.GetValueOrDefault(chunkKey);
        if (chunk is not null)
        {
            chunk.Tiles[x & 63 + (y & 63 << 6)] = gameData.Blocks[blockId];
        }
    }

    public void AddEntity(Entity entity)
    {
        // TODO: Fix this
        Entities.TryAdd(entity.Id, entity);
        if (entity.Id == gameData.MyPlayerId)
        {
            gameData.MyPlayer = entity;
            CameraCentre = new Vector2f((float) gameData.MyPlayer.X, (float) gameData.MyPlayer.Y);
        }
    }

    public void MoveEntity(Entity entity)
    {
        // TODO: Fix this
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
        // TODO: Fix this
        Entities.TryRemove(entity.Id, out _);
        if (entity == gameData.MyPlayer)
        {
            gameData.MyPlayerId = -1;
        }

        entity.Chunk?.Entities.Remove(entity);
    }

    private void HideChatInput()
    {
        gameData.Window.SetMouseCursor(SfmlHelpers.DefaultCursor);
        GameChatInput.State = State.Default;
        GameChatInput.Focused = false;
        GameChatInput.Text = "";
        GameGuiPage.Children.Remove(GameChatInput);
    }

    private void RenderSky(RenderWindow window, View worldLayer, View backgroundLayer)
    {
        window.SetView(backgroundLayer);

        if (Dimension == Game.Dimension.Overworld)
        {
            var time = TickCount % 24000;
            var lightness = time < 1800 ? time / 1800 * 255 : time < 13800 ? 255 : time < 15600 ? (15600 - time) / 1800 * 255 : 0;
            var orangeness = time switch
            {
                < 1800 => (int) (255 * (1 - Math.Abs(time - 900) / 900f)),
                >= 13800 and < 15600 => (int) (255 * (1 - Math.Abs(time - 14700) / 900f)),
                _ => 0
            };

            var atmosphereColour = new Color(10, 12, 20); // dark sky
            var horizonColour = new Color(4, 6, 9); //darkest sky 
            // Night sky backing sky layer
            CreateSkyGradient(window, atmosphereColour, horizonColour);
            // Daylight horizon and sky
            CreateSkyGradient(window, new Color(120, 167, 255, (byte) lightness), // bright blue daylight sky
                new Color(195, 210, 255, (byte) lightness));
            // Orange sunrise/sunset hue
            CreateSkyGradient(window, Color.Transparent, new Color(197, 86, 59, (byte) orangeness)); // sunset sunrise orange
        }
        else if (Dimension == Game.Dimension.Nether)
        {
            var backgroundRect = new RectangleShape(window.GetView().Size)
            {
                FillColor = new Color(25, 4, 4)
            };

            window.Draw(backgroundRect);
        }
        else if (Dimension == Game.Dimension.End)
        {
            var backgroundRect = new RectangleShape(window.GetView().Size)
            {
                FillColor = Color.Black
            };
            window.Draw(backgroundRect);

            var skyRect = new RectangleShape
            {
                Size = window.GetView().Size,
                Texture = EndSky,
                TextureRect = new IntRect((int) window.GetView().Viewport.Left, (int) window.GetView().Viewport.Top,
                    (int) window.GetView().Viewport.Width, (int) window.GetView().Viewport.Height),
                FillColor = new Color(255, 255, 255, 38)
            };

            window.Draw(skyRect);
        }

        window.SetView(worldLayer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CreateSkyGradient(RenderWindow window, Color atmosphereColour, Color horizonColour)
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

        if (Mouse.IsButtonPressed(Mouse.Button.Left))
        {
            var mousePosition = Mouse.GetPosition();
            ScreenToWorld(new Vector2f(mousePosition.X, mousePosition.Y));
            //SetBlock(mousePosition.X, mousePosition.Y, gameData.BlockIndex[nameof(Stone)]);
        }
        if (!lastPosition.Equals(CameraPosition))
        {
            //Console.WriteLine(CameraPosition);
            lastPosition = CameraPosition;
        }
    }

    // https://github.com/open-mc/client/blob/e147ff57d0f9653e1ef6bafea27744a1ced40376/iframe/index.js#L31
    private void Tick(object? sender, ElapsedEventArgs args)
    {
        TickCount++;
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
