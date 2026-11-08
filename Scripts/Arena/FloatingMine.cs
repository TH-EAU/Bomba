using Bomba.Vehicles;
using Bomba.Vehicles.Modules;
using Godot;

namespace Bomba.Arena;

/// <summary>
/// Mine flottante : une bombe suspendue sous un ballon à gaz, retenue à un point d'ancrage par un câble.
/// Elle n'a aucun code de flottaison : elle monte grâce à <see cref="Balloon"/>. Le seul rappel vers
/// l'ancre n'agit qu'au-delà du mou du câble.
/// Explose au contact d'un véhicule, puis réapparaît à son ancre après <see cref="RespawnDelay"/>.
/// </summary>
[Tool]
public partial class FloatingMine : RigidBody3D
{
    /// <summary>Longueur du câble (m), du centre de la mine à la base du ballon.</summary>
    [Export] public float TetherLength { get; set; } = 4f;

    /// <summary>Mou du câble (m) : au-delà de cette distance à l'ancre, le rappel s'applique.</summary>
    [Export] public float AnchorSlack { get; set; } = 5f;

    /// <summary>Raideur du rappel vers l'ancre (m/s² par mètre de dépassement).</summary>
    [Export] public float AnchorStiffness { get; set; } = 6f;

    /// <summary>Amortissement du rappel vers l'ancre (1/s).</summary>
    [Export] public float AnchorDamping { get; set; } = 1.5f;

    /// <summary>Changement de vitesse (m/s) infligé au véhicule à l'explosion.</summary>
    [Export] public float ExplosionImpulse { get; set; } = 25f;

    /// <summary>Délai (s) avant que la mine réapparaisse.</summary>
    [Export] public float RespawnDelay { get; set; } = 8f;

    private static readonly Color FlashColor = new(1f, 0.5f, 0.1f);

    private Vector3 _anchor;
    private bool _exploded;
    private CollisionShape3D _collision;

    public override void _Ready()
    {
        Mass = 3f;
        GravityScale = 1f;
        LinearDamp = 0.5f;
        AngularDamp = 2f;
        ContactMonitor = true;
        MaxContactsReported = 4;
        CanSleep = false;

        _anchor = GlobalPosition;

        BuildBody();
        BuildTether();
        BuildBalloon();

        if (!Engine.IsEditorHint())
            BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Engine.IsEditorHint())
            return;
        Vector3 offset = GlobalPosition - _anchor;
        float d = offset.Length();
        if (d > AnchorSlack)
            ApplyCentralForce((-offset / d * (d - AnchorSlack) * AnchorStiffness - LinearVelocity * AnchorDamping) * Mass);
    }

    private void BuildBody()
    {
        var rust = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.45f, 0.4f, 0.35f),
            Metallic = 0.8f,
            Roughness = 0.5f,
        };

        AddChild(new MeshInstance3D
        {
            Name = "Corps",
            Mesh = new SphereMesh { Radius = 0.9f, Height = 1.8f },
            MaterialOverride = rust,
        });

        // Huit pointes, une par coin du cube, orientées vers l'extérieur.
        for (int i = 0; i < 8; i++)
        {
            var dir = new Vector3(
                (i & 1) == 0 ? -1f : 1f,
                (i & 2) == 0 ? -1f : 1f,
                (i & 4) == 0 ? -1f : 1f).Normalized();

            // Le cône pointe vers +Y local : on fait pivoter +Y vers la direction, et on le pose sur la sphère.
            AddChild(new MeshInstance3D
            {
                Name = $"Pointe{i}",
                Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.15f, Height = 0.6f },
                MaterialOverride = rust,
                Transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, dir)), dir * 1.2f),
            });
        }

        _collision = new CollisionShape3D { Name = "Collision", Shape = new SphereShape3D { Radius = 1.1f } };
        AddChild(_collision);
    }

    private void BuildTether()
    {
        AddChild(new MeshInstance3D
        {
            Name = "Cable",
            Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = TetherLength },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.1f) },
            Position = new Vector3(0f, TetherLength / 2f, 0f),
        });
    }

    private void BuildBalloon()
    {
        AddChild(new Balloon
        {
            Name = "Ballon",
            Mode = BalloonMode.Gas,
            Channel = InputChannel.Always,
            LiftForce = Mass * 9.8f * 1.05f,
            AirDrag = 3f,
            Radius = 1.4f,
            FabricColor = new Color(0.7f, 0.2f, 0.15f),
            Position = new Vector3(0f, TetherLength + 1.4f, 0f),
        });
    }

    private void OnBodyEntered(Node body)
    {
        if (_exploded || body is not Vehicle v)
            return;
        _exploded = true;

        Vector3 dir = ((v.GlobalPosition - GlobalPosition).Normalized() + Vector3.Up * 0.3f).Normalized();
        v.ApplyCentralImpulse(dir * ExplosionImpulse * v.Mass);

        Vector3 spin = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f).Normalized();
        v.ApplyTorqueImpulse(spin * v.Mass * 3f);

        SpawnFlash();

        Visible = false;
        // Différé : on est dans un callback de collision (flush des requêtes physiques).
        SetDeferred(RigidBody3D.PropertyName.Freeze, true);
        _collision.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);

        GetTree().CreateTimer(RespawnDelay).Timeout += Respawn;
    }

    /// <summary>Flash orange : une sphère qui grossit et une lumière, ajoutés au parent de la mine.</summary>
    private void SpawnFlash()
    {
        var sphere = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = FlashColor,
                EmissionEnabled = true,
                Emission = FlashColor,
                EmissionEnergyMultiplier = 4f,
            },
            Scale = Vector3.One * 0.5f,
        };
        var light = new OmniLight3D
        {
            LightColor = FlashColor,
            LightEnergy = 8f,
            OmniRange = 25f,
        };

        Node parent = GetParent();
        parent.AddChild(sphere);
        parent.AddChild(light);
        sphere.GlobalPosition = GlobalPosition;
        light.GlobalPosition = GlobalPosition;

        Tween tween = sphere.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(sphere, "scale", Vector3.One * 6f, 0.3f);
        tween.TweenProperty(light, "light_energy", 0f, 0.3f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            sphere.QueueFree();
            light.QueueFree();
        }));
    }

    private void Respawn()
    {
        if (!IsInstanceValid(this))
            return;

        var transform = new Transform3D(Basis.Identity, _anchor);
        PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.Transform, transform);
        PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.LinearVelocity, Vector3.Zero);
        PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.AngularVelocity, Vector3.Zero);
        GlobalTransform = transform;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        ResetPhysicsInterpolation();

        Visible = true;
        Freeze = false;
        _collision.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
        _exploded = false;
    }
}
