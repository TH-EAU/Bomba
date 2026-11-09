using System.Collections.Generic;
using System.Linq;
using Bomba.Clouds;
using Godot;

namespace Bomba.DebugTools;

/// <summary>
/// Prises de vue fixes des nuages pour le réglage artistique. Chaque plan dure <see cref="FramesPerShot"/>
/// frames ; à capturer avec <c>--write-movie</c>. Le plan k occupe les frames [k×N, k×N + N − 1].
/// Plans : 0 dessus, 1 tour, 2 dedans, 3 dessous, 4 orage (avec éclair), 5 contre-jour, 6 panorama,
/// 7 anti-solaire (ombre de la Terre), 8 altitude (au-dessus de l'ombre, soleil visible).
/// </summary>
public partial class CloudShots : Node
{
    [Export] public int FramesPerShot { get; set; } = 15;

    private Camera3D _cam;
    private readonly List<(string Name, Vector3 Pos, Vector3 Target, bool Lightning)> _shots = new();
    private int _frame;

    public override void _Ready()
    {
        // -- --shot-frames=N : durée des plans (ex. 150 pour mesurer les FPS de chaque point de vue avec --fps).
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--shot-frames=") && int.TryParse(arg.Substring(14), out int n))
                FramesPerShot = n;
        CallDeferred(MethodName.Setup);
    }

    private void Setup()
    {
        CloudField f = CloudField.Instance;
        if (f == null || !f.IsBaked)
        {
            GD.PrintErr("CloudShots : pas de CloudField.");
            return;
        }

        var towers = f.Towers.ToList();
        // Congestus local (les satellites géants sont plus larges mais moins hauts).
        var calm = towers.Where(t => !t.Storm && t.Radius < 600f).OrderByDescending(t => t.Top).FirstOrDefault();
        var storm = towers.FirstOrDefault(t => t.Storm);
        if (towers.Count == 0) return;
        if (storm.Radius == 0) storm = towers[0];
        if (calm.Radius == 0) calm = towers[0];

        Vector3 Toward(Vector3 from, Vector3 to, float dist)
        {
            Vector3 d = to - from; d.Y = 0; d = d.Normalized();
            return to - d * dist;
        }

        Vector3 calmMid = calm.Position + Vector3.Up * (calm.Top - calm.Position.Y) * 0.5f;
        Vector3 stormMid = storm.Position + Vector3.Up * (storm.Top - storm.Position.Y) * 0.5f;

        // Point dans la couche : on cherche une densité suffisante près de l'origine.
        float layerMid = (f.LayerBottom + f.LayerTop) * 0.5f;
        Vector3 inside = new(0, layerMid, 0);
        for (float r = 0; r < 4000 && f.GetDensity(inside) < 0.3f; r += 40f)
            for (float a = 0; a < Mathf.Tau && f.GetDensity(inside) < 0.3f; a += 0.3f)
                inside = new Vector3(r * Mathf.Cos(a), layerMid, r * Mathf.Sin(a));

        Vector3 sun = new(0, 0.12f, -1);
        foreach (Node n in GetTree().Root.FindChildren("*", "DirectionalLight3D", true, false))
            if (n is DirectionalLight3D l) { sun = l.GlobalBasis.Z.Normalized(); break; }

        float above = f.LayerTop + 200f;
        _shots.Add(("dessus", Toward(Vector3.Zero, calmMid, calm.Radius * 4f + 1500f) with { Y = above },
            calmMid with { Y = f.LayerTop + 150f }, false));
        _shots.Add(("tour", Toward(Vector3.Zero, calmMid, calm.Radius * 2.5f + 600f) with { Y = f.LayerTop + 80f },
            calmMid, false));
        _shots.Add(("dedans", inside, inside + new Vector3(1, 0.05f, 0.3f) * 50f, false));
        _shots.Add(("dessous", new Vector3(inside.X, f.LayerBottom - 170f, inside.Z), inside + new Vector3(300, 120, 100), false));
        _shots.Add(("orage", Toward(Vector3.Zero, stormMid, storm.Radius * 1.9f + 4000f) with { Y = f.LayerTop + 900f },
            stormMid, true));
        Vector3 backPos = new(0, above, 0);
        _shots.Add(("contre-jour", backPos, backPos + new Vector3(sun.X, 0, sun.Z).Normalized() * 400f + Vector3.Down * 60f, false));
        // Panorama : un cumulonimbus vu de loin, pour juger sa hauteur totale.
        _shots.Add(("panorama", Toward(Vector3.Zero, stormMid, storm.Radius * 3f + 15000f) with { Y = f.LayerTop + 900f },
            stormMid with { Y = storm.Top * 0.45f }, false));
        // Crépuscule : dos au soleil (ombre de la Terre, ceinture de Vénus), puis au-dessus de l'ombre face au soleil.
        Vector3 toSun = new(Bomba.Core.Twilight.SunAzimuth.X, 0f, Bomba.Core.Twilight.SunAzimuth.Y);
        _shots.Add(("anti-solaire", new Vector3(0, above, 0), new Vector3(0, above, 0) - toSun * 1000f + Vector3.Up * 120f, false));
        Vector3 high = new(0, Bomba.Core.Twilight.ShadowAltitude + 900f, 0);
        _shots.Add(("altitude", high, high + toSun * 1000f - Vector3.Up * 40f, false));
        // Pluie : sous la nappe, face au rideau de pluie du cumulonimbus (de la mer à la base des nuages).
        Vector3 stormBase = storm.Position with { Y = 120f };
        _shots.Add(("pluie", Toward(Vector3.Zero, stormBase, storm.Radius * 0.6f + 5000f) with { Y = 120f }, stormBase, false));
        // Sous l'orage : dans la pluie (particules, vent d'orage), regard vers l'extérieur à travers le rideau.
        Vector3 underPos = Toward(Vector3.Zero, stormBase, storm.Radius * 0.35f) with { Y = 150f };
        _shots.Add(("sous l'orage", underPos, underPos + (underPos - stormBase) with { Y = 0f } + Vector3.Up * 60f, false));
        // Bord : caméra au ras du flanc d'un cumulus, regard tangent à travers son bord vers l'horizon
        // (les nuages lointains doivent rester visibles à travers le bord fin).
        Vector3 side = new Vector3(1f, 0f, 0.35f).Normalized();
        Vector3 edgePos = calmMid;
        for (float r = 0f; r < 6000f && f.GetDensity(edgePos) > 0.01f; r += 10f)
            edgePos = calmMid + side * r;
        edgePos -= side * 15f;   // juste dans la frange du nuage
        _shots.Add(("bord", edgePos - new Vector3(-side.Z, 0f, side.X) * 120f,
            edgePos + new Vector3(-side.Z, 0f, side.X) * 1000f + Vector3.Up * 20f, false));

        // Les gouttes d'écran dépendent du véhicule (qui traverse des nuages) : masquées pour les plans fixes.
        if (GetTree().Root.FindChild("ScreenDroplets", true, false) is CanvasLayer droplets)
            droplets.Visible = false;

        _cam = new Camera3D { Fov = 70f, Far = 90000f };
        AddChild(_cam);
        _cam.MakeCurrent();
        ApplyShot(0);

        for (int i = 0; i < _shots.Count; i++)
            GD.Print($"[Plan {i}] {_shots[i].Name} : frames {i * FramesPerShot}-{i * FramesPerShot + FramesPerShot - 1}");
    }

    private void ApplyShot(int i)
    {
        var s = _shots[i];
        _cam.GlobalPosition = s.Pos;
        _cam.LookAt(s.Target, Vector3.Up);
        _cam.ResetPhysicsInterpolation();
        if (s.Lightning)
            CloudField.Instance.TriggerLightning(s.Target);
    }

    public override void _Process(double delta)
    {
        if (_cam == null || _shots.Count == 0)
            return;
        _cam.MakeCurrent();
        _frame++;
        int shot = _frame / FramesPerShot;
        if (shot >= _shots.Count)
        {
            GetTree().Quit();
            return;
        }
        if (_frame % FramesPerShot == 0)
            ApplyShot(shot);
        else if (_shots[shot].Lightning && _frame % FramesPerShot == FramesPerShot - 4)
            CloudField.Instance.TriggerLightning(_shots[shot].Target);
    }
}
