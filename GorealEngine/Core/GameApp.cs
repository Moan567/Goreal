using System.Numerics;
using Goreal.Core.Components;
using Goreal.Core.ECS;
using Goreal.Core.Graphics;
using Goreal.Core.Physics;
using Goreal.Core.Scene;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Maths;

namespace Goreal.Core;

/// <summary>
/// Thin windowing / main-loop wrapper. Owns map, collision, ECS and renderer.
/// </summary>
public sealed class GameApp : IDisposable
{
    readonly IWindow _window;
    GL? _gl;
    Map? _map;
    MapRenderer? _renderer;
    TextureManager? _texMan;
    Camera _camera = new();
    readonly CollisionWorld _collision = new();
    readonly InputState _input = new();
    readonly World _world = new();
    readonly List<ISystem> _systems = new();
    DoorSystem? _doorSystem;
    TriggerSystem? _triggerSystem;
    PropSystem? _propSystem;
    ModelManager? _modelManager;
    ModelRenderer? _modelRenderer;
    IInputContext? _inputCtx;
    IKeyboard? _kbd;
    IMouse? _mouse;
    Entity _player;
    float _totalTime;
    bool _mouseCaptured = true;

    public string MapPath { get; }

    public GameApp(string mapPath, int width = 1280, int height = 720, string title = "Goreal - .map Walk")
    {
        MapPath = mapPath;
        var opts = WindowOptions.Default;
        opts.Size = new Vector2D<int>(width, height);
        opts.Title = title;
        _window = Window.Create(opts);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += () => Dispose();
        _window.FramebufferResize += s => _gl?.Viewport(s);
    }

    public void Run() => _window.Run();

    void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _inputCtx = _window.CreateInput();
        _kbd = _inputCtx.Keyboards.FirstOrDefault();
        _mouse = _inputCtx.Mice.FirstOrDefault();
        if (_mouse != null)
        {
            Vector2 lastPos = new(_mouse.Position.X, _mouse.Position.Y);
            _mouse.MouseMove += (_, pos) =>
            {
                if (_mouseCaptured)
                {
                    var cur = new Vector2(pos.X, pos.Y);
                    _input.AddMouseDelta(cur - lastPos);
                    lastPos = cur;
                }
                else
                {
                    lastPos = new Vector2(pos.X, pos.Y);
                }
            };
            _mouse.Click += (_, btn, _) =>
            {
                if (btn == MouseButton.Left && !_mouseCaptured) SetCapture(true);
            };
        }
        if (_kbd != null)
        {
            _kbd.KeyDown += (_, k, _) => { _input.SetKey(k, true); if (k == Key.Escape) { if (_mouseCaptured) SetCapture(false); else _window.Close(); } };
            _kbd.KeyUp += (_, k, _) => _input.SetKey(k, false);
        }
        SetCapture(true);

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.ClearColor(0.53f, 0.66f, 0.78f, 1f);

            // load map
        ReloadMap();
        _texMan = new TextureManager(_gl, _map?.SourceDirectory ?? Path.GetDirectoryName(MapPath) ?? "");
        _modelManager = new ModelManager(_gl, _texMan);
        _renderer = new MapRenderer(_gl);
        // spawn entities (doors/triggers/props) before building renderer so dynamic batches are built
        var doorInfos = SpawnEntities();
        if (_map != null) _renderer.Build(_map, _texMan, doorInfos);
        _modelRenderer = new ModelRenderer(_gl, _renderer.Shader, _modelManager, _camera);

        // create player entity
        _player = _world.CreateEntity();
        var start = _map?.PlayerStart ?? new Vector3(0, 1f, 0);
        // lift a bit to avoid starting inside floor
        start.Y += 0.5f;
        _world.AddComponent(_player, new Transform(start));
        _world.AddComponent(_player, new Velocity());
        _world.AddComponent(_player, new PlayerController { Yaw = _map?.PlayerYaw ?? 0f, Pitch = 0f });

        // add movement system – try to use Game's PlayerMoveSystem if available, otherwise fallback simple
        ISystem? move = TryCreateGameMoveSystem();
        if (move == null) move = new FallbackMoveSystem(_collision, _input);
        _systems.Add(move);

        // sync camera
        _camera.Position = start;
        _camera.Yaw = _map?.PlayerYaw ?? 0f;

        Console.WriteLine($"[GameApp] map: {MapPath}");
        Console.WriteLine($"[GameApp] brushes: {_map?.Brushes.Count}, player start: {start} yaw {(_map?.PlayerYaw ?? 0)}");
        Console.WriteLine("[GameApp] WASD move, Mouse look, Space jump, Ctrl crouch/down, V noclip, Esc unlock/quit");
    }

    ISystem? TryCreateGameMoveSystem()
    {
        // Reflection: look for Goreal.Runtime.Systems.PlayerMoveSystem in Game assembly (referenced)
        var t = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(x => x.Name == "PlayerMoveSystem");
        if (t == null) return null;
        try
        {
            return (ISystem?)Activator.CreateInstance(t, _collision, _input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameApp] PlayerMoveSystem create failed: {ex.Message}, using fallback");
            return null;
        }
    }

    void SetCapture(bool capture)
    {
        _mouseCaptured = capture;
        _input.MouseCaptured = capture;
        if (_mouse != null) _mouse.Cursor.CursorMode = capture ? CursorMode.Raw : CursorMode.Normal;
    }

    void ReloadMap()
    {
        if (!File.Exists(MapPath))
        {
            Console.WriteLine($"[GameApp] map not found: {MapPath}, using empty");
            _map = new Map();
            return;
        }
        try
        {
            _map = MapLoader.Load(MapPath);
            var datas = _map.Brushes.Select(b => b.CollisionData).ToList();
            _collision.Load(datas);
            Console.WriteLine($"[GameApp] loaded {datas.Count} static collision brushes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameApp] map load failed: {ex}");
            _map = new Map();
        }
    }

    List<MapRenderer.DoorBuildInfo> SpawnEntities()
    {
        var doorInfos = new List<MapRenderer.DoorBuildInfo>();
        if (_map == null) return doorInfos;

        _doorSystem = new DoorSystem(_collision, _input);
        _triggerSystem = new TriggerSystem(_doorSystem);
        _propSystem = new PropSystem(_collision);
        _systems.Add(_doorSystem);
        _systems.Add(_triggerSystem);
        _systems.Add(_propSystem);
        // ModelManager needs GL – will be created in OnLoad after _texMan, but props need it now for static? Defer model loading to PropSystem
        // Ensure _modelManager exists (created in OnLoad before SpawnEntities is called – so create here if not)
        if (_modelManager == null && _gl != null && _texMan != null) _modelManager = new ModelManager(_gl, _texMan);

        foreach (var ent in _map.Entities)
        {
            if (ent.ClassName == "func_door")
            {
                // parse properties
                float angle = 0; if (ent.Properties.TryGetValue("angle", out var aStr) && float.TryParse(aStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var av)) angle = av;
                float speed = 100; if (ent.Properties.TryGetValue("speed", out var sStr) && float.TryParse(sStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv)) speed = sv;
                float lip = 8; if (ent.Properties.TryGetValue("lip", out var lStr) && float.TryParse(lStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lv)) lip = lv;
                float wait = 3; if (ent.Properties.TryGetValue("wait", out var wStr) && float.TryParse(wStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wv)) wait = wv;
                ent.Properties.TryGetValue("targetname", out var tname);
                ent.Properties.TryGetValue("target", out var target);

                // move dir
                Vector3 moveDir;
                if (angle == -1) moveDir = Vector3.UnitY;
                else if (angle == -2) moveDir = -Vector3.UnitY;
                else
                {
                    float rad = angle * Units.Deg2Rad;
                    // Quake angle 0 = +X, 90 = +Y north -> engine -Z
                    moveDir = new Vector3(MathF.Cos(rad), 0, -MathF.Sin(rad));
                    if (moveDir.LengthSquared() < 1e-6f) moveDir = Vector3.UnitY;
                    moveDir = Vector3.Normalize(moveDir);
                }

                // bounds
                Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
                var datas = new List<CollisionBrushData>();
                foreach (var b in ent.Brushes) { datas.Add(b.CollisionData); bmin = Vector3.Min(bmin, b.CollisionData.Min); bmax = Vector3.Max(bmax, b.CollisionData.Max); }
                if (datas.Count == 0) continue;
                float size = 0;
                if (MathF.Abs(moveDir.X) > 0.5f) size = bmax.X - bmin.X;
                else if (MathF.Abs(moveDir.Y) > 0.5f) size = bmax.Y - bmin.Y;
                else size = bmax.Z - bmin.Z;
                if (size < 0) size = (bmax - bmin).Length() * 0.5f;
                float lipM = lip * Units.QuakeToMeters;
                float dist = MathF.Max(0.05f, size - lipM);
                float speedM = speed * Units.QuakeToMeters;

                var door = new Door
                {
                    ClosedPos = Vector3.Zero,
                    OpenPos = moveDir * dist,
                    CurrentPos = Vector3.Zero,
                    MoveDir = moveDir,
                    MoveDistance = dist,
                    Speed = speedM <= 0 ? 2f : speedM,
                    Wait = wait,
                    Lip = lipM,
                    IsOpen = false, IsMoving = false, MoveProgress = 0, MoveSign = 1, WaitTimer = 0,
                    TargetName = tname ?? "", Target = target ?? "",
                    TriggerOnTouch = string.IsNullOrEmpty(tname),
                    UseActivates = true
                };
                var de = _world.CreateEntity();
                _world.AddComponent(de, door);
                int handle = _collision.RegisterDynamic(datas);
                _doorSystem.RegisterDoor(de, datas, bmin, bmax, handle);
                doorInfos.Add(new MapRenderer.DoorBuildInfo { EntityId = de.Id, Brushes = ent.Brushes });
                Console.WriteLine($"[Door] entity {de.Id} angle {angle} dir {moveDir} dist {dist:F2} speed {speedM:F2} wait {wait} brushes {ent.Brushes.Count}");
            }
            else if (ent.ClassName.StartsWith("trigger_"))
            {
                Vector3 tmin = new(float.MaxValue), tmax = new(float.MinValue);
                foreach (var b in ent.Brushes) { tmin = Vector3.Min(tmin, b.CollisionData.Min); tmax = Vector3.Max(tmax, b.CollisionData.Max); }
                if (tmin.X == float.MaxValue) continue;
                ent.Properties.TryGetValue("target", out var target);
                ent.Properties.TryGetValue("targetname", out var tname2);
                float wait2 = 0.5f; if (ent.Properties.TryGetValue("wait", out var wt) && float.TryParse(wt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wv2)) wait2 = wv2;
                var trig = new Goreal.Core.Components.Trigger { Target = target ?? "", TargetName = tname2 ?? "", TouchActivates = true, Wait = wait2, Min = tmin, Max = tmax };
                var te = _world.CreateEntity();
                _world.AddComponent(te, trig);
                Console.WriteLine($"[Trigger] {ent.ClassName} target '{trig.Target}' bounds {tmin}->{tmax}");
            }
            else if (ent.ClassName == "misc_model" || ent.ClassName == "prop_static" || ent.ClassName == "prop_bob")
            {
                if (!ent.Properties.TryGetValue("model", out var modelPath) || string.IsNullOrWhiteSpace(modelPath)) continue;
                Vector3 origin = Vector3.Zero; if (ent.Properties.TryGetValue("origin", out var oStr)) origin = MapLoaderQuakeToEngine(ParseVec(oStr));
                Vector3 angles = Vector3.Zero; if (ent.Properties.TryGetValue("angles", out var aStr)) angles = ParseVec(aStr); else if (ent.Properties.TryGetValue("angle", out var angStr) && float.TryParse(angStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ay)) angles = new Vector3(0, ay, 0);
                float scale = 1; if (ent.Properties.TryGetValue("scale", out var sc) && float.TryParse(sc, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv)) scale = sv;
                var mover = PropMoverType.Static;
                float speed = 0, height = 0;
                if (ent.ClassName == "prop_bob")
                {
                    mover = PropMoverType.Bob;
                    if (ent.Properties.TryGetValue("speed", out var s) && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv2)) speed = sv2; else speed = 0.5f;
                    if (ent.Properties.TryGetValue("height", out var h) && float.TryParse(h, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hv)) height = hv * Units.QuakeToMeters; else height = 8 * Units.QuakeToMeters;
                }
                var prop = new Prop{ ModelPath=modelPath, Origin=origin, Angles=angles, Scale=new Vector3(scale), Mover=mover, Speed=speed, BobHeight=height, CurrentPos=origin, CurrentRot=Quaternion.CreateFromYawPitchRoll(angles.Y*MathF.PI/180f, angles.X*MathF.PI/180f, angles.Z*MathF.PI/180f)};
                var pe2 = _world.CreateEntity();
                _world.AddComponent(pe2, prop);
                Console.WriteLine($"[Prop] {ent.ClassName} model {modelPath} at {origin} angles {angles} mover {mover}");
            }
            else if (ent.ClassName == "func_rotating" || ent.ClassName == "func_movelinear")
            {
                Vector3 origin = Vector3.Zero; if (ent.Properties.TryGetValue("origin", out var oStr2)) origin = MapLoaderQuakeToEngine(ParseVec(oStr2));
                else if (ent.Brushes.Count>0)
                {
                    Vector3 bmin2=new(float.MaxValue), bmax2=new(float.MinValue);
                    foreach(var b in ent.Brushes){ bmin2=Vector3.Min(bmin2,b.CollisionData.Min); bmax2=Vector3.Max(bmax2,b.CollisionData.Max); }
                    origin = (bmin2+bmax2)*0.5f;
                }
                Vector3 angles = Vector3.Zero; if (ent.Properties.TryGetValue("angles", out var aStr2)) angles = ParseVec(aStr2);
                float speed2 = 45; if (ent.Properties.TryGetValue("speed", out var s2) && float.TryParse(s2, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv3)) speed2 = sv3;
                // check if it has a model key – if so treat as prop with model, else as brush mover
                ent.Properties.TryGetValue("model", out var mpath);
                PropMoverType mt = ent.ClassName=="func_rotating" ? PropMoverType.Rotate : PropMoverType.MoveLinear;
                Vector3 moveDir = Vector3.Zero; float dist=0;
                if (mt==PropMoverType.MoveLinear)
                {
                    if (ent.Properties.TryGetValue("movedir", out var mdStr)) moveDir = ParseVec(mdStr);
                    else if (ent.Properties.TryGetValue("angle", out var ang2) && float.TryParse(ang2, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var av2))
                    {
                        float rad = av2*Units.Deg2Rad;
                        moveDir = new Vector3(MathF.Cos(rad),0,-MathF.Sin(rad));
                    }
                    else moveDir = new Vector3(0,0,1);
                    if (moveDir.LengthSquared()>1e-6f) moveDir = Vector3.Normalize(moveDir);
                    if (ent.Properties.TryGetValue("distance", out var dStr) && float.TryParse(dStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv)) dist = dv*Units.QuakeToMeters; else dist = 64*Units.QuakeToMeters;
                }
                var prop2 = new Prop{ ModelPath=mpath??"", Origin=origin, Angles=angles, Scale=new Vector3(1), Mover=mt, MoveDir=moveDir, MoveDistance=dist, Speed=speed2, CurrentPos=origin, CurrentRot=Quaternion.Identity};
                var pe3 = _world.CreateEntity();
                _world.AddComponent(pe3, prop2);
                if (ent.Brushes.Count>0)
                {
                    var datas2 = ent.Brushes.Select(b=>b.CollisionData).ToList();
                    Vector3 bmin3=new(float.MaxValue), bmax3=new(float.MinValue);
                    foreach(var b in ent.Brushes){ bmin3=Vector3.Min(bmin3,b.CollisionData.Min); bmax3=Vector3.Max(bmax3,b.CollisionData.Max); }
                    int handle2 = _collision.RegisterDynamic(datas2);
                    _propSystem?.RegisterBrushMover(pe3, datas2, bmin3, bmax3, handle2);
                    doorInfos.Add(new MapRenderer.DoorBuildInfo{ EntityId=pe3.Id, Brushes=ent.Brushes});
                    Console.WriteLine($"[Mover] {ent.ClassName} entity {pe3.Id} mover {mt} brushes {ent.Brushes.Count} at {origin}");
                }
                else if (!string.IsNullOrEmpty(mpath))
                {
                    Console.WriteLine($"[Mover] {ent.ClassName} model {mpath} at {origin}");
                }
            }
        }
        return doorInfos;
    }

    static Vector3 ParseVec(string s)
    {
        var parts = s.Trim().Split(new[]{' ','\t'}, StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length<3) return Vector3.Zero;
        return new Vector3(
            float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)?x:0,
            float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)?y:0,
            float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)?z:0);
    }
    static Vector3 MapLoaderQuakeToEngine(Vector3 q) => new Vector3(q.X, q.Z, -q.Y) * Units.QuakeToMeters;

    void OnUpdate(double dtRaw)
    {
        float dt = (float)dtRaw;
        if (dt > 0.1f) dt = 0.1f;
        _totalTime += dt;

        foreach (var s in _systems) s.Update(_world, dt);

        // sync camera from player
        var ent = _world.FindPlayer();
        if (ent.HasValue)
        {
            ref var t = ref _world.GetTransform(ent.Value);
            ref var pc = ref _world.GetPlayer(ent.Value);
            _camera.Position = t.Position + new Vector3(0, 0.55f, 0); // eye height
            _camera.Yaw = pc.Yaw;
            _camera.Pitch = pc.Pitch;
        }
        else
        {
            // no ECS player: just fly debug
        }

        _input.EndFrame();
    }

    void OnRender(double _)
    {
        if (_gl == null || _renderer == null) return;
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        float aspect = (float)_window.Size.X / _window.Size.Y;
        _renderer.Render(_camera, aspect, _world);
        _modelRenderer?.Render(_world, aspect);
    }

    public void Dispose()
    {
        _renderer?.Dispose();
        _modelRenderer?.Dispose();
        _modelManager?.Dispose();
        _texMan?.Dispose();
    }

    // fallback if Game.PlayerMoveSystem not found (e.g. during engine-only test)
    sealed class FallbackMoveSystem : ISystem
    {
        readonly CollisionWorld _col;
        readonly InputState _input;
        const float Speed = 4f;
        const float Sens = 0.12f;
        public FallbackMoveSystem(CollisionWorld c, InputState i) { _col = c; _input = i; }
        public void Update(World world, float dt)
        {
            world.Query<Transform, Velocity, PlayerController>((Entity e, ref Transform t, ref Velocity v, ref PlayerController pc) =>
            {
                if (_input.MouseCaptured)
                {
                    pc.Yaw -= _input.MouseDelta.X * Sens;
                    pc.Pitch = Math.Clamp(pc.Pitch - _input.MouseDelta.Y * Sens, -89, 89);
                }
                float f = (_input.IsDown(Key.W) ? 1 : 0) - (_input.IsDown(Key.S) ? 1 : 0);
                float s = (_input.IsDown(Key.D) ? 1 : 0) - (_input.IsDown(Key.A) ? 1 : 0);
                var look = PlayerController.LookDirection(pc.Yaw, pc.Pitch);
                var right = new Vector3(MathF.Sin(pc.Yaw * Units.Deg2Rad), 0, MathF.Cos(pc.Yaw * Units.Deg2Rad));
                var wish = look * f + right * s;
                if (wish.LengthSquared() > 1) wish = Vector3.Normalize(wish);
                t.Position += wish * Speed * dt;
                if (_input.IsDown(Key.Space)) t.Position += new Vector3(0, Speed * dt, 0);
                if (_input.IsDown(Key.ControlLeft)) t.Position -= new Vector3(0, Speed * dt, 0);
            });
        }
    }
}
